using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using StudyStash.Audio;
using StudyStash.Core.Ai;

namespace StudyStash.Core.Tests;

/// <summary>Syft Notes on Linux: recording from PulseAudio or PipeWire (through a stand-in sound server, and the real
/// one where it runs), what a student reads when it won't record, the words for the computer, the tarball it installs
/// and updates from, and the terminals it opens.</summary>
public class LinuxTests
{
    // ---- recording ------------------------------------------------------------------------------------------------

    /// <summary>A stream that hands over the same level every read, until it's told to fail; it keeps whether it was
    /// closed.</summary>
    sealed class FakeStream(float level) : IPulseStream
    {
        public volatile bool Fail;
        public volatile bool Disposed;
        public int Reads;

        public void Read(float[] into)
        {
            Thread.Sleep(5);
            if (Fail) throw new PulseException(PulseError.ConnectionRefused, "Connection refused");
            Array.Fill(into, level);
            Interlocked.Increment(ref Reads);
        }

        public void Dispose() => Disposed = true;
    }

    static async Task<bool> Until(Func<bool> done, double seconds = 5)
    {
        var clock = Stopwatch.StartNew();
        while (!done())
        {
            if (clock.Elapsed.TotalSeconds > seconds) return false;
            await Task.Delay(10);
        }
        return true;
    }

    [Fact]
    public async Task The_microphone_comes_a_tenth_of_a_second_at_a_time_as_16_khz_mono()
    {
        var mic = new FakeStream(0.25f);
        var opened = new List<string?>();
        using var sound = new LinuxSound(false, (device, _) =>
        {
            opened.Add(device);
            return mic;
        });
        var got = new List<float[]>();
        sound.Samples += s =>
        {
            lock (got) got.Add(s);
        };
        sound.Start();
        Assert.True(await Until(() =>
        {
            lock (got) return got.Count >= 3;
        }));
        sound.Stop();
        Assert.Equal([null], opened); // the default microphone, and nothing else
        Assert.Equal(16000, sound.SampleRate);
        Assert.Equal(1, sound.Channels);
        Assert.Equal("Microphone", sound.Name);
        lock (got) Assert.All(got, s => Assert.Equal(LinuxSound.Block, s.Length));
        Assert.True(mic.Disposed);
    }

    [Fact]
    public async Task What_the_computer_plays_is_added_onto_the_microphone()
    {
        var mic = new FakeStream(0.25f);
        var speakers = new FakeStream(0.5f);
        var opened = new List<string?>();
        using var sound = new LinuxSound(true, (device, _) =>
        {
            lock (opened) opened.Add(device);
            return device is null ? mic : speakers;
        });
        float loudest = 0;
        sound.Samples += s => loudest = Math.Max(loudest, s.Max());
        sound.Start();
        Assert.True(await Until(() => loudest >= 0.74f), $"loudest was {loudest}");
        sound.Stop();
        Assert.Equal([null, LinuxSound.Monitor], opened.Order().ToList());
        Assert.Equal("Microphone and computer audio", sound.Name);
        Assert.True(mic.Disposed && speakers.Disposed);
    }

    [Fact]
    public async Task No_speakers_to_listen_to_records_the_microphone_alone()
    {
        var mic = new FakeStream(0.25f);
        using var sound = new LinuxSound(true, (device, _) => device is null ? mic : throw new PulseException(PulseError.NoEntity, "No such entity"));
        int buffers = 0;
        sound.Samples += _ => Interlocked.Increment(ref buffers);
        sound.Start();
        Assert.True(await Until(() => buffers > 0));
        sound.Stop();
    }

    [Fact]
    public async Task A_sound_server_that_goes_away_mid_lecture_says_the_microphone_stopped()
    {
        var mic = new FakeStream(0.1f);
        using var sound = new LinuxSound(false, (_, _) => mic);
        string? failed = null;
        sound.Failed += why => failed = why;
        sound.Start();
        Assert.True(await Until(() => mic.Reads > 0));
        mic.Fail = true;
        Assert.True(await Until(() => failed is not null));
        Assert.StartsWith("The microphone stopped", failed);
        sound.Stop();
    }

    [Fact]
    public void Stopping_says_nothing_went_wrong()
    {
        var mic = new FakeStream(0.1f);
        using var sound = new LinuxSound(false, (_, _) => mic);
        string? failed = null;
        sound.Failed += why => failed = why;
        sound.Start();
        sound.Stop();
        sound.Stop(); // twice is fine
        Assert.Null(failed);
        Assert.True(mic.Disposed);
    }

