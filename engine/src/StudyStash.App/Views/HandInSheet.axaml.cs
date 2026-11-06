using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using StudyStash.App.Controls;
using StudyStash.App.ViewModels;

namespace StudyStash.App.Views;

/// <summary>The checklist after a hand-in, over a window's dialog layer (<see cref="ShowAsync"/>). It closes when the
/// model is <see cref="HandInModel.Over"/>: filed, or Not now.</summary>
public partial class HandInSheet : UserControl
{
    public HandInSheet()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    /// <summary>Shows <paramref name="model"/> over <paramref name="layer"/>'s window, without bringing the window forward,
    /// and finds what was handed in; until it's over. Escape or a click outside counts as Not now. Nothing shows when
    /// something is over the window already.</summary>
    public static async Task ShowAsync(ConfirmLayer layer, HandInModel model)
    {
        if (layer.IsOpen) return;
        var sheet = new HandInSheet { DataContext = model };
        void Close(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(HandInModel.Over) && model.Over) layer.Close(null);
        }
        model.PropertyChanged += Close;
        try
        {
            var showing = layer.ShowAsync(sheet, raise: false);
            await model.LoadAsync();
            await showing;
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

    // "Not now" has the keyboard: Return and Space move nothing.
    void OnLoaded(object? sender, RoutedEventArgs e) => Dispatcher.UIThread.Post(() => NotNow.Focus(NavigationMethod.Tab), DispatcherPriority.Loaded);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key != Key.Escape || DataContext is not HandInModel model) return;
        e.Handled = true;
        model.NotNowCommand.Execute(null);
    }
}
