using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace StudyStash.App.Services;

/// <summary>
/// The daily count (docs/daily-count.md): once a local day, while the app runs, one anonymous document to Firestore
/// (a random code for this copy, the day, the version and the kind of computer) so Joey knows how many copies are in
/// use. The code lives in count-code beside the settings; turning "Count this copy" off stops this and deletes it.
/// Every failure is silent and tried again at the next check.
/// </summary>
public sealed class DailyCount
{
    const string Endpoint = "https://firestore.googleapis.com/v1/projects/study-stash-app/databases/(default)/documents/pings";

    /// <summary>For tests: the real endpoint is never asked.</summary>
    public static bool Off { get; set; }

    public static readonly TimeSpan First = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan Every = TimeSpan.FromHours(3);

    readonly string codePath, dayPath;
    readonly Func<bool> on;
    readonly HttpClient http;
    readonly Func<DateTime> now;
    readonly string version;
    readonly Action<string> log;

    public DailyCount(string home, Func<bool> on, HttpClient http, string version, Func<DateTime>? now = null, Action<string>? log = null)
    {
        codePath = Path.Combine(home, "count-code");
        dayPath = Path.Combine(home, "count-day");
        this.on = on;
        this.http = http;
        this.version = version;
        this.now = now ?? (() => DateTime.Now);
        this.log = log ?? (_ => { });
    }

    static string Platform => OperatingSystem.IsMacOS() ? "mac" : OperatingSystem.IsWindows() ? "windows" : "linux";

    /// <summary>This copy's code: 32 random lowercase hex characters, made the first time and kept.</summary>
    public string Code()
    {
        try
        {
            string saved = File.Exists(codePath) ? File.ReadAllText(codePath).Trim() : "";
            if (saved.Length == 32 && saved.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f')) return saved;
        }
        catch (IOException) { }
        string code = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        File.WriteAllText(codePath, code);
        return code;
    }

    /// <summary>Counting is switched off: the code and the day go, so switching on again is a new copy.</summary>
    public void Forget()
    {
        foreach (string p in new[] { codePath, dayPath })
            try { File.Delete(p); } catch (IOException) { }
    }

    /// <summary>One look: counts today unless it already has. True when today is counted.</summary>
    public async Task<bool> RoundAsync(CancellationToken ct = default)
    {
        if (!on()) { Forget(); return false; }
        string day = now().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        try
        {
            if (File.Exists(dayPath) && File.ReadAllText(dayPath).Trim() == day) return true;
            string code = Code();
            string body = $$$$"""{"fields":{"d":{"stringValue":"{{{{day}}}}"},"v":{"stringValue":"{{{{version}}}}"},"p":{"stringValue":"{{{{Platform}}}}"}}}""";
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{Endpoint}?documentId={code}_{day}")
            { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            using var response = await http.SendAsync(request, ct);
            if (response.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.Conflict)) return false;
            File.WriteAllText(dayPath, day);
            return true;
        }
        catch (Exception e) when (e is HttpRequestException or IOException or TaskCanceledException or UnauthorizedAccessException)
        {
            return false; // offline, or a folder that can't be written: the next look tries again
        }
    }

    public async Task RunAsync(CancellationToken ct)
    {
        if (Off) return;
        try
        {
            await Task.Delay(First, ct);
            while (true)
            {
                await RoundAsync(ct);
                await Task.Delay(Every, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { log($"[count] stopped: {e.Message}"); }
    }
}
