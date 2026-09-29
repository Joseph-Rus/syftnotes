using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace StudyStash.App.Platform;

/// <summary>What a global shortcut does (the keys are the student's: <see cref="Keybindings"/>).</summary>
public enum Shortcut
{
    /// <summary>⌥Space on a Mac, Alt+Shift+Space on Windows, unless changed: the quick panel.</summary>
    Quick = 1,
    /// <summary>⌥⇧R on a Mac, Ctrl+Alt+R on Windows, unless changed: start or stop recording.</summary>
    Record = 2,
}

/// <summary>Whether each shortcut is registered, so Settings can say which one (if any) is taken by another app.</summary>
public readonly record struct HotkeyResult(bool Quick, bool Record)
{
    public bool All => Quick && Record;
}

/// <summary>
/// The app's shortcuts, working from any app: Carbon's hot keys on a Mac (no permission needed), RegisterHotKey on
/// Windows. Pressed, they call back on the UI thread. <see cref="Register"/> can be called again after
/// <see cref="Unregister"/> (the Shortcuts toggle in Settings turns them on and off without restarting).
/// </summary>
public static class Hotkeys
{
    static Action<Shortcut>? pressed;

    public static HotkeyResult Register(Action<Shortcut> onPressed, KeyCombo quick, KeyCombo record)
    {
        pressed = onPressed;
        try
        {
            if (OperatingSystem.IsMacOS()) return Mac.Register(quick, record);
            if (OperatingSystem.IsWindows()) return Win.Register(quick, record);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
        }
        return new HotkeyResult(false, false);
    }

    /// <summary>Let both shortcuts go: another app (or nobody) can have them until <see cref="Register"/> again.</summary>
    public static void Unregister()
    {
        try
        {
            if (OperatingSystem.IsMacOS()) Mac.Unregister();
            else if (OperatingSystem.IsWindows()) Win.Unregister();
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
        }
    }

    static void Fire(Shortcut s) => Avalonia.Threading.Dispatcher.UIThread.Post(() => pressed?.Invoke(s));

    [SupportedOSPlatform("macos")]
    static unsafe class Mac
    {
        const string Carbon = "/System/Library/Frameworks/Carbon.framework/Carbon";
        const uint KeyboardClass = 0x6B657962; // 'keyb'
        const uint HotKeyPressed = 5;
        const uint Signature = 0x53747348; // 'StsH'

        [StructLayout(LayoutKind.Sequential)]
        struct EventTypeSpec
        {
            public uint Class, Kind;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct HotKeyId
        {
            public uint Signature, Id;
        }

        [DllImport(Carbon)]
        static extern IntPtr GetApplicationEventTarget();

        [DllImport(Carbon)]
        static extern int InstallEventHandler(IntPtr target, delegate* unmanaged<IntPtr, IntPtr, IntPtr, int> handler, nuint count, EventTypeSpec* types,
            IntPtr userData, IntPtr* outRef);

        [DllImport(Carbon)]
        static extern int RegisterEventHotKey(uint keyCode, uint modifiers, HotKeyId id, IntPtr target, uint options, IntPtr* outRef);

        [DllImport(Carbon)]
        static extern int UnregisterEventHotKey(IntPtr hotKeyRef);

        [DllImport(Carbon)]
        static extern int GetEventParameter(IntPtr evt, uint name, uint type, IntPtr outType, nuint size, IntPtr outSize, void* data);

        static bool installed;
        static IntPtr quickRef, recordRef;

        public static HotkeyResult Register(KeyCombo quickKeys, KeyCombo recordKeys)
        {
            IntPtr target = GetApplicationEventTarget();
            if (!installed)
            {
                var spec = new EventTypeSpec { Class = KeyboardClass, Kind = HotKeyPressed };
                IntPtr handler;
                if (InstallEventHandler(target, &OnHotKey, 1, &spec, IntPtr.Zero, &handler) != 0) return new HotkeyResult(false, false);
                installed = true;
            }
            IntPtr a, b;
            var (qCode, qMods) = quickKeys.Mac();
            var (rCode, rMods) = recordKeys.Mac();
            bool quick = RegisterEventHotKey(qCode, qMods, new HotKeyId { Signature = Signature, Id = (uint)Shortcut.Quick }, target, 0, &a) == 0;
            bool record = RegisterEventHotKey(rCode, rMods, new HotKeyId { Signature = Signature, Id = (uint)Shortcut.Record }, target, 0, &b) == 0;
            quickRef = quick ? a : IntPtr.Zero;
            recordRef = record ? b : IntPtr.Zero;
            return new HotkeyResult(quick, record);
        }

