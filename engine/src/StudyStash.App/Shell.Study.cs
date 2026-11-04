using StudyStash.App.Services;
using StudyStash.App.ViewModels;
using StudyStash.Core.Study;

namespace StudyStash.App;

/// <summary>The study tools in the library window: the Study page from the sidebar (Review and exam prep), and a
/// lecture's own tools under its exam flags (its cards in Review, and Listen).</summary>
public static partial class Shell
{
    /// <summary>The connected library's study tools.</summary>
    static IStudyLibrary Study()
    {
        var cc = host.Client();
        return new StudyRemote(cc.ServerUrl, cc.PoolKey);
    }

    /// <summary>The Study page, in the pane: what's due in Review, by class.</summary>
    static async Task ShowStudyAsync(string? reviewLecture = null)
    {
        pageState = pageState.Next();
        SelectInSidebar(null);
        library.StudySelected = true;
        if (host.Remote() is null) return;
        var study = new StudyModel(Study())
        {
            OnOpenLecture = (id, at) =>
            {
                Remember();
                if (at is double t) OpenAt(id, t);
                else OpenLecture(id);
            },
            OnCount = due => library.StudyDue = due,
        };
        study.Classes(library.Classes.Where(c => !c.IsDue && !c.IsHome && c.Name != Core.Configs.Unsorted).Select(c => c.Name));
        ClearPane();
        library.Overview = null;
        library.Study = study;
        await study.LoadAsync();
        if (reviewLecture is not null && ReferenceEquals(library.Study, study)) await study.ReviewAsync(null, reviewLecture);
    }

    /// <summary>The sidebar's Study count: how many cards are due now.</summary>
    static async Task CountStudyAsync()
    {
        if (host.Remote() is null) return;
        try
        {
            if (await Study().ReviewAsync(limit: 1) is { } state) library.StudyDue = state.Counts.Due;
        }
        catch (Exception e) when (LibraryCall.Handled(e, "[study] CountStudyAsync"))
        {
        }
    }

    /// <summary>A lecture's study tools: its cards in Review (a lecture being recorded, or not in the library yet, has none).</summary>
    static void ShowLectureStudy(NoteModel? note)
    {
        if (note is null || note.Live || note.Local || host.Remote() is null)
        {
            library.LectureStudy = null;
            return;
        }
        Run(() => NameFlagsAsync(note), "NameFlags");
        if (library.LectureStudy is { } kept && kept.LectureId == note.Id) return;
        var study = new LectureStudyModel(Study(), note.Id, note.Noun)
        {
            OnMade = () => Run(() => CountStudyAsync(), "CountStudy"),
            OnReview = id =>
            {
                Remember();
                Run(() => ShowStudyAsync(reviewLecture: id), "ShowStudy");
            },
        };
        study.Listen = new ListenModel(Study(), note.Id);
        library.LectureStudy = study;
        Run(() => study.LoadAsync(), "study.Load");
        Run(() => study.Listen.LoadAsync(), "study.Listen.Load");
    }

    static readonly Dictionary<string, List<KeyIdea>> namedFlags = [];

    /// <summary>What the lecture's flags were about, from the library's AI: until it answers (or with no AI to ask)
    /// the card shows the sentences they were said in.</summary>
    static async Task NameFlagsAsync(NoteModel note)
    {
        if (!note.HasExamFlags) return;
        // Named already this run: straight away, so the page coming back doesn't show the sentences first.
        if (namedFlags.TryGetValue(note.Id, out var had)) note.NameFlags(had);
        try
        {
            if (await Study().ImportantAsync(note.Id) is { } named)
            {
                namedFlags[note.Id] = named;
                if (ReferenceEquals(library.Note, note)) note.NameFlags(named);
            }
        }
        catch (Exception e) when (LibraryCall.Handled(e, "[study] NameFlagsAsync"))
        {
        }
    }
}
