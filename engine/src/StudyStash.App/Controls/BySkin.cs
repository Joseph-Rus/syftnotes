using Avalonia.Controls;

namespace StudyStash.App.Controls;

/// <summary>
/// A place in a view both looks share for a piece each look draws its own way (what's attached, the notes with
/// "Rewrite", an answer's card): it holds the Mac's view or Windows', whichever look the app is in, and hands it the
/// data it was given. <c>&lt;c:BySkin Mac="{x:Type v:MacAttachments}" Win="{x:Type v:WinAttachments}" DataContext="{Binding Files}" /&gt;</c>
/// (A plain decorator: nothing of its own is drawn, and nothing of the view's is clipped, so a floating bar keeps its shadow.)
/// </summary>
public sealed class BySkin : Decorator
{
    public Type? Mac { get; set; }
    public Type? Win { get; set; }

    /// <summary>Classes for the view it makes (what a look's own view styles itself by: "inwindow").</summary>
    public string? ViewClasses { get; set; }

    /// <summary>The view it made, once it's in a window.</summary>
    public Control? View => Child;

    protected override void OnInitialized()
    {
        base.OnInitialized();
        var type = Skin.Current == SkinKind.Mac ? Mac : Win;
        if (Child is not null || type is null || Activator.CreateInstance(type) is not Control view) return;
        foreach (string name in (ViewClasses ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries)) view.Classes.Add(name);
        Child = view;
    }
}
