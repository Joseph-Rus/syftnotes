using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Styling;
using Avalonia.Threading;
using SkiaSharp;
using StudyStash.App.Controls;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;
using StudyStash.Core;

using Avalonia.Input;
using Avalonia.VisualTree;
using Avalonia.Controls.Primitives;
using StudyStash.App.Controls.Rich;
using StudyStash.App.Platform;
using StudyStash.Core.Rich;
using StudyStash.Core.Setup;
using System.Net.Http;

namespace StudyStash.App.Tests;

/// <summary>(Only the App tests have the fake hardware and library settings this needs.)</summary>
public static partial class Scenes
{
    /// <summary>A settings window over a temp home (never a real one), for a section's shot; disposed after.</summary>
    internal static (Services.SettingsModel Model, Services.AppHost Host, string Home) MakeSettings(string section, SkinKind skin = SkinKind.Mac)
    {
        string home = Path.Combine(Path.GetTempPath(), "studystash-settings-" + Guid.NewGuid().ToString("N"));
        // "Recording": the models on a Mac with Apple silicon, or a PC with no graphics card Whisper can use, the one
        // for it in use; "Recording-heavier": an Intel Mac or that PC still on large-v3 from before, one model
        // downloading.
        bool heavier = section == "Recording-heavier";
        if (heavier) new Services.AppSettings { SetupDone = true, Model = Audio.WhisperModels.LargeV3.Id }.Save(home);
        // "Recording-after": written down after class (in a window tall enough to show the choice under the models).
        bool after = section == "Recording-after";
        if (after) new Services.AppSettings { SetupDone = true, LiveTranscript = false }.Save(home);
        // "Recording-remove": switched to the lighter model, asked whether to remove the one before (Whisper small).
        bool removing = section == "Recording-remove";
        if (removing)
        {
            new Services.AppSettings { SetupDone = true, Model = Audio.WhisperModels.LargeV3TurboSmall.Id }.Save(home);
            ModelAdviceTests.PretendDownloaded(home, Audio.WhisperModels.Small);
        }
        var hardware = FakeHardware.For(skin, appleSilicon: !heavier && !removing);
        // "One-computer…": the Library page of just this computer's library, before a laptop is added, while one is
        // being added (its password typed), and once laptops can connect.
        bool one = section.StartsWith("One-computer", StringComparison.Ordinal);
        if (one) new Services.AppSettings { SetupDone = true, Role = Services.AppRole.Both }.Save(home);
        var host = new Services.AppHost(home, hardware: hardware);
        // The library's own pages read the design's example library (Sam's, on a Mac mini); "Unreachable" shows the
        // Library page when it doesn't answer.
        var library = new FakeLibrarySettings { Down = section == "Unreachable" };
        if (one) library.Settings["reach"]!["laptops"] = section == "One-computer-on";
        // "Rename": Settings → Classes with "Use Canvas course names" and its preview open.
        bool renaming = section == "Rename";
        if (renaming) library.Settings = FakeLibrarySettings.CodeNamed();
        var model = Services.SettingsModel.Make(host, library: () => library.Call);
        model.Section = section == "Unreachable" || one ? "Library" : renaming ? "Classes" : heavier || removing || after ? "Recording" : section;
        if (removing && model.Models.FirstOrDefault(c => c.CanRemove) is { } spare) model.AskRemoveModelCommand.Execute(spare);
        if (heavier)
        {
            model.ModelDownloading = true;
            model.ModelProgress = 0.38;
            model.ModelLine = "Downloading Whisper large-v3 turbo (compact): 218 MB of 574 MB. About a minute left.";
        }
        if (renaming) model.Lib.ConfirmingCourseNames = true;
        if (section == "One-computer-adding")
        {
            model.Lib.AddLaptopCommand.Execute(null);
            model.Lib.LaptopPassword = "correct-horse";
        }
        if (section == "One-computer-on")
            model.Lib.LaptopSay = "Laptops can connect now. On your laptop, install Syft Notes, and in its setup open Advanced and choose “My laptop for class”. Then enter one of the addresses below and this password.";
        return (model, host, home);
    }
}
