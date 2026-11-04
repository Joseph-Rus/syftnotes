using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DialogHostAvalonia;
using StudyStash.App.ViewModels.Kit;

namespace StudyStash.App.Views;

/// <summary>The form for the student's own Due items, in a window's dialog layer (<see cref="Controls.ConfirmLayer.ShowAsync"/> shows
/// it). It ends the moment its model says it is no longer open (Save, Delete, Mark done, Cancel), and Escape asks for that.</summary>
public partial class OwnDueForm : UserControl
{
    OwnDueFormModel? model;

    public OwnDueForm()
    {
        InitializeComponent();
        Loaded += (_, _) => Dispatcher.UIThread.Post(() => TitleBox.Focus(), DispatcherPriority.Loaded);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (model is not null) model.PropertyChanged -= OnModelChanged;
        model = DataContext as OwnDueFormModel;
        if (model is not null) model.PropertyChanged += OnModelChanged;
    }

    /// <summary>A form whose model closed before it was on the screen (nothing to show any more) goes at once.</summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (model is { IsOpen: false }) Dispatcher.UIThread.Post(() => this.FindAncestorOfType<DialogHost>()?.CurrentSession?.Close(null));
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (model is not null) model.PropertyChanged -= OnModelChanged;
        model = null;
    }

    void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OwnDueFormModel.IsOpen) && model is { IsOpen: false }) this.FindAncestorOfType<DialogHost>()?.CurrentSession?.Close(null);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key != Key.Escape || e.Handled) return;
        e.Handled = true;
        model?.Close();
    }
}
