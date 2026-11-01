using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using StudyStash.App.Services;
using StudyStash.Core;
using StudyStash.Core.PhoneDrive;

namespace StudyStash.App;

/// <summary>
/// The background library on CI's throwaway computers (<see cref="BackgroundOn"/>): Just this computer's run ends by
/// linking a phone through a stand-in for Google (google.json for an account, a folder of this run for the Drive
/// folder) and turning on "Answer your phone when Syft Notes is closed" for real, so the computer itself runs the
/// library (a LaunchAgent, the Run key, a systemd --user unit or an autostart entry). Then the app quits as usual, and
/// the script that started it runs the program once more with STUDYSTASH_SELFTEST_AFTER_QUIT=1 (<see cref="AfterQuit"/>):
/// the library still answers, a phone request put in the stand-in folder gets its answer, and taking the registration
/// away leaves nothing behind.
/// </summary>
public static partial class SelfTest
{
    /// <summary>The stand-in for the Google Drive folder, inside the run's own folder.</summary>
    internal static string DriveFolder(string home) => Path.Combine(home, "drive-stand-in");

    static async Task RunBackgroundAsync(AppHost host)
    {
        // A phone linked through Google, standing in: an account in google.json, and a folder for the Drive folder.
        new GoogleAccount("phone@example.com", "stand-in", DateTimeOffset.UtcNow).Save(host.Home);
        await host.Drive.RefreshAsync();
        host.Save(s => s.AnswerPhoneWhenClosed = true);
        await host.SyncBackgroundAsync();
        var lib = host.LocalLibrary ?? throw new InvalidOperationException("no library of the app's own");
        string way = host.Background is BackgroundLibrary b ? Way(b, host.Home) : "?";
        Say($"background: wanted {host.BackgroundWanted}, registered {host.Background.IsRegistered(host.Home)} ({way}), " +
            $"in the background {lib.InBackground}, state {lib.State}{(lib.BackgroundProblem is { } p ? $" ({p})" : "")}");
        if (!lib.InBackground) throw new InvalidOperationException("the library didn't move to the background");
        if (lib.Pid is not null) throw new InvalidOperationException("the app still runs a library of its own beside the background one");
        bool back = await Until(() => host.Library == LibraryState.Connected, 30);
        Say(back ? "the app reads its library in the background" : $"the app can't reach its library in the background: {host.Library}");
        if (!back) throw new InvalidOperationException("the app lost its library when it moved to the background");

        // Settings → Phone with the switch on.
        Shell.ShowSettings();
        if ((Shell.Windows.Settings?.Content as Control)?.DataContext is SettingsModel sm)
        {
            sm.Section = "Phone";
            await Settle();
            Say($"settings → phone: \"{sm.Phones.Google?.WhenClosedTitle}\" {(sm.Phones.Google?.AnswerWhenClosed == true ? "on" : "off")}");
            Shot(Shell.Windows.Settings, "settings-phone-background");
        }
        Shell.Windows.Settings?.Close();
        Say("background library registered: quitting leaves it answering the phone");
    }

    static string Way(BackgroundLibrary b, string home) => b.System switch
    {
        "Darwin" => "launchd",
        "Windows" => "the Run key",
        _ => b.LinuxWay(home) ?? "none",
    };

    /// <summary>
    /// Run by the script once the app has quit (STUDYSTASH_SELFTEST_AFTER_QUIT=1), with no window: checks the library
    /// still answers, puts a phone's request in the stand-in Drive folder and waits for its answer, then takes the
    /// background library away and checks nothing is left: no registration, no process, the port let go. Writes
    /// background.txt beside selftest.txt, "ok" last when it all held. Its exit code: 0, or 1.
    /// </summary>
    public static int AfterQuit(string home) => AfterQuitAsync(home).GetAwaiter().GetResult();

