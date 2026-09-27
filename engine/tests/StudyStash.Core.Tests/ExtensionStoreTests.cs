using System.Buffers.Binary;
using System.IO.Compression;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using StudyStash.Core.Canvas;
using StudyStash.Library;

namespace StudyStash.Core.Tests;

/// <summary>The Chrome Web Store copy of the extension: the zip to upload holds only the extension, asks for no site
/// until the student connects it, and connects with the code Study Stash hands out.</summary>
public partial class ExtensionStoreTests
{
    static JsonObject ManifestIn(ZipArchive zip) => JsonNode.Parse(Text(zip, "manifest.json"))!.AsObject();

    static string Text(ZipArchive zip, string name)
    {
        using var r = new StreamReader(zip.GetEntry(name)!.Open());
        return r.ReadToEnd();
    }

    /// <summary>A PNG's width and height, from its IHDR chunk.</summary>
    static (int W, int H) PngSize(ZipArchive zip, string name)
    {
        using var s = zip.GetEntry(name)!.Open();
        var head = new byte[24];
        s.ReadExactly(head);
        return (BinaryPrimitives.ReadInt32BigEndian(head.AsSpan(16)), BinaryPrimitives.ReadInt32BigEndian(head.AsSpan(20)));
    }

    [GeneratedRegex("(?:src|href)=\"([^\"]+)\"")]
    private static partial Regex Referenced();

    [Fact]
    public void The_store_zip_holds_only_the_extension_and_reaches_no_site_until_connected()
    {
        using var dir = new TempDir();
        var names = Extension.PackForStore(dir["out/x.zip"]);

        using var zip = ZipFile.OpenRead(dir["out/x.zip"]);
        var entries = zip.Entries.Select(e => e.FullName).ToList();
        Assert.Equal(names, entries);
        Assert.Equal(entries.Order(StringComparer.Ordinal), entries);
        Assert.All(entries, e => Assert.DoesNotContain('/', e)); // flat: manifest.json at the top, as the store wants
        Assert.DoesNotContain("config.json", entries);            // a store copy is connected by code, never by a file
        Assert.DoesNotContain("config.js", entries);
        foreach (string needed in new[] { "manifest.json", "background.js", "connection.js", "popup.html", "popup.js" })
            Assert.Contains(needed, entries);

        var m = ManifestIn(zip);
        Assert.Equal(3, m["manifest_version"]!.GetValue<int>());
        Assert.Equal(Extension.Version(), m["version"]!.GetValue<string>());
        Assert.Null(m["host_permissions"]);
        Assert.Equal(["https://*/*", "http://*/*"], m["optional_host_permissions"]!.AsArray().Select(h => h!.GetValue<string>()));
        Assert.Equal(["alarms", "storage"], m["permissions"]!.AsArray().Select(p => p!.GetValue<string>()));

        // The store's rules: a name of 45 characters at most, a description of 132, a short name of 12.
        Assert.Equal("Study Stash for Canvas", m["name"]!.GetValue<string>());
        Assert.InRange(m["description"]!.GetValue<string>().Length, 1, 132);
        Assert.InRange(m["short_name"]!.GetValue<string>().Length, 1, 12);
        Assert.Matches(@"^\d+$", m["minimum_chrome_version"]!.GetValue<string>());

        // Every icon it names is in the zip, at its size; so are the service worker and the popup.
        foreach (var (size, icon) in m["icons"]!.AsObject())
        {
            Assert.Contains(icon!.GetValue<string>(), entries);
            Assert.Equal((int.Parse(size), int.Parse(size)), PngSize(zip, icon.GetValue<string>()));
        }
        Assert.Equal(["128", "16", "32", "48"], m["icons"]!.AsObject().Select(p => p.Key).Order(StringComparer.Ordinal));
        foreach (var (_, icon) in m["action"]!["default_icon"]!.AsObject()) Assert.Contains(icon!.GetValue<string>(), entries);
        Assert.Contains(m["background"]!["service_worker"]!.GetValue<string>(), entries);
        Assert.Contains(m["action"]!["default_popup"]!.GetValue<string>(), entries);
        foreach (Match r in Referenced().Matches(Text(zip, "popup.html")))
            Assert.Contains(r.Groups[1].Value, entries);
    }

