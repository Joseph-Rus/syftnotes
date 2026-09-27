using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace StudyStash.App.Controls;

/// <summary>Study Stash's own icon in a view: the cream tile with the navy "S." (Assets/icon.png, drawn by
/// macos/make_icon.swift), e.g. at the left of a Windows title bar.</summary>
public sealed class AppIcon : Image
{
    static Bitmap? tile;

    public AppIcon()
    {
        Source = tile ??= new Bitmap(AssetLoader.Open(new Uri("avares://StudyStash/Assets/icon.png")));
        Width = Height = 16;
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.HighQuality);
    }
}

/// <summary>The monochrome "S." (the menu bar's and the tray's icon) in <see cref="Foreground"/>: for pictures of the
/// tray or menu bar inside the app, like setup's "Keep Study Stash on the taskbar".</summary>
public sealed class AppMarkGlyph : Border
{
    public static readonly StyledProperty<IBrush?> ForegroundProperty = AvaloniaProperty.Register<AppMarkGlyph, IBrush?>(nameof(Foreground));

    static Bitmap? mark;

    public AppMarkGlyph()
    {
        mark ??= new Bitmap(AssetLoader.Open(new Uri("avares://StudyStash/Assets/mark-32.png")));
        OpacityMask = new ImageBrush(mark) { Stretch = Stretch.Uniform };
        Width = Height = 16;
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ForegroundProperty) Background = Foreground;
    }
}
