using Avalonia.Input;

namespace StudyStash.App.Platform;

/// <summary>What a shortcut Settings lets the student change does. <see cref="Quick"/> and <see cref="Record"/> work
/// from any app; <see cref="Ask"/> works in the search panel and <see cref="Settings"/> in any Study Stash window.</summary>
public enum KeyAction
{
    Quick,
    Record,
    Ask,
    Settings,
}

/// <summary>A shortcut's modifier keys. <see cref="Command"/> is ⌘ on a Mac and the Windows key on Windows.</summary>
[Flags]
public enum KeyMods
{
    None = 0,
    Control = 1,
    Alt = 2,
    Shift = 4,
    Command = 8,
}

/// <summary>
/// One shortcut: its modifiers and a key, by name ("R", "7", "Space", "Enter", "F5", "Comma"…). Saved as text
/// ("Alt+Shift+R"), shown the platform's way ("⌥⇧R" on a Mac, "Alt+Shift+R" on Windows), and turned into what each
/// platform registers: Carbon's key code, Windows' virtual key, or Avalonia's <see cref="KeyGesture"/>.
/// </summary>
public readonly record struct KeyCombo(KeyMods Mods, string Key)
{
    /// <summary>Every key a shortcut can use: (name, Avalonia key, Carbon key code, Windows virtual key, shown as).</summary>
    static readonly (string Name, Key Key, uint Mac, uint Win, string Shown)[] Table = Build();

    static (string, Key, uint, uint, string)[] Build()
    {
        // Carbon's key codes follow the keyboard's layout, not the alphabet (kVK_ANSI_*).
        uint[] macLetters = [0x00, 0x0B, 0x08, 0x02, 0x0E, 0x03, 0x05, 0x04, 0x22, 0x26, 0x28, 0x25, 0x2E, 0x2D, 0x1F, 0x23, 0x0C, 0x0F, 0x01, 0x11, 0x20, 0x09, 0x0D, 0x07, 0x10, 0x06];
        uint[] macDigits = [0x1D, 0x12, 0x13, 0x14, 0x15, 0x17, 0x16, 0x1A, 0x1C, 0x19];
        uint[] macF = [0x7A, 0x78, 0x63, 0x76, 0x60, 0x61, 0x62, 0x64, 0x65, 0x6D, 0x67, 0x6F];
        var rows = new List<(string, Key, uint, uint, string)>();
        for (int i = 0; i < 26; i++)
        {
            string c = ((char)('A' + i)).ToString();
            rows.Add((c, Avalonia.Input.Key.A + i, macLetters[i], (uint)('A' + i), c));
        }
        for (int i = 0; i < 10; i++) rows.Add((i.ToString(), Avalonia.Input.Key.D0 + i, macDigits[i], (uint)('0' + i), i.ToString()));
        for (int i = 0; i < 12; i++) rows.Add(($"F{i + 1}", Avalonia.Input.Key.F1 + i, macF[i], (uint)(0x70 + i), $"F{i + 1}"));
        rows.Add(("Space", Avalonia.Input.Key.Space, 0x31, 0x20, "Space"));
        rows.Add(("Enter", Avalonia.Input.Key.Enter, 0x24, 0x0D, "Enter"));
        rows.Add(("Comma", Avalonia.Input.Key.OemComma, 0x2B, 0xBC, ","));
        rows.Add(("Period", Avalonia.Input.Key.OemPeriod, 0x2F, 0xBE, "."));
        rows.Add(("Slash", Avalonia.Input.Key.OemQuestion, 0x2C, 0xBF, "/"));
        rows.Add(("Semicolon", Avalonia.Input.Key.OemSemicolon, 0x29, 0xBA, ";"));
        rows.Add(("Quote", Avalonia.Input.Key.OemQuotes, 0x27, 0xDE, "'"));
        rows.Add(("LeftBracket", Avalonia.Input.Key.OemOpenBrackets, 0x21, 0xDB, "["));
        rows.Add(("RightBracket", Avalonia.Input.Key.OemCloseBrackets, 0x1E, 0xDD, "]"));
        rows.Add(("Backslash", Avalonia.Input.Key.OemPipe, 0x2A, 0xDC, "\\"));
        rows.Add(("Minus", Avalonia.Input.Key.OemMinus, 0x1B, 0xBD, "-"));
        rows.Add(("Equals", Avalonia.Input.Key.OemPlus, 0x18, 0xBB, "="));
        rows.Add(("Backtick", Avalonia.Input.Key.OemTilde, 0x32, 0xC0, "`"));
        return [.. rows];
    }

    static int Find(string name) => Array.FindIndex(Table, r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));

    public bool IsFunctionKey => Key.Length > 1 && Key[0] == 'F' && char.IsDigit(Key[1]);

    /// <summary>"Control+Alt+Shift+Command+R": what's saved.</summary>
    public override string ToString()
    {
        var parts = new List<string>();
        foreach (var m in new[] { KeyMods.Control, KeyMods.Alt, KeyMods.Shift, KeyMods.Command })
            if (Mods.HasFlag(m)) parts.Add(m.ToString());
        parts.Add(Key);
        return string.Join('+', parts);
    }

    /// <summary>"⌃⌥⇧⌘R" on a Mac; "Ctrl+Alt+Shift+Win+R" on Windows.</summary>
    public string Show(bool mac)
    {
        int at = Find(Key);
        string key = at < 0 ? Key : mac && Key == "Enter" ? "↩" : Table[at].Shown;
        if (mac)
            return (Mods.HasFlag(KeyMods.Control) ? "⌃" : "") + (Mods.HasFlag(KeyMods.Alt) ? "⌥" : "") + (Mods.HasFlag(KeyMods.Shift) ? "⇧" : "")
                + (Mods.HasFlag(KeyMods.Command) ? "⌘" : "") + key;
        var parts = new List<string>();
        if (Mods.HasFlag(KeyMods.Control)) parts.Add("Ctrl");
        if (Mods.HasFlag(KeyMods.Alt)) parts.Add("Alt");
        if (Mods.HasFlag(KeyMods.Shift)) parts.Add("Shift");
        if (Mods.HasFlag(KeyMods.Command)) parts.Add("Win");
        parts.Add(key);
        return string.Join('+', parts);
    }

    /// <summary>"Alt+Shift+R" back to a shortcut; false for anything else (an unknown key, no key, a stray part).</summary>
    public static bool TryParse(string? text, out KeyCombo combo)
    {
        combo = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var mods = KeyMods.None;
        string? key = null;
        foreach (string raw in text.Split('+'))
        {
            string part = raw.Trim();
            if (Enum.TryParse<KeyMods>(part, ignoreCase: true, out var m) && m != KeyMods.None && !int.TryParse(part, out _)) mods |= m;
            else if (key is null && Find(part) is >= 0 and var at) key = Table[at].Name;
            else return false;
        }
        if (key is null) return false;
        combo = new KeyCombo(mods, key);
        return true;
    }

    /// <summary>What the student pressed, as a shortcut: null for a key a shortcut can't use (or a modifier alone).
    /// ⌘ on a Mac and the Windows key on Windows are both <see cref="KeyMods.Command"/>.</summary>
    public static KeyCombo? From(Key key, KeyModifiers modifiers)
    {
        if (key == Avalonia.Input.Key.Return) key = Avalonia.Input.Key.Enter;
        int at = Array.FindIndex(Table, r => r.Key == key);
        if (at < 0) return null;
        var mods = KeyMods.None;
        if (modifiers.HasFlag(KeyModifiers.Control)) mods |= KeyMods.Control;
        if (modifiers.HasFlag(KeyModifiers.Alt)) mods |= KeyMods.Alt;
        if (modifiers.HasFlag(KeyModifiers.Shift)) mods |= KeyMods.Shift;
        if (modifiers.HasFlag(KeyModifiers.Meta)) mods |= KeyMods.Command;
        return new KeyCombo(mods, Table[at].Name);
    }

    /// <summary>The key alone, pressed with none of the modifiers a shortcut uses.</summary>
    public bool Matches(Key key, KeyModifiers modifiers) => From(key, modifiers) == this;

    public KeyGesture Gesture()
    {
        var mods = KeyModifiers.None;
        if (Mods.HasFlag(KeyMods.Control)) mods |= KeyModifiers.Control;
        if (Mods.HasFlag(KeyMods.Alt)) mods |= KeyModifiers.Alt;
        if (Mods.HasFlag(KeyMods.Shift)) mods |= KeyModifiers.Shift;
        if (Mods.HasFlag(KeyMods.Command)) mods |= KeyModifiers.Meta;
        int at = Find(Key);
        return new KeyGesture(at < 0 ? Avalonia.Input.Key.None : Table[at].Key, mods);
    }

    /// <summary>Carbon's key code and modifiers (cmdKey, shiftKey, optionKey, controlKey).</summary>
    public (uint Code, uint Mods) Mac()
    {
        uint mods = (Mods.HasFlag(KeyMods.Command) ? 0x0100u : 0) | (Mods.HasFlag(KeyMods.Shift) ? 0x0200u : 0)
            | (Mods.HasFlag(KeyMods.Alt) ? 0x0800u : 0) | (Mods.HasFlag(KeyMods.Control) ? 0x1000u : 0);
        int at = Find(Key);
        return (at < 0 ? 0xFFFFu : Table[at].Mac, mods);
    }

    /// <summary>Windows' virtual key and RegisterHotKey's MOD_ALT, MOD_CONTROL, MOD_SHIFT and MOD_WIN.</summary>
    public (uint Vk, uint Mods) Win()
    {
        uint mods = (Mods.HasFlag(KeyMods.Alt) ? 0x1u : 0) | (Mods.HasFlag(KeyMods.Control) ? 0x2u : 0)
            | (Mods.HasFlag(KeyMods.Shift) ? 0x4u : 0) | (Mods.HasFlag(KeyMods.Command) ? 0x8u : 0);
        int at = Find(Key);
        return (at < 0 ? 0u : Table[at].Win, mods);
    }
}

