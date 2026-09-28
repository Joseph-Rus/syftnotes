using Avalonia.Controls;

namespace StudyStash.App.Views;

public partial class MacAttachments : UserControl
{
    public MacAttachments()
    {
        InitializeComponent();
        AttachFiles.Wire(this);
    }
}
