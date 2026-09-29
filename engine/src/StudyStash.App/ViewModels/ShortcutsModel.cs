using System.Collections.ObjectModel;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudyStash.App.Platform;

namespace StudyStash.App.ViewModels;

/// <summary>One shortcut in the list: what it does, where it works, its keys, and whether it's listening for new ones.</summary>
public sealed partial class ShortcutRow(ShortcutsModel owner, KeyAction action, string title, string where) : ObservableObject
{
    public KeyAction Action { get; } = action;
    public string Title { get; } = title;
    public string Where { get; } = where;
    /// <summary>"⌥⇧R", or "Press keys…" while listening.</summary>
    [ObservableProperty] public partial string Keys { get; set; } = "";
    [ObservableProperty] public partial bool Listening { get; set; }
    /// <summary>Changed from its default, so Reset has something to do.</summary>
    [ObservableProperty] public partial bool Changed { get; set; }

    [RelayCommand]
    void Change() => owner.Listen(this);

    [RelayCommand]
    void Reset() => owner.Reset(this);
}

/// <summary>
/// Settings → Shortcuts (and setup's last page): every shortcut the student can change. Change listens for the next
/// keys pressed (the view hands them to <see cref="Press"/>); Escape stops listening without a change. A shortcut
/// that can't be used (no modifier, another shortcut's keys, one the computer keeps) is refused in words and the row
/// keeps listening. Saved at once, into app.json's "keys", and <see cref="Saved"/> tells the app to apply it.
/// </summary>
public sealed partial class ShortcutsModel : ObservableObject
{
    readonly Func<IReadOnlyDictionary<string, string>> read;
    readonly Action<Action<Dictionary<string, string>>> write;
    readonly bool mac;

    /// <summary>Over the saved shortcuts: <paramref name="read"/> gives them, <paramref name="write"/> changes and saves
    /// them. <paramref name="records"/> false (a library that doesn't record) leaves Record out.</summary>
    public ShortcutsModel(Func<IReadOnlyDictionary<string, string>> read, Action<Action<Dictionary<string, string>>> write, bool mac, bool records = true)
    {
        this.read = read;
        this.write = write;
        this.mac = mac;
        Rows.Add(new ShortcutRow(this, KeyAction.Quick, "Search your lectures", "From any app"));
        if (records) Rows.Add(new ShortcutRow(this, KeyAction.Record, "Start or stop recording", "From any app"));
        Rows.Add(new ShortcutRow(this, KeyAction.Ask, "Ask about what you searched", "In the search panel"));
        Rows.Add(new ShortcutRow(this, KeyAction.Settings, "Open Settings", "In any Study Stash window"));
        Fill();
    }

    public ObservableCollection<ShortcutRow> Rows { get; } = [];

    /// <summary>Record's row shows only on a computer that records (setup can change which kind this is).</summary>
    public void ShowRecord(bool records)
    {
        var row = Rows.FirstOrDefault(r => r.Action == KeyAction.Record);
        if (records && row is null)
        {
            Rows.Insert(1, new ShortcutRow(this, KeyAction.Record, "Start or stop recording", "From any app"));
            Fill();
        }
        else if (!records && row is not null)
        {
            if (listening == row) Stop();
            Rows.Remove(row);
        }
    }
    /// <summary>Why the keys just pressed can't be used, or what changed.</summary>
    [ObservableProperty] public partial string? Say { get; set; }
    public bool HasSay => !string.IsNullOrEmpty(Say);
    partial void OnSayChanged(string? value) => OnPropertyChanged(nameof(HasSay));
    [ObservableProperty] public partial bool AnyChanged { get; set; }

    /// <summary>A row started (true) or stopped (false) listening: the app lets go of its shortcuts meanwhile, so
    /// pressing one reaches the list instead of doing it.</summary>
    public Action<bool>? Listening { get; set; }
    /// <summary>A shortcut was saved: the app applies it (registers the global ones again).</summary>
    public Action? Saved { get; set; }

    public string Hint => mac
        ? "Click a shortcut, then press the keys you want, holding ⌘, ⌥ or ⌃. Escape leaves it as it was."
        : "Click a shortcut, then press the keys you want, holding Ctrl, Alt or Win. Escape leaves it as it was.";

    public KeyCombo Of(KeyAction action) => Keybindings.Of(action, read(), mac);

    ShortcutRow? listening;

    void Fill()
    {
        foreach (var row in Rows)
        {
            var now = Of(row.Action);
            if (!row.Listening) row.Keys = now.Show(mac);
            row.Changed = now != Keybindings.Default(row.Action, mac);
        }
        AnyChanged = Rows.Any(r => r.Changed);
    }

    internal void Listen(ShortcutRow row)
    {
        if (listening == row)
        {
            Stop();
            return;
        }
        if (listening is { } before) before.Listening = false;
        bool was = listening is not null;
        listening = row;
        row.Listening = true;
        row.Keys = "Press keys…";
        Say = null;
        Fill();
        if (!was) Listening?.Invoke(true);
    }

    /// <summary>Stop listening, keeping what was there.</summary>
    public void Stop()
    {
        if (listening is not { } row) return;
        listening = null;
        row.Listening = false;
        Fill();
        Listening?.Invoke(false);
    }

    public bool IsListening => listening is not null;

    /// <summary>A key pressed while the list has focus. True when the list used it (it was listening).</summary>
    public bool Press(Key key, KeyModifiers modifiers)
    {
        if (listening is not { } row) return false;
        if (key is Key.LeftShift or Key.RightShift or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin
            or Key.CapsLock or Key.None)
            return true; // a modifier on its own: keep listening for the rest
        if (key == Key.Escape && modifiers == KeyModifiers.None)
        {
            Say = null;
            Stop();
            return true;
        }
        if (KeyCombo.From(key, modifiers) is not { } combo)
        {
            Say = "That key can't be a shortcut. Try a letter, a number, Space, Enter or F1–F12.";
            return true;
        }
        var others = Keybindings.All.Where(a => a != row.Action).ToDictionary(a => a, Of);
        if (Keybindings.Problem(row.Action, combo, others, mac) is { } problem)
        {
            Say = problem;
            return true;
        }
        Save(row.Action, combo);
        Say = $"{row.Title}: {combo.Show(mac)}.";
        Stop();
        return true;
    }

    void Save(KeyAction action, KeyCombo? combo)
    {
        write(keys =>
        {
            if (combo is { } c && c != Keybindings.Default(action, mac)) keys[action.ToString()] = c.ToString();
            else keys.Remove(action.ToString());
        });
        Fill();
        Saved?.Invoke();
    }

    internal void Reset(ShortcutRow row)
    {
        if (listening == row) Stop();
        var back = Keybindings.Default(row.Action, mac);
        var others = Keybindings.All.Where(a => a != row.Action).ToDictionary(a => a, Of);
        foreach (var (other, used) in others)
            if (used == back)
            {
                Say = $"{back.Show(mac)} is {Keybindings.Name(other)}'s shortcut now. Change that one first.";
                return;
            }
        Save(row.Action, null);
        Say = null;
    }

    /// <summary>Every shortcut back to its default.</summary>
    [RelayCommand]
    void ResetAll()
    {
        Stop();
        write(keys => keys.Clear());
        Fill();
        Say = null;
        Saved?.Invoke();
    }
}
