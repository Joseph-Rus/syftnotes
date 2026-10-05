using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyStash.App.Controls;
using StudyStash.App.Controls.Rich;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;
using StudyStash.Core;
using StudyStash.Core.Ai;
using StudyStash.Core.Rich;

namespace StudyStash.App.Tests;

/// <summary>The notes arrive before their diagrams: the lecture's byline says quietly that they're coming, and when they
/// do they appear in the notes on screen, in place, the student's page staying exactly where they're reading.</summary>
public class DiagramsArriveTests
{
    const string Chain = "flowchart LR\n  Low[\"Low blood pressure\"] --> JG[\"JG cells\"]\n  JG -->|renin| A1[\"Angiotensin I\"]\n"
        + "  A1 -->|ACE in lungs| A2[\"Angiotensin II\"]\n  A2 --> BP[\"Blood pressure rises\"]\n  BP -.->|stops renin| JG";

    static readonly string Notes = "## Summary\nHow the kidney raises blood pressure.\n\n## Key points\n- Renin starts it.\n- Angiotensin II does the work.\n\n"
        + "## Details and examples\n" + string.Join("\n\n", Enumerable.Range(1, 12).Select(i => $"Paragraph {i}: ACE in the lungs converts angiotensin I into angiotensin II, and the pressure rises until renin stops."))
        + "\n\n## Questions to review\n1. What releases renin?\n2. Where is ACE?";

    [AvaloniaFact]
    public async Task Diagrams_that_arrive_after_the_notes_appear_in_place_and_the_page_stays_where_the_student_reads()
    {
        string at = new DateTime(2026, 9, 29, 9, 40, 0).ToString("o");
        string drawn = DiagramDesign.Insert(Notes, [new DesignedDiagram("The renin chain", "Key points", 25, NoteBlockKind.Mermaid, Chain, "Low pressure releases renin.")]);
        bool landed = false;
        var lib = new FakeAiLibrary
        {
            Overview = AiTestData.MixedOverview(),
            OnRewrite = _ => landed
                ? new RewriteInfo("lec-1", "none") { Current = new NotesVersion(drawn, "Claude Code", at) }
                : new RewriteInfo("lec-1", "none") { Current = new NotesVersion(Notes, "Claude Code", at), Diagrams = "adding" },
        };
        var poll = new SemaphoreSlim(0);
        var model = new AiNotesModel(lib) { Delay = (_, ct) => poll.WaitAsync(ct) };
        await model.Load("lec-1", Notes, "Claude Code", at);
        Assert.Equal("Written by Claude Code · Tue 9:40 · Adding diagrams…", model.ShownByline);

        Skin.UseTheme(ColourThemes.Default);
        ((App)Application.Current!).UseSkin(SkinKind.Mac);
        var page = new MacAiNotes { DataContext = model, Width = 620, HorizontalAlignment = HorizontalAlignment.Left };
        var scroller = new ScrollViewer { Content = page, Height = 360, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        var window = new Window { Width = 660, Height = 400, RequestedThemeVariant = ThemeVariant.Light, Content = scroller };
        window.Show();
        DiagramsReady.Wait(window);
        var view = page.GetVisualDescendants().OfType<NoteView>().First(v => v.IsEffectivelyVisible);
        var reading = view.Children.OfType<TextBlock>().Last(t => t.Classes.Contains(NoteView.HeadingClass)); // "Questions to review"
        scroller.Offset = new Vector(0, reading.TranslatePoint(default, page)!.Value.Y - 40);
        DiagramsReady.Wait(window);
        double before = reading.TranslatePoint(default, scroller)!.Value.Y;
        var kept = view.Children.ToList();

        landed = true;
        poll.Release();
        for (int i = 0; i < 200 && model.DiagramsLine != "Diagrams added"; i++) await Task.Delay(10);
        DiagramsReady.Wait(window);
        Dispatcher.UIThread.RunJobs();
        DiagramsReady.Wait(window);

        Assert.Equal("Written by Claude Code · Tue 9:40 · Diagrams added", model.ShownByline);
        Assert.Same(view, page.GetVisualDescendants().OfType<NoteView>().First(v => v.IsEffectivelyVisible)); // the same page, not a new one
        Assert.Single(view.GetVisualDescendants().OfType<DiagramView>());
        Assert.All(kept, c => Assert.Contains(c, view.Children)); // every piece already on screen is the same control
        Assert.Equal(before, reading.TranslatePoint(default, scroller)!.Value.Y, 1.0); // a diagram above, and the student's line hasn't moved
        Assert.True(scroller.Offset.Y > 0);
        model.Dispose();
        window.Close();
    }
}
