using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Styling;
using StudyStash.App.Controls;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;

namespace StudyStash.App.Tests;

/// <summary>The egghead where the app shows him (Controls/Doodle.cs): every doodle draws, and the screens he's on,
/// light and dark, into STUDYSTASH_SHOTS: a home with no notes yet, notes being written, a library that can't be
/// read, and the menu bar panel while it records.</summary>
public class DoodleShots
{
    static readonly Size Wide = new(1408, 988);

    [AvaloniaFact]
    public void Every_doodle_has_a_light_and_a_dark_drawing()
    {
        foreach (string art in new[] { "hello", "head", "chicklet", "thinking", "sleepy", "cracked", "scrambled", "search", "overeasy", "writing", "listening" })
        {
            Assert.NotNull(Doodle.Find(art, dark: false));
            Assert.NotNull(Doodle.Find(art, dark: true));
        }
        Assert.Null(Doodle.Find("no-such-doodle", dark: false));
    }

    [AvaloniaFact]
    public void Doodle_shots()
    {
        foreach (var t in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            Shot.Take("mac-50-home-no-notes", SkinKind.Mac, t, () =>
            {
                var m = Demo.Semester();
                m.Overview!.Notes.Clear();
                m.Overview.NotesEmpty = "No notes yet. Record something and its notes land here.";
                return new MacLibrary { DataContext = m, Width = 1280, Height = 860 };
            }, size: Wide);
            Shot.Take("mac-51-notes-being-written", SkinKind.Mac, t, () =>
            {
                var m = Demo.Semester(Demo.Page.Note);
                m.Note = new NoteModel
                {
                    Id = "n2", ClassName = m.Note!.ClassName, Dot = m.Note.Dot, Noun = Nouns.Lecture, When = "Wednesday 14 October at 9:30 AM, 50 min",
                    Title = "Attention, Continued", Writing = true,
                    Pending = "Your library is writing the notes for this lecture. They show here when they're done.",
                }.Titled();
                m.Notes = null;
                return new MacLibrary { DataContext = m, Width = 1280, Height = 860 };
            }, size: Wide);
            Shot.Take("mac-52-library-unreachable", SkinKind.Mac, t, () =>
            {
                var m = Demo.Semester();
                m.Overview = null;
                m.Empty = "Can't reach your library. Make sure the computer it's on is awake and on the same network.";
                return new MacLibrary { DataContext = m, Width = 1280, Height = 860 };
            }, size: Wide);
            Shot.Take("mac-53-dropdown-recording", SkinKind.Mac, t, () => new MacPanel { DataContext = Demo.Panel(recording: true), VerticalAlignment = VerticalAlignment.Top }, size: new Size(520, 560));
        }
    }
}
