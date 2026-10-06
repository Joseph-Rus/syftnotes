using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using StudyStash.App.ViewModels;

namespace StudyStash.App.Views;

/// <summary>What the notes' editor does the same way in both looks: "Edit" puts the caret in it, and ⌘S (Ctrl+S on
/// Windows) saves.</summary>
static class AiNotesEditor
{
    public static void Attach(Control view, TextBox editor)
    {
        AiNotesModel? model = null;
        void Changed(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AiNotesModel.Editing) && model?.Editing == true)
                Dispatcher.UIThread.Post(() => editor.Focus(), DispatcherPriority.Loaded);
        }
        view.DataContextChanged += (_, _) =>
        {
            if (model is not null) model.PropertyChanged -= Changed;
            model = view.DataContext as AiNotesModel;
            if (model is not null) model.PropertyChanged += Changed;
        };
        editor.KeyDown += (_, e) =>
        {
            var command = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
            if (e.Key != Key.S || e.KeyModifiers != command || model is null) return;
            e.Handled = true;
            model.SaveEditCommand.Execute(null);
        };
    }
}
