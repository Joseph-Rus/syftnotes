using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudyStash.App.Services;

namespace StudyStash.App.ViewModels;

/// <summary>
/// A class page's reader (design 10): a module page's saved text, or an announcement's body, under one small meta
/// line ("Announcement · Dr. Okafor · Mon 22 Sep" / "Page · CS 101"). <see cref="CanvasClassModel"/> builds one of
/// these and hands it to the host whenever the student opens a page or an announcement.
/// </summary>
public sealed partial class CanvasReaderModel(CanvasContext context) : ObservableObject
{
    [ObservableProperty] public partial string Meta { get; set; } = "";
    [ObservableProperty] public partial string Title { get; set; } = "";
    [ObservableProperty] public partial string Body { get; set; } = "";

    string? url;

    /// <summary>"Announcement · Dr. Okafor · Mon 22 Sep".</summary>
    public void ShowAnnouncement(CanvasApi.AnnouncementRow a)
    {
        string author = a.Author is { Length: > 0 } au ? $" · {au}" : "";
        Meta = $"Announcement{author} · {CanvasWords.Day(a.PostedAt, context.Clock.Zone)}";
        Title = a.Title;
        Body = a.Body ?? "";
        url = a.Url;
    }

    /// <summary>"Page · CS 101".</summary>
    public void ShowPage(string cls, string title, string body, string? url)
    {
        Meta = $"Page · {cls}";
        Title = title;
        Body = body;
        this.url = url;
    }

    [RelayCommand]
    void OpenInCanvas()
    {
        if (url is { Length: > 0 } u) context.Actions.OpenUrl(u);
    }
}
