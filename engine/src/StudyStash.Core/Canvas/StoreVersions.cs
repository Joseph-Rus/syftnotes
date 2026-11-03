using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace StudyStash.Core.Canvas;

/// <summary>The browsers' two stores that hand out Syft Notes for Canvas: the Chrome Web Store (Chrome, Edge, Brave, Arc
/// and Vivaldi) and Firefox Add-ons.</summary>
public enum AddOnStore
{
    Chrome,
    Firefox,
}

/// <summary>
/// Which add-on each browser store serves right now, asked of the stores' own public services (Google's update service
/// for the Chrome Web Store, Mozilla's add-ons API for Firefox): a version number and nothing about the student. From
/// add-on 1.8 on it finds the library and Canvas by itself, so setup asks for nothing when the student's store serves
/// that and asks for the school's address once when it doesn't, with no app release in between. The answer is kept for
/// a few hours under the app's home, and the last one stays when a store can't be reached; with none ever heard (offline
/// from the start) it's unknown, which asks for the address, as that works with every version.
/// </summary>
public sealed class StoreVersions
{
    /// <summary>The first add-on that finds Canvas by itself.</summary>
    public const string FindsCanvasFrom = "1.8";

    /// <summary>How long an answer is kept before the store is asked again.</summary>
    public static readonly TimeSpan Fresh = TimeSpan.FromHours(3);

    /// <summary>How long a store is given to answer.</summary>
    public static readonly TimeSpan Wait = TimeSpan.FromSeconds(4);

    /// <summary>Google's update check for the add-on: what Chrome itself asks (the current version number of a recent Chrome
    /// stands for the browser).</summary>
    public static string ChromeUrl => "https://clients2.google.com/service/update2/crx?response=updatecheck&acceptformat=crx3&prodversion=130.0.0.0&x=id%3D"
        + Extension.ChromeStoreId + "%26uc";

    /// <summary>Mozilla's page about the add-on, as data.</summary>
    public static string FirefoxUrl => "https://addons.mozilla.org/api/v5/addons/addon/" + Uri.EscapeDataString(Extension.FirefoxId) + "/";

    readonly HttpClient http;
    readonly string cachePath;
    readonly Func<DateTimeOffset> clock;
    readonly SemaphoreSlim gate = new(1, 1);

    public StoreVersions(HttpClient http, string cachePath, Func<DateTimeOffset>? clock = null)
    {
        this.http = http;
        this.cachePath = cachePath;
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>The version a store serves: the kept answer while it's fresh, else the store's, else the last one kept;
    /// null when none was ever heard.</summary>
    public async Task<string?> VersionAsync(AddOnStore store, CancellationToken cancel = default)
    {
        await gate.WaitAsync(cancel);
        try
        {
            var cache = Load();
            string key = store.ToString();
            if (cache.TryGetValue(key, out var kept) && kept.Version.Length > 0 && clock() - kept.At < Fresh) return kept.Version;
            string? now = null;
            try
            {
                using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancel);
                limit.CancelAfter(Wait);
                string body = await http.GetStringAsync(store == AddOnStore.Chrome ? ChromeUrl : FirefoxUrl, limit.Token);
                now = store == AddOnStore.Chrome ? ParseChrome(body) : ParseFirefox(body);
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or OperationCanceledException)
            {
                if (cancel.IsCancellationRequested) throw;
                // Unreachable: what was kept stays.
            }
            if (now is null) return kept?.Version is { Length: > 0 } old ? old : null;
            cache[key] = new Kept(now, clock());
            Save(cache);
            return now;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Whether a store's add-on finds Canvas by itself (<see cref="FindsCanvasFrom"/> on), or what
    /// STUDYSTASH_ADDON_FINDS_CANVAS says (1 or 0, for trying either before a store has it). False when not known.</summary>
    public async Task<bool> FindsCanvasAsync(AddOnStore store, CancellationToken cancel = default)
    {
        if (Override() is { } said) return said;
        return await VersionAsync(store, cancel) is { } v && !Extension.IsOlder(v, FindsCanvasFrom) && Extension.IsVersion(v);
    }

    /// <summary>STUDYSTASH_ADDON_FINDS_CANVAS: true for 1, false for 0, null when unset.</summary>
    public static bool? Override() => Environment.GetEnvironmentVariable("STUDYSTASH_ADDON_FINDS_CANVAS") switch
    {
        "1" => true,
        "0" => false,
        _ => null,
    };

    /// <summary>The version in Google's answer (<c>&lt;updatecheck ... version="1.6"/&gt;</c>); null when the answer has
    /// none (the item isn't there, or isn't published).</summary>
    public static string? ParseChrome(string xml)
    {
        var m = Regex.Match(xml, "<updatecheck\\b[^>]*?\\sversion=\"([0-9][0-9.]*)\"");
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>The version in Mozilla's answer (<c>current_version.version</c>); null when it has none.</summary>
    public static string? ParseFirefox(string json)
    {
        try
        {
            return JsonNode.Parse(json)?["current_version"]?["version"]?.GetValue<string>() is { Length: > 0 } v ? v : null;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    sealed record Kept(string Version, DateTimeOffset At);

    Dictionary<string, Kept> Load()
    {
        try
        {
            if (File.Exists(cachePath) && JsonSerializer.Deserialize<Dictionary<string, Kept>>(File.ReadAllText(cachePath)) is { } d) return d;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            // Unreadable: asked afresh.
        }
        return [];
    }

    void Save(Dictionary<string, Kept> cache)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            File.WriteAllText(cachePath, JsonSerializer.Serialize(cache));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Not kept: asked again next time.
        }
    }
}
