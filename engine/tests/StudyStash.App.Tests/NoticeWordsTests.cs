using StudyStash.App.Services;
using StudyStash.App.ViewModels;
using StudyStash.Audio;
using StudyStash.Core;

namespace StudyStash.App.Tests;

/// <summary>What the app's own notifications say (<see cref="NoticeWords"/>): the right thing, in plain words, short
/// enough to read, with the button that does something about it.</summary>
public class NoticeWordsTests
{
    [Fact]
    public void A_filed_lecture_names_its_class_and_title_and_opens_its_note()
    {
        Assert.Equal(("Filed in CS 101", "Recursion and the call stack", "Open note"), NoticeWords.Filed("CS 101", "Recursion and the call stack", ""));
        Assert.Equal(("Filed in your library", "Its notes are written.", "Open note"), NoticeWords.Filed("", "", ""));
    }

    [Fact]
    public void A_lecture_filed_without_notes_says_so_instead_of_saying_they_are_written()
    {
        var (title, text, action) = NoticeWords.Filed("BIO 110", "Cell membranes", "The library couldn't write notes for it");
        Assert.Equal("Filed in BIO 110", title);
        Assert.Equal("Cell membranes: its notes couldn't be written. The transcript is there.", text);
        Assert.Equal("Open", action);
        Assert.DoesNotContain("written.", NoticeWords.Filed("BIO 110", "", "failed").Text.Replace("couldn't be written", ""));
    }

    [Fact]
    public void A_paused_lecture_offers_resume_and_doesnt_say_paused_twice()
    {
        Assert.Equal(("The microphone stopped. Plug it back in, then press Resume.", "Resume", false), NoticeWords.Paused(RecordingWords.MicStopped, windows: false));
        Assert.Equal(("Your disk is full. Free some space, then press Resume.", "Resume", false), NoticeWords.Paused(RecordingWords.DiskFull, windows: true));
        var (text, action, settings) = NoticeWords.Paused(RecordingWords.CouldntSave("the disk went away."), windows: false);
        Assert.Equal("The recording couldn't be saved (the disk went away).", text);
        Assert.Equal("Resume", action);
        Assert.False(settings);
        Assert.Equal(NoticeWords.PausedTitle, AppHost.RecordingPaused);
    }

    [Fact]
    public void A_paused_lecture_the_system_wont_let_study_stash_hear_opens_the_privacy_settings()
    {
        Assert.Equal((RecordingWords.CantHearMac, "Open System Settings", true), NoticeWords.Paused(RecordingWords.CantHearMac, windows: false));
        Assert.Equal((RecordingWords.CantHearWindows, "Open Settings", true), NoticeWords.Paused(RecordingWords.CantHearWindows, windows: true));
    }

    [Fact]
    public void A_saved_recording_says_where_it_goes_from_here()
    {
        Assert.Equal("Study Stash is writing it down; the library files it and writes your notes.", NoticeWords.Saved(LibraryState.Connected, mac: true));
        Assert.Equal("Study Stash is writing it down. It waits on this Mac and goes to your library when it's back.", NoticeWords.Saved(LibraryState.Unreachable, mac: true));
        Assert.Contains("this PC", NoticeWords.Saved(LibraryState.Unreachable, mac: false));
        Assert.Contains("new password in Settings", NoticeWords.Saved(LibraryState.WrongPassword, mac: true));
        Assert.Contains("Connect to your library in Settings", NoticeWords.Saved(LibraryState.NotSetUp, mac: false));
    }

    [Fact]
    public void Record_before_the_model_is_here_offers_the_download_or_says_how_far_it_is()
    {
        Assert.Equal(("Download the transcription model", "Record works once it's here.", "Download"), NoticeWords.NoModelYet(null));
        var (title, text, action) = NoticeWords.NoModelYet(new DownloadProgress(200_000_000, 574_041_195, 0));
        Assert.Equal("Downloading the transcription model", title);
        Assert.Equal("200 MB of 574 MB. Record works once it's here.", text);
        Assert.Null(action);
    }

