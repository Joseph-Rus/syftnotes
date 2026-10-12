using System.Globalization;
using Avalonia.Threading;
using StudyStash.Core;
using StudyStash.Core.Setup;

namespace StudyStash.App.Services;

/// <summary>What Settings' helper reaches: this window's own settings, read and changed exactly as the student's own
/// clicks would (each change shows on its page as it's made).</summary>
public sealed partial class SettingsModel
{
    /// <summary>The settings tools' driver over this window.</summary>
    public ISettingsDriver Driver() => new AssistantDriver(this);

    sealed class AssistantDriver(SettingsModel m) : ISettingsDriver
    {
        static Task<T> Ui<T>(Func<Task<T>> f) => Dispatcher.UIThread.CheckAccess() ? f() : Dispatcher.UIThread.InvokeAsync(f);

        public bool Shown(SettingEntry entry) => Dispatcher.UIThread.CheckAccess() ? m.Shows(entry) : Dispatcher.UIThread.Invoke(() => m.Shows(entry));

        public string? LibraryProblem { get; private set; }

        static bool LibraryOwn(SettingEntry e) => e.Section is "Library" or "Classes" or "Notes" or "Folders";

        /// <summary>The library's settings, read if they haven't been: null when they're here, else why not.</summary>
        async Task<string?> LibraryAsync()
        {
            if (!m.Lib.IsReady) await m.Lib.Load();
            return m.Lib.IsReady ? null : m.Lib.Problem.Length > 0 ? m.Lib.Problem : "the library didn't answer.";
        }

        static string OnOff(bool on) => on ? "on" : "off";

        public Task<IReadOnlyList<SettingNow>> ValuesAsync() => Ui<IReadOnlyList<SettingNow>>(async () =>
        {
            var keys = SettingsIndex.All.Where(e => e.Changeable && m.Shows(e)).ToList();
            LibraryProblem = keys.Any(LibraryOwn) ? await LibraryAsync() : null;
            var list = new List<SettingNow>();
            foreach (var e in keys)
            {
                if (LibraryOwn(e) && LibraryProblem is not null) continue;
                list.Add(e.Key switch
                {
                    "appearance" => new SettingNow(e, m.Appearance.ToString().ToLowerInvariant()),
                    "colour_theme" => new SettingNow(e, m.ColourTheme, string.Join(", ", ColourThemes.All.Select(t => t.Name))),
                    "start_at_login" => new SettingNow(e, OnOff(m.StartAtLogin)),
                    "auto_update" => new SettingNow(e, OnOff(m.AutoUpdateHere)),
                    "shortcuts_anywhere" => new SettingNow(e, OnOff(m.Shortcuts)),
                    "transcription_model" => new SettingNow(e, m.Models.FirstOrDefault(x => x.Chosen)?.Model.Id ?? "",
                        string.Join("; ", m.Models.Select(x => $"{x.Model.Id} ({x.Name}, {x.Size}{(x.Recommended ? ", recommended here" : "")}{(x.Here ? ", downloaded" : "")})"))),
                    "live_transcript" => new SettingNow(e, m.LiveTranscript ? "as_you_record" : "after_class"),
                    "language" => new SettingNow(e, m.Language.Length > 0 ? m.Language : "auto"),
                    "computer_audio" => new SettingNow(e, OnOff(m.ComputerAudio)),
                    "speakers" => new SettingNow(e, OnOff(m.Speakers)),
                    "keep_recordings_days" => new SettingNow(e, m.KeepAudio),
                    "display_name" => new SettingNow(e, m.DisplayName),
                    "library_auto_update" => new SettingNow(e, OnOff(m.Lib.AutoUpdate)),
                    "write_notes" => new SettingNow(e, OnOff(m.Lib.WriteNotes)),
                    "sort_with_ai" => new SettingNow(e, OnOff(m.Lib.SortWithAi)),
                    "sort_confidence" => new SettingNow(e, Math.Round(m.Lib.Confidence * 100).ToString(CultureInfo.InvariantCulture)),
                    _ => new SettingNow(e, ""),
                });
            }
            return list;
        });

        public Task OpenAsync(SettingEntry entry) => Ui(() =>
        {
            m.Show(entry);
            return Task.FromResult(true);
        });

        static bool? Flag(string v) => v.ToLowerInvariant() switch
        {
            "on" or "true" or "yes" or "1" => true,
            "off" or "false" or "no" or "0" => false,
            _ => null,
        };

        public Task<string?> ChangeAsync(SettingEntry e, string value) => Ui(async () =>
        {
            string v = value.Trim();
            if (LibraryOwn(e) && await LibraryAsync() is { } why) return $"The library's settings can't be changed now: {why}";
            string? refused = Change(e, v);
            if (refused is null) m.Show(e);
            return refused;
        });

