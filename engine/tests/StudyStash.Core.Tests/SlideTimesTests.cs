using StudyStash.Core;

namespace StudyStash.Core.Tests;

public class SlideTimesTests
{
    const string Course = "Marine Biology 210";
    const string Reefs = "Coral reefs: polyps, zooxanthellae, bleaching";
    const string Kelp = "Kelp forests: holdfast, stipe, urchins, otters";
    const string Tides = "Tides: lunar gravity, spring and neap";
    const string Plankton = "Plankton: diatoms, copepods, krill";

    /// <summary>Three minutes on a topic from <paramref name="start"/> (seconds), a line every ten seconds, two of its
    /// words a line and the course's name every third line.</summary>
    static IEnumerable<(int, string)> Taught(int start, string topic, int seconds = 180)
    {
        var words = topic.Split([' ', ':', ','], StringSplitOptions.RemoveEmptyEntries).Where(w => w != "and").ToArray();
        for (int i = 0; i < seconds / 10; i++)
            yield return (start + i * 10, $"{(i % 3 == 0 ? Course + " here, " : "")}{words[2 * i % words.Length]} with {words[(2 * i + 1) % words.Length]} matters");
    }

    static IEnumerable<(int, string)> Chat(int from, int to)
    {
        for (int t = from; t < to; t += 10) yield return (t, $"{Course} reminders about the field trip and the lab report");
    }

    static List<Spoken> Transcript(IEnumerable<(int At, string Text)> lines) =>
        TimedText.Parse(string.Join('\n', lines.OrderBy(l => l.At).Select(l => $"[{TimedText.Clock(l.At)}] {l.Text}")));

    static void Within(SlideTime slide, int fromMinute, int toMinute)
    {
        Assert.NotNull(slide.At);
        Assert.InRange(slide.At.Value, TimeSpan.FromMinutes(fromMinute), TimeSpan.FromMinutes(toMinute) - TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Slides_are_placed_where_they_were_taught_and_left_unsure_otherwise()
    {
        string[] deck =
        [
            $"{Course}\n{Reefs}",
            "",                                                                    // a picture
            $"{Course}\n{Kelp}",
            $"{Course}\nDeep sea vents: chemosynthesis, tubeworms, hydrothermal", // never said
            $"{Course}\n{Tides}",                                                  // said again, as fully, at 40 minutes
            $"{Course}\n{Plankton}",
        ];
        var lecture = Transcript([
            .. Taught(0, Reefs), .. Taught(180, Kelp), .. Taught(360, Tides), .. Taught(540, Plankton), .. Chat(720, 2400),
            (300, "Tides lunar gravity spring neap"), .. Taught(2400, Tides, 60),
        ]);

        var times = SlideTimes.Match(deck, lecture);

        Assert.Equal([0, 1, 2, 3, 4, 5], times.Select(t => t.Slide));
        Within(times[0], 0, 3);
        Within(times[2], 3, 6);
        Within(times[5], 9, 12);
        Assert.True(times[0].At < times[2].At && times[2].At < times[5].At);
        Assert.Null(times[1].At);
        Assert.Null(times[3].At);
        Assert.Null(times[4].At);

        // A lecture that skips the kelp slide still places the others; a slide built up over two pages gets one time.
        string[] builtUp = [deck[0], deck[2], deck[5], $"{deck[5]}\nKrill feed whales"];
        var skipped = SlideTimes.Match(builtUp, Transcript([.. Taught(0, Reefs), .. Chat(180, 360), .. Taught(360, Plankton)]));
        Within(skipped[0], 0, 3);
        Assert.Null(skipped[1].At);
        Within(skipped[2], 6, 9);
        Assert.Equal(skipped[2].At, skipped[3].At);
    }
}
