using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace StudyStash.Core.Ai;

/// <summary>
/// A program run in a terminal nobody sees: a pseudo-terminal Syft Notes holds itself, with no window, no Terminal app
/// and nothing on the screen. An AI's sign-in (Claude Code's, Codex's, agy's) is made for a person at a terminal, and
/// some of it stops at once without one (Claude Code's own "Raw mode is not supported"); in here it finds a terminal,
/// and what it shows comes back as text for <see cref="AgentSignIn"/> to read, the way a person would read the screen.
/// <para>
/// On a Mac and Linux it's the system's own pseudo-terminal (<c>posix_openpt</c>), the program started in a session of
/// its own with that terminal as its controlling one (<c>posix_spawn</c>, nothing of .NET's runs in between). On
/// Windows it's the system's pseudo console (ConPTY, <c>CreatePseudoConsole</c>), whose console host runs headless:
/// no console window is made, so nothing flashes. Nothing here needs a library of its own or an entitlement, so the
/// signed app is as it was.
/// </para>
/// <para>The terminal is wide (<see cref="Columns"/>): a program that wraps its text to the terminal's width keeps a long
/// sign-in address on one line.</para>
/// </summary>
public sealed class HiddenTerminal : IDisposable
{
    public const int Columns = 1000;
    public const int Rows = 50;

    readonly IBackend backend;
    readonly TaskCompletionSource<int> exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly TaskCompletionSource ended = new(TaskCreationOptions.RunContinuationsAsynchronously);
    int reading;
    volatile bool disposed;

    HiddenTerminal(IBackend backend)
    {
        this.backend = backend;
        var t = new Thread(() =>
        {
            int code = backend.WaitForExit();
            exit.TrySetResult(code);
        }) { IsBackground = true, Name = "hidden terminal: exit" };
        t.Start();
    }

    /// <summary>The program's process id.</summary>
    public int Id => backend.Pid;

    /// <summary>Its exit code once it has stopped (a signal's number plus 128 on a Mac or Linux).</summary>
    public Task<int> Exit => exit.Task;

    public bool HasExited => exit.Task.IsCompleted;

    /// <summary>Done once everything it showed has been handed on (the terminal closed behind it).</summary>
    public Task Ended => ended.Task;

    /// <summary>Starts <paramref name="launch"/> in a hidden terminal of its own, with exactly <paramref name="env"/> as
    /// its environment. Throws <see cref="IOException"/> when the terminal or the program couldn't be started.</summary>
    public static HiddenTerminal Start(Launch launch, IReadOnlyDictionary<string, string> env, string? cwd = null) =>
        new(OperatingSystem.IsWindows() ? WindowsBackend.Start(launch, env, cwd) : UnixBackend.Start(launch, env, cwd));

    /// <summary>The environment a program gets in here: this process's own, with <paramref name="changes"/> on top (a
    /// null value takes one out), and a TERM saying what kind of terminal it's in when there's none.</summary>
    public static Dictionary<string, string> Environment(IEnumerable<KeyValuePair<string, string?>> changes)
    {
        var env = new Dictionary<string, string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (System.Collections.DictionaryEntry e in System.Environment.GetEnvironmentVariables())
            if (e.Key is string k && e.Value is string v) env[k] = v;
        foreach (var (k, v) in changes)
        {
            if (v is null) env.Remove(k);
            else env[k] = v;
        }
        if (!OperatingSystem.IsWindows() && !env.ContainsKey("TERM")) env["TERM"] = "xterm-256color";
        return env;
    }

