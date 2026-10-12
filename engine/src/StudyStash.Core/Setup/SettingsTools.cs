using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace StudyStash.Core.Setup;

/// <summary>A setting the helper may change, as it is now: its entry, its value in words, and what it takes here
/// (the colour themes and models are this computer's own).</summary>
public sealed record SettingNow(SettingEntry Entry, string Value, string Takes = "");

/// <summary>
/// What the settings tools reach: the Settings window that's open. Reading what's set, finding and showing a setting,
/// and changing the few that are the student's own and easy to change back (each shown changing in the window).
/// Nothing here touches a password, what this computer is for, a download's removal, or another app.
/// </summary>
public interface ISettingsDriver
{
    /// <summary>Whether this computer's Settings shows <paramref name="entry"/> (its section, and its role).</summary>
    bool Shown(SettingEntry entry);
    /// <summary>Every changeable setting shown here, as it is now. A library that can't be reached leaves its own out,
    /// and <see cref="LibraryProblem"/> says why.</summary>
    Task<IReadOnlyList<SettingNow>> ValuesAsync();
    /// <summary>Why the library's own settings can't be read now, or null.</summary>
    string? LibraryProblem { get; }
    /// <summary>Shows <paramref name="entry"/>'s page, with the setting picked out.</summary>
    Task OpenAsync(SettingEntry entry);
    /// <summary>Changes it to <paramref name="value"/> (shown in the window as it changes). Null when it's changed;
    /// else why not, in words.</summary>
    Task<string?> ChangeAsync(SettingEntry entry, string value);
    /// <summary>Adds a class to the library, in the sidebar folder <paramref name="folder"/> ("" for Classes). Null when
    /// it's in; else why not.</summary>
    Task<string?> AddClassAsync(string name, string about, string folder);
    /// <summary>Moves a class to the sidebar folder <paramref name="folder"/> (Clubs, Work, one of the student's own;
    /// "" back to Classes). Null when it's moved; else why not.</summary>
    Task<string?> MoveToFolderAsync(string name, string folder);
}

/// <summary>
/// The settings helper's tools (on the same locked-down MCP server as guided setup's, <see cref="Ai.SetupChat.Server"/>),
/// over an <see cref="ISettingsDriver"/>. Each answers in short plain text the AI reads, and says no in words, never
/// by failing: an unknown setting, a value it doesn't take, too many calls in one turn.
/// </summary>
public sealed class SettingsTools(ISettingsDriver driver)
{
    public const int MaxCallsPerTurn = 12;
    public const int MaxClasses = 30;
    int calls, classes;

    /// <summary>A new turn of the chat: its calls are counted afresh.</summary>
    public void NewTurn() => Interlocked.Exchange(ref calls, 0);

    /// <summary>What each tool does: "read", "show" (shows the student a page; changes nothing) or "direct" (changes
    /// a setting). A test fails when a tool isn't named here.</summary>
    public static string? KindOf(string tool) => tool switch
    {
        "get_settings" or "find_setting" => "read",
        "open_settings" => "show",
        "change_setting" or "add_class" or "move_to_folder" => "direct",
        _ => null,
    };

    static CallToolResult Say(string text) => new() { Content = [new TextContentBlock { Text = text }] };
    static CallToolResult No(string text) => new() { IsError = true, Content = [new TextContentBlock { Text = text }] };

    static McpServerToolCreateOptions Named(string name, string title, string description) =>
        new() { Name = name, Title = title, Description = description, ReadOnly = KindOf(name) != "direct", Destructive = false, Idempotent = true, OpenWorld = false };

    /// <summary>Every tool, counted: past <see cref="MaxCallsPerTurn"/> in one turn each says so instead.</summary>
    public List<McpServerTool> Tools() => [.. Made().Select(t => (McpServerTool)new Counted(t, this))];

