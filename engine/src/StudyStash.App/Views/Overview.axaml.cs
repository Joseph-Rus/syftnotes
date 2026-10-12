using Avalonia.Controls;
using Avalonia.Interactivity;
using StudyStash.App.ViewModels;

namespace StudyStash.App.Views;

public partial class Overview : UserControl
{
    public Overview()
    {
        InitializeComponent();
        AttachFiles.AcceptDrops(Page, () => (DataContext as OverviewModel)?.Files);
        Fades.Under(Fade, "Surface", 0.55);
        // Another page opens at its top, not wherever the last one was scrolled to; the same one read again (Canvas
        // synced, a note came in) stays where the student was.
        string? showing = null;
        DataContextChanged += (_, _) =>
        {
            if (DataContext is not OverviewModel m) return;
            string key = m.IsHome ? m.OnlyUnsorted ? "\u0001unsorted" : "" : m.IsDuePage ? "\u0001due" : m.ClassName;
            if (key != showing) Page.Offset = default;
            showing = key;
        };
        // Home's notes go on: near the end of what's showing, the next page is asked for.
        Page.ScrollChanged += (_, _) =>
        {
            if (DataContext is OverviewModel { IsHome: true, CanLoadMore: true, LoadingMore: false } m
                && Page.Extent.Height - Page.Offset.Y - Page.Viewport.Height < MoreWithin) m.LoadMoreCommand.Execute(null);
        };
    }

    /// <summary>How near the end of the list the next page is asked for.</summary>
    const double MoreWithin = 700;

    /// <summary>A pick in the filter closes its menu.</summary>
    void OnPicked(object? sender, RoutedEventArgs e) => Menus.Close(sender as Control);
}