    /// <summary>Starts handing on what the program shows, as text, to <paramref name="shown"/> (on a thread of its own).
    /// Once only.</summary>
    public void Read(Action<string> shown)
    {
        if (Interlocked.Exchange(ref reading, 1) == 1) return;
        var t = new Thread(() =>
        {
            var decoder = new UTF8Encoding(false).GetDecoder();
            var bytes = new byte[8192];
            var chars = new char[8192 + 4];
            try
            {
                while (!disposed)
                {
                    int n = backend.Read(bytes, () => disposed);
                    if (n <= 0) break;
                    int c = decoder.GetChars(bytes, 0, n, chars, 0, flush: false);
                    if (c > 0) shown(new string(chars, 0, c));
                }
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException)
            {
            }
            finally
            {
                ended.TrySetResult();
            }
        }) { IsBackground = true, Name = "hidden terminal: output" };
        t.Start();
    }

    /// <summary>Types <paramref name="text"/> into the terminal, as keys pressed (Enter is "\r").</summary>
    public void Type(string text)
    {
        if (disposed || text.Length == 0) return;
        try
        {
            backend.Write(Encoding.UTF8.GetBytes(text));
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
        }
    }

    /// <summary>Stops the program and everything it started, if it's still going.</summary>
    public void Stop()
    {
        if (HasExited) return;
        backend.Kill();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Stop();
        backend.Close(exit.Task);
    }

    interface IBackend
    {
        int Pid { get; }
        int WaitForExit();
        /// <summary>Bytes the program showed, 0 at its end; looks at <paramref name="stop"/> now and then.</summary>
        int Read(byte[] into, Func<bool> stop);
        void Write(byte[] bytes);
        void Kill();
        void Close(Task<int> exited);
    }

