using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudyStash.App.Services;

namespace StudyStash.App.ViewModels;

/// <summary>One Canvas course in the picker: its name as a class ("Bridge Design"), its short code and term under it
/// ("MECH 4120 · Fall 2026"), and its tick. An unticked course that the picker wouldn't tick for a student choosing
/// afresh says why on the right ("Past term", "Not a class").</summary>
public sealed partial class CoursePick : ObservableObject
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    /// <summary>"MECH 4120 · Fall 2026": the short code and the term, whichever Canvas gave.</summary>
    public string Detail { get; init; } = "";
    public bool HasDetail => Detail.Length > 0;
    /// <summary>Why it starts unticked ("Past term", "No term", "Not a class"); "" for this term's courses.</summary>
    public string Why { get; init; } = "";
    /// <summary>It's brought in now: its tick as the library has it.</summary>
    public bool Was { get; init; }
    /// <summary>The class it's linked to now; "" when none.</summary>
    public string Class { get; init; } = "";

    [NotifyPropertyChangedFor(nameof(Right))]
    [ObservableProperty]
    public partial bool Ticked { get; set; }

    /// <summary>The right column: why it's left out, while it is.</summary>
    public string Right => Ticked ? "" : Why;

    public Action? OnTicked { get; set; }
    partial void OnTickedChanged(bool value) => OnTicked?.Invoke();
}

/// <summary>
/// Which Canvas courses to bring in (setup's Canvas step, the connect window, and Settings → Canvas): every course
/// Find my courses found, ticked as the library has them, or — choosing for the first time — this term's courses
/// ticked and the rest (past terms, sandboxes, chapel, courses in no term) not. Only ticked courses become classes and
/// sync. In Settings a change waits for Save; one that drops courses asks first whether to keep their classes and
/// files (<see cref="KeepCommand"/>) or remove them (<see cref="RemoveCommand"/>).
/// </summary>
public sealed partial class CoursePickerModel(CanvasContext context) : ObservableObject
{
    public ObservableCollection<CoursePick> Courses { get; } = [];
    public bool HasCourses => Courses.Count > 0;

    /// <summary>The library has never been told which courses to bring in (and links none): the ticks are the
    /// picker's suggestions.</summary>
    public bool Fresh { get; private set; }

    /// <summary>Settings: a change waits for Save (or Keep / Remove when it drops courses). Setup and the connect
    /// window save the ticks themselves when the student moves on.</summary>
    public bool SavesItself { get; init; }
    /// <summary>The Save bar shows: in Settings, while the ticks differ from the library's.</summary>
    public bool ShowSaveBar => SavesItself && HasChanges;
    public bool HasSay => !string.IsNullOrEmpty(Say);
    partial void OnSayChanged(string? value) => OnPropertyChanged(nameof(HasSay));

    [ObservableProperty] public partial bool Saving { get; set; }
    [ObservableProperty] public partial string? Say { get; set; }

    /// <summary>After a save went through: the host reloads (Settings' rows, the library's classes).</summary>
    public Func<Task>? OnSaved { get; set; }
    /// <summary>A tick changed.</summary>
    public Action? OnChanged { get; set; }

    public IEnumerable<CoursePick> Ticked => Courses.Where(c => c.Ticked);
    public int TickedCount => Courses.Count(c => c.Ticked);
    int Adding => Courses.Count(c => c.Ticked && !c.Was);
    int Dropping => Courses.Count(c => !c.Ticked && c.Was);

    /// <summary>The ticks differ from what the library has.</summary>
    public bool HasChanges => Courses.Any(c => c.Ticked != c.Was);
    /// <summary>The change drops courses: Save asks whether to keep their classes and files.</summary>
    public bool Drops => Dropping > 0;
    public bool OnlyAdds => HasChanges && !Drops;

    /// <summary>"Bring in 2 courses." / "Stop syncing 1 course. Keep its class and files?"</summary>
    public string ChangeLine
    {
        get
        {
            int add = Adding, drop = Dropping;
            string Courses(int n) => n == 1 ? "1 course" : $"{n} courses";
            string bring = add > 0 ? $"Bring in {Courses(add)}. " : "";
            if (drop == 0) return bring.TrimEnd();
            return bring + $"Stop syncing {Courses(drop)}. " + (drop == 1 ? "Keep its class and files?" : "Keep their classes and files?");
        }
    }

