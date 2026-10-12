using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudyStash.App.ViewModels;
using StudyStash.Core;
using StudyStash.Core.Setup;

namespace StudyStash.App.Services;

/// <summary>One search result in Settings' sidebar: the setting, the section it's in, and that section's icon.</summary>
public sealed class SettingsHit(SettingEntry entry, string glyph)
{
    public SettingEntry Entry { get; } = entry;
    public string Glyph { get; } = glyph;
    public string Title => Entry.Title;
    /// <summary>Where it is: the section for a setting, what the page is for for a page.</summary>
    public string Where => Entry.IsPage ? Py.Head(Entry.Detail, 60) : SettingsIndex.SectionName(Entry.Section);
}

/// <summary>Settings' search (the field above the sidebar) and its helper ("Ask your AI").</summary>
public sealed partial class SettingsModel
{
    /// <summary>The helper's row, above the sidebar's two groups.</summary>
    public NavItem AssistantNav { get; } = new() { Id = "Assistant", Glyph = "forum", Label = "Ask your AI" };

    public bool OnAssistant => Section == "Assistant";

    /// <summary>Settings' helper: the student's own AI, finding and changing settings for them.</summary>
    public SettingsChatModel Assistant { get; private set; } = null!;

    /// <summary>What's typed in the search field; while there's anything, the sidebar shows what matches.</summary>
    [ObservableProperty] public partial string SearchText { get; set; } = "";
    public ObservableCollection<SettingsHit> SearchHits { get; } = [];
    public bool Searching => SearchText.Trim().Length > 0;
    public bool NoHits => Searching && SearchHits.Count == 0;
    /// <summary>The row under the results that hands the search to the helper.</summary>
    public string AskLine => $"Ask {Assistant.Brand} about “{Py.Head(SearchText.Trim(), 40)}”";

    /// <summary>Asks the window to pick out a setting on the page now showing (it scrolls to it and flashes it).</summary>
    public event Action<SettingEntry>? Spotlit;

    /// <summary>Whether this computer's Settings has <paramref name="entry"/>: its section is in the sidebar, and its
    /// role shows it.</summary>
    public bool Shows(SettingEntry entry) =>
        NavItems.Any(n => n.Id == entry.Section) && SettingsIndex.ShownOn(entry, host.Settings.Role != AppRole.Library, host.Settings.Role == AppRole.Laptop);

    partial void OnSearchTextChanged(string value)
    {
        SearchHits.Clear();
        if (value.Trim().Length > 0)
            foreach (var e in SettingsIndex.Search(value, Shows))
                SearchHits.Add(new SettingsHit(e, NavItems.FirstOrDefault(n => n.Id == e.Section)?.Glyph ?? "settings"));
        OnPropertyChanged(nameof(Searching));
        OnPropertyChanged(nameof(NoHits));
        OnPropertyChanged(nameof(AskLine));
    }

    /// <summary>Goes to <paramref name="entry"/>'s page and picks the setting out.</summary>
    public void Show(SettingEntry entry)
    {
        Section = entry.Section;
        if (!entry.IsPage) Spotlit?.Invoke(entry);
    }

    [RelayCommand]
    void OpenHit(SettingsHit hit) => Show(hit.Entry);

    /// <summary>Return in the search field: the first result.</summary>
    [RelayCommand]
    void OpenFirstHit()
    {
        if (SearchHits.Count > 0) Show(SearchHits[0].Entry);
        else if (Searching) AskAboutSearch();
    }

    [RelayCommand]
    void ClearSearch() => SearchText = "";

    /// <summary>The search, asked of the helper instead: its page opens with the words sent.</summary>
    [RelayCommand]
    void AskAboutSearch()
    {
        string words = SearchText.Trim();
        if (words.Length == 0) return;
        SearchText = "";
        Section = "Assistant";
        _ = Assistant.SendAsync(words);
    }
}
