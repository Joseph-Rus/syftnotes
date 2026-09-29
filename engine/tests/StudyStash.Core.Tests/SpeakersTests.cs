using System.Text.Json;

namespace StudyStash.Core.Tests;

/// <summary>Who said each line of a lecture: how voices become "Speaker 2:", and what the transcript looks like.</summary>
public class SpeakersTests
{
    static Spoken Line(double start, double end, string text) => new(start, end, text);

    [Fact]
    public void The_lecturer_is_speaker_1_and_a_student_who_asks_is_another()
    {
        // The diarizer numbered the student first (voice 3) and the lecturer second (voice 0): the numbers say nothing.
        List<Spoken> lines = [Line(0, 100, "Today we cover trees."), Line(100, 108, "Is this on the exam?"), Line(108, 200, "It is, and here is why.")];
        VoiceTurn[] turns = [new(0, 100, 0), new(100, 108, 3), new(108, 200, 0)];

        var labelled = Speakers.Label(lines, turns);

        Assert.Equal([1, 2, 1], labelled.Select(l => l.Speaker));
        // The transcript says it only where the voice changes.
        Assert.Equal("[00:00] Speaker 1: Today we cover trees.\n[01:40] Speaker 2: Is this on the exam?\n[01:48] Speaker 1: It is, and here is why.", TimedText.Format(labelled));
    }

    [Fact]
    public void A_lecturer_who_comes_out_as_two_voices_is_still_one_speaker()
    {
        // Voice 4 is the lecturer near the microphone (60%), voice 0 the lecturer turned away (35%): a student is the 4%.
        List<Spoken> lines = [Line(0, 120, "a"), Line(120, 190, "b"), Line(190, 198, "Question?"), Line(198, 200, "c")];
        var labelled = Speakers.Label(lines, [new(0, 120, 4), new(120, 190, 0), new(190, 198, 7), new(198, 200, 4)]);

        Assert.Equal([1, 1, 2, 1], labelled.Select(l => l.Speaker));
    }

    [Fact]
    public void A_cough_is_not_a_person_and_a_lecture_with_one_voice_has_no_labels()
    {
        // Voice 9 speaks for 3 seconds in all: not a speaker. Its line goes on with the voice before it.
        List<Spoken> lines = [Line(0, 40, "One."), Line(40, 43, "Cough."), Line(43, 90, "Two.")];
        var labelled = Speakers.Label(lines, [new(0, 40, 1), new(40, 43, 9), new(43, 90, 1)]);

        Assert.All(labelled, l => Assert.Equal(0, l.Speaker)); // that left one voice: nothing to tell apart
        Assert.Equal("[00:00] One.\n[00:40] Cough.\n[00:43] Two.", TimedText.Format(labelled));
    }

    [Fact]
    public void A_line_no_voice_covers_takes_the_one_before_it_and_the_first_takes_the_next()
    {
        List<Spoken> lines = [Line(0, 5, "Hm."), Line(10, 110, "A."), Line(111, 113, "quiet"), Line(120, 128, "B?"), Line(130, 230, "C.")];
        var labelled = Speakers.Label(lines, [new(10, 110, 4), new(120, 128, 6), new(130, 230, 4)]);

        Assert.Equal([1, 1, 1, 2, 1], labelled.Select(l => l.Speaker));
    }

    [Fact]
    public void A_line_says_its_speaker_again_after_a_line_that_had_none()
    {
        List<Spoken> lines = [new(0, 5, "a", 1), new(5, 10, "b", 1), new(10, 15, "c"), new(15, 20, "d", 1), new(20, 25, "e", 2)];
        Assert.Equal("[00:00] Speaker 1: a\n[00:05] b\n[00:10] c\n[00:15] Speaker 1: d\n[00:20] Speaker 2: e", TimedText.Format(lines));
    }

    [Fact]
    public void Speakers_are_kept_in_a_lectures_file_only_when_there_are_some()
    {
        string plain = JsonSerializer.Serialize(new Spoken(1, 2, "hi"));
        Assert.DoesNotContain("Speaker", plain);
        var said = JsonSerializer.Serialize(new Spoken(1, 2, "hi", 2));
        Assert.Equal(2, JsonSerializer.Deserialize<Spoken>(said)!.Speaker);
        // A lecture written before speakers existed still opens, with none.
        Assert.Equal(0, JsonSerializer.Deserialize<Spoken>(plain)!.Speaker);
        Assert.True(Speakers.HasLabels("[00:00] Speaker 1: hi\n[00:05] ok"));
        Assert.True(Speakers.HasLabels("ok\nSpeaker 2: hi"));
        Assert.False(Speakers.HasLabels("[00:00] the Speaker 1: hi"));
    }
}
