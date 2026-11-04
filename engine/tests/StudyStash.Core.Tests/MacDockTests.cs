using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace StudyStash.Core.Tests;

/// <summary>A Dock icon pinned to "Study Stash.app" before the rename, pointed at "Syft Notes.app" (MacDock.cs). Made-up
/// Dock settings only; the real Dock is never read here.</summary>
public class MacDockTests
{
    const string App = "/Applications/Syft Notes.app";

    static string Tile(string url, string? id, int type = 15) => $"""
        <dict>
        	<key>GUID</key>
        	<integer>12345</integer>
        	<key>tile-data</key>
        	<dict>
        		<key>book</key>
        		<data>Ym9va21hcmsgYnl0ZXM=</data>
        		{(id is null ? "" : $"<key>bundle-identifier</key>\n\t\t<string>{id}</string>")}
        		<key>file-data</key>
        		<dict>
        			<key>_CFURLString</key>
        			<string>{url}</string>
        			<key>_CFURLStringType</key>
        			<integer>{type}</integer>
        		</dict>
        		<key>file-label</key>
        		<string>Label</string>
        	</dict>
        	<key>tile-type</key>
        	<string>file-tile</string>
        </dict>
        """;

    static byte[] Dock(params string[] tiles) => Encoding.UTF8.GetBytes($"""
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
        <plist version="1.0">
        <dict>
        	<key>autohide</key>
        	<false/>
        	<key>persistent-apps</key>
        	<array>
        	{string.Join("\n", tiles)}
        	</array>
        	<key>tilesize</key>
        	<integer>48</integer>
        </dict>
        </plist>
        """);

    static XElement Top(byte[] plist)
    {
        using var reader = XmlReader.Create(new MemoryStream(plist), new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null });
        return XDocument.Load(reader).Root!.Element("dict")!;
    }

    static List<XElement> Tiles(byte[] plist) => [.. Top(plist).Element("array")!.Elements("dict")];

    static string Text(XElement e) => e.ToString(SaveOptions.DisableFormatting);

    [Fact]
    public void A_pinned_tile_for_the_old_app_that_is_gone_points_at_the_new_one_and_nothing_else_changes()
    {
        byte[] before = Dock(
            Tile("file:///System/Applications/Mail.app/", "com.apple.mail"),
            Tile("file:///Applications/Study%20Stash.app/", "com.study-stash.app"),
            Tile("/Applications/Study Stash.app", null, 0),
            Tile("file:///Applications/Notes%20Helper.app/", "com.example.helper"));
        var fix = MacDock.Repaired(before, App, _ => false);

        Assert.True(fix.Changed);
        Assert.Null(fix.Problem);
        Assert.Equal(2, fix.Tiles);
        var was = Tiles(before);
        var now = Tiles(fix.Plist);
        Assert.Equal(was.Count, now.Count);
        // The other apps and the Dock's own settings come out exactly as they went in.
        Assert.Equal(Text(was[0]), Text(now[0]));
        Assert.Equal(Text(was[3]), Text(now[3]));
        var top = Top(fix.Plist);
        Assert.Equal("48", top.Elements("integer").Single().Value);
        Assert.Single(top.Elements("false"));
        // Each repaired tile: the new place in the form it already had, the old bookmark gone, the rest as it was.
        foreach (var (tile, url, type) in new[] { (now[1], "file:///Applications/Syft%20Notes.app/", "15"), (now[2], "/Applications/Syft Notes.app/", "0") })
        {
            var data = tile.Element("dict")!;
            Assert.DoesNotContain(data.Elements("key"), k => k.Value == "book");
            Assert.Empty(data.Elements("data"));
            var file = data.Element("dict")!;
            Assert.Equal(url, file.Element("string")!.Value);
            Assert.Equal(type, file.Element("integer")!.Value);
            Assert.Equal("Label", data.Elements("string").Last().Value);
            Assert.Equal("12345", tile.Element("integer")!.Value);
        }
    }

    [Fact]
    public void A_Dock_with_nothing_to_repair_is_left_exactly_as_it_was()
    {
        // No old tile; an old tile whose app is still there; someone else's app by that name.
        byte[] before = Dock(
            Tile("file:///System/Applications/Mail.app/", "com.apple.mail"),
            Tile("file:///Applications/Study%20Stash.app/", "com.study-stash.app"),
            Tile("file:///Users/Shared/Study%20Stash.app/", "com.example.other"));
        var fix = MacDock.Repaired(before, App, path => path == "/Applications/Study Stash.app");
        Assert.False(fix.Changed);
        Assert.Null(fix.Problem);
        Assert.Same(before, fix.Plist);
    }

    [Theory]
    [InlineData("not a plist at all")]
    [InlineData("<?xml version=\"1.0\"?><plist version=\"1.0\"><dict><key>persistent-apps</key><array><dict>")]
    [InlineData("<?xml version=\"1.0\"?><plist version=\"1.0\"><array/></plist>")]
    public void Settings_it_cannot_read_change_nothing(string text)
    {
        byte[] before = Encoding.UTF8.GetBytes(text);
        var fix = MacDock.Repaired(before, App, _ => false);
        Assert.False(fix.Changed);
        Assert.NotNull(fix.Problem);
        Assert.Same(before, fix.Plist);
    }
}
