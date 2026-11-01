using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using StudyStash.App.Services;
using StudyStash.Core;
using StudyStash.Core.PhoneDrive;
using StudyStash.Library;

namespace StudyStash.App.Tests;

/// <summary>LibraryService runs a real, disposable copy of the built engine: no LaunchAgent, no Startup entry, just a
/// child process on a spare port in a temp home, gone again when the test is.</summary>
public sealed class LibraryServiceTests
{
    static string BuiltEngine()
    {
        var bin = new DirectoryInfo(AppContext.BaseDirectory); // tests/StudyStash.App.Tests/bin/<config>/net10.0/
        string config = bin.Parent!.Name, framework = bin.Name;
        string engineDir = Path.GetFullPath(Path.Combine(bin.FullName, "..", "..", "..", "..", "..", "src", "StudyStash.Engine", "bin", config, framework));
        return Path.Combine(engineDir, OperatingSystem.IsWindows() ? "studystash.exe" : "studystash");
    }

    static string TempHome()
    {
        string dir = Path.Combine(Path.GetTempPath(), "studystash-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>A config.toml written and ready to serve, on a spare loopback port nothing else is using.</summary>
    static Config ReadyConfig(string home)
    {
        var cfg = Configs.Load(home);
        cfg.PoolDir = Path.Combine(home, "pool");
        cfg.PoolPassword = "library-service-test";
        // 0.0.0.0 (the default): a wildcard bind is what PortStatusAsync's own free-port probe actually conflicts with.
        cfg.WebPort = AppPage.FreePort(19200, 400);
        cfg.OllamaEnabled = false;
        cfg.AutoUpdate = false;
        Configs.Save(cfg);
        return cfg;
    }

    static async Task<LibraryServiceState> Until(LibraryService svc, Func<LibraryServiceState, bool> done, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (done(svc.State)) break;
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }
        return svc.State;
    }

    [Fact]
    public async Task Starts_on_a_free_port_and_answers_with_the_password()
    {
        string exe = BuiltEngine();
        Assert.True(File.Exists(exe), $"build the solution first: no {exe}");
        string home = TempHome();
        try
        {
            var cfg = ReadyConfig(home);
            var svc = new LibraryService(home, cfg, [exe]);
            await svc.StartAsync();
            Assert.Equal(LibraryServiceState.Running, svc.State);

            using var http = new HttpClient();
            var req = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{cfg.WebPort}/api/health");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cfg.PoolPassword);
            using var resp = await http.SendAsync(req, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

            await svc.StopAsync();
        }
        finally { try { Directory.Delete(home, true); } catch (IOException) { } }
    }

    [Fact]
    public async Task Comes_back_on_its_own_when_the_child_is_killed()
    {
        string exe = BuiltEngine();
        Assert.True(File.Exists(exe), $"build the solution first: no {exe}");
        string home = TempHome();
        try
        {
            var cfg = ReadyConfig(home);
            var svc = new LibraryService(home, cfg, [exe]);
            await svc.StartAsync();
            Assert.Equal(LibraryServiceState.Running, svc.State);
            int pid = svc.Pid ?? throw new InvalidOperationException("no pid");
            Process.GetProcessById(pid).Kill(entireProcessTree: true);

            var state = await Until(svc, s => s == LibraryServiceState.Running, TimeSpan.FromSeconds(10));
            Assert.Equal(LibraryServiceState.Running, state);

            await svc.StopAsync();
        }
        finally { try { Directory.Delete(home, true); } catch (IOException) { } }
    }

    [Fact]
    public async Task Stop_frees_the_port_and_removes_the_pid_file()
    {
        string exe = BuiltEngine();
        Assert.True(File.Exists(exe), $"build the solution first: no {exe}");
        string home = TempHome();
        try
        {
            var cfg = ReadyConfig(home);
            var svc = new LibraryService(home, cfg, [exe]);
            await svc.StartAsync();
            Assert.Equal(LibraryServiceState.Running, svc.State);

            await svc.StopAsync();
            Assert.Equal(LibraryServiceState.Stopped, svc.State);
            Assert.False(File.Exists(svc.PidPath));

            using var probe = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            probe.Bind(new IPEndPoint(IPAddress.Loopback, cfg.WebPort)); // throws if still taken
        }
        finally { try { Directory.Delete(home, true); } catch (IOException) { } }
    }

    [Fact]
    public async Task A_second_service_on_the_same_home_steps_aside()
    {
        string exe = BuiltEngine();
        Assert.True(File.Exists(exe), $"build the solution first: no {exe}");
        string home = TempHome();
        try
        {
            var cfg = ReadyConfig(home);
            var first = new LibraryService(home, cfg, [exe]);
            await first.StartAsync();
            Assert.Equal(LibraryServiceState.Running, first.State);

            var second = new LibraryService(home, cfg, [exe]);
            await second.StartAsync();
            Assert.Equal(LibraryServiceState.Elsewhere, second.State);

            await first.StopAsync();
        }
        finally { try { Directory.Delete(home, true); } catch (IOException) { } }
    }

    [Fact]
    public async Task A_second_service_steps_aside_for_a_library_on_this_computer_alone()
    {
        // Just this computer's library listens on 127.0.0.1 only (Windows would let a network-wide probe share its port).
        string exe = BuiltEngine();
        Assert.True(File.Exists(exe), $"build the solution first: no {exe}");
        string home = TempHome();
        try
        {
            var cfg = ReadyConfig(home);
            cfg.WebHost = "127.0.0.1";
            Configs.Save(cfg);
            var first = new LibraryService(home, cfg, [exe]);
            await first.StartAsync();
            Assert.Equal(LibraryServiceState.Running, first.State);

            var second = new LibraryService(home, cfg, [exe]);
            await second.StartAsync();
            Assert.Equal(LibraryServiceState.Elsewhere, second.State);

            await first.StopAsync();
        }
        finally { try { Directory.Delete(home, true); } catch (IOException) { } }
    }

    [Fact]
    public async Task A_service_that_stepped_aside_takes_over_once_the_other_library_is_gone()
    {
        // The app was ended and started again at once: its old library was still shutting down, so the new one
        // stepped aside. Once that one's gone, the new one runs its own.
        string exe = BuiltEngine();
        Assert.True(File.Exists(exe), $"build the solution first: no {exe}");
        string home = TempHome();
        try
        {
            var cfg = ReadyConfig(home);
            cfg.WebHost = "127.0.0.1";
            Configs.Save(cfg);
            var old = new LibraryService(home, cfg, [exe]);
            await old.StartAsync();
            var fresh = new LibraryService(home, cfg, [exe]);
            await fresh.StartAsync();
            Assert.Equal(LibraryServiceState.Elsewhere, fresh.State);

            await fresh.TakeOverIfGoneAsync();
            Assert.Equal(LibraryServiceState.Elsewhere, fresh.State); // the old one still answers: nothing changes

            await old.StopAsync();
            await fresh.TakeOverIfGoneAsync();
            Assert.Equal(LibraryServiceState.Running, fresh.State);
            await fresh.StopAsync();
        }
        finally { try { Directory.Delete(home, true); } catch (IOException) { } }
    }

    [Fact]
    public async Task The_library_stops_by_itself_when_the_app_that_started_it_goes()
    {
        // The app keeps the library's input open; when the app is gone (ended in Task Manager, or crashed) its end of
        // the pipe closes, and the library shuts down cleanly instead of running on with nobody to stop it.
        string exe = BuiltEngine();
        Assert.True(File.Exists(exe), $"build the solution first: no {exe}");
        string home = TempHome();
        try
        {
            var cfg = ReadyConfig(home);
            var info = new ProcessStartInfo(exe)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                ArgumentList = { "--home", home, "serve" },
            };
            info.Environment[Cli.StopWhenInputEndsEnv] = "1";
            using var p = Process.Start(info)!;
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            try
            {
                Assert.True(await HostInfo.WaitForServerAsync(cfg, TimeSpan.FromSeconds(30)), "the library never answered");
                p.StandardInput.Close();
                using var wait = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                await p.WaitForExitAsync(wait.Token);
                Assert.Equal(0, p.ExitCode);
            }
            finally
            {
                if (!p.HasExited) p.Kill(entireProcessTree: true);
            }
        }
        finally { try { Directory.Delete(home, true); } catch (IOException) { } }
    }

    [Fact]
    public async Task No_config_fails_with_not_set_up_yet()
    {
        string home = TempHome();
        try
        {
            var cfg = new Config(home, Path.Combine(home, "pool")) { WebPort = AppPage.FreePort(19600, 400) };
            var svc = new LibraryService(home, cfg, [BuiltEngine()]);
            await svc.StartAsync();
            Assert.Equal(LibraryServiceState.Failed, svc.State);
            Assert.Equal("Not set up yet", svc.Failure);
        }
        finally { try { Directory.Delete(home, true); } catch (IOException) { } }
    }

    [Fact]
    public async Task Kept_alive_by_itself_the_background_library_comes_back_and_goes_with_its_keeper()
    {
        // Windows' Run key (and Linux's autostart) start `helper --keep-alive`: it keeps a library going as its child,
        // starts it again if it dies, lets a second keeper go at once, and once it's ended its library stops by itself.
        string exe = BuiltEngine();
        Assert.True(File.Exists(exe), $"build the solution first: no {exe}");
        string home = TempHome();
        Process? keeper = null;
        try
        {
            var cfg = ReadyConfig(home);
            var info = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, ArgumentList = { "--home", home, "helper", "--keep-alive" } };
            keeper = Process.Start(info)!;
            Assert.True(await HostInfo.WaitForServerAsync(cfg, TimeSpan.FromSeconds(30)), "the kept library never answered");
            var pids = BackgroundLibrary.ReadPids(home);
            Assert.Equal(2, pids.Count);
            Assert.Equal(keeper.Id, pids[0]);

            // A second keeper (login and the app both starting one) goes at once.
            using (var second = Process.Start(info)!)
            {
                using var quick = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                await second.WaitForExitAsync(quick.Token);
                Assert.Equal(0, second.ExitCode);
            }

            // Its library dies: it comes back, as a new child.
            using (var child = Process.GetProcessById(pids[1])) child.Kill();
            int Child() => BackgroundLibrary.ReadPids(home) is [_, var c] ? c : 0;
            var until = DateTime.UtcNow.AddSeconds(30);
            while (DateTime.UtcNow < until && (Child() == 0 || Child() == pids[1])) await Task.Delay(100, TestContext.Current.CancellationToken);
            Assert.NotEqual(pids[1], Child());
            Assert.True(await HostInfo.WaitForServerAsync(cfg, TimeSpan.FromSeconds(30)), "the library didn't come back");

            // The keeper is ended outright (an uninstall, Task Manager): its library sees its input end and stops.
            int last = Child();
            keeper.Kill();
            using var lastChild = Process.GetProcessById(last);
            using var wait = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await lastChild.WaitForExitAsync(wait.Token);
        }
        finally
        {
            if (keeper is { HasExited: false }) keeper.Kill(entireProcessTree: true);
            keeper?.Dispose();
            try { Directory.Delete(home, true); } catch (IOException) { }
        }
    }

