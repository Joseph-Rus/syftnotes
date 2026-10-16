using System.Text.Json.Nodes;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using StudyStash.Core.Ai;
using StudyStash.Core.Setup;
using StudyStash.Library;

namespace StudyStash.Core.Tests;

/// <summary>A Settings window that only writes down what it was asked: a laptop's (no library-only settings), with a
/// few values set.</summary>
sealed class FakeSettingsDriver : ISettingsDriver
{
    public readonly Dictionary<string, string> Values = new() { ["appearance"] = "system", ["keep_recordings_days"] = "30", ["write_notes"] = "on" };
    public readonly List<string> Opened = [], Changed = [], Classes = [], Moved = [];
    public bool Laptop { get; set; } = true;
    public string? LibraryProblem { get; set; }

    public bool Shown(SettingEntry entry) => SettingsIndex.ShownOn(entry, records: true, laptop: Laptop);

    public Task<IReadOnlyList<SettingNow>> ValuesAsync() =>
        Task.FromResult<IReadOnlyList<SettingNow>>([.. SettingsIndex.All.Where(e => e.Changeable && Shown(e) && Values.ContainsKey(e.Key)).Select(e => new SettingNow(e, Values[e.Key]))]);

    public Task OpenAsync(SettingEntry entry)
    {
        Opened.Add($"{entry.Section}/{entry.Title}");
        return Task.CompletedTask;
    }

    public Task<string?> ChangeAsync(SettingEntry entry, string value)
    {
        if (entry.Key == "appearance" && value is not ("system" or "light" or "dark")) return Task.FromResult<string?>("Mode is system, light or dark.");
        Changed.Add($"{entry.Key}={value}");
        Values[entry.Key] = value;
        return Task.FromResult<string?>(null);
    }

    public Task<string?> AddClassAsync(string name, string about, string folder)
    {
        Classes.Add(folder.Length > 0 ? $"{name} in {folder}" : name);
        return Task.FromResult<string?>(null);
    }

    public Task<string?> MoveToFolderAsync(string name, string folder)
    {
        if (!Classes.Any(c => c == name || c.StartsWith(name + " in ", StringComparison.Ordinal))) return Task.FromResult<string?>($"There's no class called {name}.");
        Moved.Add($"{name} → {(folder.Length > 0 ? folder : "Classes")}");
        return Task.FromResult<string?>(null);
    }
}

/// <summary>Settings' search, and the settings helper's tools called as Claude Code or Codex calls them: over HTTP to the
/// window's own door, against a window that only writes down what it was asked.</summary>
public sealed class SettingsToolsTests : IAsyncLifetime
{
    readonly FakeSettingsDriver driver = new();
    SetupMcpHost host = null!;

    public async Task InitializeAsync() => host = await SetupMcpHost.StartAsync(new SettingsTools(driver));

    public async Task DisposeAsync() => await host.DisposeAsync();

