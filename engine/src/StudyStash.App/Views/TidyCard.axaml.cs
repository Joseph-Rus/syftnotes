using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyStash.App.Controls;
using StudyStash.App.Platform;
using StudyStash.App.ViewModels;

namespace StudyStash.App.Views;

/// <summary>The tidy card over a window's dialog layer (<see cref="ShowAsync"/>). On Tidy up the files that move slide and
/// shrink into the class's folder, which bounces once (about 600 ms, <see cref="Motion"/>: none with reduced motion);
/// then it says Filed with Undo, and fades a few seconds later (not while the pointer is on it).</summary>
public partial class TidyCard : UserControl
{
    static readonly TimeSpan Flight = TimeSpan.FromMilliseconds(600), Stay = TimeSpan.FromSeconds(4), Fade = TimeSpan.FromMilliseconds(250);
    IDisposable? running;
    IDisposable? staying;

    public TidyCard()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        DataContextChanged += (_, _) =>
        {
            if (DataContext is TidyCardModel m) m.PropertyChanged += OnModelChanged;
        };
        DetachedFromVisualTree += (_, _) =>
        {
            running?.Dispose();
            staying?.Dispose();
        };
    }

    /// <summary>Shows <paramref name="model"/> over <paramref name="layer"/>'s window until it's over. Escape or a click
    /// outside counts as Later. Nothing shows when something is over the window already.</summary>
    public static async Task ShowAsync(ConfirmLayer layer, TidyCardModel model)
    {
        if (layer.IsOpen) return;
        var card = new TidyCard { DataContext = model };
        void Close(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(TidyCardModel.Over) && model.Over) layer.Close(null);
        }
        model.PropertyChanged += Close;
        try
        {
            await layer.ShowAsync(card);
        }
        finally
        {
            model.PropertyChanged -= Close;
        }
    }

    /// <summary>The system's file picker over the window <paramref name="near"/> is in, opened in <paramref name="folder"/>.</summary>
    public static async Task<string?> PickFileAsync(Control near, string folder, string title)
    {
        if (TopLevel.GetTopLevel(near)?.StorageProvider is not { CanOpen: true } storage) return null;
        var start = await storage.TryGetFolderFromPathAsync(folder);
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions { Title = title, SuggestedStartLocation = start, AllowMultiple = false });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    // Later takes the keyboard, quietly: no focus ring until the student uses the keyboard.
    void OnLoaded(object? sender, RoutedEventArgs e) => Dispatcher.UIThread.Post(() => Later.Focus(), DispatcherPriority.Loaded);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key != Key.Escape || DataContext is not TidyCardModel model) return;
        e.Handled = true;
        if (model.Filed) model.Close();
        else model.LaterCommand.Execute(null);
    }

    void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not TidyCardModel model) return;
        if (e.PropertyName == nameof(TidyCardModel.Moving) && model.Moving) Dispatcher.UIThread.Post(() => Fly(model));
        if (e.PropertyName == nameof(TidyCardModel.Undone)) StayThenFade(model);
    }

    /// <summary>The files that move fly into the folder and shrink as they go; everything else fades; the folder comes
    /// up and bounces once. Then the card says Filed.</summary>
    void Fly(TidyCardModel model)
    {
        var tiles = Files.GetVisualDescendants().OfType<Control>()
            .Where(c => c == HandedIn && HandedIn.IsVisible || c is Button { DataContext: TidyFile { On: true } } && c.Classes.Contains("tidy-file")).ToList();
        var rest = new Control[] { Buttons.Children[1], Buttons.Children[2], Title }.Concat(Files.GetVisualDescendants().OfType<Button>()
            .Where(b => b.Classes.Contains("tidy-file") && !tiles.Contains(b))).ToList();
        var target = Folder.TranslatePoint(new Point(Folder.Bounds.Width / 2, Folder.Bounds.Height / 2), Card) ?? default;
        var flights = tiles.Select(t =>
        {
            var from = t.TranslatePoint(new Point(t.Bounds.Width / 2, t.Bounds.Height / 2), Card) ?? target;
            var scale = new ScaleTransform();
            var shift = new TranslateTransform();
            t.RenderTransformOrigin = RelativePoint.Center;
            t.RenderTransform = new TransformGroup { Children = { scale, shift } };
            return (Tile: t, By: target - from, Scale: scale, Shift: shift);
        }).ToList();
        var bounce = new ScaleTransform();
        Folder.RenderTransformOrigin = RelativePoint.Center;
        Folder.RenderTransform = bounce;
        running = Motion.Animate(this, Flight, t =>
        {
            double go = Math.Min(1, t / 0.7);
            foreach (var f in flights)
            {
                f.Shift.X = f.By.X * go;
                f.Shift.Y = f.By.Y * go;
                f.Scale.ScaleX = f.Scale.ScaleY = 1 - 0.8 * go;
                f.Tile.Opacity = 1 - go;
            }
            foreach (var r in rest) r.Opacity = 1 - go;
            Folder.Opacity = Math.Min(1, t / 0.3);
            bounce.ScaleX = bounce.ScaleY = t > 0.7 ? 1 + 0.18 * Math.Sin(Math.PI * (t - 0.7) / 0.3) : 1;
        }, () =>
        {
            model.Moving = false;
            StayThenFade(model);
        });
    }

    /// <summary>A few seconds to press Undo (more while the pointer rests on the card), then it fades away.</summary>
    void StayThenFade(TidyCardModel model)
    {
        staying?.Dispose();
        staying = DispatcherTimer.RunOnce(() =>
        {
            if (IsPointerOver || model.Undoing.Busy)
            {
                StayThenFade(model);
                return;
            }
            running = Motion.Animate(this, Fade, t => Opacity = 1 - t, model.Close);
        }, Stay);
    }
}