        /// <summary>Sets one setting through the window's own property (which saves it), or says why not.</summary>
        string? Change(SettingEntry e, string v)
        {
            bool? on = Flag(v);
            string needsOnOff = $"{e.Title} is on or off.";
            switch (e.Key)
            {
                case "appearance":
                    AppAppearance? mode = v.ToLowerInvariant() switch
                    {
                        "system" or "match system" or "auto" => AppAppearance.System,
                        "light" => AppAppearance.Light,
                        "dark" => AppAppearance.Dark,
                        _ => null,
                    };
                    if (mode is not { } chosen) return "Mode is system, light or dark.";
                    m.Appearance = chosen;
                    return null;
                case "colour_theme":
                    if (ColourThemes.All.FirstOrDefault(t => t.Name.Equals(v, StringComparison.OrdinalIgnoreCase)) is not { } theme)
                        return $"There's no colour theme called \"{v}\". They are: {string.Join(", ", ColourThemes.All.Select(t => t.Name))}.";
                    m.ColourTheme = theme.Name;
                    return null;
                case "start_at_login":
                    if (on is not { } login) return needsOnOff;
                    m.StartAtLogin = login;
                    return m.StartAtLogin == login ? null : "This computer wouldn't change it. The student can try the box in General.";
                case "auto_update":
                    if (on is not { } auto) return needsOnOff;
                    m.AutoUpdateHere = auto;
                    return null;
                case "shortcuts_anywhere":
                    if (on is not { } keys) return needsOnOff;
                    m.Shortcuts = keys;
                    return null;
                case "transcription_model":
                    if (m.Models.FirstOrDefault(x => x.Model.Id.Equals(v, StringComparison.OrdinalIgnoreCase) || x.Name.Equals(v, StringComparison.OrdinalIgnoreCase)) is not { } model)
                        return $"There's no model \"{v}\" here. They are: {string.Join(", ", m.Models.Select(x => x.Model.Id))}.";
                    if (m.host.Recorder.Current is not null) return "Not while a lecture is recording: the student can change it once it's over.";
                    m.PickModelCommand.Execute(model);
                    return null;
                case "live_transcript":
                    bool? live = v.ToLowerInvariant().Replace(' ', '_') switch
                    {
                        "as_you_record" or "live" => true,
                        "after_class" or "after" => false,
                        _ => null,
                    };
                    if (live is not { } l) return "It's as_you_record or after_class.";
                    m.LiveTranscript = l;
                    return null;
                case "language":
                    string lang = v.Equals("auto", StringComparison.OrdinalIgnoreCase) ? "" : v.ToLowerInvariant();
                    if (lang.Length > 12 || !lang.All(c => char.IsAsciiLetter(c) || c == '-')) return "Give a language code like en, es or fr, or auto.";
                    m.Language = lang;
                    return null;
                case "computer_audio":
                    if (!m.CanRecordComputerAudio) return "This computer can't record its own sound.";
                    if (on is not { } audio) return needsOnOff;
                    m.ComputerAudio = audio;
                    return null;
                case "speakers":
                    if (on is not { } speakers) return needsOnOff;
                    m.Speakers = speakers;
                    return null;
                case "keep_recordings_days":
                    if (!int.TryParse(v.Split(' ')[0], NumberStyles.None, CultureInfo.InvariantCulture, out int days) || days > 3650)
                        return "Give a number of days, 0 to 3650 (0 keeps them).";
                    m.KeepAudio = days.ToString(CultureInfo.InvariantCulture);
                    return null;
                case "display_name":
                    if (v.Length > 60) return "Keep the name under 60 characters.";
                    m.DisplayName = v;
                    return null;
                case "library_auto_update":
                    if (on is not { } libAuto) return needsOnOff;
                    m.Lib.AutoUpdate = libAuto;
                    return null;
                case "write_notes":
                    if (on is not { } write) return needsOnOff;
                    m.Lib.WriteNotes = write;
                    return null;
                case "sort_with_ai":
                    if (on is not { } sort) return needsOnOff;
                    m.Lib.SortWithAi = sort;
                    return null;
                case "sort_confidence":
                    string pct = v.TrimEnd('%').Trim();
                    if (!double.TryParse(pct, NumberStyles.Float, CultureInfo.InvariantCulture, out double p)) return "Give 40, 50, 60, 70, 80 or 90.";
                    if (p > 1) p /= 100;
                    if (m.Lib.ConfidenceChoices.FirstOrDefault(c => Math.Abs(double.Parse(c.Id, CultureInfo.InvariantCulture) - p) < 0.001) is not { } choice)
                        return "Give 40, 50, 60, 70, 80 or 90.";
                    choice.Pick.Execute(null);
                    return null;
            }
            return $"{e.Title} can't be changed from here.";
        }

        public Task<string?> AddClassAsync(string name, string about) => Ui(async () =>
        {
            if (await LibraryAsync() is { } why) return $"Classes can't be added now: {why}";
            if (m.Lib.Classes.Any(c => c.Name.Trim().Equals(name, StringComparison.OrdinalIgnoreCase))) return $"There's already a class called {name}.";
            m.Section = "Classes";
            m.Lib.NewClass = name;
            await m.Lib.AddClassCommand.ExecuteAsync(null);
            if (m.Lib.NewClass.Length > 0) return m.Lib.Say ?? "The library didn't take it.";
            if (about.Length > 0 && m.Lib.Classes.FirstOrDefault(c => c.Name.Trim() == name) is { } row)
            {
                row.Description = about;
                await m.Lib.SaveClassesCommand.ExecuteAsync(null);
            }
            return (string?)null;
        });
    }
}
