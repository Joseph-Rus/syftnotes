using System.Globalization;
using System.Text;

namespace StudyStash.Core.Ai;

/// <summary>
/// Windows's own text recognition (Windows.Media.Ocr, in every Windows 10 and 11), with Windows.Data.Pdf drawing a
/// PDF's pages for it. Both are Windows Runtime APIs, which a plain .NET program can't reach without a Windows-only
/// build, so Windows PowerShell 5.1 (on every Windows, and able to call them) runs a short script instead. The script
/// writes each page's words to a file as it goes, so a document cut off by the time limit keeps the pages it read.
/// Windows's recognition reads print well and handwriting poorly: see docs/document-text.md.
/// </summary>
internal static class WindowsOcr
{
    public static bool Available => OperatingSystem.IsWindows();

    /// <summary>The words Windows reads in a picture (png, jpg, bmp, gif, tiff; heic only with Microsoft's HEIF
    /// extension); null when it can't.</summary>
    public static string? ImageText(string path, DateTime until, Runner? run = null) =>
        Run(path, null, until, run).GetValueOrDefault(-1);

    /// <summary>The words Windows reads on each of these pages of a PDF (numbered from 0), each drawn as a picture.
    /// Stops at <paramref name="until"/>; a page it couldn't read is left out.</summary>
    public static IReadOnlyDictionary<int, string> PdfPageText(string path, IReadOnlyList<int> pages, DateTime until, CancellationToken ct) =>
        PdfPageText(path, pages, until, ct, null);

    internal static Dictionary<int, string> PdfPageText(string path, IReadOnlyList<int> pages, DateTime until, CancellationToken ct, Runner? run)
    {
        ct.ThrowIfCancellationRequested();
        return Run(path, pages, until, run);
    }

    static Dictionary<int, string> Run(string path, IReadOnlyList<int>? pages, DateTime until, Runner? run)
    {
        var left = until - DateTime.UtcNow;
        if (left <= TimeSpan.Zero) return [];
        string output = Path.Combine(Path.GetTempPath(), $"studystash-ocr-{Guid.NewGuid():N}.txt");
        try
        {
            // Killed at the time limit, it still leaves the pages it finished.
            (run ?? Machine.Run)("powershell", ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", Script(path, pages, output)],
                left < TimeSpan.FromSeconds(10) ? TimeSpan.FromSeconds(10) : left);
            return File.Exists(output) ? Parse(File.ReadAllText(output, Encoding.UTF8)) : [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
        finally
        {
            try
            {
                File.Delete(output);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>What the script wrote: for each page read, a form feed, its number (-1 for a picture), a line break
    /// and its lines.</summary>
    internal static Dictionary<int, string> Parse(string written)
    {
        var read = new Dictionary<int, string>();
        foreach (string part in written.Split('\f'))
        {
            int nl = part.IndexOf('\n', StringComparison.Ordinal);
            if (nl > 0 && int.TryParse(part[..nl], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int page))
                read[page] = part[(nl + 1)..];
        }
        return read;
    }

    /// <summary>A PowerShell string of exactly <paramref name="s"/>: single-quoted, each quote doubled (PowerShell
    /// counts the curly quotes a Mac puts in file names as quotes too).</summary>
    internal static string Quote(string s)
    {
        var b = new StringBuilder("'");
        foreach (char c in s)
        {
            b.Append(c);
            if (c is '\'' or '‘' or '’' or '‚' or '‛') b.Append(c);
        }
        return b.Append('\'').ToString();
    }

    /// <summary>The script: a picture, or the PDF pages given, read with the languages the student uses Windows in.</summary>
    internal static string Script(string path, IReadOnlyList<int>? pages, string output) =>
        $"$path = {Quote(path)}; $out = {Quote(output)}; "
        + (pages is null ? "$pages = $null; " : $"$pages = @({string.Join(",", pages.Select(p => p.ToString(CultureInfo.InvariantCulture)))}); ")
        + """
        $ErrorActionPreference = 'Stop'
        Add-Type -AssemblyName System.Runtime.WindowsRuntime
        $null = [Windows.Storage.StorageFile, Windows.Storage, ContentType = WindowsRuntime]
        $null = [Windows.Media.Ocr.OcrEngine, Windows.Foundation, ContentType = WindowsRuntime]
        $null = [Windows.Graphics.Imaging.BitmapDecoder, Windows.Graphics, ContentType = WindowsRuntime]
        $null = [Windows.Data.Pdf.PdfDocument, Windows.Data.Pdf, ContentType = WindowsRuntime]
        $null = [Windows.Storage.Streams.InMemoryRandomAccessStream, Windows.Storage.Streams, ContentType = WindowsRuntime]
        $ext = [System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 }
        $asOp = $ext | Where-Object { $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' } | Select-Object -First 1
        $asAction = $ext | Where-Object { $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncAction' } | Select-Object -First 1
        function Await($op, [Type]$type) { $t = $asOp.MakeGenericMethod($type).Invoke($null, @($op)); $null = $t.Wait(-1); $t.Result }
        function Done($action) { $null = $asAction.Invoke($null, @($action)).Wait(-1) }
        $ocr = [Windows.Media.Ocr.OcrEngine]::TryCreateFromUserProfileLanguages()
        if ($null -eq $ocr) { exit 3 }
        $utf8 = New-Object System.Text.UTF8Encoding $false
        function Recognize($stream) {
          $decoder = Await ([Windows.Graphics.Imaging.BitmapDecoder]::CreateAsync($stream)) ([Windows.Graphics.Imaging.BitmapDecoder])
          $bitmap = Await ($decoder.GetSoftwareBitmapAsync()) ([Windows.Graphics.Imaging.SoftwareBitmap])
          $result = Await ($ocr.RecognizeAsync($bitmap)) ([Windows.Media.Ocr.OcrResult])
          ($result.Lines | ForEach-Object { $_.Text }) -join "`n"
        }
        function Put($index, $text) { [System.IO.File]::AppendAllText($out, "`f$index`n$text", $utf8) }
        $file = Await ([Windows.Storage.StorageFile]::GetFileFromPathAsync($path)) ([Windows.Storage.StorageFile])
        if ($null -eq $pages) {
          $stream = Await ($file.OpenAsync([Windows.Storage.FileAccessMode]::Read)) ([Windows.Storage.Streams.IRandomAccessStream])
          Put -1 (Recognize $stream)
          exit 0
        }
        $pdf = Await ([Windows.Data.Pdf.PdfDocument]::LoadFromFileAsync($file)) ([Windows.Data.Pdf.PdfDocument])
        foreach ($i in $pages) {
          if ($i -lt 0 -or $i -ge $pdf.PageCount) { continue }
          try {
            $page = $pdf.GetPage($i)
            $scale = [Math]::Min(2.5, 2800 / [Math]::Max($page.Size.Width, $page.Size.Height))
            $options = New-Object Windows.Data.Pdf.PdfPageRenderOptions
            $options.DestinationWidth = [uint32]($page.Size.Width * $scale)
            $options.DestinationHeight = [uint32]($page.Size.Height * $scale)
            $stream = New-Object Windows.Storage.Streams.InMemoryRandomAccessStream
            Done ($page.RenderToStreamAsync($stream, $options))
            Put $i (Recognize $stream)
            $stream.Dispose(); $page.Dispose()
          } catch { }
        }
        """;
}
