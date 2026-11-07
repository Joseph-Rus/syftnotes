using Microsoft.Extensions.Time.Testing;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;

namespace StudyStash.App.Tests;

/// <summary>The form for a lecture that already happened: what it fills in from the file, what Add hands the library, and
/// that a refusal keeps the form open with the library's words.</summary>
public class FromFileModelTests
{
    static readonly (string Name, int Color)[] Classes = [("CS 101", 1), ("BIO 110", 2)];

    static FromFileModel Form(string file, string cls, Func<NewFileLecture, CancellationToken, Task<Outcome>> add, Action? close = null)
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 12, 13, 0, 0, TimeSpan.Zero));
        clock.SetLocalTimeZone(TimeZoneInfo.Utc);
        return new FromFileModel(new RecordingFile(file, 3_000_000, new DateTime(2026, 10, 9, 9, 31, 0)), cls, Classes, add, close ?? (() => { }), clock);
    }

    [Fact]
    public async Task Add_hands_the_library_what_the_form_shows_and_closes_once_it_took_it()
    {
        NewFileLecture? got = null;
        bool closed = false;
        var form = Form("Lecture_6_gradient_descent.m4a", "CS 101", (lecture, _) =>
        {
            got = lecture;
            return Task.FromResult(Outcome.Done);
        }, () => closed = true);

        // Filled in from the file and the page it was started on.
        Assert.Equal("Lecture 6 gradient descent", form.Title);
        Assert.Equal("CS 101", form.Class);
        Assert.Equal(new DateTime(2026, 10, 9), form.Date);
        Assert.False(form.IsVideo);

        form.Date = new DateTime(2026, 10, 8);
        await form.Add.Command.ExecuteAsync(null);

        Assert.Equal(new NewFileLecture("Lecture_6_gradient_descent.m4a", "CS 101", "Lecture 6 gradient descent", new DateOnly(2026, 10, 8)), got);
        Assert.True(closed);
    }

    /// <summary>Cancel while a long video is still being got ready: the add is told to stop (so no lecture is added and
    /// nothing is left of it), the form closes, and it ends quietly: nothing said, nothing thrown.</summary>
    [Fact]
    public async Task Cancel_while_it_gets_ready_stops_the_add_and_closes_quietly()
    {
        bool closed = false;
        var started = new TaskCompletionSource();
        var form = Form("Zoom Recording - Week 6.mp4", "", async (_, ct) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, ct); // until it's cancelled
            return Outcome.Done;
        }, () => closed = true);

        var run = form.Add.Command.ExecuteAsync(null);
        await started.Task;
        Assert.True(form.Add.Busy);
        form.CancelCommand.Execute(null);
        await run;

        Assert.True(closed);
        Assert.False(form.Add.Busy);
        Assert.Null(form.Problem);
    }

    [Fact]
    public async Task A_refusal_stays_in_the_form_in_the_librarys_words_and_a_file_it_cant_read_cant_be_added()
    {
        bool closed = false;
        var form = Form("Zoom Recording.mp4", "", (_, _) => Task.FromResult(Outcome.Failed(new Failure(FailureKind.Refused, "Syft Notes couldn't read that recording."))),
            () => closed = true);
        Assert.True(form.IsVideo);
        Assert.Equal("", form.Class); // Home: the library sorts it
        Assert.Contains("Only the sound is used", form.FileDetail);

        await form.Add.Command.ExecuteAsync(null);
        Assert.Equal("Syft Notes couldn't read that recording.", form.Problem);
        Assert.False(closed);
        Assert.True(form.CanAdd); // and can be tried again

        var pdf = Form("Syllabus.pdf", "CS 101", (_, _) => Task.FromResult(Outcome.Done));
        Assert.Equal(FromFileWords.CantRead, pdf.Problem);
        Assert.False(pdf.CanAdd);
    }
}
