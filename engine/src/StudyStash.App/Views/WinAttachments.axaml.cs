using Avalonia.Controls;

namespace StudyStash.App.Views;

public partial class WinAttachments : UserControl
{
    public WinAttachments()
    {
        InitializeComponent();
        AttachFiles.Wire(this);
    }
}
