using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyStash.App.Controls;
using StudyStash.App.ViewModels;

namespace StudyStash.App.Views;

/// <summary>
/// The recorder's transcript keeps its newest line in view: whenever what it holds or the room it has changes (the live
/// words, every second or so; the recorder opening and growing), it scrolls to the bottom, where the newest is. A
/// student who scrolls up to read an older line, or presses on one to select it, keeps their place for a while, until
/// they scroll back down.
/// </summary>
sealed class NewestLine
{
    static readonly TimeSpan Reading = TimeSpan.FromSeconds(20);
    readonly ScrollViewer scroller;
    RecorderModel? model;
    DateTime readingSince = DateTime.MinValue;

    NewestLine(ScrollViewer scroller) => this.scroller = scroller;

    public static void Keep(UserControl view, ScrollViewer scroller)
    {
        var keep = new NewestLine(scroller);
        view.DataContextChanged += (_, _) => keep.Follow(view.DataContext as RecorderModel);
        keep.Follow(view.DataContext as RecorderModel);
        // A wheel (or trackpad) up is reading back; back at the bottom, it follows again.
        scroller.PointerWheelChanged += (_, e) =>
        {
            if (e.Delta.Y > 0) keep.readingSince = DateTime.UtcNow;
        };
        // So is a press on its words, to select some: the lines stay where they are while they're copied.
        scroller.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            if (e.Source is Visual v && v.GetSelfAndVisualAncestors().OfType<NoteText>().Any()) keep.readingSince = DateTime.UtcNow;
        }, RoutingStrategies.Bubble, handledEventsToo: true);
        scroller.ScrollChanged += (_, e) =>
        {
            if (e.ExtentDelta.Y != 0 || e.ViewportDelta.Y != 0) keep.Soon();
            else if (keep.AtBottom) keep.readingSince = DateTime.MinValue;
        };
    }

    bool AtBottom => scroller.Offset.Y >= scroller.Extent.Height - scroller.Viewport.Height - 24;

    void Follow(RecorderModel? m)
    {
        if (ReferenceEquals(m, model)) return;
        if (model is not null) model.PropertyChanged -= ModelChanged;
        model = m;
        if (m is not null) m.PropertyChanged += ModelChanged;
    }

    void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(RecorderModel.Expanded) || model?.Expanded != true) return;
        readingSince = DateTime.MinValue;
        Soon();
    }

    /// <summary>Once laid out: to the bottom, unless the student is reading further up.</summary>
    void Soon() => Dispatcher.UIThread.Post(() =>
    {
        if (DateTime.UtcNow - readingSince > Reading) scroller.ScrollToEnd();
    }, DispatcherPriority.Loaded);
}