        public static void Unregister()
        {
            if (quickRef != IntPtr.Zero) UnregisterEventHotKey(quickRef);
            if (recordRef != IntPtr.Zero) UnregisterEventHotKey(recordRef);
            quickRef = recordRef = IntPtr.Zero;
        }

        [UnmanagedCallersOnly]
        static int OnHotKey(IntPtr call, IntPtr evt, IntPtr user)
        {
            HotKeyId id;
            if (GetEventParameter(evt, 0x2D2D2D2D /* '----' */, 0x686B6964 /* 'hkid' */, IntPtr.Zero, (nuint)sizeof(HotKeyId), IntPtr.Zero, &id) == 0
                && id.Signature == Signature)
                Fire((Shortcut)id.Id);
            return 0;
        }
    }

    [SupportedOSPlatform("windows")]
    static unsafe class Win
    {
        const uint ModNoRepeat = 0x4000, WmHotKey = 0x0312;
        static readonly IntPtr MessageOnly = new(-3);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct WndClass
        {
            public uint Size, Style;
            public delegate* unmanaged<IntPtr, uint, IntPtr, IntPtr, IntPtr> Proc;
            public int ClsExtra, WndExtra;
            public IntPtr Instance, Icon, Cursor, Background;
            public IntPtr MenuName, ClassName;
            public IntPtr IconSmall;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern ushort RegisterClassExW(WndClass* cls);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr CreateWindowExW(uint exStyle, string cls, string name, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);

        [DllImport("user32.dll")]
        static extern IntPtr DefWindowProcW(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);

        [DllImport("user32.dll")]
        static extern bool RegisterHotKey(IntPtr hwnd, int id, uint mods, uint vk);

        [DllImport("user32.dll")]
        static extern bool UnregisterHotKey(IntPtr hwnd, int id);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr GetModuleHandleW(string? name);

        static IntPtr window;

        public static HotkeyResult Register(KeyCombo quickKeys, KeyCombo recordKeys)
        {
            if (window == IntPtr.Zero)
            {
                IntPtr name = Marshal.StringToHGlobalUni("StudyStashHotkeys");
                var cls = new WndClass { Size = (uint)sizeof(WndClass), Proc = &Proc, Instance = GetModuleHandleW(null), ClassName = name };
                RegisterClassExW(&cls);
                window = CreateWindowExW(0, "StudyStashHotkeys", "Study Stash", 0, 0, 0, 0, 0, MessageOnly, IntPtr.Zero, cls.Instance, IntPtr.Zero);
                if (window == IntPtr.Zero) return new HotkeyResult(false, false);
            }
            var (qVk, qMods) = quickKeys.Win();
            var (rVk, rMods) = recordKeys.Win();
            bool quick = RegisterHotKey(window, (int)Shortcut.Quick, qMods | ModNoRepeat, qVk);
            bool record = RegisterHotKey(window, (int)Shortcut.Record, rMods | ModNoRepeat, rVk);
            return new HotkeyResult(quick, record);
        }

        public static void Unregister()
        {
            if (window == IntPtr.Zero) return;
            UnregisterHotKey(window, (int)Shortcut.Quick);
            UnregisterHotKey(window, (int)Shortcut.Record);
        }

        [UnmanagedCallersOnly]
        static IntPtr Proc(IntPtr hwnd, uint msg, IntPtr w, IntPtr l)
        {
            if (msg == WmHotKey)
            {
                Fire((Shortcut)(int)w);
                return IntPtr.Zero;
            }
            return DefWindowProcW(hwnd, msg, w, l);
        }
    }
}
