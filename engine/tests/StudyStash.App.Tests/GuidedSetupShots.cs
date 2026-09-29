using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;
using StudyStash.Core.Ai;

namespace StudyStash.App.Tests;

/// <summary>Guided setup's screens in both looks, light and dark: "mac-05-setup-guided-pick-light.png" and so on, the
/// size of setup's other pictures, to lay beside ref/*-05-setup-* and ref/*-15-ai-library-setup-*.</summary>
public sealed class GuidedSetupShots
{
    static readonly ThemeVariant[] Themes = [ThemeVariant.Light, ThemeVariant.Dark];
    static readonly string Home = Path.Combine(Path.GetTempPath(), "studystash-app-tests", "guided-shots");

    /// <summary>A guided setup that reaches nothing: its state is set by hand for each picture.</summary>
    internal static GuidedSetupModel Guided(SkinKind skin, AppRole role = AppRole.Both)
    {
        var services = new GuidedServices { Home = Home, Windows = skin == SkinKind.Win, Find = _ => AgentFound.None, OpenUrl = _ => { }, Post = a => a() };
        return new GuidedSetupModel(SetupModel.For(skin, role), services, () => new AppSettings(), _ => { });
    }

    static readonly AgentFound ClaudeHere = new("/Users/student/.local/bin/claude", new Version(2, 1, 260), "2.1.260 (Claude Code)");

    /// <summary>Each state of the first three screens.</summary>
    internal static IEnumerable<(string Name, Func<SkinKind, GuidedSetupModel> Make)> States()
    {
        yield return ("pick", skin => Guided(skin));
        yield return ("pick-installed", skin =>
        {
            var g = Guided(skin);
            g.ClaudeFound = ClaudeHere;
            g.Picked = "claude";
            return g;
        });
        yield return ("install", skin =>
        {
            var g = Guided(skin);
            g.Picked = "claude";
            g.Screen = GuidedScreen.Install;
            return g;
        });
        yield return ("installing", skin =>
        {
            var g = Guided(skin);
            g.Picked = "codex";
            g.Screen = GuidedScreen.Install;
            g.Installing = true;
            g.Phase = "Downloading Codex…";
            return g;
        });
        yield return ("installed", skin =>
        {
            var g = Guided(skin);
            g.Picked = "claude";
            g.Screen = GuidedScreen.Install;
            g.Installed = true;
            g.InstalledWords = "Claude Code 2.1.260 is installed.";
            return g;
        });
        yield return ("install-offline", skin =>
        {
            var g = Guided(skin);
            g.Picked = "claude";
            g.Screen = GuidedScreen.Install;
            g.InstallFailure = InstallFailure.Offline;
            g.InstallProblem = "Study Stash couldn't reach claude.ai. Check your internet connection, then try again.";
            g.ShowDetails = true;
            g.InstallOutput = "curl: (6) Could not resolve host: claude.ai";
            return g;
        });
        yield return ("install-region", skin =>
        {
            var g = Guided(skin);
            g.Picked = "claude";
            g.Screen = GuidedScreen.Install;
            g.InstallFailure = InstallFailure.Region;
            g.InstallProblem = "Claude Code isn't offered in your country yet.";
            return g;
        });
        yield return ("sign-in", skin =>
        {
            var g = Guided(skin);
            g.ClaudeFound = ClaudeHere;
            g.Picked = "claude";
            g.Screen = GuidedScreen.SignIn;
            return g;
        });
        yield return ("sign-in-waiting", skin =>
        {
            var g = Guided(skin);
            g.ClaudeFound = ClaudeHere;
            g.Picked = "claude";
            g.Screen = GuidedScreen.SignIn;
            g.WaitingSignIn = true;
            return g;
        });
        yield return ("sign-in-ready", skin =>
        {
            var g = Guided(skin);
            g.ClaudeFound = ClaudeHere;
            g.Picked = "claude";
            g.Screen = GuidedScreen.SignIn;
            g.SignedIn = true;
            g.AiReady = true;
            return g;
        });
        yield return ("sign-in-plan", skin =>
        {
            var g = Guided(skin);
            g.Picked = "codex";
            g.Screen = GuidedScreen.SignIn;
            g.SignedIn = true;
            g.PlanProblemTitle = "Your ChatGPT plan doesn't include Codex in the app";
            g.PlanProblemText = "It needs Plus or higher.";
            g.PlanProblem = ChatProblem.Plan;
            return g;
        });
    }

    static Control View(SkinKind skin, GuidedSetupModel g) =>
        skin == SkinKind.Mac ? new MacGuidedSetup { DataContext = g, DrawChrome = true } : new WinGuidedSetup { DataContext = g, DrawChrome = true };

    [AvaloniaFact]
    public void Pick_install_and_sign_in()
    {
        foreach (var skin in new[] { SkinKind.Mac, SkinKind.Win })
        {
            string look = skin == SkinKind.Mac ? "mac" : "win";
            foreach (var (name, make) in States())
                foreach (var t in Themes)
                    Shot.Take($"{look}-05-setup-guided-{name}", skin, t, () => View(skin, make(skin)), size: new Size(850, 608));
        }
    }
}