/// <summary>
/// The shortcuts the student can change (Settings → Shortcuts, and setup's last page): each one's default on this
/// platform, what's saved in place of it, and whether a new one can be used.
/// </summary>
public static class Keybindings
{
    public static readonly KeyAction[] All = [KeyAction.Quick, KeyAction.Record, KeyAction.Ask, KeyAction.Settings];

    /// <summary>The shortcuts as saved (app.json's "keybindings"): read on every press, so a change applies at once.
    /// The app points it at its settings; with none, every shortcut is its default.</summary>
    public static Func<IReadOnlyDictionary<string, string>?> Saved { get; set; } = () => null;

    /// <summary>⌥Space, ⌥⇧R, ⌘↩, ⌘, on a Mac; Alt+Shift+Space, Ctrl+Alt+R, Ctrl+Enter, Ctrl+, on Windows.</summary>
    public static KeyCombo Default(KeyAction action, bool mac) => action switch
    {
        KeyAction.Quick => new(mac ? KeyMods.Alt : KeyMods.Alt | KeyMods.Shift, "Space"),
        KeyAction.Record => new(mac ? KeyMods.Alt | KeyMods.Shift : KeyMods.Control | KeyMods.Alt, "R"),
        KeyAction.Ask => new(mac ? KeyMods.Command : KeyMods.Control, "Enter"),
        _ => new(mac ? KeyMods.Command : KeyMods.Control, "Comma"),
    };

