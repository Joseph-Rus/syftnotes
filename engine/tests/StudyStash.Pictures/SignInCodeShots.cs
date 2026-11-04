using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;
using StudyStash.App.ViewModels.Kit;
using StudyStash.App.Views;

namespace StudyStash.App.Pictures;

/// <summary>
/// Gemini's sign-in step with the box for Google's code (empty, with a code in it, sent), and the same box in its own
/// small window (waiting, and once the code didn't work), Mac and Windows, light and dark. Only when STUDYSTASH_SHOTS
/// names the folder to put them in:
/// <code>STUDYSTASH_SHOTS=/tmp/shots dotnet test engine/tests/StudyStash.Pictures --filter SignInCodeShots</code>
/// </summary>
public sealed class SignInCodeShots
{
    static readonly ThemeVariant[] Themes = [ThemeVariant.Light, ThemeVariant.Dark];
    static readonly AgentFound GeminiHere = new("/Users/student/.local/bin/agy", new Version(1, 4, 12), "1.4.12");

    static SignInCodeModel Box(string code = "", bool sent = false, bool window = false)
    {
        var box = window
            ? new SignInCodeModel("Gemini", "Google", (_, _) => Task.FromResult(Outcome.Done), openAgain: () => { }, tryAgain: () => { })
            : new SignInCodeModel("Gemini", "Google", (_, _) => Task.FromResult(Outcome.Done));
        box.Code = sent ? "4/0AVGzR1Bx-example-code" : code;
        if (sent) box.Send.Command.Execute(null);
        return box;
    }

    static GuidedSetupModel Step(SkinKind skin, SignInCodeModel box)
    {
        var g = Scenes.Guided(skin);
        g.GeminiFound = GeminiHere;
        g.Picked = "gemini";
        g.Screen = GuidedScreen.SignIn;
        g.CodeBox = box;
        g.WaitingSignIn = true;
        return g;
    }

    [AvaloniaFact]
    public void Geminis_sign_in_step_and_the_code_window()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("STUDYSTASH_SHOTS") is { Length: > 0 }, "Only with STUDYSTASH_SHOTS naming a folder.");
        foreach (var skin in new[] { SkinKind.Mac, SkinKind.Win })
        {
            string look = skin == SkinKind.Mac ? "mac" : "win";
            foreach (var t in Themes)
            {
                Shot.Take($"{look}-05-setup-guided-sign-in-gemini-waiting", skin, t, () => Scenes.View(skin, Step(skin, Box())), size: new Size(888, 748));
                Shot.Take($"{look}-05-setup-guided-sign-in-gemini-code", skin, t, () => Scenes.View(skin, Step(skin, Box("4/0AVGzR1Bx-example-code"))), size: new Size(888, 748));
                Shot.Take($"{look}-05-setup-guided-sign-in-gemini-code-sent", skin, t, () => Scenes.View(skin, Step(skin, Box(sent: true))), size: new Size(888, 748));
                Shot.Take($"{look}-15-sign-in-code", skin, t, () => new SignInCodePage { DataContext = Box(window: true), DrawChrome = true }, size: new Size(608, 420));
                Shot.Take($"{look}-15-sign-in-code-didnt-work", skin, t, () =>
                {
                    // As it really goes: a code sent, then turned down by Google.
                    var box = Box(sent: true, window: true);
                    box.Stop(AiWords.CodeDidntWork);
                    return new SignInCodePage { DataContext = box, DrawChrome = true };
                }, size: new Size(608, 420));
            }
        }
    }
}