    [Fact]
    public void A_lighter_model_is_suggested_in_a_title_that_fits()
    {
        var (title, text) = NoticeWords.LighterModel(new ModelAdvice(WhisperModels.LargeV3TurboSmall,
            "This PC has no graphics card Whisper can use, so the compact model keeps up with a lecture."));
        Assert.Equal("A lighter model would keep up better", title);
        Assert.True(title.Length <= 40);
        Assert.EndsWith("Switch in Settings → Recording.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_newer_version_starting_says_it_updated_once_and_nothing_else_does()
    {
        Assert.Equal(("Study Stash updated to 0.10.1", "Everything is just as you left it.", "What's new"),
            NoticeWords.Updated("0.10.0", "0.10.1"));
        Assert.Equal("Study Stash updated to 1.0.0", NoticeWords.Updated("0.9.1", "v1.0.0")!.Value.Title);
        Assert.Null(NoticeWords.Updated("", "0.9.2")); // the very first start
        Assert.Null(NoticeWords.Updated("0.9.2", "0.9.2"));
        Assert.Null(NoticeWords.Updated("0.10.0", "0.9.2")); // an older copy
    }

    /// <summary>Quitting for an update that then didn't take (Windows' Setup.exe stopped short, and the old copy opened
    /// again) says so on the next start, with the download; one that took, or none, says nothing of the kind.</summary>
    [Fact]
    public void An_update_that_didnt_take_is_said_on_the_next_start()
    {
        Assert.Equal(("Study Stash couldn't update", "You're still on 0.10.0. It tries again later, or you can download 0.10.1 now.", "Download"),
            NoticeWords.UpdateDidntTake("0.10.1", "0.10.0"));
        Assert.Null(NoticeWords.UpdateDidntTake("0.10.1", "0.10.1"));
        Assert.Null(NoticeWords.UpdateDidntTake("", "0.10.0"));
        Assert.Equal("https://github.com/Joseph-Rus/study-stash/releases/tag/v0.10.1", Updates.ReleasePage("0.10.1"));
    }

    [Theory]
    [InlineData(ProblemKind.WhisperFailed, true)]
    [InlineData(ProblemKind.DownloadFailed, true)]
    [InlineData(ProblemKind.LibraryStopped, true)]
    [InlineData(ProblemKind.WrongPassword, true)]
    [InlineData(ProblemKind.Unreachable, false)]
    [InlineData(ProblemKind.NotSetUp, false)]
    [InlineData(ProblemKind.NoModel, false)]
    [InlineData(ProblemKind.Downloading, false)]
    [InlineData(ProblemKind.DiskFull, false)]
    [InlineData(ProblemKind.MicDenied, false)]
    [InlineData(ProblemKind.NoMic, false)]
    public void Only_problems_that_stop_lectures_until_you_act_are_notified(ProblemKind kind, bool notified) =>
        Assert.Equal(notified, NoticeWords.WorthNotifying(kind));

    /// <summary>A notification shows a title on one line and four lines under it: the app's own words fit, so nothing
    /// that says what to do is cut off.</summary>
    [Fact]
    public void The_apps_own_words_fit_the_notification()
    {
        var texts = new List<string>
        {
            NoticeWords.Paused(RecordingWords.CantHearWindows, windows: true).Text,
            NoticeWords.Saved(LibraryState.Unreachable, mac: true),
            AppHost.BehindWords(900, 600, WhisperModels.LargeV3, WhisperModels.Advise(FakeHardware.PlainPc().Probe()))!.Value.Text,
            NoticeWords.LighterModel(WhisperModels.Advise(FakeHardware.PlainPc().Probe())).Text,
            IconWords.WhereItIs(mac: false, hidden: false).Text,
            IconWords.WhereItIs(mac: true, hidden: true).Text,
            NoticeWords.UpdateReady("10.10.10").Text,
            NoticeWords.UpdateBlocked("10.10.10", "Move Study Stash into Applications, then open it again, so it can update itself.").Text,
            NoticeWords.UpdateFailed("10.10.10", "10.10.9").Text,
        };
        // About 46 characters a line at the notification's width: four lines.
        foreach (var t in texts) Assert.True(t.Length <= 4 * 46, $"{t.Length} characters: {t}");
    }
}
