using Avalonia.Input;
using StudyStash.App.Platform;
using StudyStash.App.ViewModels;

namespace StudyStash.App.Tests;

/// <summary>The shortcuts the student can change: saved as text, shown each platform's way, turned into what each
/// platform registers, and changed in the list (Settings → Shortcuts, setup's last page) by pressing new keys.</summary>
public class ShortcutsModelTests
{
    sealed class Store
    {
        public Dictionary<string, string> Keys { get; } = [];
        public int Saves { get; private set; }

        public ShortcutsModel Model(bool mac = true, bool records = true) =>
            new(() => Keys, change =>
            {
                change(Keys);
                Saves++;
            }, mac, records);
    }

    [Fact]
    public void A_shortcut_reads_back_from_its_text_and_shows_each_platforms_way()
    {
        Assert.True(KeyCombo.TryParse("Alt+Shift+R", out var record));
        Assert.Equal(new KeyCombo(KeyMods.Alt | KeyMods.Shift, "R"), record);
        Assert.Equal("Alt+Shift+R", record.ToString());
        Assert.Equal("⌥⇧R", record.Show(mac: true));
        Assert.Equal("Alt+Shift+R", record.Show(mac: false));

        Assert.True(KeyCombo.TryParse("Command+Enter", out var ask));
        Assert.Equal("⌘↩", ask.Show(mac: true));
        Assert.Equal("Win+Enter", ask.Show(mac: false));
        Assert.True(KeyCombo.TryParse("control+comma", out var settings));
        Assert.Equal("Ctrl+,", settings.Show(mac: false));
        Assert.True(KeyCombo.TryParse("Control+Alt+7", out var seven));
        Assert.Equal("7", seven.Key);

        Assert.False(KeyCombo.TryParse("Alt+Shift", out _));
        Assert.False(KeyCombo.TryParse("Alt+Banana", out _));
        Assert.False(KeyCombo.TryParse("R+T", out _));
        Assert.False(KeyCombo.TryParse("", out _));
    }

    [Fact]
    public void The_defaults_are_the_shortcuts_the_app_always_had()
    {
        Assert.Equal("⌥Space", Keybindings.Default(KeyAction.Quick, mac: true).Show(true));
        Assert.Equal("⌥⇧R", Keybindings.Default(KeyAction.Record, mac: true).Show(true));
        Assert.Equal("⌘↩", Keybindings.Default(KeyAction.Ask, mac: true).Show(true));
        Assert.Equal("⌘,", Keybindings.Default(KeyAction.Settings, mac: true).Show(true));
        Assert.Equal("Alt+Shift+Space", Keybindings.Default(KeyAction.Quick, mac: false).Show(false));
        Assert.Equal("Ctrl+Alt+R", Keybindings.Default(KeyAction.Record, mac: false).Show(false));
        Assert.Equal("Ctrl+Enter", Keybindings.Default(KeyAction.Ask, mac: false).Show(false));
        Assert.Equal("Ctrl+,", Keybindings.Default(KeyAction.Settings, mac: false).Show(false));
    }

    [Fact]
    public void Each_platform_gets_the_codes_it_registers()
    {
        // ⌥⇧R and ⌥Space, as Carbon's key codes and modifiers had them hard-coded before.
        Assert.Equal((0x0Fu, 0x0800u | 0x0200u), Keybindings.Default(KeyAction.Record, true).Mac());
        Assert.Equal((0x31u, 0x0800u), Keybindings.Default(KeyAction.Quick, true).Mac());
        // Ctrl+Alt+R and Alt+Shift+Space, as RegisterHotKey had them.
        Assert.Equal((0x52u, 0x2u | 0x1u), Keybindings.Default(KeyAction.Record, false).Win());
        Assert.Equal((0x20u, 0x1u | 0x4u), Keybindings.Default(KeyAction.Quick, false).Win());
        Assert.Equal((0x70u + 4, 0x2u), new KeyCombo(KeyMods.Control, "F5").Win());
        Assert.Equal(new KeyGesture(Key.OemComma, KeyModifiers.Meta), Keybindings.Default(KeyAction.Settings, true).Gesture());
    }

    [Fact]
    public void What_was_pressed_becomes_a_shortcut_and_matches_it_again()
    {
        var combo = KeyCombo.From(Key.K, KeyModifiers.Control | KeyModifiers.Shift);
        Assert.Equal(new KeyCombo(KeyMods.Control | KeyMods.Shift, "K"), combo);
        Assert.True(combo!.Value.Matches(Key.K, KeyModifiers.Control | KeyModifiers.Shift));
        Assert.False(combo.Value.Matches(Key.K, KeyModifiers.Control));
        Assert.Equal("Enter", KeyCombo.From(Key.Return, KeyModifiers.Meta)!.Value.Key);
        Assert.Null(KeyCombo.From(Key.Left, KeyModifiers.Control));
    }

