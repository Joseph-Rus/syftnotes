using System.Globalization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudyStash.App.Services;
using StudyStash.App.ViewModels.Kit;
using StudyStash.Core;

namespace StudyStash.App.ViewModels;

/// <summary>The words of adding a lecture a friend shared (a .syft file), in one place.</summary>
public static class SharedWords
{
    public const string WindowTitle = "Add a lecture a friend shared";
    public static string Question(string title) => $"Add “{title}” to which class?";
    public const string ClassLabel = "Class";
    public const string Next = "Its notes and transcript come in as they are. You can ask about it, or have your AI rewrite the notes.";
    public const string Add = "Add lecture";
    public const string Cancel = "Cancel";
    public const string Working = "Adding it…";
    public const string CantOpen = "Couldn't open that file";
    public const string CantRead = "Syft Notes couldn't read that file. Check that it's still there, then open it again.";
    public static string Added(string title) => $"Added “{title}”";
    public static string AddedTo(string className) => $"In {ClassNames.Display(className)}";
    public const string Open = "Open";
}

/// <summary>
/// The one question before a lecture a friend shared comes in: which class. It starts on a class of the name it had
/// there, else the page it was opened on, else the library's own sort. Add hands the class to <c>add</c> (the library's
/// call, wired by the shell) and closes once it took it; if it didn't (it's there already), the words stay here.
/// </summary>
public sealed partial class SharedLectureModel : ObservableObject
{
    readonly Func<string, CancellationToken, Task<Outcome>> add;
    readonly Action close;

    /// <param name="lecture">The lecture, as the file holds it.</param>
    /// <param name="pageClass">The class of the page it was dropped or picked on; "" on Home or when it was opened.</param>
    /// <param name="classes">The student's classes, for the class menu: name and highlighter.</param>
    /// <param name="add">Adds it to the class it's given ("" lets the library sort it); its failure carries the words.</param>
    /// <param name="close">Closes the window (after Add worked, or on Cancel).</param>
    public SharedLectureModel(SharedLecture lecture, string pageClass, IReadOnlyList<(string Name, int Color)> classes,
        Func<string, CancellationToken, Task<Outcome>> add, Action close)
    {
        Lecture = lecture;
        Classes = classes;
        this.add = add;
        this.close = close;
        Class = classes.FirstOrDefault(c => c.Name.Equals(lecture.ClassName, StringComparison.OrdinalIgnoreCase)).Name
            ?? (classes.Any(c => c.Name == pageClass) ? pageClass : "");
        Add = new AsyncAction(AddAsync);
        Add.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AsyncAction.Busy)) OnPropertyChanged(nameof(CanAdd));
        };
    }

    public SharedLecture Lecture { get; }
    public IReadOnlyList<(string Name, int Color)> Classes { get; }
    public string Question => SharedWords.Question(Lecture.Title);

    /// <summary>"Cell Biology · Tue 3 Nov 2026": the class it had there and its day.</summary>
    public string Detail => string.Join(" · ", new[]
    {
        ClassNames.Display(Lecture.ClassName),
        DateTimeOffset.TryParse(Lecture.Date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d.ToString("ddd d MMM yyyy", CultureInfo.InvariantCulture) : "",
    }.Where(s => s.Length > 0));

    /// <summary>The class it goes in; "" lets the library sort it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ClassWords), nameof(ClassDot), nameof(HasClassDot))]
    public partial string Class { get; set; }

    public string ClassWords => Class.Length == 0 ? ClassNames.SortLabel : ClassNames.Display(Class);
    public IBrush? ClassDot => Classes.FirstOrDefault(c => c.Name == Class) is { Name.Length: > 0 } c ? Skin.ClassDot(c.Color) : null;
    public bool HasClassDot => ClassDot is not null;

    public AsyncAction Add { get; }
    public bool CanAdd => !Add.Busy;

    [RelayCommand]
    void Cancel()
    {
        Add.Cancel();
        close();
    }

    async Task<Outcome> AddAsync(CancellationToken ct)
    {
        var done = await add(Class, ct);
        if (done.Ok) close();
        return done;
    }
}