    /// <summary>launchd, the Run key or systemd, standing in: "registering" starts the real engine's `helper` as the
    /// computer would, except tied to this test (its input), so nothing outlives it; what it's asked is noted.</summary>
    sealed class PretendLogin(string exe) : IBackgroundLibrary, IDisposable
    {
        public readonly List<string> Calls = [];
        List<string>? args;
        Process? running;

        public bool IsRegistered(string home) => args is not null;
        public IReadOnlyList<string>? Registered(string home) => args;
        public List<(string Home, IReadOnlyList<string> Args)> All() => args is null ? [] : [(args[2], args)];

        public void Register(string home, string program, IReadOnlyList<string>? extra = null)
        {
            Calls.Add("register");
            Kill();
            args = [program, "--home", home, "helper", .. extra ?? []];
            Launch();
        }

        public void Unregister(string home)
        {
            Calls.Add("unregister");
            Kill();
            args = null;
        }

        public void Stop(string home) => Kill();

        public void Start(string home)
        {
            Calls.Add("start");
            if (running is null or { HasExited: true }) Launch();
        }

        public void Restart(string home)
        {
            Kill();
            Launch();
        }

        void Launch()
        {
            var info = new ProcessStartInfo(exe)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            };
            foreach (string a in args!.Skip(1)) info.ArgumentList.Add(a);
            info.Environment[Cli.StopWhenInputEndsEnv] = "1";
            running = Process.Start(info)!;
            running.BeginOutputReadLine();
            running.BeginErrorReadLine();
        }