    [Fact]
    public void Pressing_new_keys_changes_the_shortcut_and_saves_only_what_differs()
    {
        var store = new Store();
        var model = store.Model();
        var saved = 0;
        var listening = new List<bool>();
        model.Saved = () => saved++;
        model.Listening = listening.Add;
        var record = model.Rows.Single(r => r.Action == KeyAction.Record);
        Assert.Equal("⌥⇧R", record.Keys);
        Assert.False(record.Changed);

        record.ChangeCommand.Execute(null);
        Assert.True(record.Listening);
        Assert.Equal("Press keys…", record.Keys);
        Assert.True(model.Press(Key.LeftShift, KeyModifiers.Shift)); // a modifier alone: still listening
        Assert.True(record.Listening);
        Assert.True(model.Press(Key.T, KeyModifiers.Control | KeyModifiers.Shift));

        Assert.False(record.Listening);
        Assert.Equal("⌃⇧T", record.Keys);
        Assert.True(record.Changed);
        Assert.True(model.AnyChanged);
        Assert.Equal(new Dictionary<string, string> { ["Record"] = "Control+Shift+T" }, store.Keys);
        Assert.Equal(1, saved);
        Assert.Equal([true, false], listening);

        record.ResetCommand.Execute(null);
        Assert.Empty(store.Keys);
        Assert.Equal("⌥⇧R", record.Keys);
        Assert.False(model.AnyChanged);
    }

    [Fact]
    public void Keys_that_cant_be_a_shortcut_are_refused_in_words_and_it_keeps_listening()
    {
        var store = new Store();
        var model = store.Model();
        var quick = model.Rows.Single(r => r.Action == KeyAction.Quick);
        quick.ChangeCommand.Execute(null);

        model.Press(Key.R, KeyModifiers.None);
        Assert.Equal("Hold ⌘, ⌥ or ⌃ with it, so typing doesn't set it off.", model.Say);
        model.Press(Key.R, KeyModifiers.Alt | KeyModifiers.Shift);
        Assert.Equal("That's already Record's shortcut.", model.Say);
        model.Press(Key.C, KeyModifiers.Meta);
        Assert.Equal("⌘C belongs to your computer. Pick another.", model.Say);
        model.Press(Key.Left, KeyModifiers.Meta);
        Assert.StartsWith("That key can't be a shortcut.", model.Say);
        Assert.True(quick.Listening);
        Assert.Equal(0, store.Saves);

        Assert.True(model.Press(Key.F6, KeyModifiers.None)); // a function key alone is fine
        Assert.Equal("F6", quick.Keys);
        Assert.Equal("F6", store.Keys["Quick"]);
    }

    [Fact]
    public void Escape_leaves_it_as_it_was_and_nothing_is_heard_when_not_listening()
    {
        var store = new Store();
        var model = store.Model(mac: false);
        Assert.False(model.Press(Key.K, KeyModifiers.Control));
        var ask = model.Rows.Single(r => r.Action == KeyAction.Ask);
        ask.ChangeCommand.Execute(null);

        Assert.True(model.Press(Key.Escape, KeyModifiers.None));

        Assert.False(ask.Listening);
        Assert.Equal("Ctrl+Enter", ask.Keys);
        Assert.Equal(0, store.Saves);
    }

    [Fact]
    public void Reset_wont_take_back_keys_another_shortcut_now_has()
    {
        var store = new Store();
        store.Keys["Record"] = "Control+Shift+T";
        store.Keys["Quick"] = "Alt+Shift+R"; // Record's old default
        var model = store.Model();
        var record = model.Rows.Single(r => r.Action == KeyAction.Record);

        record.ResetCommand.Execute(null);

        Assert.Equal("⌥⇧R is Search's shortcut now. Change that one first.", model.Say);
        Assert.Equal("Control+Shift+T", store.Keys["Record"]);

        model.ResetAllCommand.Execute(null);
        Assert.Empty(store.Keys);
    }

    [Fact]
    public void A_library_that_doesnt_record_has_no_Record_row_until_it_records()
    {
        var model = new Store().Model(records: false);
        Assert.DoesNotContain(model.Rows, r => r.Action == KeyAction.Record);
        model.ShowRecord(true);
        Assert.Equal([KeyAction.Quick, KeyAction.Record, KeyAction.Ask, KeyAction.Settings], model.Rows.Select(r => r.Action));
        model.ShowRecord(false);
        Assert.DoesNotContain(model.Rows, r => r.Action == KeyAction.Record);
    }

    [Fact]
    public void A_saved_shortcut_that_no_longer_reads_falls_back_to_the_default()
    {
        var saved = new Dictionary<string, string> { ["Record"] = "nonsense", ["Ask"] = "Alt+K" };
        Assert.Equal(Keybindings.Default(KeyAction.Record, true), Keybindings.Of(KeyAction.Record, saved, true));
        Assert.Equal(new KeyCombo(KeyMods.Alt, "K"), Keybindings.Of(KeyAction.Ask, saved, true));
    }
}