    [Fact]
    public void The_same_engine_packs_the_same_bytes()
    {
        using var dir = new TempDir();
        Extension.PackForStore(dir["a.zip"]);
        Thread.Sleep(1100); // a zip's times are to 2 s: a clock in them would show
        Extension.PackForStore(dir["b.zip"]);
        Assert.Equal(File.ReadAllBytes(dir["a.zip"]), File.ReadAllBytes(dir["b.zip"]));
    }

    [Fact]
    public void A_folder_s_copy_has_the_same_name_and_icons_and_its_own_sites()
    {
        using var dir = new TempDir();
        string ext = dir["chrome-extension"];
        Extension.Ensure(ext, "http://127.0.0.1:8787", "k3y", "https://canvas.test");
        var m = JsonNode.Parse(File.ReadAllText(Path.Combine(ext, "manifest.json")))!.AsObject();
        Assert.Equal("Study Stash for Canvas", m["name"]!.GetValue<string>());
        Assert.Null(m["optional_host_permissions"]);
        Assert.NotEmpty(m["host_permissions"]!.AsArray());
        foreach (var (_, icon) in m["icons"]!.AsObject()) Assert.True(File.Exists(Path.Combine(ext, icon!.GetValue<string>())));
    }

    [Fact]
    public void The_connection_code_carries_the_folder_s_connection()
    {
        string code = Extension.ConnectionCode("https://mini.tail.ts.net/", "k3y+/=", "https://school.instructure.com/");
        Assert.Matches("^[A-Za-z0-9_-]+$", code); // survives a chat app, an email or a URL
        var expected = new ExtensionConnection("https://mini.tail.ts.net", "k3y+/=", "https://school.instructure.com");
        Assert.Equal(expected, Extension.ReadConnectionCode(code));
        // Copied with a line break, spaces or quotes around it: still the same code.
        Assert.Equal(expected, Extension.ReadConnectionCode($"  \"{code[..10]}\n{code[10..]}\" \n"));

        // What a folder for the same connection says.
        using var dir = new TempDir();
        Extension.Ensure(dir.Path, "https://mini.tail.ts.net/", "k3y+/=", "https://school.instructure.com/");
        Assert.Equal(Extension.Connection(dir.Path), Extension.ReadConnectionCode(code));
    }

    [Theory]
    [InlineData("https://mini.tail.ts.net", "k3y", "")]
    [InlineData("https://mini.tail.ts.net", "", "https://school.instructure.com")]
    [InlineData("", "k3y", "https://school.instructure.com")]
    public void There_is_no_code_until_there_is_a_canvas_address_a_key_and_a_library(string library, string key, string canvas) =>
        Assert.Equal("", Extension.ConnectionCode(library, key, canvas));

    [Theory]
    [InlineData("")]
    [InlineData("hello there")]
    [InlineData("eyJub3QiOiJvdXJzIn0")]    // {"not":"ours"}
    [InlineData("WyJhIiwiYiJd")]           // ["a","b"]
    [InlineData("bm90IGpzb24")]            // not json
    public void Text_that_isn_t_a_code_reads_as_none(string text) => Assert.Null(Extension.ReadConnectionCode(text));

    [Fact]
    public async Task The_engine_packs_the_store_zip_without_making_a_home()
    {
        Assert.True(Cli.IsCommand(["extension-zip", "x.zip"]));
        using var dir = new TempDir();
        Assert.Equal(0, await Cli.RunAsync(["--home", dir["home"], "extension-zip", dir["store/x.zip"]]));
        Assert.False(Directory.Exists(dir["home"]));
        using (var zip = ZipFile.OpenRead(dir["store/x.zip"]))
            Assert.Equal(Extension.StoreFiles(), zip.Entries.Select(e => e.FullName));

        Assert.Equal(2, await Cli.RunAsync(["--home", dir["home"], "extension-zip"])); // where to?
    }
}
