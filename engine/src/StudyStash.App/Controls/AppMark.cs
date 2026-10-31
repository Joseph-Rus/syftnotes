namespace StudyStash.App.Controls;

/// <summary>Syft Notes' own icon in a view: the app icon's dark tile, the pixel s on its empty grid (the boards' "app
/// icon ladder"), drawn as vectors, e.g. at the left of a Windows title bar. The bits under the s come at sizes with
/// room for them.</summary>
public sealed class AppIcon : SyftMark
{
    public AppIcon()
    {
        Look = SyftLook.Dark;
        Tile = SyftTile.Square;
        Size = Width = Height = 16;
    }
}

/// <summary>The one-colour pixel s (the menu bar's and the tray's icon) in <see cref="SyftMark.Foreground"/>: for
/// pictures of the tray or menu bar inside the app, like setup's "Keep Syft Notes on the taskbar".</summary>
public sealed class AppMarkGlyph : SyftMark
{
    public AppMarkGlyph()
    {
        Look = SyftLook.Ink;
        Size = Width = Height = 16;
    }
}
