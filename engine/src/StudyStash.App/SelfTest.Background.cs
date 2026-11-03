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
        // A Mac: a PDF for the background library to read once it starts (reading one with PDFKit checked it in with
        // Launch Services as the app, and Syft Notes wouldn't open). AfterQuit checks it read it and stayed out of the
        // way.
        if (OperatingSystem.IsMacOS()) WriteHandout(Configs.Load(host.Home).PoolDir);
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

            if (OperatingSystem.IsMacOS()) await MacAppStillOpensAsync(home, dir, Line);

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

    /// <summary>
    /// A Mac, with the app closed and its background library answering (the app's own executable, run by launchd): it
    /// has read the handout PDF, Launch Services lists nothing as the app (com.study-stash.app), and opening the app the
    /// way Finder, the Dock and Spotlight do (`open`, not `open -n`) starts it. What it starts is the app's `version`, so
    /// no window opens. Throws when any of it fails.
    /// </summary>
    static async Task MacAppStillOpensAsync(string home, string dir, Action<string> line)
    {
        // The file index's first pass, 20 s after the library starts.
        string log = Path.Combine(home, "logs", "library.log");
        bool read = await Until(() => ReadAPdfSinceStarting(log), 60);
        line(read ? "the background library read the handout PDF" : "the background library didn't read the handout PDF in 60 s");
        if (!read) throw new InvalidOperationException("the background library didn't read the handout PDF");

        string listed = Machine.Run("/usr/bin/lsappinfo", ["list"], TimeSpan.FromSeconds(20))?.Stdout ?? "";
        var asApp = Listed(listed, "com.study-stash.app");
        line(asApp.Count == 0 ? "Launch Services lists nothing as Syft Notes with the app closed"
            : $"Launch Services lists {string.Join(", ", asApp)} as Syft Notes with the app closed");
        if (asApp.Count > 0) throw new InvalidOperationException("Launch Services took the background library for the app");

        string bundle = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Environment.ProcessPath!)!, "..", ".."));
        string said = Path.Combine(dir, "open-version.txt");
        File.Delete(said);
        var opened = Machine.Run("/usr/bin/open", ["-W", "--stdout", said, bundle, "--args", "version"], TimeSpan.FromSeconds(30));
        string version = File.Exists(said) ? (await File.ReadAllTextAsync(said)).Trim() : "";
        bool started = version == StudyStash.Core.Engine.Version;
        line(started ? $"opening Syft Notes with open starts it ({version})"
            : $"opening Syft Notes with open didn't start it (open: {(opened is null ? "no answer in 30 s" : $"exit {opened.ExitCode}")})");
        if (!started) throw new InvalidOperationException("opening Syft Notes didn't start it");
    }

    /// <summary>Whether the library's log says it read a file (the handout PDF) since the background library last
    /// started.</summary>
    static bool ReadAPdfSinceStarting(string log)
    {
        try
        {
            using var f = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var lines = new StreamReader(f).ReadToEnd().Split('\n');
            int start = Array.FindLastIndex(lines, l => l.Contains("[background] Syft Notes", StringComparison.Ordinal));
            return start >= 0 && lines.Skip(start).Any(l => System.Text.RegularExpressions.Regex.IsMatch(l, @"\[files\] [1-9][0-9]* read"));
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>The processes `lsappinfo list` shows with this bundle identifier, as "pid N".</summary>
    static List<string> Listed(string lsappinfo, string bundleId)
    {
        var found = new List<string>();
        bool ours = false;
        foreach (string raw in lsappinfo.Split('\n'))
        {
            string l = raw.Trim();
            if (System.Text.RegularExpressions.Regex.IsMatch(raw, @"^\s*\d+\) ")) ours = false;
            else if (l.StartsWith("bundleID=", StringComparison.Ordinal)) ours = l == $"bundleID=\"{bundleId}\"";
            else if (ours && l.StartsWith("pid = ", StringComparison.Ordinal))
            {
                found.Add("pid " + l["pid = ".Length..].Split(' ')[0]);
                ours = false;
            }
        }
        return found;
    }

    /// <summary>A one-page PDF with typed words, in the library's folder; new words each run, so no reading of it is
    /// kept from before.</summary>
    static void WriteHandout(string poolDir)
    {
        string content = $"BT /F1 14 Tf 72 720 Td (Self-test handout {Guid.NewGuid():N}: lexers, parsers and grammars.) Tj ET";
        string[] objects =
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>",
            $"<< /Length {content.Length} >>\nstream\n{content}\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
        ];
        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (int i = 0; i < objects.Length; i++)
        {
            offsets.Add(pdf.Length);
            pdf.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        int xref = pdf.Length;
        pdf.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (int at in offsets) pdf.Append($"{at:D10} 00000 n \n");
        pdf.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        Directory.CreateDirectory(poolDir);
        File.WriteAllText(Path.Combine(poolDir, "Self-test handout.pdf"), pdf.ToString(), new UTF8Encoding(false));
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
