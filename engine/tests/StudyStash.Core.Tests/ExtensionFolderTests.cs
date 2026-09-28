using System.Text.Json.Nodes;
using StudyStash.Core.Canvas;

namespace StudyStash.Core.Tests;

/// <summary>Where the Chrome extension's folder is: on a Mac, a folder Chrome's Load unpacked window shows (the home is
/// a dot-folder it hides), never in iCloud's Desktop or Documents, only this person's; and a Chrome that loaded the
/// old folder inside the home keeps getting its updates. Every "home" here is a made-up one in a temp folder.</summary>
public class ExtensionFolderTests
{
    static string Key(string folder) => JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "config.json")))!["key"]!.GetValue<string>();

    [Fact]
    public void On_a_Mac_the_usual_home_s_folder_is_Study_Stash_Chrome_extension_in_your_home()
    {
        using var user = new TempDir();
        string home = user[".study-stash"];
        string folder = Extension.Folder(home, user.Path, mac: true);
        Assert.Equal(Path.Combine(user.Path, "Study Stash", "Chrome extension"), folder);

        // Something Chrome's picker shows: no dot-folder, not ~/Library, not iCloud's Desktop or Documents.
        string relative = Path.GetRelativePath(user.Path, folder);
        Assert.DoesNotContain(relative.Split(Path.DirectorySeparatorChar), p => p.StartsWith('.'));
        Assert.False(relative.StartsWith("Library", StringComparison.Ordinal) || relative.StartsWith("Desktop", StringComparison.Ordinal)
                     || relative.StartsWith("Documents", StringComparison.Ordinal));
    }

    [Fact]
    public void Another_hidden_home_gets_a_folder_of_its_own_and_a_visible_or_outside_home_keeps_its_own()
    {
        using var user = new TempDir();
        string other = Extension.Folder(user[".granola-share"], user.Path, mac: true);
        string library = Extension.Folder(Path.Combine(user.Path, "Library", "Application Support", "Study Stash"), user.Path, mac: true);
        Assert.Matches(@"Chrome extension \([0-9a-f]{8}\)$", other);
        Assert.Matches(@"Chrome extension \([0-9a-f]{8}\)$", library);
        Assert.NotEqual(other, library);
        Assert.Equal(Path.Combine(user.Path, "Study Stash"), Path.GetDirectoryName(other));

        // A home Chrome already shows, one outside this person's home (a test's), and Windows: the home's own, as always.
        string visible = Path.Combine(user.Path, "Lectures", "study-stash");
        Assert.Equal(Path.Combine(visible, "chrome-extension"), Extension.Folder(visible, user.Path, mac: true));
        using var outside = new TempDir();
        Assert.Equal(outside["chrome-extension"], Extension.Folder(outside.Path, user.Path, mac: true));
        Assert.Equal(Path.Combine(user[".study-stash"], "chrome-extension"), Extension.Folder(user[".study-stash"], user.Path, mac: false));
    }

    [Fact]
    public void A_Chrome_that_loaded_the_old_folder_keeps_getting_its_updates()
    {
        using var user = new TempDir();
        string home = user[".study-stash"];
        string old = Extension.InHome(home);
        Extension.Ensure(old, "http://127.0.0.1:8765", "key-before", "https://school.instructure.com");

        string folder = Extension.Folder(home, user.Path, mac: true);
        var made = Extension.EnsureFor(home, folder, "http://127.0.0.1:8765", "key-after", "https://canvas.other.edu");

        Assert.Equal(folder, made.Path);
        Assert.True(made.Changed);
        Assert.True(Extension.Ready(folder));
        Assert.Equal("key-after", Key(folder));
        Assert.Equal("key-after", Key(old)); // the loaded copy follows the new key, so it never loses its connection
        Assert.Equal(Extension.Version(), JsonNode.Parse(File.ReadAllText(Path.Combine(old, "manifest.json")))!["version"]!.GetValue<string>());

        // The same again changes nothing in either.
        Assert.False(Extension.EnsureFor(home, folder, "http://127.0.0.1:8765", "key-after", "https://canvas.other.edu").Changed);
    }

    [Fact]
    public void With_no_old_folder_nothing_is_made_inside_the_home()
    {
        using var user = new TempDir();
        string home = user[".study-stash"];
        string folder = Extension.Folder(home, user.Path, mac: true);
        Extension.EnsureFor(home, folder, "http://127.0.0.1:8765", "k", "https://school.instructure.com");
        Assert.True(Extension.Ready(folder));
        Assert.False(Directory.Exists(Extension.InHome(home)));
    }

    [Fact]
    public void The_folder_and_the_Study_Stash_folder_it_s_in_are_only_this_person_s()
    {
        if (OperatingSystem.IsWindows()) return; // no such bits there
        using var user = new TempDir();
        string folder = Extension.Folder(user[".study-stash"], user.Path, mac: true);
        Extension.EnsureFor(user[".study-stash"], folder, "http://127.0.0.1:8765", "k", "https://school.instructure.com");
        const UnixFileMode ownerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        Assert.Equal(ownerOnly, File.GetUnixFileMode(folder));
        Assert.Equal(ownerOnly, File.GetUnixFileMode(Path.GetDirectoryName(folder)!));

        // A folder from before, made open to others, is closed up when it's next written.
        string old = user["older"];
        Directory.CreateDirectory(old);
        File.SetUnixFileMode(old, ownerOnly | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        Extension.Ensure(old, "http://127.0.0.1:8765", "k", "https://school.instructure.com");
        Assert.Equal(ownerOnly, File.GetUnixFileMode(old));
    }
}