        public bool Alive => running is { HasExited: false };

        void Kill()
        {
            if (running is null) return;
            try
            {
                running.StandardInput.Close();
                if (!running.WaitForExit(TimeSpan.FromSeconds(15))) running.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }
            running.Dispose();
            running = null;
        }

        public void Dispose() => Kill();
    }

    [Fact]
    public async Task The_app_steps_aside_for_the_background_library_which_answers_the_phone_after_it_quits()
    {
        string exe = BuiltEngine();
        Assert.True(File.Exists(exe), $"build the solution first: no {exe}");
        string home = TempHome();
        using var login = new PretendLogin(exe);
        try
        {
            // Just this computer, a phone linked through Google (standing in: an account, and a folder for Drive).
            var cfg = ReadyConfig(home);
            new AppSettings { SetupDone = true, Role = AppRole.Both }.Save(home);
            new Core.PhoneDrive.GoogleAccount("phone@example.com", "stand-in", DateTimeOffset.UtcNow).Save(home);
            string folder = Path.Combine(home, "drive");
            bool wanted = true;
            var svc = new LibraryService(home, cfg, [exe]) { Background = new BackgroundPlan(login, () => wanted, exe, ["--drive-folder", folder]) };

            await svc.StartAsync();
            Assert.Equal(LibraryServiceState.Elsewhere, svc.State);
            Assert.True(svc.InBackground);
            Assert.Null(svc.Pid); // no library of the app's own beside it
            Assert.Equal(["register"], login.Calls);

            // The app quits: the background library is left running.
            await svc.StopAsync();
            svc.Dispose();
            Assert.True(login.Alive);
            Assert.True(await HostInfo.WaitForServerAsync(cfg, TimeSpan.FromSeconds(5)), "the background library stopped with the app");

            // With the app gone, the phone asks through the folder and is answered, by the library's own link.
            var drive = new Core.PhoneDrive.FolderDrive(folder);
            var names = new Core.PhoneDrive.DriveNames();
            string rid = Guid.NewGuid().ToString();
            string answer = await drive.CreateAsync(names.Answer(rid), "{}"u8.ToArray(), ct: TestContext.Current.CancellationToken);
            var box = new System.Text.Json.Nodes.JsonObject
            {
                ["v"] = 1, ["phone"] = new System.Text.Json.Nodes.JsonObject { ["id"] = "p1", ["name"] = "Sam's iPhone", ["model"] = "iPhone" },
                ["open_until"] = DateTimeOffset.UtcNow.AddMinutes(2).ToString("o"),
                ["requests"] = new System.Text.Json.Nodes.JsonArray(new System.Text.Json.Nodes.JsonObject
                {
                    ["id"] = rid, ["at"] = DateTimeOffset.UtcNow.ToString("o"), ["method"] = "GET", ["path"] = "/library", ["answer"] = answer,
                }),
            };
            await drive.CreateAsync(names.Phones + "p1.json", System.Text.Encoding.UTF8.GetBytes(box.ToJsonString()), ct: TestContext.Current.CancellationToken);
            System.Text.Json.Nodes.JsonObject? got = null;
            var until = DateTime.UtcNow.AddSeconds(60);
            while (got is null && DateTime.UtcNow < until)
            {
                if (await drive.ReadBytesAsync(answer, TestContext.Current.CancellationToken) is { } b
                    && System.Text.Json.Nodes.JsonNode.Parse(b) is System.Text.Json.Nodes.JsonObject a && a["done"]?.GetValue<bool>() == true)
                    got = a;
                else await Task.Delay(100, TestContext.Current.CancellationToken);
            }
            Assert.NotNull(got);
            Assert.Equal(200, (int)got["status"]!);

            // The app opens again and finds it; then the switch goes off: it's taken away and the app runs its own.
            var again = new LibraryService(home, cfg, [exe]) { Background = new BackgroundPlan(login, () => wanted, exe, ["--drive-folder", folder]) };
            await again.StartAsync();
            Assert.True(again.InBackground);
            Assert.Equal(["register"], login.Calls); // already registered, running this program and version: left as it is
            wanted = false;
            await again.SwitchAsync();
            Assert.Equal(LibraryServiceState.Running, again.State);
            Assert.False(again.InBackground);
            Assert.False(login.IsRegistered(home));
            Assert.NotNull(again.Pid);
            await again.StopAsync();
        }
        finally { try { Directory.Delete(home, true); } catch (IOException) { } }
    }

    [Fact]
    public async Task A_background_library_left_on_an_older_version_is_started_again_on_this_one()
    {
        // After an update that didn't restart it (or one that couldn't), the app finds it running the old version.
        string home = TempHome();
        try
        {
            var cfg = ReadyConfig(home);
            var calls = new List<string>();
            var registered = new List<string> { "/Apps/Syft Notes.app/Contents/MacOS/StudyStash", "--home", home, "helper" };
            var bg = new Recording(calls, registered);
            var versions = new Queue<string>(["0.20.0", Engine.Version, Engine.Version]);
            var svc = new LibraryService(home, cfg, ["/nowhere/studystash"], spawn: _ => throw new InvalidOperationException("no child here"),
                waitHealthy: (_, _) => Task.FromResult(true), portStatus: _ => Task.FromResult("ours"),
                health: _ => Task.FromResult<(string, bool)?>((versions.Dequeue(), true)))
            {
                Background = new BackgroundPlan(bg, () => true, registered[0]),
            };
            await svc.StartAsync();
            Assert.Equal(["register"], calls); // registered afresh, which stops the old one and starts this version
            Assert.True(svc.InBackground);
            Assert.Equal(LibraryServiceState.Elsewhere, svc.State);
        }
        finally { try { Directory.Delete(home, true); } catch (IOException) { } }
    }

    sealed class Recording(List<string> calls, List<string> registered) : IBackgroundLibrary
    {
        public bool IsRegistered(string home) => true;
        public IReadOnlyList<string>? Registered(string home) => registered;
        public List<(string Home, IReadOnlyList<string> Args)> All() => [(registered[2], registered)];
        public void Register(string home, string program, IReadOnlyList<string>? extra = null) => calls.Add("register");
        public void Unregister(string home) => calls.Add("unregister");
        public void Stop(string home) => calls.Add("stop");
        public void Start(string home) => calls.Add("start");
        public void Restart(string home) => calls.Add("restart");
    }
}
