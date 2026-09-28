using System.Text.RegularExpressions;
using StudyStash.Core.Ai;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Writer;

namespace StudyStash.Core.Tests;

/// <summary>Windows's text recognition, run through a Windows PowerShell script: the script as it's written and read
/// back on every computer, and the real thing on Windows.</summary>
public class WindowsOcrTests
{
    /// <summary>A PowerShell that "reads" each page asked for as "page N", writing the file the way the script does.</summary>
    static Runner Fake(List<(string Exe, IReadOnlyList<string> Args, TimeSpan Timeout)> calls, bool dies = false) => (exe, args, timeout) =>
    {
        calls.Add((exe, args, timeout));
        string script = args[^1];
        string output = Regex.Match(script, @"\$out = '((?:[^']|'')*)'").Groups[1].Value.Replace("''", "'");
        var pages = Regex.Match(script, @"\$pages = @\(([\d,]*)\)").Groups[1].Value.Split(',', StringSplitOptions.RemoveEmptyEntries);
        foreach (string p in pages.Length == 0 ? ["-1"] : pages) File.AppendAllText(output, $"\f{p}\npage {p}\nsecond line");
        return dies ? null : new ProcResult(0, "");
    };

    [Fact]
    public void A_path_reaches_PowerShell_exactly_as_it_is()
    {
        Assert.Equal("'C:\\Notes\\Eli''s notes.pdf'", WindowsOcr.Quote("C:\\Notes\\Eli's notes.pdf"));
        Assert.Equal("'Eli\u2019\u2019s $home `n.pdf'", WindowsOcr.Quote("Eli\u2019s $home `n.pdf")); // no $ or ` is read
        string script = WindowsOcr.Script("C:\\a'b.pdf", [0, 4], "C:\\Temp\\out.txt");
        Assert.StartsWith("$path = 'C:\\a''b.pdf'; $out = 'C:\\Temp\\out.txt'; $pages = @(0,4); ", script);
        Assert.Contains("$pages = $null; ", WindowsOcr.Script("scan.png", null, "out.txt"));
        Assert.Contains("TryCreateFromUserProfileLanguages", script);
    }

    [Fact]
    public void What_the_script_wrote_is_read_page_by_page()
    {
        var read = WindowsOcr.Parse("\f0\nMidterm\nTuesday\f3\n\f-1\npicture\fnot a page\f7");
        Assert.Equal("Midterm\nTuesday", read[0]);
        Assert.Equal("", read[3]);
        Assert.Equal("picture", read[-1]);
        Assert.Equal(3, read.Count);
        Assert.Empty(WindowsOcr.Parse(""));
    }

    [Fact]
    public void Pages_are_read_by_Windows_PowerShell_within_the_time_left_and_the_file_it_wrote_is_removed()
    {
        var calls = new List<(string Exe, IReadOnlyList<string> Args, TimeSpan Timeout)>();
        var read = WindowsOcr.PdfPageText("C:\\notes.pdf", [1, 2], DateTime.UtcNow.AddMinutes(2), default, Fake(calls));

        Assert.Equal("page 1\nsecond line", read[1]);
        Assert.Equal("page 2\nsecond line", read[2]);
        var call = Assert.Single(calls);
        Assert.Equal("powershell", call.Exe);
        Assert.Equal(["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command"], call.Args.Take(5));
        Assert.InRange(call.Timeout, TimeSpan.FromSeconds(100), TimeSpan.FromMinutes(2));
        string output = Regex.Match(call.Args[^1], @"\$out = '([^']*)'").Groups[1].Value;
        Assert.False(File.Exists(output));
    }

    [Fact]
    public void Pages_read_before_the_time_limit_stopped_PowerShell_are_kept()
    {
        var calls = new List<(string Exe, IReadOnlyList<string> Args, TimeSpan Timeout)>();
        var read = WindowsOcr.PdfPageText("C:\\notes.pdf", [0], DateTime.UtcNow.AddMinutes(1), default, Fake(calls, dies: true));
        Assert.Equal("page 0\nsecond line", read[0]);
        Assert.Equal("page -1\nsecond line", WindowsOcr.ImageText("C:\\scan.png", DateTime.UtcNow.AddMinutes(1), Fake(calls)));
    }

    [Fact]
    public void Nothing_is_started_once_the_time_is_up_or_after_stopping()
    {
        var calls = new List<(string Exe, IReadOnlyList<string> Args, TimeSpan Timeout)>();
        Assert.Empty(WindowsOcr.PdfPageText("C:\\notes.pdf", [0], DateTime.UtcNow.AddSeconds(-1), default, Fake(calls)));
        Assert.Null(WindowsOcr.ImageText("C:\\scan.png", DateTime.UtcNow, Fake(calls)));
        Assert.Throws<OperationCanceledException>(() => WindowsOcr.PdfPageText("C:\\notes.pdf", [0], DateTime.UtcNow.AddMinutes(1), new CancellationToken(true), Fake(calls)));
        Assert.Empty(calls);
        // PowerShell missing altogether reads as nothing.
        Assert.Null(WindowsOcr.ImageText("C:\\scan.png", DateTime.UtcNow.AddMinutes(1), (_, _, _) => null));
    }

    /// <summary>The real thing, on Windows: printed words in a picture, and in a PDF of a scan.</summary>
    [Fact]
    public async Task Windows_reads_printed_words_in_a_picture_and_a_scanned_PDF()
    {
        if (!OperatingSystem.IsWindows()) return;
        var options = new DocumentTextOptions();
        string png = DocumentOcrTests.Fixture("typed.png");
        Assert.Contains("chloroplast", await DocumentText.ExtractAsync(png, options, default), StringComparison.OrdinalIgnoreCase);

        using var dir = new TempDir();
        var b = new PdfDocumentBuilder();
        b.AddPage(PageSize.Letter).AddPng(File.ReadAllBytes(png), new PdfRectangle(36, 500, 576, 635));
        File.WriteAllBytes(dir["scan.pdf"], b.Build());
        Assert.Contains("chloroplast", await DocumentText.ExtractAsync(dir["scan.pdf"], options, default), StringComparison.OrdinalIgnoreCase);
    }
}
