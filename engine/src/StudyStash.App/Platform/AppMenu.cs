using System.Runtime.InteropServices;
using System.Text;
using Avalonia.Controls;
using Avalonia.Input;
using CommunityToolkit.Mvvm.Input;
using StudyStash.Core;

namespace StudyStash.App.Platform;

/// <summary>
/// Settings from anywhere. On a Mac, while a Study Stash window is in front, the menu bar has the app's own menu
/// (About Study Stash, Settings… ⌘, and the system's Hide and Quit ⌘Q) and a Window menu; every window, the dropdown
/// and the quick panel included, opens Settings with ⌘, (Ctrl+, on Windows, where the tray's menu has Settings too).
/// </summary>
public static class AppMenu
{
    /// <summary>⌘, on a Mac, Ctrl+, on Windows, unless the student changed it (Settings → Shortcuts).</summary>
    public static KeyGesture SettingsGesture => Keybindings.Of(KeyAction.Settings).Gesture();

    /// <summary>The app menu's Settings… items, whose shortcut follows a change in Settings.</summary>
    static readonly List<NativeMenuItem> settingsItems = [];

    /// <summary>Every window's Settings key, to follow a change (weakly held: a closed window's goes with it).</summary>
    static readonly List<WeakReference<KeyBinding>> settingsBindings = [];

    /// <summary>The menu and every window use <paramref name="keys"/> for Settings (a no-op when they already do).</summary>
    public static void UseSettingsKeys(KeyCombo keys)
    {
        var gesture = keys.Gesture();
        foreach (var item in settingsItems)
            if (!gesture.Equals(item.Gesture)) item.Gesture = gesture;
        settingsBindings.RemoveAll(w => !w.TryGetTarget(out _));
        foreach (var weak in settingsBindings)
            if (weak.TryGetTarget(out var binding) && !gesture.Equals(binding.Gesture)) binding.Gesture = gesture;
    }

    /// <summary>The app menu's own items; the system adds Services, Hide and Quit ⌘Q after them.</summary>
    public static NativeMenu Build(Action about, Action settings)
    {
        var menu = new NativeMenu();
        Fill(menu, about, settings);
        return menu;
    }

    /// <summary>Makes <paramref name="menu"/> the app menu's own items (About Study Stash, Settings… ⌘,), in place: the
    /// menu bar follows a menu it already shows (Avalonia's default, "About Avalonia", if the app's was set too late).</summary>
    public static void Fill(NativeMenu menu, Action about, Action settings)
    {
        menu.Items.Clear();
        menu.Add(Item("About Study Stash", about));
        menu.Add(new NativeMenuItemSeparator());
        var item = Item("Settings…", settings, SettingsGesture);
        settingsItems.Add(item);
        menu.Add(item);
    }

    /// <summary>The app's menu is Study Stash's own: set on the app before the menu bar first reads it, or filled in
    /// where it already shows Avalonia's default.</summary>
    public static void Use(Avalonia.Application app, Action about, Action settings)
    {
        if (NativeMenu.GetMenu(app) is { } shown)
        {
            if (!shown.Items.OfType<NativeMenuItem>().Any(i => i.Header == "Settings…")) Fill(shown, about, settings);
            return;
        }
        NativeMenu.SetMenu(app, Build(about, settings));
    }

    /// <summary>The menu bar's menus while <paramref name="window"/> is in front: Window (Minimize ⌘M, Zoom, Close ⌘W,
    /// then the library and Settings).</summary>
    public static NativeMenu ForWindow(Window window, Action library, Action settings)
    {
        // The items hold the window weakly: the system's menu bar keeps the menu (and so its items) for as long as the app
        // runs, and items that held the window would keep every closed Settings window, with everything in it, alive.
        var weak = new WeakReference<Window>(window);
        void On(Action<Window> act)
        {
            if (weak.TryGetTarget(out var w)) act(w);
        }
        var items = new NativeMenu();
        items.Add(Item("Minimize", () => On(w => w.WindowState = WindowState.Minimized), new KeyGesture(Key.M, KeyModifiers.Meta)));
        items.Add(Item("Zoom", () => On(w =>
        {
            if (w.CanResize) w.WindowState = w.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        })));
        items.Add(Item("Close", () => On(w => w.Close()), new KeyGesture(Key.W, KeyModifiers.Meta)));
        items.Add(new NativeMenuItemSeparator());
        items.Add(Item("Study Stash library", library));
        items.Add(Item("Settings…", settings));
        var bar = new NativeMenu();
        bar.Add(new NativeMenuItem("Window") { Menu = items });
        return bar;
    }

    /// <summary>⌘, (Ctrl+,) opens Settings from <paramref name="window"/>, and on a Mac the menu bar gets its Window
    /// menu while it's in front.</summary>
    public static void Attach(Window window, Action library, Action settings)
    {
        AddSettingsKey(window, settings);
        if (!OperatingSystem.IsMacOS()) return;
        var bar = ForWindow(window, library, settings);
        NativeMenu.SetMenu(window, bar);
        // The system's menu bar keeps a window's menus after the window has closed, for as long as the app runs (about
        // 24 KB a window: each item, what watches it, and its hold on the system's own item). Emptied, the items are
        // let go one by one and next to nothing is left to keep.
        window.Closed += (_, _) => bar.Items.Clear();
    }

