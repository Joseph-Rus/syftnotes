using StudyStash.App.ViewModels;
using StudyStash.Core.Study;

namespace StudyStash.App;

/// <summary>The study tools in the library window: the Study page from the sidebar (Review and exam prep), and a
/// lecture's own tools under its exam flags (its cards in Review).</summary>
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
        ++pageTurn;
        LeavePage();
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
        catch
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
        if (library.LectureStudy is { } kept && kept.LectureId == note.Id) return;
        var study = new LectureStudyModel(Study(), note.Id, note.Noun)
        {
            OnMade = () => _ = CountStudyAsync(),
            OnReview = id =>
            {
                Remember();
                _ = ShowStudyAsync(reviewLecture: id);
            },
        };
        library.LectureStudy = study;
        _ = study.LoadAsync();
    }
}