    /// <summary>Ends a process and every process it started (as <see cref="Process.Kill(bool)"/> does for the ones .NET
    /// started itself).</summary>
    static void KillTree(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            p.Kill(entireProcessTree: true);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
        }
    }

    // --- a Mac and Linux ---------------------------------------------------------------------------------------------

    sealed class UnixBackend : IBackend
    {
        readonly int master;
        int closed;

        UnixBackend(int master, int pid)
        {
            this.master = master;
            Pid = pid;
        }

        public int Pid { get; }

        static bool Mac => OperatingSystem.IsMacOS();
        // Linux's: a Mac's are only used through openpty and its own FIOCLEX.
        const int NoCtty = 0x100, CloseOnExec = 0x80000;
        const nuint LinuxSetWindowSize = 0x5414, MacCloseOnExec = 0x20006601;
        const int ReadWrite = 2;
        const int EINTR = 4;
        const short PollIn = 0x1;
        // posix_spawn's flags: the default for every signal and none blocked (the runtime ignores some, and an ignored
        // signal stays ignored across exec), a session of its own; on a Mac, also no descriptor of Syft Notes' own.
        const short SetSigDefault = 0x04, SetSigMask = 0x08;
        static short SetSid => (short)(Mac ? 0x0400 : 0x80);
        const short MacCloseOthers = 0x4000;

        public static UnixBackend Start(Launch launch, IReadOnlyDictionary<string, string> env, string? cwd)
        {
            var size = new WinSize { Rows = Rows, Columns = Columns };
            int master, slave = -1;
            // A Mac's ioctl and fcntl take their last argument the way C's "..." does, which a call from .NET can't give
            // on Apple silicon: openpty sets the size itself, and the ends are kept from other programs with the
            // two-argument ioctl. Linux's posix_openpt and open take that at once.
            if (Mac)
            {
                if (openpty(out master, out slave, IntPtr.Zero, IntPtr.Zero, ref size) != 0) throw new IOException($"no pseudo-terminal: {Marshal.GetLastPInvokeError()}");
                ioctl(master, MacCloseOnExec);
                ioctl(slave, MacCloseOnExec);
            }
            else
            {
                master = posix_openpt(ReadWrite | NoCtty | CloseOnExec);
                if (master < 0) throw new IOException($"no pseudo-terminal: {Marshal.GetLastPInvokeError()}");
            }
            var argv = new List<IntPtr>();
            var envp = new List<IntPtr>();
            IntPtr actions = IntPtr.Zero, attr = IntPtr.Zero, sigs = IntPtr.Zero;
            try
            {
                if (!Mac && (grantpt(master) != 0 || unlockpt(master) != 0)) throw new IOException($"pseudo-terminal not unlocked: {Marshal.GetLastPInvokeError()}");
                var nameBuf = new byte[256];
                if (ptsname_r(master, nameBuf, (nuint)nameBuf.Length) != 0) throw new IOException("pseudo-terminal has no name");
                string name = Encoding.UTF8.GetString(nameBuf, 0, Array.IndexOf(nameBuf, (byte)0) is var z and >= 0 ? z : nameBuf.Length);
                if (!Mac)
                {
                    // Its size, set on the far end Syft Notes holds open until the program has it.
                    slave = open(name, ReadWrite | NoCtty | CloseOnExec);
                    if (slave < 0) throw new IOException($"pseudo-terminal not opened: {Marshal.GetLastPInvokeError()}");
                    ioctl(slave, LinuxSetWindowSize, ref size);
                }

                actions = Marshal.AllocHGlobal(1024);
                attr = Marshal.AllocHGlobal(1024);
                sigs = Marshal.AllocHGlobal(256);
                Zero(actions, 1024);
                Zero(attr, 1024);
                Zero(sigs, 256);
                Check(posix_spawn_file_actions_init(actions), "file actions");
                Check(posix_spawnattr_init(attr), "attributes");
                // Opened by the program itself, as its session's leader, it's the program's controlling terminal (tried
                // on a Mac: /dev/tty opens in it), then its output and errors too.
                Check(posix_spawn_file_actions_addopen(actions, 0, name, ReadWrite, 0), "terminal");
                Check(posix_spawn_file_actions_adddup2(actions, 0, 1), "output");
                Check(posix_spawn_file_actions_adddup2(actions, 0, 2), "errors");
                if (cwd is not null) Check(posix_spawn_file_actions_addchdir_np(actions, cwd), "folder");
                sigfillset(sigs);
                posix_spawnattr_setsigdefault(attr, sigs);
                sigemptyset(sigs);
                posix_spawnattr_setsigmask(attr, sigs);
                Check(posix_spawnattr_setflags(attr, (short)(SetSigDefault | SetSigMask | SetSid | (Mac ? MacCloseOthers : 0))), "flags");

                foreach (string a in (IEnumerable<string>)[launch.FileName, .. launch.Arguments]) argv.Add(Marshal.StringToCoTaskMemUTF8(a));
                argv.Add(IntPtr.Zero);
                foreach (var (k, v) in env) envp.Add(Marshal.StringToCoTaskMemUTF8($"{k}={v}"));
                envp.Add(IntPtr.Zero);
                int err = posix_spawn(out int pid, launch.FileName, actions, attr, [.. argv], [.. envp]);
                if (err != 0) throw new IOException($"{Path.GetFileName(launch.FileName)} didn't start: {err}");
                return new UnixBackend(master, pid);
            }
            catch
            {
                close(master);
                throw;
            }
            finally
            {
                if (slave >= 0) close(slave);
                if (actions != IntPtr.Zero)
                {
                    posix_spawn_file_actions_destroy(actions);
                    Marshal.FreeHGlobal(actions);
                }
                if (attr != IntPtr.Zero)
                {
                    posix_spawnattr_destroy(attr);
                    Marshal.FreeHGlobal(attr);
                }
                if (sigs != IntPtr.Zero) Marshal.FreeHGlobal(sigs);
                foreach (var p in argv.Concat(envp)) if (p != IntPtr.Zero) Marshal.FreeCoTaskMem(p);
            }
        }

        static void Zero(IntPtr at, int length)
        {
            for (int i = 0; i < length; i += 8) Marshal.WriteInt64(at, i, 0);
        }

        static void Check(int err, string what)
        {
            if (err != 0) throw new IOException($"pseudo-terminal: {what} ({err})");
        }

        public int WaitForExit()
        {
            while (true)
            {
                int r = waitpid(Pid, out int status, 0);
                if (r == Pid) return (status & 0x7f) == 0 ? (status >> 8) & 0xff : 128 + (status & 0x7f);
                if (r < 0 && Marshal.GetLastPInvokeError() == EINTR) continue;
                return -1;
            }
        }

        public int Read(byte[] into, Func<bool> stop)
        {
            var fds = new PollFd[1];
            while (!stop())
            {
                fds[0] = new PollFd { Fd = master, Events = PollIn };
                int r = poll(fds, 1, 250);
                if (r < 0)
                {
                    if (Marshal.GetLastPInvokeError() == EINTR) continue;
                    return 0;
                }
                if (r == 0) continue;
                // Data first; a hang-up with nothing left to read is the end (every copy of the far end is closed).
                nint n = read(master, into, into.Length);
                if (n > 0) return (int)n;
                if (n < 0 && Marshal.GetLastPInvokeError() == EINTR) continue;
                return 0;
            }
            return 0;
        }

        public void Write(byte[] bytes)
        {
            int at = 0;
            while (at < bytes.Length && Volatile.Read(ref closed) == 0)
            {
                nint n = write(master, bytes[at..], bytes.Length - at);
                if (n < 0)
                {
                    if (Marshal.GetLastPInvokeError() == EINTR) continue;
                    throw new IOException("the hidden terminal took no more");
                }
                at += (int)n;
            }
        }

        public void Kill()
        {
            // Its session's process group first (the program leads it), then anything that left it.
            kill(-Pid, 15);
            KillTree(Pid);
        }

        public void Close(Task<int> exited)
        {
            if (Interlocked.Exchange(ref closed, 1) == 1) return;
            // The reader looks at the end every quarter second: the descriptor is closed only after it's let go.
            _ = Task.Run(async () =>
            {
                await Task.WhenAny(exited, Task.Delay(TimeSpan.FromSeconds(5)));
                await Task.Delay(500);
                close(master);
            });
        }

        [StructLayout(LayoutKind.Sequential)]
        struct WinSize
        {
            public ushort Rows, Columns, X, Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct PollFd
        {
            public int Fd;
            public short Events, Revents;
        }

        [DllImport("libc", SetLastError = true)] static extern int posix_openpt(int flags);
        [DllImport("libc", SetLastError = true)] static extern int grantpt(int fd);
        [DllImport("libc", SetLastError = true)] static extern int unlockpt(int fd);
        [DllImport("libc", SetLastError = true)] static extern int ptsname_r(int fd, byte[] buf, nuint len);
        [DllImport("libc", SetLastError = true)] static extern int open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);
        [DllImport("libc", SetLastError = true)] static extern int close(int fd);
        [DllImport("libc", SetLastError = true)] static extern int openpty(out int master, out int slave, IntPtr name, IntPtr termios, ref WinSize size);
        [DllImport("libc", SetLastError = true)] static extern int ioctl(int fd, nuint request, ref WinSize size);
        [DllImport("libc", SetLastError = true)] static extern int ioctl(int fd, nuint request);
        [DllImport("libc", SetLastError = true)] static extern nint read(int fd, byte[] buf, nint count);
        [DllImport("libc", SetLastError = true)] static extern nint write(int fd, byte[] buf, nint count);
        [DllImport("libc", SetLastError = true)] static extern int poll([In, Out] PollFd[] fds, nuint count, int timeout);
        [DllImport("libc", SetLastError = true)] static extern int waitpid(int pid, out int status, int options);
        [DllImport("libc", SetLastError = true)] static extern int kill(int pid, int sig);
        [DllImport("libc")] static extern int posix_spawn_file_actions_init(IntPtr actions);
        [DllImport("libc")] static extern int posix_spawn_file_actions_destroy(IntPtr actions);
        [DllImport("libc")] static extern int posix_spawn_file_actions_addopen(IntPtr actions, int fd, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags, int mode);
        [DllImport("libc")] static extern int posix_spawn_file_actions_adddup2(IntPtr actions, int fd, int to);
        [DllImport("libc")] static extern int posix_spawn_file_actions_addchdir_np(IntPtr actions, [MarshalAs(UnmanagedType.LPUTF8Str)] string path);
        [DllImport("libc")] static extern int posix_spawnattr_init(IntPtr attr);
        [DllImport("libc")] static extern int posix_spawnattr_destroy(IntPtr attr);
        [DllImport("libc")] static extern int posix_spawnattr_setflags(IntPtr attr, short flags);
        [DllImport("libc")] static extern int posix_spawnattr_setsigdefault(IntPtr attr, IntPtr set);
        [DllImport("libc")] static extern int posix_spawnattr_setsigmask(IntPtr attr, IntPtr set);
        [DllImport("libc")] static extern int sigfillset(IntPtr set);
        [DllImport("libc")] static extern int sigemptyset(IntPtr set);
        [DllImport("libc")] static extern int posix_spawn(out int pid, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, IntPtr actions, IntPtr attr, IntPtr[] argv, IntPtr[] envp);
    }

    // --- Windows -----------------------------------------------------------------------------------------------------

    sealed class WindowsBackend : IBackend
    {
        readonly IntPtr console;
        readonly IntPtr process;
        readonly FileStream input;
        readonly FileStream output;
        int closed;

        WindowsBackend(IntPtr console, IntPtr process, int pid, FileStream input, FileStream output)
        {
            this.console = console;
            this.process = process;
            Pid = pid;
            this.input = input;
            this.output = output;
        }

        public int Pid { get; }

        const uint ExtendedStartupInfo = 0x00080000, UnicodeEnvironment = 0x00000400;
        const int UseStdHandles = 0x00000100;
        static readonly IntPtr PseudoConsoleAttribute = 0x00020016;

        public static WindowsBackend Start(Launch launch, IReadOnlyDictionary<string, string> env, string? cwd)
        {
            if (!CreatePipe(out var inRead, out var inWrite, IntPtr.Zero, 0) || !CreatePipe(out var outRead, out var outWrite, IntPtr.Zero, 0))
                throw new IOException($"no pipes for the hidden console: {Marshal.GetLastPInvokeError()}");
            IntPtr console = IntPtr.Zero, list = IntPtr.Zero, envBlock = IntPtr.Zero;
            try
            {
                int hr = CreatePseudoConsole(new Coord { X = Columns, Y = Rows }, inRead, outWrite, 0, out console);
                if (hr != 0) throw new IOException($"no hidden console: 0x{hr:x8}");
                // The console host keeps its own copies of these two ends.
                inRead.Dispose();
                outWrite.Dispose();

                IntPtr size = IntPtr.Zero;
                InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
                list = Marshal.AllocHGlobal(size);
                if (!InitializeProcThreadAttributeList(list, 1, 0, ref size)
                    || !UpdateProcThreadAttribute(list, 0, PseudoConsoleAttribute, console, IntPtr.Size, IntPtr.Zero, IntPtr.Zero))
                    throw new IOException($"the hidden console couldn't be given: {Marshal.GetLastPInvokeError()}");

                var si = new StartupInfoEx();
                si.StartupInfo.cb = Marshal.SizeOf<StartupInfoEx>();
                // No handles of Syft Notes' own (a test runner's or a service's redirected output would be taken
                // instead of the console): the program's input, output and errors are the hidden console's.
                si.StartupInfo.dwFlags = UseStdHandles;
                si.lpAttributeList = list;

                var block = new StringBuilder();
                foreach (var (k, v) in env.OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase)) block.Append(k).Append('=').Append(v).Append('\0');
                block.Append('\0');
                envBlock = Marshal.StringToHGlobalUni(block.ToString());

                string line = launch.CommandLine is { } whole
                    ? $"{WindowsCommand.Quote(launch.FileName)} {whole}"
                    : string.Join(' ', ((IEnumerable<string>)[launch.FileName, .. launch.Arguments]).Select(WindowsCommand.Quote));
                var cmd = new StringBuilder(line);
                if (!CreateProcessW(null, cmd, IntPtr.Zero, IntPtr.Zero, false, ExtendedStartupInfo | UnicodeEnvironment, envBlock, cwd, ref si, out var pi))
                    throw new IOException($"{Path.GetFileName(launch.FileName)} didn't start: {Marshal.GetLastPInvokeError()}");
                CloseHandle(pi.hThread);
                return new WindowsBackend(console, pi.hProcess, pi.dwProcessId,
                    new FileStream(inWrite, FileAccess.Write, 1, false), new FileStream(outRead, FileAccess.Read, 1, false));
            }
            catch
            {
                if (console != IntPtr.Zero) ClosePseudoConsole(console);
                inWrite.Dispose();
                outRead.Dispose();
                inRead.Dispose();
                outWrite.Dispose();
                throw;
            }
            finally
            {
                if (list != IntPtr.Zero)
                {
                    DeleteProcThreadAttributeList(list);
                    Marshal.FreeHGlobal(list);
                }
                if (envBlock != IntPtr.Zero) Marshal.FreeHGlobal(envBlock);
            }
        }

        public int WaitForExit()
        {
            WaitForSingleObject(process, 0xFFFFFFFF);
            int code = GetExitCodeProcess(process, out uint c) ? unchecked((int)c) : -1;
            // Its console is closed once it's gone (while its output is still read: closing waits for that), and
            // the reader then comes to the end.
            _ = Task.Run(() => CloseConsole());
            return code;
        }

        void CloseConsole()
        {
            if (Interlocked.Exchange(ref closed, 1) == 1) return;
            ClosePseudoConsole(console);
        }

        public int Read(byte[] into, Func<bool> stop) => output.Read(into, 0, into.Length);

        public void Write(byte[] bytes)
        {
            input.Write(bytes);
            input.Flush();
        }

        public void Kill() => KillTree(Pid);

        public void Close(Task<int> exited)
        {
            _ = Task.Run(async () =>
            {
                await Task.WhenAny(exited, Task.Delay(TimeSpan.FromSeconds(5)));
                CloseConsole();
                await Task.Delay(500);
                input.Dispose();
                output.Dispose();
                CloseHandle(process);
            });
        }

        [StructLayout(LayoutKind.Sequential)]
        struct Coord
        {
            public short X, Y;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct StartupInfo
        {
            public int cb;
            public IntPtr lpReserved, lpDesktop, lpTitle;
            public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
            public short wShowWindow, cbReserved2;
            public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct StartupInfoEx
        {
            public StartupInfo StartupInfo;
            public IntPtr lpAttributeList;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct ProcessInformation
        {
            public IntPtr hProcess, hThread;
            public int dwProcessId, dwThreadId;
        }

        [DllImport("kernel32.dll", SetLastError = true)] static extern bool CreatePipe(out SafeFileHandle read, out SafeFileHandle write, IntPtr attributes, int size);
        [DllImport("kernel32.dll", SetLastError = true)] static extern int CreatePseudoConsole(Coord size, SafeFileHandle input, SafeFileHandle output, uint flags, out IntPtr console);
        [DllImport("kernel32.dll", SetLastError = true)] static extern void ClosePseudoConsole(IntPtr console);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, int flags, ref IntPtr size);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, IntPtr attribute, IntPtr value, IntPtr size, IntPtr previous, IntPtr returnSize);
        [DllImport("kernel32.dll", SetLastError = true)] static extern void DeleteProcThreadAttributeList(IntPtr list);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool CreateProcessW(string? application, StringBuilder commandLine, IntPtr processAttributes, IntPtr threadAttributes, bool inheritHandles,
            uint flags, IntPtr environment, string? folder, ref StartupInfoEx startup, out ProcessInformation info);
        [DllImport("kernel32.dll", SetLastError = true)] static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetExitCodeProcess(IntPtr process, out uint code);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool CloseHandle(IntPtr handle);
    }
}