    sealed class Counted(McpServerTool inner, SettingsTools owner) : DelegatingMcpServerTool(inner)
    {
        public override async ValueTask<CallToolResult> InvokeAsync(RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref owner.calls) > MaxCallsPerTurn) return No("Too many steps at once: ask the student first.");
            try
            {
                return await base.InvokeAsync(request, cancellationToken);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                return No($"Study Stash couldn't do that just now ({e.Message.TrimEnd('.')}). Tell the student, and show them the place with open_settings.");
            }
        }
    }

    List<McpServerTool> Made() =>
    [
        McpServerTool.Create(ValuesAsync, Named("get_settings", "Read the settings",
            "Every setting you can change with change_setting: its key, its section and name in Settings, what it's set to now, and what it "
            + "takes. Call it before changing something, and when the student asks what something is set to.")),
        McpServerTool.Create(
            ([Description("What the student is looking for, in a few words (like dark mode, or zoom lectures).")] string query) => Find(query),
            Named("find_setting", "Find a setting",
                "Searches everything in Settings, not only what you can change: where each match is (section and name), what it does, and its "
                + "change_setting key when it has one.")),
        McpServerTool.Create(
            ([Description("The section: its name as find_setting gives it (like Recording or Your AI).")] string section,
             [Description("The setting's name or key in that section, to pick it out. Leave out for the page itself.")] string? setting = null) =>
                OpenAsync(section, setting ?? ""),
            Named("open_settings", "Show a setting",
                "Shows the student that page of Settings, with the setting picked out. Use it for anything change_setting can't do, then say "
                + "which button or field to use.")),
        McpServerTool.Create(
            ([Description("The setting's key, from get_settings or find_setting.")] string key,
             [Description("The new value, as get_settings says it takes (on, off, dark, 7…).")] string value) => ChangeAsync(key, value),
            Named("change_setting", "Change a setting",
                "Changes one setting now. The student sees it change in Settings, and can change it back there. Only for what the student asked "
                + "for or agreed to.")),
        McpServerTool.Create(
            ([Description("The class's name, like BIO 110 (up to 80 characters).")] string name,
             [Description("What it covers, in the student's words (up to 300 characters). It helps file each lecture.")] string? about = null,
             [Description("Its sidebar folder: Classes (the default) for a course, Clubs for a club, Work for a job, or a folder name of the student's own.")] string? folder = null) =>
                AddClassAsync(name, about ?? "", folder ?? ""),
            Named("add_class", "Add a class, club or job",
                "Adds a class to the student's library, with what it covers, in its sidebar folder: a club goes in Clubs, a job in Work. The "
                + "student sees it in the sidebar and in Settings → Classes, where they can change it.")),
        McpServerTool.Create(
            ([Description("The class's name, as it is in the library.")] string name,
             [Description("The sidebar folder: Clubs, Work, a folder name of the student's own, or Classes to put it back with the courses.")] string folder) =>
                MoveToFolderAsync(name, folder),
            Named("move_to_folder", "Move to a sidebar folder",
                "Moves one of the library's classes to another sidebar folder (Classes, Clubs, Work or one of the student's own). Its lectures "
                + "go with it. Only for what the student asked for or agreed to.")),
    ];

    // --- reading ------------------------------------------------------------------------------------------------------

    async Task<CallToolResult> ValuesAsync()
    {
        var now = await driver.ValuesAsync();
        var b = new StringBuilder();
        foreach (var group in now.GroupBy(n => n.Entry.Section))
        {
            b.Append(SettingsIndex.SectionName(group.Key)).AppendLine(":");
            foreach (var n in group)
            {
                string takes = n.Takes.Length > 0 ? n.Takes : n.Entry.Takes;
                b.Append("- ").Append(n.Entry.Key).Append(" · ").Append(n.Entry.Title).Append(": ").Append(n.Value.Length > 0 ? n.Value : "not set");
                if (takes.Length > 0) b.Append(" (takes ").Append(takes).Append(')');
                b.AppendLine();
            }
        }
        if (driver.LibraryProblem is { } why) b.Append("The library's own settings can't be read now: ").AppendLine(why);
        return Say(b.Length > 0 ? b.ToString().TrimEnd() : "There are no settings to change here right now.");
    }

    CallToolResult Find(string query)
    {
        query = (query ?? "").Trim();
        if (query.Length == 0) return No("Say what to look for.");
        if (query.Length > 200) return No("Keep the search under 200 characters.");
        var hits = SettingsIndex.Search(query, driver.Shown);
        if (hits.Count == 0) return Say($"Nothing in Settings matches \"{query}\". Try other words, or get_settings for what you can change.");
        var b = new StringBuilder();
        foreach (var e in hits)
        {
            b.Append("- ").Append(SettingsIndex.SectionName(e.Section));
            if (!e.IsPage) b.Append(" → ").Append(e.Title);
            else b.Append(" (the page)");
            if (e.Detail.Length > 0) b.Append(": ").Append(e.Detail.TrimEnd('.'));
            b.Append(e.Changeable ? $". change_setting key: {e.Key}" : ". Open it with open_settings");
            b.AppendLine(".");
        }
        return Say(b.ToString().TrimEnd());
    }

    // --- showing ------------------------------------------------------------------------------------------------------

    /// <summary>The entry the AI means in <paramref name="section"/>: the page itself when no setting is named, else the
    /// one named exactly (its title or key), else the best match within that section.</summary>
    SettingEntry? Resolve(string section, string setting)
    {
        if (SettingsIndex.SectionOf(section) is not { } id) return null;
        var here = SettingsIndex.All.Where(e => e.Section == id && driver.Shown(e)).ToList();
        if (here.Count == 0) return null;
        setting = setting.Trim();
        if (setting.Length == 0) return here.FirstOrDefault(e => e.IsPage) ?? here[0];
        return here.FirstOrDefault(e => e.Title.Equals(setting, StringComparison.OrdinalIgnoreCase) || e.Key == setting)
            ?? SettingsIndex.Search(setting, e => e.Section == id && driver.Shown(e), 1).FirstOrDefault()
            ?? here.FirstOrDefault(e => e.IsPage) ?? here[0];
    }

    string Sections() => string.Join(", ", SettingsIndex.Sections.Where(s => SettingsIndex.All.Any(e => e.Section == s.Id && e.IsPage && driver.Shown(e))).Select(s => s.Name));

    async Task<CallToolResult> OpenAsync(string section, string setting)
    {
        if ((section ?? "").Length > 80 || setting.Length > 200) return No("That's too long: give the section's name, and the setting's.");
        if (Resolve(section ?? "", setting) is not { } entry) return No($"There's no section called \"{section}\" here. The sections are: {Sections()}.");
        await driver.OpenAsync(entry);
        string where = SettingsIndex.SectionName(entry.Section) + (entry.IsPage ? "" : " → " + entry.Title);
        return Say($"Settings is showing {where}{(entry.IsPage ? "" : ", picked out")}. Tell the student what to do there.");
    }

    // --- changing -----------------------------------------------------------------------------------------------------

    async Task<CallToolResult> ChangeAsync(string key, string value)
    {
        key = (key ?? "").Trim();
        value = (value ?? "").Trim();
        if (SettingsIndex.ByKey(key) is not { } entry || !driver.Shown(entry))
        {
            if (SettingsIndex.All.FirstOrDefault(e => e.Title.Equals(key, StringComparison.OrdinalIgnoreCase)) is { } named && driver.Shown(named))
                return named.Changeable
                    ? No($"Use its key: {named.Key}.")
                    : No($"{named.Title} isn't one you can change: show it with open_settings and tell the student what to press.");
            return No($"There's no setting \"{key}\" you can change here. get_settings lists them; for anything else, open_settings shows the student where it is.");
        }
        if (value.Length == 0) return No($"Give the new value: {entry.Title} takes {entry.Takes}.");
        if (value.Length > 200) return No("That value is too long.");
        if (await driver.ChangeAsync(entry, value) is { } refused) return No(refused);
        return Say($"Done: {SettingsIndex.SectionName(entry.Section)} → {entry.Title} is now {value}. The student sees it in Settings, and can change it back there.");
    }

    async Task<CallToolResult> AddClassAsync(string name, string about, string folder)
    {
        name = (name ?? "").Trim();
        about = about.Trim();
        if (name.Length == 0) return No("Give the class's name.");
        if (name.Length > 80) return No("That class name is too long: keep it under 80 characters.");
        if (about.Length > 300) return No("Keep what it covers under 300 characters.");
        if (Volatile.Read(ref classes) >= MaxClasses) return No($"That's {MaxClasses} classes in this chat: the student can add more in Settings → Classes.");
        if (Folder(folder) is not { } where) return No("Keep a folder's name under 40 characters.");
        if (await driver.AddClassAsync(name, about, where) is { } refused) return No(refused);
        Interlocked.Increment(ref classes);
        return Say(where.Length == 0 ? $"Added {name}. The student sees it under Classes, and in Settings → Classes."
            : $"Added {name} in {where}. The student sees it there in the sidebar, and in Settings → Classes.");
    }

    async Task<CallToolResult> MoveToFolderAsync(string name, string folder)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0) return No("Give the class's name.");
        if (Folder(folder) is not { } where) return No("Keep a folder's name under 40 characters.");
        if (await driver.MoveToFolderAsync(name, where) is { } refused) return No(refused);
        return Say($"Moved {name} to {(where.Length == 0 ? "Classes" : where)}.");
    }

    /// <summary>A sidebar folder as the library keeps it: "" for Classes; null when it's too long.</summary>
    static string? Folder(string? folder)
    {
        string f = (folder ?? "").Trim();
        if (f.Equals("Classes", StringComparison.OrdinalIgnoreCase)) return "";
        return f.Length > 40 ? null : f;
    }
}
