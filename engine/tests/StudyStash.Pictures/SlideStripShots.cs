using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;
using StudyStash.Core;

namespace StudyStash.App.Pictures;

/// <summary>
/// Pictures of a lecture's page with the strip of its slides, both looks, light and dark: the strip (cards in the order
/// the slides came up, and the ones that couldn't be placed under their own heading), the transcript with a small card
/// above each line a slide was taught at, and the same right after a card was clicked, the line marked. Also while the
/// deck's pages are still being read, a deck where nothing could be placed, and a narrow window. The lecture is the
/// demo library's, and the deck is made up. Drawn off screen into STUDYSTASH_SHOTS; only with STUDYSTASH_FILM=1.
/// </summary>
public class SlideStripShots
{
    /// <summary>What the lecturer said, a line at a time: seconds into the lecture, and the words.</summary>
    static readonly (double At, string Text)[] Said =
    [
        (12, "Okay, let's get started. Last week was functions. Today is recursion, and the call stack that makes it work."),
        (30, "Two ideas that only make sense together: a function that calls itself, and the memory that keeps track of the calls."),
        (131, "Before the new material, a reminder that the midterm is in two weeks and it covers everything up to today."),
        (225, "So what is recursion? A function that solves a problem by calling itself on a smaller version of the same problem."),
        (312, "You have already seen this with a folder that holds folders: to count the files, count what is in each folder inside it."),
        (580, "Every recursive function needs a base case, an input it can answer directly without calling itself."),
        (671, "Without one, the calls never stop, and I will show you what that looks like on the stack in a minute."),
        (1170, "Think of each call as a plate on a stack. You can only take the top one off."),
        (1312, "When factorial of three calls factorial of two, the first call is paused, waiting for the answer."),
        (1388, "Factorial of two does the same to factorial of one, and now there are three plates on the stack."),
        (1452, "And when we hit the base case, the frames come off one by one, each call finishing its multiplication."),
        (1600, "The mistake I see most is forgetting the base case, and then the stack grows until the program runs out of room."),
        (1684, "Okay, let's take a short break and then we will trace a few of these together."),
    ];

    /// <summary>The deck: its titles and the second each was taught at, null where the library couldn't tell.</summary>
    static readonly (string Title, double? At)[] Deck =
    [
        ("Recursion and the call stack", 30), ("What recursion is", 225), ("The base case", 580), ("Plates on a stack", 1170),
        ("Tracing factorial(3)", 1312), ("When the base case returns", 1452), ("Common mistakes", null), ("Practice problems for the midterm", null),
    ];

    static JsonArray Slides(bool pinned) => new([.. Deck.Select((s, i) => (JsonNode)new JsonObject
    {
        ["n"] = i + 1, ["title"] = s.Title, ["at"] = pinned ? s.At : null,
    })]);

    /// <summary>Lists one deck with the given slides (null while its pages are still being read).</summary>
    sealed class OneDeck(Func<JsonArray?> slides) : IAttachmentLibrary
    {
        public Task<JsonObject?> AttachmentListAsync(string? className, string? lectureId, CancellationToken stop = default) =>
            Task.FromResult<JsonObject?>(new JsonObject
            {
                ["attachments"] = new JsonArray(new JsonObject
                {
                    ["id"] = "deck", ["name"] = "Week 4 slides.pdf", ["class"] = className, ["lecture"] = lectureId, ["size"] = 2_516_582,
                    ["type"] = "application/pdf", ["added"] = "2026-09-23T10:00:00Z", ["hasText"] = true, ["reading"] = false, ["slides"] = slides(),
                }),
                ["rewrite"] = false,
            });

        public Task<JsonArray> AttachAsync(IEnumerable<string> paths, string? className, string? lectureId, CancellationToken stop = default) => Task.FromResult(new JsonArray());
        public Task<bool> RemoveAttachmentAsync(string id) => Task.FromResult(false);
        public Task DownloadAttachmentAsync(string id, string path, CancellationToken stop = default) => Task.CompletedTask;
        public Task RewriteAsync(string id) => Task.CompletedTask;
    }

    /// <summary>The demo library on its lecture, with a transcript, and the deck attached as <paramref name="slides"/> lists it.</summary>
    static async Task<(LibraryModel Page, AttachmentsModel Files)> PageAsync(Func<JsonArray?> slides, bool narrow = false)
    {
        var page = Demo.Library();
        var note = page.Note!;
        foreach (var (at, text) in Said) note.Transcript.Add(new HeardLine { Time = TimedText.Clock(at), Text = text, Start = at });
        var files = new AttachmentsModel(new OneDeck(slides), note.ClassName, note.Id, note.ShowAt, note.PinSlides) { PollEvery = TimeSpan.FromHours(1) };
        page.LectureFiles = files;
        page.Narrow = narrow;
        _ = files.LoadAsync();
        await Wait.UntilAsync(() => files.ShowSlides, "the slides to be listed");
        return (page, files);
    }

