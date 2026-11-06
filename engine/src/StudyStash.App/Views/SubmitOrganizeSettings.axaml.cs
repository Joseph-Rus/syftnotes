using Avalonia.Controls;
using Avalonia.Platform.Storage;
using StudyStash.App.ViewModels;

namespace StudyStash.App.Views;

/// <summary>"After you hand in" and "Filed work": its Add a folder… opens the system's folder picker over this window.</summary>
public partial class SubmitOrganizeSettings : UserControl
{
    public SubmitOrganizeSettings()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is SubmitOrganizeModel m) m.PickFolder ??= PickAsync;
        };
    }

    async Task<string?> PickAsync()
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { CanPickFolder: true } storage) return null;
        var picked = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = FilingWords.FoldersTitle });
        return picked.Count > 0 ? picked[0].TryGetLocalPath() : null;
    }
}