    Task<McpClient> Connect() =>
        McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(host.Url), TransportMode = HttpTransportMode.StreamableHttp,
            AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer " + host.Token },
        }));

    static async Task<(bool Error, string Text)> Call(McpClient mcp, string tool, Dictionary<string, object?>? args = null)
    {
        var r = await mcp.CallToolAsync(tool, args ?? []);
        return (r.IsError == true, string.Concat(r.Content.OfType<TextContentBlock>().Select(c => c.Text)));
    }

    // --- search ---------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("dark mode", "Mode")]
    [InlineData("make it dark", "Mode")]
    [InlineData("colour", "Colour theme")]
    [InlineData("color", "Colour theme")]
    [InlineData("zoom", "Also record what this computer plays (a lecture on Zoom or Teams)")]
    [InlineData("keep recordings", "Keep recordings")]
    [InlineData("password", "Password")]
    [InlineData("canvas", "Canvas")]
    [InlineData("whisper", "Transcription model")]
    [InlineData("start at login", "Open when you log in")]
    [InlineData("iphone", "Phone")]
    [InlineData("hotkeys", "Search and Record work from any app")]
    public void Search_finds_a_setting_by_the_words_a_student_uses(string query, string first)
    {
        var hits = SettingsIndex.Search(query);
        Assert.NotEmpty(hits);
        Assert.Equal(first, hits[0].Title);
    }

    [Fact]
    public void Search_finds_nothing_for_nonsense_and_keeps_to_what_this_computer_shows()
    {
        Assert.Empty(SettingsIndex.Search("xyzzy"));
        Assert.Empty(SettingsIndex.Search("   "));
        // A library that doesn't record has no Recording.
        var hits = SettingsIndex.Search("zoom", e => SettingsIndex.ShownOn(e, records: false, laptop: false));
        Assert.DoesNotContain(hits, e => e.Section == "Recording");
    }

    [Fact]
    public void Every_changeable_setting_has_a_unique_key_and_says_what_it_takes()
    {
        // Unique on any one computer (a setting may sit on another page on a laptop than where the library is).
        foreach (var (records, laptop) in new[] { (true, true), (true, false), (false, false) })
        {
            var keys = SettingsIndex.All.Where(e => e.Changeable && SettingsIndex.ShownOn(e, records, laptop)).Select(e => e.Key).ToList();
            Assert.Equal(keys.Count, keys.Distinct().Count());
        }
        Assert.All(SettingsIndex.All.Where(e => e.Changeable && e.Key is not ("colour_theme" or "transcription_model")), e => Assert.NotEmpty(e.Takes));
        Assert.All(SettingsIndex.All, e => Assert.NotNull(SettingsIndex.Sections.FirstOrDefault(s => s.Id == e.Section).Id));
        Assert.All(SettingsIndex.Sections, s => Assert.Single(SettingsIndex.All, e => e.Section == s.Id && e.IsPage));
    }

    // --- the tools -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Every_tool_is_there_and_each_is_read_show_or_direct()
    {
        await using var mcp = await Connect();
        var names = (await mcp.ListToolsAsync()).Select(t => t.Name).ToList();
        Assert.Equal(["add_class", "change_setting", "find_setting", "get_settings", "move_to_folder", "open_settings"], names.Order());
        foreach (string n in names) Assert.NotNull(SettingsTools.KindOf(n));
    }

    [Fact]
    public async Task It_reads_finds_shows_and_changes_settings()
    {
        await using var mcp = await Connect();
        var (_, values) = await Call(mcp, "get_settings");
        Assert.Contains("appearance · Mode: system (takes system, light or dark)", values);
        Assert.Contains("Recording:", values);

        var (_, found) = await Call(mcp, "find_setting", new() { ["query"] = "zoom lectures" });
        Assert.Contains("change_setting key: computer_audio", found);

        var (openError, opened) = await Call(mcp, "open_settings", new() { ["section"] = "recording", ["setting"] = "language" });
        Assert.False(openError);
        Assert.Contains("Recording → Language", opened);
        Assert.Equal(["Recording/Language"], driver.Opened);

        var (changeError, changed) = await Call(mcp, "change_setting", new() { ["key"] = "appearance", ["value"] = "dark" });
        Assert.False(changeError);
        Assert.Contains("Mode is now dark", changed);
        Assert.Equal(["appearance=dark"], driver.Changed);
    }

    [Fact]
    public async Task It_adds_a_club_to_Clubs_and_moves_a_class_between_folders()
    {
        await using var mcp = await Connect();
        var (e1, t1) = await Call(mcp, "add_class", new() { ["name"] = "SAE Club", ["about"] = "Formula SAE build meetings", ["folder"] = "Clubs" });
        Assert.False(e1);
        Assert.Contains("in Clubs", t1);
        var (e2, _) = await Call(mcp, "add_class", new() { ["name"] = "BIO 110", ["folder"] = "Classes" });
        Assert.False(e2);
        Assert.Equal(["SAE Club in Clubs", "BIO 110"], driver.Classes);

        var (e3, t3) = await Call(mcp, "move_to_folder", new() { ["name"] = "BIO 110", ["folder"] = "Work" });
        Assert.False(e3);
        Assert.Contains("Moved BIO 110 to Work", t3);
        var (e4, _) = await Call(mcp, "move_to_folder", new() { ["name"] = "SAE Club", ["folder"] = "classes" });
        Assert.False(e4);
        Assert.Equal(["BIO 110 → Work", "SAE Club → Classes"], driver.Moved);

        var (e5, t5) = await Call(mcp, "move_to_folder", new() { ["name"] = "Chess Club", ["folder"] = "Clubs" });
        Assert.True(e5);
        Assert.Contains("no class called Chess Club", t5);
        var (e6, t6) = await Call(mcp, "move_to_folder", new() { ["name"] = "BIO 110", ["folder"] = new string('x', 41) });
        Assert.True(e6);
        Assert.Contains("40 characters", t6);
    }

    [Fact]
    public async Task It_says_no_in_words_to_what_it_cant_change()
    {
        await using var mcp = await Connect();
        var (e1, t1) = await Call(mcp, "change_setting", new() { ["key"] = "password", ["value"] = "hunter2" });
        Assert.True(e1);
        Assert.Contains("open_settings", t1);
        var (e2, t2) = await Call(mcp, "change_setting", new() { ["key"] = "appearance", ["value"] = "purple" });
        Assert.True(e2);
        Assert.Contains("system, light or dark", t2);
        // A library's own: not on a laptop.
        driver.Laptop = false;
        var (e3, _) = await Call(mcp, "change_setting", new() { ["key"] = "auto_update", ["value"] = "on" });
        Assert.True(e3);
        var (e4, t4) = await Call(mcp, "open_settings", new() { ["section"] = "Nowhere" });
        Assert.True(e4);
        Assert.Contains("The sections are", t4);
        Assert.Empty(driver.Changed);
    }

    [Fact]
    public void Its_chips_name_what_it_did()
    {
        Assert.Equal("Mode: dark", SetupChat.Chip("change_setting", new JsonObject { ["key"] = "appearance", ["value"] = "dark" }));
        Assert.Equal("Added SAE Club to Clubs", SetupChat.Chip("add_class", new JsonObject { ["name"] = "SAE Club", ["folder"] = "Clubs" }));
        Assert.Equal("Moved BIO 110 to Work", SetupChat.Chip("move_to_folder", new JsonObject { ["name"] = "BIO 110", ["folder"] = "Work" }));
        Assert.Equal("Changed Recording the computer's sound", SetupChat.Chip("change_setting", new JsonObject { ["key"] = "computer_audio", ["value"] = "a very long value indeed here" }));
        Assert.Equal("Showed Notes", SetupChat.Chip("open_settings", new JsonObject { ["section"] = "notes" }));
        Assert.Equal("Searched Settings", SetupChat.Chip("find_setting", null));
    }

    [Fact]
    public void Its_brief_keeps_it_to_the_settings_tools_and_off_passwords()
    {
        string brief = SettingsChat.Brief(windows: false, "This Mac records lectures and is your library.");
        Assert.Contains("only use the study_stash_setup tools", brief);
        Assert.Contains("Never ask for passwords", brief);
        Assert.Contains("This computer: This Mac records lectures", brief);
        Assert.DoesNotContain("\r", brief);
    }
}
