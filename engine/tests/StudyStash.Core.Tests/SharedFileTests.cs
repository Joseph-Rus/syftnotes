namespace StudyStash.Core.Tests;

/// <summary>The library's Canvas files are rewritten while the app reads them. On Windows a file someone has open
/// can't be replaced, and a finished sync used to fail right there (so the app never saw it): the write now waits for
/// the reader and goes through, and the library's own reads never hold a file against a write.</summary>
public class SharedFileTests
{
    [Fact]
    public async Task A_write_waits_for_a_reader_holding_the_file_and_then_goes_through()
    {
        using var dir = new TempDir();
        string path = dir["assignments.json"];
        SharedFile.Write(path, "old");
        Task write;
        // Opened the way File.ReadAllText opens a file (or a virus scanner might): others may read, not replace.
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            write = Task.Run(() => SharedFile.Write(path, "new"));
            await Task.Delay(300);
        }
        await write;
        Assert.Equal("new", SharedFile.Read(path));
        Assert.Empty(Directory.EnumerateFiles(dir.Path, "*.tmp"));
    }

    [Fact]
    public async Task Reads_and_writes_at_once_never_fail_or_see_half_a_file()
    {
        using var dir = new TempDir();
        string path = dir["canvas.json"];
        string Big(int n) => new((char)('a' + n % 26), 200_000);
        SharedFile.Write(path, Big(0));
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var writer = Task.Run(() =>
        {
            for (int n = 1; !stop.IsCancellationRequested; n++) SharedFile.Write(path, Big(n));
        });
        var readers = Enumerable.Range(0, 3).Select(_ => Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                string text = SharedFile.Read(path)!;
                Assert.Equal(200_000, text.Length);
                Assert.True(text.All(c => c == text[0]), "a whole file, never half of two");
            }
        })).ToList();
        await Task.WhenAll([writer, .. readers]);
    }

    [Fact]
    public void A_missing_file_reads_as_nothing()
    {
        using var dir = new TempDir();
        Assert.Null(SharedFile.Read(dir["nope.json"]));
        Assert.Null(SharedFile.Read(Path.Combine(dir["no-folder"], "nope.json")));
    }
}
