using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;
using StudyStash.Core.Ai;
using StudyStash.App.Tests;

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

    /// <summary>A chat under way: the AI's greeting, the student's answer, what Study Stash noted, and a card.</summary>
    internal static GuidedSetupModel Chat(SkinKind skin, string card, AppRole role = AppRole.Both, bool chosen = true, double? downloading = null)
    {
        var services = new GuidedServices
        {
            Home = Home, Windows = skin == SkinKind.Win, Find = _ => AgentFound.None, OpenUrl = _ => { }, Post = a => a(), Downloading = () => downloading,
            // The AI app card's computer has the ChatGPT app, not yet connected.
            AiApps = () => card == "ai_app" ? [new AiAppState("codex", "ChatGPT", true, false, false, null, null, "")] : [],
        };
        var g = new GuidedSetupModel(SetupModel.For(skin, role), services, () => new AppSettings(), _ => { });
        g.LoadAiApps();
        g.ClaudeFound = ClaudeHere;
        g.Picked = "claude";
        g.AiReady = true;
        g.Screen = GuidedScreen.Chat;
        string device = skin == SkinKind.Mac ? "Mac" : "PC";
        var hi = new AiEntry("Claude") { Text = $"Hi! This takes about 5 minutes. Will you use Study Stash on just this {device}, or record on a laptop and keep your library on another computer?" };
        hi.Chip("Checked your setup");
        g.Thread.Add(hi);
        if (chosen && role == AppRole.Both)
        {
            g.Thread.Add(new StudentEntry($"Just this {device}"));
            g.Thread.Add(new NoteEntry($"Your library is ready on this {device}", true));
            g.RoleChosen = true;
            g.Setup.LibraryOk = true;
            g.NotesWriter = "claude";
        }
        else if (chosen)
        {
            g.Thread.Add(new StudentEntry(role == AppRole.Library ? "This is my library" : "This is my laptop"));
            g.Thread.Add(new NoteEntry(role == AppRole.Library ? "This is your library" : "This is your laptop", true));
            g.RoleChosen = true;
            if (role == AppRole.Laptop) g.Setup.Address = "mac-mini:8787";
        }
        var next = new AiEntry("Claude")
        {
            Text = card switch
            {
                "microphone_check" => "Next, let's make sure Study Stash can hear your lectures.",
                "model_download" => "Next, the model that turns speech into text.",
                "chrome_helper" => "Great, your school uses Canvas. Let's connect it through Chrome.",
                "course_picker" => "Chrome is connected! Here are the courses Canvas found.",
                "ai_app" => $"You have the ChatGPT app on this {device}. Want it to be able to answer questions from your own lectures?",
                "start_at_login" => "Last thing: Study Stash can start when you log in, so notes get written.",
                "taskbar_tip" => "One more tip for Windows: keep Study Stash on the taskbar.",
                "finish" => "You're all set. Your library is ready, and Claude writes your notes.",
                "library_password" => "Give your library a name and a password. Your laptop uses the password to connect.",
                "library_connection" => "Let's connect this laptop to your library. Press Find it, or type its address.",
                _ => "",
            },
        };
        if (card.Length > 0 && card != "computer_setup") g.Thread.Add(next);
        var s = g.Setup;
        switch (card)
        {
            case "microphone_check":
                s.MicAllowed = true;
                s.MicLevels = [.. Enumerable.Range(0, 24).Select(i => 0.2 + 0.6 * Math.Abs(Math.Sin(i * 0.7)))];
                break;
            case "model_download":
                s.Models.Add(new ModelChoice { Model = StudyStash.Audio.WhisperModels.Find("large-v3-turbo-q5")!, Recommended = true, Why = "Fast on this computer" });
                s.Models.Add(new ModelChoice { Model = StudyStash.Audio.WhisperModels.Find("large-v3")! });
                s.MicAllowed = true;
                s.MicHeard = true;
                break;
            case "chrome_helper":
                var ctx = CanvasFixtures.Context(new FakeLibrary());
                s.Canvas = new CanvasConnectModel(ctx, new CanvasWatch(ctx), forSetup: true) { AddedToChrome = true, ExtensionFolder = "/Users/student/Study Stash/Chrome extension" };
                break;
            case "course_picker":
                foreach (var (id, name, code) in new[] { ("1", "Intro to Biology", "BIO 110"), ("2", "Intro to Computer Science", "CS 101"), ("3", "Modern World History", "HIST 120") })
                    s.Courses.Add(new SetupCourse { Id = id, Name = name, Code = code });
                break;
            case "finish":
                s.MicAllowed = true;
                s.MicHeard = true;
                s.ModelReady = true;
                s.ModelName = "Whisper large-v3 turbo (compact)";
                s.Classes.Add(new SetupClass { Name = "BIO 110" });
                s.Classes.Add(new SetupClass { Name = "CS 101" });
                g.StartsAtLogin = true;
                g.TaskbarDone = true;
                break;
        }
        if (card.Length > 0)
        {
            string arg = card == "model_download" ? "large-v3-turbo-q5" : card == "chrome_helper" ? "school.instructure.com" : card == "ai_app" ? "ChatGPT" : "";
            g.ShowCard(new Core.Setup.SetupCard(card, arg));
        }
        g.Refresh();
        return g;
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

    /// <summary>The chat with each card open, a problem, the AI thinking, and ready to finish.</summary>
    [AvaloniaFact]
    public void The_chat_its_checklist_and_cards()
    {
        foreach (var skin in new[] { SkinKind.Mac, SkinKind.Win })
        {
            string look = skin == SkinKind.Mac ? "mac" : "win";
            var states = new List<(string Name, Func<GuidedSetupModel> Make)>
            {
                ("computer", () => Chat(skin, "computer_setup", chosen: false)),
                ("microphone", () => Chat(skin, "microphone_check")),
                ("model", () => Chat(skin, "model_download")),
                ("chrome", () => Chat(skin, "chrome_helper", downloading: 0.42)),
                ("courses", () => Chat(skin, "course_picker", downloading: 0.42)),
                ("ai-app", () => Chat(skin, "ai_app", downloading: 0.42)),
                ("start-at-login", () => Chat(skin, "start_at_login", downloading: 0.42)),
                ("finish", () => Chat(skin, "finish")),
                ("password", () => Chat(skin, "library_password", AppRole.Library)),
                ("connect", () => Chat(skin, "library_connection", AppRole.Laptop)),
                ("problem", () =>
                {
                    var g = Chat(skin, "");
                    g.ChatProblemKind = ChatProblem.Limit;
                    g.ChatProblemText = "Claude is at its usage limit until 3 pm. You can finish by hand; what's done stays done.";
                    return g;
                }),
                ("thinking", () =>
                {
                    var g = Chat(skin, "");
                    g.Thread.Add(new StudentEntry("Does it work with Canvas?") { Queued = true });
                    g.Busy = true;
                    g.QuickReplies.Add("Yes");
                    return g;
                }),
                ("question", () =>
                {
                    var g = Chat(skin, "", chosen: false);
                    g.ShowQuestion("Does your school use Canvas?", ["Yes", "No", "Not sure"]);
                    return g;
                }),
            };
            if (skin == SkinKind.Win) states.Add(("taskbar", () => Chat(skin, "taskbar_tip")));
            foreach (var (name, make) in states)
                foreach (var t in Themes)
                    Shot.Take($"{look}-05-setup-guided-chat-{name}", skin, t, () => View(skin, make()), size: new Size(1030, 688));
        }
    }
}