    static async Task<int> AfterQuitAsync(string home)
    {
        string dir = Dir ?? home;
        var lines = new List<string>();
        void Line(string s)
        {
            lines.Add(s);
            Console.WriteLine(s);
        }
        var bg = BackgroundLibrary.ThisComputer();
        int code = 0;
        try
        {
            var cfg = Configs.Load(home);
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            Line($"after quitting: registered {bg.IsRegistered(home)} ({Way(bg, home)})");
            if (!bg.IsRegistered(home)) throw new InvalidOperationException("nothing is registered to run the library in the background");

            // The library still answers, as the background one.
            JsonObject? health = null;
            var until = DateTime.UtcNow.AddSeconds(30);
            while (health is null && DateTime.UtcNow < until)
            {
                health = await GetAsync(http, cfg, "/api/health");
                if (health is null) await Task.Delay(250);
            }
            Line(health is null ? "the library doesn't answer with the app closed"
                : $"the library answers with the app closed: version {health["version"]}, background {health["background"]}");
            if (health?["background"]?.GetValue<bool>() != true) throw new InvalidOperationException("no background library answers");

            // A phone's request, the way the iPhone app makes one: its answer file first, then its box with the request.
            var drive = new FolderDrive(DriveFolder(home));
            var names = new DriveNames();
            string rid = Guid.NewGuid().ToString();
            string answer = await drive.CreateAsync(names.Answer(rid), "{}"u8.ToArray());
            var box = new JsonObject
            {
                ["v"] = 1, ["phone"] = new JsonObject { ["id"] = "self-test-phone", ["name"] = "Self-test iPhone", ["model"] = "iPhone" },
                ["open_until"] = DateTimeOffset.UtcNow.AddMinutes(5).ToString("o"),
                ["requests"] = new JsonArray(new JsonObject
                {
                    ["id"] = rid, ["at"] = DateTimeOffset.UtcNow.ToString("o"), ["method"] = "GET", ["path"] = "/library", ["answer"] = answer,
                }),
            };
            await drive.CreateAsync(names.Phones + "self-test-phone.json", Encoding.UTF8.GetBytes(box.ToJsonString()));
            Line("a phone asked for /library through the stand-in Drive folder");
            JsonObject? got = null;
            var asked = DateTime.UtcNow;
            until = asked.AddSeconds(120);
            while (DateTime.UtcNow < until)
            {
                if (await drive.ReadBytesAsync(answer) is { } bytes && JsonNode.Parse(bytes) is JsonObject a && a["done"]?.GetValue<bool>() == true)
                {
                    got = a;
                    break;
                }
                await Task.Delay(250);
            }
            Line(got is null ? "no answer reached the phone in 120 s"
                : $"the phone got its answer with the app closed: {got["status"]} in {(DateTime.UtcNow - asked).TotalSeconds:0} s");
            if ((int?)got?["status"] != 200) throw new InvalidOperationException("the phone wasn't answered with the app closed");

            // Taken away: nothing registered, nothing running, the port let go.
            var pids = BackgroundLibrary.ReadPids(home);
            bg.Unregister(home);
            bool free = false;
            until = DateTime.UtcNow.AddSeconds(30);
            while (DateTime.UtcNow < until && !(free = HostInfo.PortFree(cfg.WebPort))) await Task.Delay(250);
            bool gone = pids.All(Gone);
            string left = string.Join(", ", new[]
            {
                bg.IsRegistered(home) ? "its registration" : null,
                File.Exists(bg.PlistPath(home)) ? bg.PlistPath(home) : null,
                File.Exists(bg.UnitPath(home)) ? bg.UnitPath(home) : null,
                File.Exists(bg.AutostartPath(home)) ? bg.AutostartPath(home) : null,
                free ? null : $"port {cfg.WebPort} still taken",
                gone ? null : "a process still running",
            }.OfType<string>());
            Line(left.Length == 0 ? "taken away: nothing registered, nothing running, the port free" : $"FAILED left behind: {left}");
            if (left.Length > 0) code = 1;
        }
        catch (Exception e)
        {
            Line($"FAILED {e.Message}");
            code = 1;
            try
            {
                bg.Unregister(home);
            }
            catch (Exception again) when (again is IOException or UnauthorizedAccessException or InvalidOperationException or PlatformNotSupportedException)
            {
            }
        }
        lines.Add(code == 0 ? "ok" : "failed");
        Directory.CreateDirectory(dir);
        await File.WriteAllLinesAsync(Path.Combine(dir, "background.txt"), lines);
        return code;
    }

    static bool Gone(int pid)
    {
        try
        {
            using var p = System.Diagnostics.Process.GetProcessById(pid);
            return p.WaitForExit(TimeSpan.FromSeconds(20));
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    static async Task<JsonObject?> GetAsync(HttpClient http, Config cfg, string path)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{cfg.WebPort}{path}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cfg.PoolPassword);
            using var r = await http.SendAsync(request);
            return r.IsSuccessStatusCode ? JsonNode.Parse(await r.Content.ReadAsStringAsync()) as JsonObject : null;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
