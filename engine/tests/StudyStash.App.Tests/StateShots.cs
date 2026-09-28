using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;

namespace StudyStash.App.Tests;

/// <summary>Setup states the static design doesn't show: hearing the microphone, a library "Find it" turned up, and
/// one made here.</summary>
public class StateShots
{
    static readonly ThemeVariant[] Themes = [ThemeVariant.Light, ThemeVariant.Dark];
    static readonly double[] Wave =
    [
        0.2, 0.5, 0.8, 0.3, 0.6, 0.9, 0.4, 0.7, 0.5, 0.2, 0.6, 0.8, 0.3, 0.5, 0.7, 0.4, 0.6, 0.9, 0.2, 0.5, 0.7, 0.3, 0.6, 0.4,
    ];

    static Control View(SkinKind skin, SetupModel m) =>
        skin == SkinKind.Mac ? new MacSetup { DataContext = m, DrawChrome = true } : new WinSetup { DataContext = m, DrawChrome = true };

    [AvaloniaFact]
    public void Setup_microphone_check()
    {
        foreach (var skin in new[] { SkinKind.Mac, SkinKind.Win })
            foreach (var t in Themes)
                Shot.Take($"{(skin == SkinKind.Mac ? "mac" : "win")}-setup-microphone-check", skin, t, () =>
                {
                    var m = SetupModel.For(skin, AppRole.Laptop);
                    m.Go(SetupStep.Microphone);
                    m.MicAllowed = true;
                    m.MicLevels = Wave;
                    m.MicHeard = true;
                    return View(skin, m);
                }, size: new Size(850, 608));
    }

    [AvaloniaFact]
    public void Setup_library_found()
    {
        foreach (var skin in new[] { SkinKind.Mac, SkinKind.Win })
            foreach (var t in Themes)
                Shot.Take($"{(skin == SkinKind.Mac ? "mac" : "win")}-setup-library-found", skin, t, () =>
                {
                    var m = SetupModel.For(skin, AppRole.Laptop);
                    m.Go(SetupStep.Library);
                    m.Address = "http://mac-mini.example.ts.net:8787";
                    m.LibraryResult = "Found mac-mini on your Tailscale network. Type its password.";
                    return View(skin, m);
                }, size: new Size(850, 608));
    }

    [AvaloniaFact]
    public void Setup_library_created()
    {
        foreach (var skin in new[] { SkinKind.Mac, SkinKind.Win })
            foreach (var t in Themes)
                Shot.Take($"{(skin == SkinKind.Mac ? "mac" : "win")}-setup-library-created", skin, t, () =>
                {
                    var m = SetupModel.For(skin, AppRole.Library);
                    m.Go(SetupStep.Password);
                    m.LibraryName = "Ada's library";
                    m.Password = "correct-horse";
                    m.LibraryOk = true;
                    m.LibraryResult = $"Ada's library is ready on this {m.DeviceWord}.";
                    return View(skin, m);
                }, size: new Size(850, 608));
    }

    // --- the dropdown, saying what's wrong (task 6) -----------------------------------------------------------------

    static Control PanelView(SkinKind skin, PanelModel m) => skin == SkinKind.Mac ? new MacPanel { DataContext = m } : new WinPanel { DataContext = m };

    /// <summary>A dropdown like <see cref="Demo.Panel"/>'s idle state, but with one of <see cref="Problems"/>'s
    /// problems showing instead of the usual hint.</summary>
    static PanelModel PanelWith(ProblemKind kind)
    {
        var m = new PanelModel { ClassName = "CS 101", ClassDot = Skin.ClassDot(0), Status = "Library connected · Model ready", CanRecord = true };
        m.Recent.Add(new LectureItem { Title = "Recursion and the call stack", Detail = "Filed in CS 101", Time = "9:02", Dot = Skin.ClassDot(0) });
        m.Recent.Add(new LectureItem { Title = "The Treaty of Versailles", Detail = "Filed in HIST 210", Time = "Mon", Dot = Skin.ClassDot(3) });
        switch (kind)
        {
            case ProblemKind.NoModel:
                m.Status = "Library connected · No transcription model";
                m.Hint = "Download the transcription model";
                m.CanRecord = false;
                break;
            case ProblemKind.Downloading:
                m.Status = "Library connected · Model 62%";
                m.Hint = "Downloading the transcription model";
                m.CanRecord = false;
                break;
            case ProblemKind.Unreachable:
                m.Status = "Can't reach your library · Model ready";
                break;
            case ProblemKind.WrongPassword:
                m.Status = "Library password changed · Model ready";
                break;
            case ProblemKind.MicDenied:
                m.Hint = "Study Stash can't use the microphone";
                m.CanRecord = false;
                break;
            case ProblemKind.DiskFull:
                m.Hint = "Your disk is full";
                m.CanRecord = false;
                break;
            case ProblemKind.WhisperFailed:
                m.Hint = "Whisper couldn't start";
                break;
        }
        return m;
    }

    [AvaloniaTheory]
    [InlineData(ProblemKind.NoModel)]
    [InlineData(ProblemKind.Downloading)]
    [InlineData(ProblemKind.Unreachable)]
    [InlineData(ProblemKind.WrongPassword)]
    [InlineData(ProblemKind.MicDenied)]
    [InlineData(ProblemKind.DiskFull)]
    [InlineData(ProblemKind.WhisperFailed)]
    public void Dropdown_says_whats_wrong(ProblemKind kind)
    {
        string slug = kind.ToString().ToLowerInvariant();
        foreach (var skin in new[] { SkinKind.Mac, SkinKind.Win })
            foreach (var t in Themes)
                Shot.Take($"{(skin == SkinKind.Mac ? "mac" : "win")}-dropdown-{slug}", skin, t, () => PanelView(skin, PanelWith(kind)), size: new Size(360, 460));
    }
}
