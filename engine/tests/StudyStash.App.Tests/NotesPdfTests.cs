using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using StudyStash.App.Services;

namespace StudyStash.App.Tests;

public class NotesPdfTests
{
    static JsonObject Lecture(string notes, string? transcript = null) => new()
    {
        ["title"] = "The cardiac cycle", ["class"] = "BIO 110", ["date"] = "2026-09-23T10:00:00", ["seconds"] = 3120.0, ["notes"] = notes,
        ["transcript"] = transcript,
    };

    [AvaloniaFact]
    public async Task Probe()
    {
        Skin.UseTheme(ColourThemes.Default);
        ((App)Application.Current!).UseSkin(SkinKind.Mac);
        using var ms = new MemoryStream();
        int pages = await NotesPdf.WriteAsync(ms, Lecture(RichDemo.CardiacLecture), false, Paper.Letter, Colors.Teal);
        string dir = Environment.GetEnvironmentVariable("STUDYSTASH_SHOTS") ?? Path.GetTempPath();
        File.WriteAllBytes(Path.Combine(dir, "probe.pdf"), ms.ToArray());
        Assert.True(pages > 0);
    }
}
