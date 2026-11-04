using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudyStash.App.Services;
using StudyStash.App.ViewModels.Kit;
using StudyStash.Core;
using StudyStash.Library;

namespace StudyStash.App.ViewModels;

/// <summary>The words of "Add a lecture from a recording", in one place.</summary>
public static class FromFileWords
{
    public const string WindowTitle = "Add a lecture from a recording";
    public const string AddButton = "Add a recording";
    public const string AddTip = "Add a lecture from a recording you already have: a voice memo, or a video of the lecture";
    public const string MenuRecord = "Record now";
    public const string MenuAdd = "Add a recording you already have";
    public const string TitleLabel = "Title";
    public const string ClassLabel = "Class";
    public const string DateLabel = "Date";
    public const string Next = "It's written down on this computer, then your AI writes the notes.";
    public const string SoundOnly = "Only the sound is used";
    public const string Add = "Add lecture";
    public const string Cancel = "Cancel";
    public const string Working = "Getting it ready…";
    public const string DropTitle = "Drop to add it as a lecture";
    public const string CantRead = "Syft Notes can't read that kind of file. A recording (.m4a, .mp3, .wav) or a lecture video (.mp4, .mov) works.";
}

/// <summary>What the form hands the library: the file, the class ("" lets the library sort it), the title, and the day
/// the lecture happened (the file's own day unless the student changed it).</summary>
public sealed record NewFileLecture(string Path, string Class, string Title, DateOnly Day);

/// <summary>A recording, or a video of a lecture, on this computer: what the form shows of it. Only the sound of a
/// video is used.</summary>
public sealed record RecordingFile(string Path, long Bytes, DateTime Date)
{
    static readonly string[] VideoEndings = [".mp4", ".mov", ".m4v"];

    public string Name => System.IO.Path.GetFileName(Path);
    string Ending => System.IO.Path.GetExtension(Path).ToLowerInvariant();
    public bool IsVideo => VideoEndings.Contains(Ending);
    /// <summary>A kind of file Syft Notes can listen to: a voice memo's or a phone's, or a video.</summary>
    public bool CanRead => IsVideo || VoiceMemos.IsAudio(Path);

    /// <summary>The endings the file picker offers: every recording and video.</summary>
    public static IEnumerable<string> Endings => VoiceMemos.Endings.Concat(VideoEndings).Distinct();

    /// <summary>Whether a dropped or picked path is one: the page takes it as a lecture instead of an attachment.</summary>
    public static bool Is(string path) => new RecordingFile(path, 0, default).CanRead;

    /// <summary>The file as it is on disk, or null when it isn't there (or can't be looked at). The date is the earlier
    /// of when it was made and last changed: a copy is made later than the recording it copies.</summary>
    public static RecordingFile? Read(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? new RecordingFile(path, info.Length, info.CreationTime < info.LastWriteTime ? info.CreationTime : info.LastWriteTime) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Program.Log($"[from-file] couldn't look at {System.IO.Path.GetFileName(path)}: {e.GetType().Name}");
            return null;
        }
    }
}

/// <summary>
/// The small form for a lecture that already happened ("Add a lecture from a recording"): the file the student chose or
/// dropped, a title made from its name, the class (the page it was started on, or the library's own sort), and the day.
/// Add hands them to <c>add</c> (the library's way of taking a file as a lecture, wired by the shell) and closes the
/// form once it took it; if it didn't, the form stays and says why in the library's own words.
/// </summary>
public sealed partial class FromFileModel : ObservableObject
{
    readonly Func<NewFileLecture, CancellationToken, Task<Outcome>> add;
    readonly Action close;

    /// <param name="file">The recording or video.</param>
    /// <param name="className">The class of the page the form was started on; "" on Home, where the library sorts it.</param>
    /// <param name="classes">The student's classes, for the class menu: name and highlighter.</param>
    /// <param name="add">Adds the file as a lecture (the library's call). Its failure carries the words the student reads.</param>
    /// <param name="close">Closes the form (after Add worked, or on Cancel).</param>
    public FromFileModel(RecordingFile file, string className, IReadOnlyList<(string Name, int Color)> classes,
        Func<NewFileLecture, CancellationToken, Task<Outcome>> add, Action close, TimeProvider? clock = null)
    {
        File = file;
        Classes = classes;
        this.add = add;
        this.close = close;
        Today = (clock ?? TimeProvider.System).GetLocalNow().Date;
        Title = TitleFrom(file.Name);
        Class = classes.Any(c => c.Name == className) ? className : "";
        Date = file.Date.Date > Today ? Today : file.Date.Date;
        Add = new AsyncAction(AddAsync);
        Add.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AsyncAction.Busy)) OnPropertyChanged(nameof(CanAdd));
            if (e.PropertyName == nameof(AsyncAction.Say)) OnPropertyChanged(nameof(Problem));
        };
    }

    public RecordingFile File { get; }
    public IReadOnlyList<(string Name, int Color)> Classes { get; }
    public DateTime Today { get; }
    public string FileName => File.Name;
    public bool IsVideo => File.IsVideo;

    /// <summary>"48 MB", and for a video that only its sound is used.</summary>
    public string FileDetail => File.IsVideo ? $"{Attachments.SizeLabel(File.Bytes)} · {FromFileWords.SoundOnly}" : Attachments.SizeLabel(File.Bytes);

    /// <summary>The lecture's title: the file's name without its ending, underscores read as spaces.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAdd))]
    public partial string Title { get; set; }

    /// <summary>The class it's filed in; "" lets the library sort it by what's said in it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ClassWords), nameof(ClassDot), nameof(HasClassDot))]
    public partial string Class { get; set; }

    /// <summary>The day it happened.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAdd), nameof(DateWords))]
    public partial DateTime? Date { get; set; }

    /// <summary>"Fri 9 Oct 2026": a day as the lists say it, never "10/9/2026".</summary>
    public string DateWords => Date?.ToString("ddd d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture) ?? "";

    public string ClassWords => Class.Length == 0 ? ClassNames.SortLabel : ClassNames.Display(Class);
    public IBrush? ClassDot => Classes.FirstOrDefault(c => c.Name == Class) is { Name.Length: > 0 } c ? Skin.ClassDot(c.Color) : null;
    public bool HasClassDot => ClassDot is not null;

    /// <summary>"Add lecture": the file goes to the library.</summary>
    public AsyncAction Add { get; }

    bool Ready => File.CanRead && Title.Trim().Length > 0 && Date is not null;
    public bool CanAdd => Ready && !Add.Busy;

    /// <summary>Why it can't be added (a kind of file that can't be read) or why it didn't work; null otherwise.</summary>
    public string? Problem => File.CanRead ? Add.Say : FromFileWords.CantRead;

    [RelayCommand]
    void Cancel() => close();

    async Task<Outcome> AddAsync(CancellationToken ct)
    {
        // Return in the title field asks too, whether or not the button is lit.
        if (!Ready) return Outcome.Done;
        var done = await add(new NewFileLecture(File.Path, Class, Title.Trim(), DateOnly.FromDateTime(Date!.Value)), ct);
        if (done.Ok) close();
        return done;
    }

    /// <summary>A title from a file's name: "Lecture_5_recursion.m4a" is "Lecture 5 recursion".</summary>
    public static string TitleFrom(string fileName) =>
        string.Join(' ', Path.GetFileNameWithoutExtension(fileName).Replace('_', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries));
}