    public static KeyCombo Of(KeyAction action, IReadOnlyDictionary<string, string>? saved, bool mac) =>
        saved is not null && saved.TryGetValue(action.ToString(), out var text) && KeyCombo.TryParse(text, out var combo) ? combo : Default(action, mac);

    /// <summary>The app's look is a Mac's (a test draws either look on either computer).</summary>
    static bool Mac => Skin.Current == SkinKind.Mac;

    /// <summary>The shortcut in use now, on this computer (what's registered and matched, whatever look is drawn).</summary>
    public static KeyCombo Of(KeyAction action) => Of(action, Saved(), OperatingSystem.IsMacOS());

    /// <summary>How it's shown now: "⌥⇧R" in the Mac's look, "Ctrl+Alt+R" in Windows'.</summary>
    public static string Show(KeyAction action) => Show(action, Mac);

    public static string Show(KeyAction action, bool mac) => Of(action, Saved(), mac).Show(mac);

    /// <summary>Works from any app (registered with the system), not just in Study Stash's windows.</summary>
    public static bool IsGlobal(KeyAction action) => action is KeyAction.Quick or KeyAction.Record;

    public static string Name(KeyAction action) => action switch
    {
        KeyAction.Quick => "Search",
        KeyAction.Record => "Record",
        KeyAction.Ask => "Ask",
        _ => "Settings",
    };

    /// <summary>Why <paramref name="combo"/> can't be <paramref name="action"/>'s shortcut, or null when it can.
    /// <paramref name="others"/> is every other shortcut in use now.</summary>
    public static string? Problem(KeyAction action, KeyCombo combo, IReadOnlyDictionary<KeyAction, KeyCombo> others, bool mac)
    {
        bool hasMod = (combo.Mods & ~KeyMods.Shift) != KeyMods.None;
        if (!hasMod && !combo.IsFunctionKey)
            return mac ? "Hold ⌘, ⌥ or ⌃ with it, so typing doesn't set it off." : "Hold Ctrl, Alt or Win with it, so typing doesn't set it off.";
        foreach (var (other, used) in others)
            if (other != action && used == combo) return $"That's already {Name(other)}'s shortcut.";
        var command = mac ? KeyMods.Command : KeyMods.Control;
        if (combo.Mods == command && combo.Key is "C" or "V" or "X" or "A" or "Z" or "Q" or "W" or "M" or "H" or "Tab")
            return $"{combo.Show(mac)} belongs to your computer. Pick another.";
        return null;
    }
}
