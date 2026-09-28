namespace StudyStash.Core;

/// <summary>
/// Files the library writes while the app reads them (Canvas's assignments, each class's index, notifications):
/// written whole to a file of their own and swapped in, and read without ever holding the file against that swap.
/// On Windows a file someone has open, even just to read it, can't be replaced, and a virus scanner or the search
/// indexer often opens a file the moment it's written; so a swap that finds the file busy waits a moment and tries
/// again rather than losing a finished sync.
/// </summary>
public static class SharedFile
{
    /// <summary>How many times a busy swap is tried before giving up, a little longer apart each time (about 3 s in all).</summary>
    const int Tries = 12;

    /// <summary>The file's text, or null when there's no file. Opened so a writer can replace it meanwhile.</summary>
    public static string? Read(string path)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
            catch (FileNotFoundException)
            {
                return null;
            }
            catch (DirectoryNotFoundException)
            {
                return null;
            }
            catch (IOException) when (attempt < Tries)
            {
                Thread.Sleep(Wait(attempt));
            }
        }
    }

    /// <summary>Writes <paramref name="text"/> to a file of its own beside <paramref name="path"/>, then swaps it in:
    /// a reader sees the old file or the new one, never half of one. Two writers at once each use their own.</summary>
    public static void Write(string path, string text)
    {
        string tmp = $"{path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(tmp, text);
        try
        {
            Replace(tmp, path);
        }
        catch
        {
            TryDelete(tmp);
            throw;
        }
    }

    /// <summary>Moves <paramref name="from"/> over <paramref name="to"/>, trying again while either is busy.</summary>
    public static void Replace(string from, string to)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(from, to, overwrite: true);
                return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException && e is not FileNotFoundException && attempt < Tries)
            {
                Thread.Sleep(Wait(attempt));
            }
        }
    }

    static TimeSpan Wait(int attempt) => TimeSpan.FromMilliseconds(Math.Min(25 * attempt * attempt, 500));

    static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}
