using Avalonia.Controls;
using StudyStash.App.ViewModels;

namespace StudyStash.App.Views;

public partial class MacOverview : UserControl
{
    public MacOverview()
    {
        InitializeComponent();
        AttachFiles.AcceptDrops(Page, () => (DataContext as OverviewModel)?.Files);
        // Another overview opens at its top, not wherever the last one was scrolled to; the same one read again
        // (Canvas synced, a lecture came in) stays where the student was.
        string? showing = null;
        DataContextChanged += (_, _) =>
        {
            if (DataContext is not OverviewModel m) return;
            string key = m.IsHome ? "" : m.ClassName;
            if (key != showing) Page.Offset = default;
            showing = key;
        };
    }
}
