namespace StudyStash.App.Tests;

public class LogTests
{
    /// <summary>The log's lines, read while it's still open for writing (Windows refuses File.ReadAllLines then).</summary>
    static string[] Lines(string path)
    {
        using var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
        return reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToArray();
    }

    [Fact]
    public void A_full_log_starts_again_and_keeps_the_last_one()
    {
        using var home = new TempHome();
        string path = home["app.log"];
        var log = new RollingLog(path, limit: 1000);
        for (int i = 0; i < 40; i++) log.Write($"line {i} " + new string('x', 40));

        Assert.True(File.Exists(path + ".1"));
        Assert.InRange(new FileInfo(path).Length, 1, 1100);
        Assert.InRange(new FileInfo(path + ".1").Length, 1000, 1100);
        Assert.EndsWith("line 39 " + new string('x', 40), Lines(path)[^1]);
    }

    [Fact]
    public void Two_copies_writing_keep_each_others_lines()
    {
        using var home = new TempHome();
        string path = home["app.log"];
        var first = new RollingLog(path);
        var second = new RollingLog(path);
        first.Write("first starts");
        second.Write("second hands off");
        first.Write("first heard it");
        second.Write("second goes");

        var lines = Lines(path).Select(l => l[20..]).ToList();
        Assert.Equal(["first starts", "second hands off", "first heard it", "second goes"], lines);
    }

    [Fact]
    public void Lines_from_many_threads_stay_whole()
    {
        using var home = new TempHome();
        string path = home["app.log"];
        var log = new RollingLog(path);
        Parallel.For(0, 8, t =>
        {
            for (int i = 0; i < 200; i++) log.Write($"[t{t}] line {i} ends here");
        });

        var lines = Lines(path);
        Assert.Equal(1600, lines.Length);
        Assert.All(lines, l => Assert.Matches(@"^\d{4}-\d\d-\d\d \d\d:\d\d:\d\d \[t\d\] line \d+ ends here$", l));
    }
}
