using Avalonia.Controls;
using StudyStash.App.Controls;

namespace StudyStash.App.Views;

/// <summary>The share/Export button's menu: download this lecture, download the whole open class (when one is open),
/// and a check for whether a download includes the transcript.</summary>
public static class DownloadMenu
{
    /// <summary><paramref name="className"/> null leaves out "Download all of…" (Due, or no class open).
    /// <paramref name="includeTranscripts"/> checks "Include transcripts". <paramref name="download"/>,
    /// <paramref name="downloadClass"/> and <paramref name="toggleTranscripts"/> run when their row is picked.</summary>
    public static ContextMenu Build(string? className, bool includeTranscripts, Action download, Action downloadClass, Action toggleTranscripts)
    {
        var menu = new ContextMenu { WindowManagerAddShadowHint = OperatingSystem.IsMacOS() };
        var one = new MenuItem { Header = "Download as Markdown…" };
        one.Click += (_, _) => download();
        menu.Items.Add(one);
        if (className is not null)
        {
            var all = new MenuItem { Header = $"Download all of {className}…" };
            all.Click += (_, _) => downloadClass();
            menu.Items.Add(all);
        }
        menu.Items.Add(new Separator());
        var transcripts = new MenuItem { Header = "Include transcripts", Icon = includeTranscripts ? new Icon { Glyph = "check", Size = 13 } : null };
        transcripts.Click += (_, _) => toggleTranscripts();
        menu.Items.Add(transcripts);
        return menu;
    }
}
