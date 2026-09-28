using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json.Nodes;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudyStash.Core;

namespace StudyStash.App.ViewModels;

/// <summary>A phone paired with the library: its name, and when it was added and last used.</summary>
public sealed record PhoneRow(string Id, string Name, string Meta);

/// <summary>
/// Settings → Phone: adding a phone to the library, and the phones already added. "Add a phone" asks the library for a
/// 6-digit code and shows it with a QR code of the phone app's address and how long the code has left; once the
/// phone pairs, it shows up in the list, where Remove locks it out at once. Everything goes through the library's
/// API (/api/v2/devices), so a laptop adds a phone to its library as easily as the library's own computer does.
/// </summary>
public sealed partial class PhonesModel : ObservableObject
{
    readonly Func<LibrarySettingsModel.Call?> connect;
    /// <summary>The time now: a test's clock stands still until it moves it.</summary>
    readonly Func<DateTimeOffset> now;
    DateTimeOffset until;
    int ticks;

    /// <summary>Over the library <paramref name="connect"/> reaches (null: no library set up yet).</summary>
    public PhonesModel(Func<LibrarySettingsModel.Call?> connect, Func<DateTimeOffset>? now = null)
    {
        this.connect = connect;
        this.now = now ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>Starts (true) or stops (false) the once-a-second tick while a code is on show. The view gives a timer;
    /// a test calls <see cref="Tick"/> itself.</summary>
    public Action<bool>? Ticking { get; set; }

    [ObservableProperty] public partial LibrarySettingsState State { get; set; } = LibrarySettingsState.Loading;
    public ObservableCollection<PhoneRow> Phones { get; } = [];
    /// <summary>What just happened, or why it didn't.</summary>
    [ObservableProperty] public partial string? Say { get; set; }
    /// <summary>A code is on show.</summary>
    [ObservableProperty] public partial bool Adding { get; set; }
    [ObservableProperty] public partial bool Asking { get; set; }
    /// <summary>"123 456", as it's typed.</summary>
    [ObservableProperty] public partial string Code { get; set; } = "";
    [ObservableProperty] public partial string Url { get; set; } = "";
    /// <summary>The QR code's dark squares, in a square <see cref="QrSize"/> wide (margin included).</summary>
    [ObservableProperty] public partial Geometry? Qr { get; set; }
    [ObservableProperty] public partial int QrSize { get; set; }
    /// <summary>"Works for 9:41 more, once." or that it ran out.</summary>
    [ObservableProperty] public partial string Left { get; set; } = "";
    [ObservableProperty] public partial bool RanOut { get; set; }

    public bool IsReady => State == LibrarySettingsState.Ready;
    public bool IsLoading => State == LibrarySettingsState.Loading;
    public bool ShowProblem => State is LibrarySettingsState.Unreachable or LibrarySettingsState.Older or LibrarySettingsState.NoLibrary;
    public bool CanRetry => State == LibrarySettingsState.Unreachable;
    public string Problem => State switch
    {
        LibrarySettingsState.NoLibrary => "There's no library yet. Connect to one in Connection, then add your phone here.",
        LibrarySettingsState.Unreachable => "Can't reach your library right now. Is its computer on, and Tailscale connected?",
        LibrarySettingsState.Older => "Your library runs an older Study Stash, without the phone app. Update it to add your phone.",
        _ => "",
    };
    public bool NoPhones => Phones.Count == 0;
    public bool ShowAdd => IsReady && !Adding;
    public string AddTitle => Phones.Count == 0 ? "Read your notes and lectures on your phone" : "Add another phone";
    public string AddSub => "Your phone reaches the library over Tailscale: install it on your phone and sign in to the same account.";

    partial void OnStateChanged(LibrarySettingsState value)
    {
        foreach (string p in new[] { nameof(IsReady), nameof(IsLoading), nameof(ShowProblem), nameof(CanRetry), nameof(Problem), nameof(ShowAdd) }) OnPropertyChanged(p);
    }

    partial void OnAddingChanged(bool value) => OnPropertyChanged(nameof(ShowAdd));

    /// <summary>Ask the library for its phones (when the page opens, and on Try again).</summary>
    [RelayCommand]
    public async Task Load()
    {
        if (connect() is not { } call)
        {
            State = LibrarySettingsState.NoLibrary;
            return;
        }
        if (State != LibrarySettingsState.Ready) State = LibrarySettingsState.Loading;
        try
        {
            if (await call(HttpMethod.Get, "", null) is not { } answer)
            {
                State = LibrarySettingsState.Older;
                return;
            }
            Fill(answer);
            State = LibrarySettingsState.Ready;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or LibraryRefusedException or InvalidOperationException)
        {
            State = LibrarySettingsState.Unreachable;
        }
    }

    static string Str(JsonNode? n) => n is JsonValue v && v.TryGetValue(out string? s) ? s : "";

    static DateTimeOffset? When(JsonNode? n) =>
        DateTimeOffset.TryParse(Str(n), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d) ? d : null;

    /// <summary>"Added Sep 28 · used today".</summary>
    string Meta(DateTimeOffset? added, DateTimeOffset? seen)
    {
        string Day(DateTimeOffset d)
        {
            var local = d.ToLocalTime().Date;
            var today = now().ToLocalTime().Date;
            if (local == today) return "today";
            if (local == today.AddDays(-1)) return "yesterday";
            return local.ToString(local.Year == today.Year ? "MMM d" : "MMM d, yyyy", CultureInfo.InvariantCulture);
        }
        var parts = new List<string>();
        if (added is { } a) parts.Add("Added " + Day(a));
        if (seen is { } s) parts.Add("used " + Day(s));
        return string.Join(" · ", parts);
    }

    /// <summary>The library's list, in place of ours. The names of phones that weren't there before.</summary>
    List<string> Fill(JsonObject answer)
    {
        var had = Phones.Select(p => p.Id).ToHashSet();
        var rows = (answer["devices"] as JsonArray ?? []).OfType<JsonObject>()
            .Select(d => new PhoneRow(Str(d["id"]), Str(d["name"]), Meta(When(d["added"]), When(d["lastSeen"]))))
            .Where(r => r.Id.Length > 0).ToList();
        Phones.Clear();
        foreach (var r in rows) Phones.Add(r);
        OnPropertyChanged(nameof(NoPhones));
        OnPropertyChanged(nameof(AddTitle));
        return [.. rows.Where(r => !had.Contains(r.Id)).Select(r => r.Name)];
    }

    /// <summary>"Add a phone": a code from the library, with the QR code of where the phone goes.</summary>
    [RelayCommand]
    async Task AddPhone()
    {
        if (connect() is not { } call || Asking) return;
        Asking = true;
        Say = null;
        try
        {
            if (await call(HttpMethod.Post, "/code", new JsonObject()) is not { } code)
            {
                State = LibrarySettingsState.Older;
                return;
            }
            string digits = Str(code["code"]);
            Url = Str(code["url"]);
            Code = digits.Length == 6 ? $"{digits[..3]} {digits[3..]}" : digits;
            until = When(code["expires"]) ?? now() + TimeSpan.FromMinutes(10);
            var modules = StudyStash.Core.Qr.Modules(Url);
            QrSize = modules.GetLength(0) + 2 * StudyStash.Core.Qr.Margin;
            Qr = Geometry.Parse(StudyStash.Core.Qr.PathData(modules));
            ticks = 0;
            Adding = true;
            Tick();
            Ticking?.Invoke(true);
        }
        catch (LibraryRefusedException e)
        {
            Say = e.Message;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            Say = "Can't reach your library right now. Is its computer on, and Tailscale connected?";
        }
        finally
        {
            Asking = false;
        }
    }

    /// <summary>Once a second while a code is on show: how long it has left, and every few seconds whether a phone has
    /// paired with it (then the code goes, and the new phone is named).</summary>
    public void Tick()
    {
        if (!Adding) return;
        var left = until - now();
        RanOut = left <= TimeSpan.Zero;
        Left = RanOut ? "This code has run out. Make a new one." : $"Works for {(int)left.TotalMinutes}:{left.Seconds:00} more, once.";
        if (RanOut) Ticking?.Invoke(false);
        if (ticks++ % 3 == 2) _ = CheckPairedAsync();
    }

    async Task CheckPairedAsync()
    {
        if (connect() is not { } call) return;
        try
        {
            if (await call(HttpMethod.Get, "", null) is not { } answer) return;
            var added = Fill(answer);
            if (added.Count > 0 && Adding)
            {
                Done();
                Say = $"{added[0]} is added. It stays signed in until you remove it here.";
            }
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or LibraryRefusedException or InvalidOperationException)
        {
            // the next tick asks again
        }
    }

    /// <summary>Put the code away.</summary>
    [RelayCommand]
    public void Done()
    {
        Adding = false;
        Ticking?.Invoke(false);
        Code = "";
        Qr = null;
    }

    /// <summary>Remove a phone: it can't read the library from its next request.</summary>
    [RelayCommand]
    async Task Remove(PhoneRow? row)
    {
        if (row is null || connect() is not { } call) return;
        try
        {
            var answer = await call(HttpMethod.Delete, "/" + Uri.EscapeDataString(row.Id), null);
            if (answer is not null) Fill(answer);
            else Phones.Remove(row);
            OnPropertyChanged(nameof(NoPhones));
            OnPropertyChanged(nameof(AddTitle));
            Say = $"{row.Name} is removed. It can't read your library any more.";
        }
        catch (LibraryRefusedException e)
        {
            Say = e.Message;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            Say = "Can't reach your library right now, so the phone is still there. Try again in a moment.";
        }
    }
}