    /// <summary>⌘, (Ctrl+,, or the student's own) opens Settings from <paramref name="window"/> (the dropdown, the quick
    /// panel, the recorder); a change in Settings reaches windows already open (<see cref="UseSettingsKeys"/>).</summary>
    public static void AddSettingsKey(Window window, Action settings)
    {
        var binding = new KeyBinding { Gesture = SettingsGesture, Command = new RelayCommand(settings) };
        settingsBindings.Add(new WeakReference<KeyBinding>(binding));
        window.KeyBindings.Add(binding);
    }

    static NativeMenuItem Item(string header, Action act, KeyGesture? gesture = null)
    {
        var item = new NativeMenuItem(header) { Gesture = gesture };
        item.Click += (_, _) => act();
        return item;
    }

    // --- a Mac's own About panel, and reading the menu bar back for the log --------------------------------------

    const string Lib = "/usr/lib/libobjc.A.dylib";
    [DllImport(Lib)] static extern IntPtr objc_getClass(string name);
    [DllImport(Lib)] static extern IntPtr sel_registerName(string name);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] static extern IntPtr Send(IntPtr r, IntPtr sel);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] static extern IntPtr SendId(IntPtr r, IntPtr sel, IntPtr a);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] static extern void SendIdId(IntPtr r, IntPtr sel, IntPtr a, IntPtr b);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] static extern IntPtr SendLong(IntPtr r, IntPtr sel, nint a);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] static extern nint GetLong(IntPtr r, IntPtr sel);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] static extern nuint GetULong(IntPtr r, IntPtr sel);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] static extern byte GetBool(IntPtr r, IntPtr sel);
    [DllImport(Lib, EntryPoint = "objc_msgSend")]
    static extern IntPtr SendString(IntPtr r, IntPtr sel, [MarshalAs(UnmanagedType.LPUTF8Str)] string s);

    static IntPtr App => Send(objc_getClass("NSApplication"), sel_registerName("sharedApplication"));
    static IntPtr NS(string s) => SendString(objc_getClass("NSString"), sel_registerName("stringWithUTF8String:"), s);

    static string Str(IntPtr nsString) =>
        nsString == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(Send(nsString, sel_registerName("UTF8String"))) ?? "";

    /// <summary>A Mac's standard About panel, named Study Stash with this version (a copy run from the build folder
    /// would otherwise say dotnet).</summary>
    public static void ShowAbout()
    {
        if (!OperatingSystem.IsMacOS()) return;
        try
        {
            var options = Send(objc_getClass("NSMutableDictionary"), sel_registerName("dictionary"));
            SendIdId(options, sel_registerName("setObject:forKey:"), NS("Study Stash"), NS("ApplicationName"));
            SendIdId(options, sel_registerName("setObject:forKey:"), NS(Engine.Version), NS("ApplicationVersion"));
            SendIdId(options, sel_registerName("setObject:forKey:"), NS(""), NS("Version"));
            Desktop.Activate();
            SendId(App, sel_registerName("orderFrontStandardAboutPanelWithOptions:"), options);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
        }
    }

    /// <summary>What the Mac's menu bar holds right now, for the log: each menu's title and its items, with their
    /// shortcuts ("Study Stash: About Study Stash, Settings… ⌘, …, Quit Study Stash ⌘Q | Window: …").</summary>
    public static string Describe()
    {
        if (!OperatingSystem.IsMacOS()) return "";
        try
        {
            var main = Send(App, sel_registerName("mainMenu"));
            if (main == IntPtr.Zero) return "(no menu bar)";
            var menus = new List<string>();
            nint count = GetLong(main, sel_registerName("numberOfItems"));
            for (nint i = 0; i < count; i++)
            {
                var top = SendLong(main, sel_registerName("itemAtIndex:"), i);
                var sub = Send(top, sel_registerName("submenu"));
                if (sub == IntPtr.Zero) continue;
                var items = new List<string>();
                nint n = GetLong(sub, sel_registerName("numberOfItems"));
                for (nint j = 0; j < n; j++)
                {
                    var item = SendLong(sub, sel_registerName("itemAtIndex:"), j);
                    if (GetBool(item, sel_registerName("isSeparatorItem")) != 0) continue;
                    string key = Str(Send(item, sel_registerName("keyEquivalent")));
                    items.Add(key.Length > 0 ? $"{Str(Send(item, sel_registerName("title")))} {Modifiers(GetULong(item, sel_registerName("keyEquivalentModifierMask")))}{key.ToUpperInvariant()}"
                        : Str(Send(item, sel_registerName("title"))));
                }
                string title = Str(Send(sub, sel_registerName("title")));
                menus.Add($"{(title.Length > 0 ? title : Str(Send(top, sel_registerName("title"))))}: {string.Join(", ", items)}");
            }
            return string.Join(" | ", menus);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return "";
        }
    }

    static string Modifiers(nuint mask)
    {
        var s = new StringBuilder();
        if ((mask & (1 << 18)) != 0) s.Append('⌃');
        if ((mask & (1 << 19)) != 0) s.Append('⌥');
        if ((mask & (1 << 17)) != 0) s.Append('⇧');
        if ((mask & (1 << 20)) != 0) s.Append('⌘');
        return s.ToString();
    }
}