    [Theory]
    [InlineData(PulseError.Access, MicTroubleKind.Denied)]
    [InlineData(PulseError.NoEntity, MicTroubleKind.NoDevice)]
    [InlineData(PulseError.Busy, MicTroubleKind.InUse)]
    [InlineData(PulseError.ConnectionRefused, MicTroubleKind.Other)]
    [InlineData(PulseError.Timeout, MicTroubleKind.Other)]
    public void A_microphone_that_wont_open_is_said_in_the_students_words(int code, MicTroubleKind kind)
    {
        using var sound = new LinuxSound(false, (_, _) => throw new PulseException(code, "pa error"));
        var e = Assert.Throws<MicrophoneException>(sound.Start);
        Assert.Equal(kind, e.Trouble.Kind);
        Assert.Equal(code, e.Status);
        // Linux has no settings page to open by address, so no button points at a Mac's or Windows' one.
        Assert.False(e.Trouble.HasAction);
        Assert.DoesNotContain("System Settings", e.Trouble.Detail);
        Assert.DoesNotContain("Windows", e.Trouble.Detail);
    }

    [Fact]
    public void No_sound_server_says_recording_needs_pipewire_or_pulseaudio()
    {
        var t = MicTrouble.FromPulse(PulseError.ConnectionRefused);
        Assert.Equal("Syft Notes can't reach your sound system", t.Title);
        Assert.Contains("PipeWire or PulseAudio", t.Detail);
        // No libpulse at all reads the same.
        using var sound = new LinuxSound(false, (_, _) => throw new DllNotFoundException("libpulse-simple.so.0"));
        Assert.Equal("Syft Notes can't reach your sound system", Assert.Throws<MicrophoneException>(sound.Start).Trouble.Title);
    }