    /// <summary>Draws the library's window with <paramref name="page"/> into <c>&lt;name&gt;-&lt;mac|win&gt;-&lt;light|dark&gt;.png</c>;
    /// <paramref name="then"/> does something on the page first (a click), and the page settles after it.</summary>
    static async Task DrawAsync(LibraryModel page, SkinKind skin, ThemeVariant variant, string name, double width = 1280, double height = 860, Func<Window, Task>? then = null)
    {
        Skin.UseTheme(ColourThemes.Default);
        ((App)Application.Current!).UseSkin(skin);
        Control view = skin == SkinKind.Mac ? new MacLibrary { DataContext = page } : new WinLibrary { DataContext = page };
        var window = new Window { Width = width, Height = height, RequestedThemeVariant = variant, Content = view };
        Look.Apply(window);
        window.Show();
        DiagramsReady.Wait(window);
        void Settle()
        {
            for (int i = 0; i < 3; i++)
            {
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
            }
        }
        Settle();
        if (then is not null) await then(window);
        Settle();
        using (var file = File.Create(Path.Combine(Shot.Dir, $"{name}-{(skin == SkinKind.Mac ? "mac" : "win")}-{(variant == ThemeVariant.Dark ? "dark" : "light")}.png")))
            window.CaptureRenderedFrame()!.Save(file, PngBitmapEncoderOptions.Default);
        window.Close();
    }

    static IEnumerable<(SkinKind Skin, ThemeVariant Variant)> Looks() =>
        from skin in new[] { SkinKind.Mac, SkinKind.Win } from variant in new[] { ThemeVariant.Light, ThemeVariant.Dark } select (skin, variant);

    static bool On => Environment.GetEnvironmentVariable("STUDYSTASH_FILM") == "1";

    [AvaloniaFact]
    public async Task The_strip_and_the_transcript_with_a_card_above_each_line_a_slide_was_taught_at()
    {
        if (!On) return;
        foreach (var (skin, variant) in Looks())
        {
            var (page, files) = await PageAsync(() => Slides(pinned: true));
            Assert.Equal(6, files.PinnedSlides.Count);
            Assert.Equal(2, files.UnpinnedSlides.Count);
            // The strip, over the notes.
            await DrawAsync(page, skin, variant, "1-lecture-strip");
            // The transcript tab: a card above each line a slide was taught at.
            page.Note!.ShowTranscript = true;
            await DrawAsync(page, skin, variant, "2-transcript-cards");
            files.Dispose();
        }
    }

    [AvaloniaFact]
    public async Task A_click_on_a_card_opens_the_transcript_at_its_line_marked()
    {
        if (!On) return;
        foreach (var (skin, variant) in Looks())
        {
            var (page, files) = await PageAsync(() => Slides(pinned: true));
            await DrawAsync(page, skin, variant, "2b-transcript-after-click", then: async window =>
            {
                var scroll = window.GetVisualDescendants().OfType<ScrollViewer>().First(s => s.Name == "NoteScroll");
                // The fifth card: "Tracing factorial(3)", 21:52.
                files.OpenSlideCommand.Execute(files.PinnedSlides[4]);
                Assert.True(page.Note!.ShowTranscript);
                await Wait.UntilAsync(() =>
                {
                    Dispatcher.UIThread.RunJobs();
                    window.UpdateLayout();
                    return scroll.Offset.Y > 100;
                }, "the page to scroll to the line");
            });
            Assert.Single(page.Note!.Transcript, l => l.Here);
            files.Dispose();
        }
    }

    [AvaloniaFact]
    public async Task While_the_slides_are_being_read()
    {
        if (!On) return;
        foreach (var (skin, variant) in Looks())
        {
            var (page, files) = await PageAsync(() => null);
            Assert.True(files.ReadingSlides);
            await DrawAsync(page, skin, variant, "3-reading-the-slides");
            files.Dispose();
        }
    }

    [AvaloniaFact]
    public async Task A_deck_where_nothing_could_be_placed()
    {
        if (!On) return;
        foreach (var (skin, variant) in Looks())
        {
            var (page, files) = await PageAsync(() => Slides(pinned: false));
            Assert.False(files.HasPinned);
            await DrawAsync(page, skin, variant, "4-nothing-placed");
            files.Dispose();
        }
    }

    [AvaloniaFact]
    public async Task A_narrow_window()
    {
        if (!On) return;
        foreach (var (skin, variant) in Looks())
        {
            var (page, files) = await PageAsync(() => Slides(pinned: true), narrow: true);
            await DrawAsync(page, skin, variant, "5-narrow-window", width: 700, height: 860);
            files.Dispose();
        }
    }
}
