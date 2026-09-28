using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudyStash.Core;
using StudyStash.Core.Ai;
using StudyStash.Core.Rich;

namespace StudyStash.App.ViewModels;

public enum QuickKind
{
    Header,
    Lecture,
    Passage,
    Class,
    Action,
    Source,
}

/// <summary>A row of the quick panel: a group's header, a lecture, a passage from notes, a class, an action, or (for
/// an answer) a source.</summary>
public sealed partial class QuickRow : ObservableObject
{
    public QuickKind Kind { get; init; }
    public string Title { get; init; } = "";
    /// <summary>Right-hand text: "CS 101 · Tue 23 Sep", "12 lectures", "⌥⇧R", "18:05".</summary>
    public string Meta { get; init; } = "";
    /// <summary>A passage's line under it: "Recursion and the call stack · Key points".</summary>
    public string Sub { get; init; } = "";
    /// <summary>The right-hand text reads in the accent, semibold: something missing on Canvas.</summary>
    public bool Strong { get; init; }
    public IBrush Dot { get; init; } = Brushes.Gray;
    public string Glyph { get; init; } = "";
    public string? LectureId { get; init; }
    public double? At { get; init; }
    public string? ClassName { get; init; }
    public Action? Run { get; init; }
    [ObservableProperty] public partial bool Selected { get; set; }

    public bool IsHeader => Kind == QuickKind.Header;
    public bool IsLecture => Kind == QuickKind.Lecture;
    public bool IsPassage => Kind == QuickKind.Passage;
    public bool IsClass => Kind == QuickKind.Class;
    public bool IsAction => Kind == QuickKind.Action;
    public bool IsSource => Kind == QuickKind.Source;
    public bool Selectable => Kind != QuickKind.Header;
    public bool First { get; init; }
}

/// <summary>
/// The quick panel (⌥Space, or Alt+Shift+Space on Windows): search as you type, results grouped as Lectures,
/// Passages from notes, Classes and Actions; ⌘Return (Ctrl+Enter) asks your notes instead, and the answer comes
/// with its sources.
/// </summary>
public sealed partial class QuickModel : ObservableObject
{
    [ObservableProperty] public partial string Query { get; set; } = "";
    [ObservableProperty] public partial bool Answering { get; set; }
    [ObservableProperty] public partial string Answer { get; set; } = "";
    [ObservableProperty] public partial bool Thinking { get; set; }
    /// <summary>Nothing matched, or the library can't be reached: said in place of results.</summary>
    [ObservableProperty] public partial string? Note { get; set; }

    public ObservableCollection<QuickRow> Rows { get; } = [];

    public bool HasNote => !string.IsNullOrEmpty(Note);
    public bool Searching => !Answering;
    /// <summary>The Return key's sign, asked for as text: on its own a Mac draws ↩ as a blue emoji keycap.</summary>
    public const string Return = "\u21A9\uFE0E";

    public string AskHint => Skin.Current == SkinKind.Mac ? $"⌘{Return} Ask your notes" : "Ctrl+Enter to ask";
    public string AskKey => Skin.Current == SkinKind.Mac ? $"⌘{Return}" : "Ctrl+Enter";
    public string OpenKey => Skin.Current == SkinKind.Mac ? Return : "Enter";
    public string CopyKey => Skin.Current == SkinKind.Mac ? "⌘C" : "Ctrl+C";
    public string CloseKey => Skin.Current == SkinKind.Mac ? "esc" : "Esc";

    partial void OnNoteChanged(string? value) => OnPropertyChanged(nameof(HasNote));
    partial void OnAnsweringChanged(bool value) => OnPropertyChanged(nameof(Searching));

    public QuickRow? Selected => Rows.FirstOrDefault(r => r.Selected);

    /// <summary>Move the selection up or down among the rows that can be chosen.</summary>
    public void Move(int by)
    {
        var choosable = Rows.Where(r => r.Selectable).ToList();
        if (choosable.Count == 0) return;
        int at = choosable.FindIndex(r => r.Selected);
        int next = at < 0 ? 0 : Math.Clamp(at + by, 0, choosable.Count - 1);
        foreach (var r in choosable) r.Selected = false;
        choosable[next].Selected = true;
        OnPropertyChanged(nameof(Selected));
    }

    public void SelectFirst()
    {
        foreach (var r in Rows) r.Selected = false;
        if (Rows.FirstOrDefault(r => r.Selectable) is { } first) first.Selected = true;
        OnPropertyChanged(nameof(Selected));
    }

    public Action<string>? OnQuery { get; set; }
    public Func<string, Task>? OnAsk { get; set; }
    public Action<QuickRow>? OnOpen { get; set; }
    public Action? OnClose { get; set; }

    partial void OnQueryChanged(string value)
    {
        answering?.Cancel(); // back to searching: the answer being written is no longer wanted
        if (Answering) Answering = false;
        OnQuery?.Invoke(value);
    }

    CancellationTokenSource? answering;

    /// <summary>
    /// Asks the library's AI and shows the answer as it's written: the spinner until its first words, then the
    /// answer growing at <see cref="Paced"/>'s pace, then its sources as rows to open. False when the library is too
    /// old to answer this way (the caller asks it the old way). A newer question, or going back to searching, stops
    /// this one.
    /// </summary>
    public async Task<bool> AnswerAsync(IAiLibrary ai, string question)
    {
        answering?.Cancel();
        using var stop = answering = new CancellationTokenSource();
        Answering = true;
        Thinking = true;
        Answer = "";
        Rows.Clear();
        Note = null;
        var paced = new Paced(text =>
        {
            Thinking = false;
            Answer = PartialText.Showable(text);
        });
        string? Partial() => paced.End() is { } text ? PartialText.Ended(text) : null;
        try
        {
            var reply = await ai.AskAsync(new AskRequest(question), paced.Show, stop.Token);
            paced.End();
            if (reply is null) return false;
            Thinking = false;
            Answer = reply.Answer;
            var sources = reply.Sources.Where(s => s.Id is not null).ToList();
            if (sources.Count > 0)
            {
                Rows.Add(new QuickRow { Kind = QuickKind.Header, Title = "Sources", First = true });
                foreach (var s in sources)
                    Rows.Add(new QuickRow
                    {
                        Kind = QuickKind.Source, Title = s.Title, Meta = s.At is double at ? TimedText.Clock(at) : s.Section, LectureId = s.Id, At = s.At,
                    });
                SelectFirst();
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            paced.End(); // what stopped it (a newer question, a new search) has the panel now
        }
        catch (Exception e) when (e is LibraryRefusedException or HttpRequestException or IOException or OperationCanceledException or System.Text.Json.JsonException)
        {
            string why = e is LibraryRefusedException refused ? refused.Message : "Your library didn't answer. Is it on?";
            Thinking = false;
            if (Partial() is { Length: > 0 } partial)
            {
                Answer = partial;
                Note = e is LibraryRefusedException ? why : "Your library stopped answering partway through. Is it on?";
            }
            else Answer = why;
        }
        finally
        {
            if (answering == stop) answering = null;
        }
        return true;
    }

    [RelayCommand]
    async Task Ask()
    {
        if (Query.Trim().Length == 0 || OnAsk is null) return;
        await OnAsk(Query.Trim());
    }

    [RelayCommand]
    void Open(QuickRow? row)
    {
        row ??= Selected;
        if (row is null) return;
        if (row.Run is not null) row.Run();
        else OnOpen?.Invoke(row);
    }

    [RelayCommand] void Close() => OnClose?.Invoke();
}