    [Fact]
    public void A_sound_server_that_never_answers_doesnt_hang_record()
    {
        using var gate = new ManualResetEventSlim();
        using var sound = new LinuxSound(false, (_, _) =>
        {
            gate.Wait(TimeSpan.FromSeconds(10));
            return new FakeStream(0);
        }) { OpenTimeout = TimeSpan.FromMilliseconds(200) };
        var clock = Stopwatch.StartNew();
        var e = Assert.Throws<MicrophoneException>(sound.Start);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5));
        Assert.Equal("The microphone didn't start", e.Trouble.Title);
        Assert.Contains("restart your computer", e.Trouble.Detail);
        gate.Set();
    }

    /// <summary>The real thing, where it can run: Linux with a sound server (CI's Linux job starts PulseAudio with a
    /// stand-in microphone). Elsewhere there's nothing to prove.</summary>
    [Fact]
    public async Task On_linux_the_real_sound_server_records()
    {
        if (!OperatingSystem.IsLinux() || Environment.GetEnvironmentVariable("STUDYSTASH_PULSE_TESTS") != "1") return;
        using var sound = Microphones.Open(withComputerAudio: true);
        Assert.IsType<LinuxSound>(sound);
        int samples = 0;
        string? failed = null;
        sound.Samples += s => Interlocked.Add(ref samples, s.Length);
        sound.Failed += why => failed = why;
        sound.Start();
        Assert.True(await Until(() => samples >= 16000 / 2, 10), $"{samples} samples in 10 s");
        sound.Stop();
        Assert.Null(failed);
    }

    [Fact]
    public void Linux_records_what_the_computer_plays_and_needs_no_permission_asked()
    {
        if (!OperatingSystem.IsLinux()) return;
        Assert.True(Microphones.CanRecordComputerAudio);
        Assert.Equal(MicAccess.Allowed, Microphones.Access());
        Assert.Equal("", Microphones.SettingsUrl);
        Assert.Contains("Sound settings", RecordingWords.CantHearLinux);
    }

    [Fact]
    public void Linux_on_arm_hears_live_words_with_the_small_whisper()
    {
        Assert.Equal(LiveEngine.SmallWhisper, LiveEngines.EngineFor(Architecture.Arm64, linux: true));
        Assert.Equal(LiveEngine.SmallWhisper, LiveEngines.EngineFor(Architecture.X64, linux: true));
        Assert.Equal(LiveEngine.Whistle, LiveEngines.EngineFor(Architecture.Arm64));
    }

    // ---- words ----------------------------------------------------------------------------------------------------

    [Fact]
    public void Linux_is_this_computer_never_a_mac_or_a_pc()
    {
        Assert.Equal("computer", Machine.DeviceFor(windows: false, linux: true));
        Assert.Equal("computer", Machine.DeviceFor(windows: true, linux: true));
        Assert.Equal("Mac", Machine.DeviceFor(windows: false, linux: false));
        Assert.Equal("PC", Machine.DeviceFor(windows: true, linux: false));
        Assert.Equal("computer", Machine.DeviceForLook(mac: false, linux: true));
        Assert.Equal("PC", Machine.DeviceForLook(mac: false, linux: false));
        Assert.Equal("Mac", Machine.DeviceForLook(mac: true, linux: true));
        Assert.Equal("restart your computer.", MicTrouble.Linux.Other().Detail[^22..]);
    }

    // ---- installing and updating -----------------------------------------------------------------------------------

    [Fact]
    public void Linux_installs_from_the_tarball_for_its_processor()
    {
        Assert.Equal("Syft-Notes-linux-x64.tar.gz", Updates.Installer("Linux", Architecture.X64));
        Assert.Equal("Syft-Notes-linux-arm64.tar.gz", Updates.Installer("Linux", Architecture.Arm64));
        Assert.Null(Updates.Installer("Linux", Architecture.X86));
        Assert.Contains(Updates.LinuxX64Asset, Updates.Installers);
        Assert.Contains(Updates.LinuxArm64Asset, Updates.Installers);
    }

    [Fact]
    public void A_linux_copy_updates_itself_only_from_an_unpacked_tarball()
    {
        using var dir = new TempDir();
        string build = dir["build"], installed = dir["syft-notes"];
        Directory.CreateDirectory(build);
        Directory.CreateDirectory(installed);
        File.WriteAllText(Path.Combine(installed, "study-stash.ini"), "[app]\nversion = 0.20.0\n");
        Assert.Null(Apps.LinuxInstallOf(build, out string why));
        Assert.Equal("not-installed", why);
        Assert.Equal(installed, Apps.LinuxInstallOf(installed + "/", out why));
        Assert.Equal("", why);
        Assert.Equal("0.20.0", Updates.InstalledVersion(installed, "Linux"));
        var host = UpdateHost.ThisComputer(installed, "Linux");
        Assert.Equal(installed, host.AppDir);
        Assert.Null(Updates.WhyNotUpdatable(host));
        Assert.Equal("This copy runs from a build folder, so it doesn't update itself.", Updates.WhyNotUpdatable(UpdateHost.ThisComputer(build, "Linux")));
    }

    [Fact]
    public void A_linux_copy_in_a_folder_it_cant_change_says_how_to_update_instead()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;
        if (Environment.UserName == "root") return; // root writes anywhere
        using var dir = new TempDir();
        string installed = dir["opt/syft-notes"];
        Directory.CreateDirectory(installed);
        File.WriteAllText(Path.Combine(installed, "study-stash.ini"), "[app]\nversion = 0.20.0\n");
        File.SetUnixFileMode(installed, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            var host = UpdateHost.ThisComputer(installed, "Linux");
            Assert.Equal("", host.AppDir);
            Assert.True(host.CantReplace);
            Assert.Contains("isn't writable", Updates.WhyNotUpdatable(host));
        }
        finally
        {
            File.SetUnixFileMode(installed, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    /// <summary>A release's tarball as linux/build.sh packs it: one folder, syft-notes/, with the program (here a script
    /// that prints its version) and study-stash.ini.</summary>
    static byte[] Tarball(TempDir dir, string version, string? printed = null)
    {
        string stage = dir["stage-" + version];
        string app = Path.Combine(stage, "syft-notes");
        Directory.CreateDirectory(app);
        File.WriteAllText(Path.Combine(app, "StudyStash"), $"#!/bin/sh\necho {printed ?? version}\n");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(Path.Combine(app, "StudyStash"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        File.WriteAllText(Path.Combine(app, "study-stash.ini"), $"[app]\nversion = {version}\n");
        File.WriteAllText(Path.Combine(app, "new.txt"), "new");
        string tar = dir[$"Syft-Notes-{version}.tar.gz"];
        Assert.Equal(0, Machine.Run("tar", ["-czf", tar, "-C", stage, "syft-notes"], TimeSpan.FromSeconds(30))!.ExitCode);
        return File.ReadAllBytes(tar);
    }

    static string Sums(string name, byte[] bytes) => $"{Convert.ToHexStringLower(SHA256.HashData(bytes))}  {name}\n";

    static (UpdateHost Host, string App) Installed(TempDir dir, FakeDownloads downloads)
    {
        string app = dir["share/syft-notes"];
        Directory.CreateDirectory(app);
        File.WriteAllText(Path.Combine(app, "StudyStash"), "old");
        File.WriteAllText(Path.Combine(app, "stale.txt"), "old");
        File.WriteAllText(Path.Combine(app, "study-stash.ini"), "[app]\nversion = 0.20.0\n");
        return (new UpdateHost
        {
            System = "Linux", AppDir = app, Http = downloads.Client(), Run = Machine.Run, Places = new ServicePlaces(dir["agents"], dir["startup"], dir["systemd"]),
            VersionOf = Updates.VersionOf, TempDir = dir["tmp"],
        }, app);
    }

    [Fact]
    public async Task A_linux_update_unpacks_the_tarball_checks_it_runs_and_swaps_it_in()
    {
        if (OperatingSystem.IsWindows()) return; // a shell script stands in for the program
        using var dir = new TempDir();
        string asset = Updates.Installer("Linux")!;
        byte[] tar = Tarball(dir, "9.9.9");
        var downloads = new FakeDownloads(new() { ["https://dl/linux.tar.gz"] = tar, ["https://dl/sums.txt"] = Encoding.UTF8.GetBytes(Sums(asset, tar)) });
        var (host, app) = Installed(dir, downloads);
        var said = new List<string>();
        bool ok = await Updates.ApplyAsync(new Release("v9.9.9", [9, 9, 9], "u", "p", new Dictionary<string, string>
        {
            [asset] = "https://dl/linux.tar.gz", [Updates.ChecksumsAsset] = "https://dl/sums.txt",
        }), dir["home"], host, said.Add);
        Assert.True(ok, string.Join(" | ", said));
        Assert.Equal(["Installing v9.9.9...", "Installed v9.9.9."], said);
        Assert.Equal("9.9.9", Updates.InstalledVersion(app, "Linux"));
        Assert.Equal("9.9.9", Updates.VersionOf(Updates.LinuxProgram(app)));
        Assert.False(File.Exists(Path.Combine(app, "stale.txt"))); // the whole folder is replaced
        Assert.True(File.Exists(Path.Combine(app, "new.txt")));
        Assert.Equal(["syft-notes"], Directory.GetFileSystemEntries(Path.GetDirectoryName(app)!).Select(Path.GetFileName));
    }

    [Fact]
    public async Task A_linux_download_that_doesnt_match_its_checksum_or_doesnt_run_leaves_this_version()
    {
        if (OperatingSystem.IsWindows()) return;
        using var dir = new TempDir();
        string asset = Updates.Installer("Linux")!;
        byte[] good = Tarball(dir, "9.9.9"), lies = Tarball(dir, "9.9.8", printed: "1.0.0");
        var downloads = new FakeDownloads(new()
        {
            ["https://dl/good.tar.gz"] = good, ["https://dl/bad-sums.txt"] = Encoding.UTF8.GetBytes(Sums(asset, [1, 2, 3])),
            ["https://dl/lies.tar.gz"] = lies, ["https://dl/lies-sums.txt"] = Encoding.UTF8.GetBytes(Sums(asset, lies)),
        });
        var (host, app) = Installed(dir, downloads);
        var said = new List<string>();
        Assert.False(await Updates.ApplyAsync(new Release("v9.9.9", [9, 9, 9], "u", "p", new Dictionary<string, string>
        {
            [asset] = "https://dl/good.tar.gz", [Updates.ChecksumsAsset] = "https://dl/bad-sums.txt",
        }), dir["home"], host, said.Add));
        Assert.Contains("The download didn't match its checksum, so this version stays.", said);
        Assert.False(await Updates.ApplyAsync(new Release("v9.9.8", [9, 9, 8], "u", "p", new Dictionary<string, string>
        {
            [asset] = "https://dl/lies.tar.gz", [Updates.ChecksumsAsset] = "https://dl/lies-sums.txt",
        }), dir["home"], host, said.Add));
        Assert.Contains("The v9.9.8 download didn't run on this computer, so this version stays.", said);
        Assert.Equal("old", File.ReadAllText(Path.Combine(app, "StudyStash")));
        Assert.Equal(["syft-notes"], Directory.GetFileSystemEntries(Path.GetDirectoryName(app)!).Select(Path.GetFileName));
    }

    [Fact]
    public async Task A_lecture_that_starts_during_the_download_puts_a_linux_update_off()
    {
        if (OperatingSystem.IsWindows()) return;
        using var dir = new TempDir();
        string asset = Updates.Installer("Linux")!;
        byte[] tar = Tarball(dir, "9.9.9");
        var downloads = new FakeDownloads(new() { ["https://dl/linux.tar.gz"] = tar, ["https://dl/sums.txt"] = Encoding.UTF8.GetBytes(Sums(asset, tar)) });
        var (host, app) = Installed(dir, downloads);
        var said = new List<string>();
        Assert.False(await Updates.ApplyAsync(new Release("v9.9.9", [9, 9, 9], "u", "p", new Dictionary<string, string>
        {
            [asset] = "https://dl/linux.tar.gz", [Updates.ChecksumsAsset] = "https://dl/sums.txt",
        }), dir["home"], host, said.Add, ready: () => false));
        Assert.Contains(Updates.PutOff, said);
        Assert.Equal("old", File.ReadAllText(Path.Combine(app, "StudyStash")));
    }

    // ---- terminals ------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("gnome-terminal", "--working-directory=/c/CS 101|--|bash|/s.sh")]
    [InlineData("ptyxis", "--new-window|--working-directory=/c/CS 101|--|bash|/s.sh")]
    [InlineData("konsole", "--workdir|/c/CS 101|-e|bash|/s.sh")]
    [InlineData("xfce4-terminal", "--working-directory=/c/CS 101|-x|bash|/s.sh")]
    [InlineData("kitty", "--directory|/c/CS 101|bash|/s.sh")]
    [InlineData("alacritty", "--working-directory|/c/CS 101|-e|bash|/s.sh")]
    [InlineData("wezterm", "start|--cwd|/c/CS 101|--|bash|/s.sh")]
    [InlineData("xterm", "-e|bash|/s.sh")]
    public void Each_linux_terminal_is_told_the_folder_and_the_script_its_own_way(string terminal, string args)
    {
        Assert.Equal(args.Split('|'), Terminal.LinuxArgs(terminal, "/c/CS 101", "/s.sh"));
    }

    [Fact]
    public void A_linux_terminal_runs_the_command_in_the_folder_then_the_students_shell()
    {
        string script = Terminal.LinuxScript("/home/zoë/Study Stash/CS 101", "/usr/bin/claude", ["--add-dir", "it's here"],
            new Dictionary<string, string> { ["HOME"] = "/tmp/agy home" });
        Assert.Equal("#!/bin/bash\ncd '/home/zoë/Study Stash/CS 101' || exit 1\nHOME='/tmp/agy home' '/usr/bin/claude' '--add-dir' 'it'\\''s here'\nexec \"${SHELL:-bash}\" -l\n", script);
        // Every terminal Syft Notes knows has its own way told, and a picked one that isn't here gives way to one that is.
        Assert.Equal("konsole", Terminal.Used("ghostty", [("konsole", "Konsole"), ("xterm", "XTerm")]));
        Assert.All(Terminal.Linux, t => Assert.NotEmpty(Terminal.LinuxArgs(t.Id, "/f", "/s")));
    }

    [Fact]
    public void The_script_bash_runs_quotes_the_command_so_the_shell_reads_it_back_whole()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;
        using var dir = new TempDir();
        string folder = dir["CS 101 & $HOME"];
        Directory.CreateDirectory(folder);
        string sh = dir["s.sh"];
        File.WriteAllText(sh, Terminal.LinuxScript(folder, "printf", ["%s|", "a b", "$PATH", "it's"]).Replace("exec \"${SHELL:-bash}\" -l\n", ""));
        var r = Machine.Run("bash", [sh], TimeSpan.FromSeconds(10))!;
        Assert.Equal(0, r.ExitCode);
        Assert.Equal("a b|$PATH|it's|", r.Stdout);
    }
}
