using Avalonia.Controls;
using Avalonia.Threading;
using StudyStash.App.ViewModels;

namespace StudyStash.App.Views;

public partial class WinNewFolder : UserControl
{
    public WinNewFolder()
    {
        InitializeComponent();
        // The name box takes the keyboard as soon as the prompt shows.
        DataContextChanged += (_, _) =>
        {
            if (DataContext is LibraryModel m)
                m.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(LibraryModel.AskingFolder) && m.AskingFolder)
                        Dispatcher.UIThread.Post(() => FolderName.Focus(), DispatcherPriority.Input);
                };
        };
    }
}