    /// <summary>"12 courses on Canvas · 5 brought in".</summary>
    public string Summary => Courses.Count == 0 ? "" : $"{(Courses.Count == 1 ? "1 course" : $"{Courses.Count} courses")} on Canvas · {TickedCount} brought in";

    /// <summary>Fills the list from the library's answer: every found course by name, ticked as the library has
    /// it, or by suggestion when nothing has been chosen yet. Suggested courses first, then the rest, each by name.</summary>
    public void Fill(CanvasApi.Overview o)
    {
        var info = o.CourseInfo.Where(c => c.Id.Length > 0).GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First());
        var linked = o.Courses.Where(kv => kv.Value > 0).Select(kv => ((long)kv.Value).ToString(System.Globalization.CultureInfo.InvariantCulture)).ToHashSet();
        // A library older than the choice says nothing of it: what's linked is what's in.
        bool Chosen(string id) => info.TryGetValue(id, out var c) && c.Chosen is { } chosen ? chosen : o.Chosen?.Contains(id) ?? linked.Contains(id);
        var found = CanvasConnectModel.FoundFrom(o);
        Fresh = o.Chosen is null && !found.Any(f => Chosen(f.Id));
        Courses.Clear();
        foreach (var f in found.OrderByDescending(f => info.GetValueOrDefault(f.Id)?.Suggested ?? true))
        {
            var c = info.GetValueOrDefault(f.Id);
            bool was = Chosen(f.Id);
            var pick = new CoursePick
            {
                Id = f.Id, Title = f.ClassName, Detail = string.Join(" · ", new[] { f.Code, c?.Term ?? "" }.Where(p => p.Length > 0)),
                Why = c?.Suggested == false ? c.Why : "", Was = was, Class = c?.Class ?? "",
                Ticked = Fresh ? c?.Suggested ?? true : was,
            };
            pick.OnTicked = Changed;
            Courses.Add(pick);
        }
        Changed();
    }

    void Changed()
    {
        OnPropertyChanged(nameof(HasCourses));
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(ShowSaveBar));
        OnPropertyChanged(nameof(Drops));
        OnPropertyChanged(nameof(OnlyAdds));
        OnPropertyChanged(nameof(ChangeLine));
        OnPropertyChanged(nameof(TickedCount));
        OnPropertyChanged(nameof(Summary));
        OnChanged?.Invoke();
    }

    /// <summary>Back to what the library has.</summary>
    [RelayCommand]
    void Cancel()
    {
        foreach (var c in Courses) c.Ticked = c.Was;
        Say = null;
    }

    /// <summary>Save a change that only brings courses in.</summary>
    [RelayCommand]
    Task Save() => ChooseAsync(keep: true);

    /// <summary>Save, keeping the dropped courses' classes and files (they just stop syncing).</summary>
    [RelayCommand]
    Task Keep() => ChooseAsync(keep: true);

    /// <summary>Save, removing the dropped courses' Canvas files, and their classes when they hold no lectures.</summary>
    [RelayCommand]
    Task Remove() => ChooseAsync(keep: false);

    /// <summary>Tells the library the ticked courses. <paramref name="match"/>: a new course may take a class of the
    /// library's own that seems to be it (the connect window, for a library that had classes before Canvas).</summary>
    public async Task<bool> ChooseAsync(bool keep, bool match = false)
    {
        if (context.Client is not { } client) return false;
        Saving = true;
        Say = null;
        try
        {
            var answer = await client.ChooseAsync([.. Ticked.Select(c => c.Id)], keep, match);
            if (answer is null)
            {
                Say = "Your library runs an older Study Stash: update it to choose courses.";
                return false;
            }
            Fill(answer);
            Say = CanvasWords.ChoiceSaid(answer.Outcome);
            if (OnSaved is { } saved) await saved();
            return true;
        }
        catch (CanvasLibraryException e)
        {
            Say = e.Message;
            return false;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            Say = "Your library didn't answer.";
            return false;
        }
        finally
        {
            Saving = false;
        }
    }
}
