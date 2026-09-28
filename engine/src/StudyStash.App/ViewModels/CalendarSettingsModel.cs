using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudyStash.Core.Calendar;
using StudyStash.Core.Calendar.Ics;

namespace StudyStash.App.ViewModels;

/// <summary>One calendar a source has, as Settings shows it: its name, colour dot, and a switch for whether it's
/// shown. Flipping the switch saves at once, through <see cref="OnToggled"/> (set by <see cref="CalendarSettingsModel"/>
/// when it builds the row).</summary>
public sealed partial class CalendarRow : ObservableObject
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required IBrush Dot { get; init; }
    [ObservableProperty] public partial bool On { get; set; }

    public Action<CalendarRow>? OnToggled { get; set; }

    partial void OnOnChanged(bool value) => OnToggled?.Invoke(this);
}

/// <summary>One connected source in Settings → Calendars: its name, kind (its icon), the calendars it has with a
/// switch each, and, when the last read failed, why in plain words. Remove takes the whole source away.</summary>
public sealed partial class CalendarSourceRow : ObservableObject
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Glyph { get; init; }
    [ObservableProperty] public partial string? Problem { get; set; }
    public bool HasProblem => Problem is not null;
    public ObservableCollection<CalendarRow> Calendars { get; } = [];

    public Action<CalendarSourceRow>? OnRemove { get; set; }

    [RelayCommand]
    void Remove() => OnRemove?.Invoke(this);
}

/// <summary>
/// Settings → Calendars: the sources the student added (each a switch per calendar, and Remove), pasting a feed's
/// address to add one, and a Connect button for every kind that offers one (none yet this round: Apple, Google and
/// Microsoft plug into the same list later). Everything here reads and writes calendars.json directly — calendars
/// run on this computer, never through the library.
/// </summary>
public sealed partial class CalendarSettingsModel : ObservableObject
{
    readonly string home;
    readonly CalendarKinds kinds;
    readonly Action wake;

    /// <summary>Over calendars.json in <paramref name="home"/>, feeds read through <paramref name="kinds"/> (the
    /// app's registry by default); <paramref name="wake"/> tells the running read to pick up the change at once.</summary>
    public CalendarSettingsModel(string home, CalendarKinds? kinds = null, Action? wake = null)
    {
        this.home = home;
        this.kinds = kinds ?? CalendarKinds.Default;
        this.wake = wake ?? (() => { });
        Load();
    }

    public ObservableCollection<CalendarSourceRow> Sources { get; } = [];
    public bool HasSources => Sources.Count > 0;

    /// <summary>Kinds with a "Connect" button (none this round); shown instead of the "coming soon" note once one registers.</summary>
    public IReadOnlyList<CalendarKind> Connectable => kinds.Connectable;
    public bool HasConnectable => Connectable.Count > 0;

    [ObservableProperty] public partial string FeedUrl { get; set; } = "";
    [ObservableProperty] public partial string? FeedError { get; set; }
    [ObservableProperty] public partial bool Adding { get; set; }

    /// <summary>Read calendars.json afresh: called after every change, so the list always shows what's actually saved.</summary>
    public void Load()
    {
        var settings = CalendarSettings.Load(home);
        Sources.Clear();
        foreach (var source in settings.Sources)
        {
            var row = new CalendarSourceRow
            {
                Id = source.Id,
                Name = source.Name,
                Glyph = kinds.Find(source.Kind)?.Glyph ?? "event",
                Problem = settings.Problems.TryGetValue(source.Id, out string? p) ? p : null,
            };
            foreach (var c in settings.Calendars.Where(c => c.SourceId == source.Id))
            {
                var calRow = new CalendarRow { Id = c.Id, Name = c.Name, Dot = DotOf(c.Color), On = settings.IsEnabled(c) };
                calRow.OnToggled = Toggle;
                row.Calendars.Add(calRow);
            }
            row.OnRemove = r => Remove(r.Id);
            Sources.Add(row);
        }
        OnPropertyChanged(nameof(HasSources));
    }

    static IBrush DotOf(string? hex)
    {
        if (hex is not null)
        {
            try
            {
                return new SolidColorBrush(Color.Parse(hex));
            }
            catch (FormatException)
            {
            }
        }
        return Brushes.Gray;
    }

    [RelayCommand]
    async Task AddFeed()
    {
        FeedError = null;
        if (IcsSource.CleanUrl(FeedUrl) is not { } url)
        {
            FeedError = CalendarWords.NotAnAddress;
            return;
        }
        string id = IcsSource.IdFor(url);
        if (CalendarSettings.Load(home).Source(id) is not null)
        {
            FeedError = CalendarWords.AlreadyAdded;
            return;
        }
        Adding = true;
        try
        {
            var settings = IcsSource.New(url, null, DateTimeOffset.Now);
            if (kinds.Create(settings) is not { } source)
            {
                FeedError = CalendarWords.NotAnAddress;
                return;
            }
            var calendars = await source.ListCalendarsAsync(default);
            if (calendars.Count > 0 && calendars[0].Name.Length > 0) settings.Name = calendars[0].Name;
            CalendarSettings.Update(home, c =>
            {
                c.Sources.Add(settings);
                c.Listed(settings.Id, calendars);
            });
            FeedUrl = "";
            Load();
            wake();
        }
        catch (CalendarFeedException e)
        {
            FeedError = e.Message;
        }
        finally
        {
            Adding = false;
        }
    }

    void Remove(string sourceId)
    {
        CalendarSettings.Update(home, c => c.Remove(sourceId));
        Load();
        wake();
    }

    void Toggle(CalendarRow row)
    {
        CalendarSettings.Update(home, c => c.Enabled[row.Id] = row.On);
        wake();
    }

    [RelayCommand]
    async Task Connect(CalendarKind kind)
    {
        if (kind.Connect is null) return;
        if (await kind.Connect(default) is not { } settings) return;
        CalendarSettings.Update(home, c => c.Sources.Add(settings));
        Load();
        wake();
    }
}
