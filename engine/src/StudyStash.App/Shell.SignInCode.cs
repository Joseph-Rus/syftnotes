using Avalonia.Controls;
using StudyStash.App.Services.Stores;
using StudyStash.App.ViewModels;
using StudyStash.App.ViewModels.Kit;
using StudyStash.App.Views;
using StudyStash.App.Windows;
using StudyStash.Core.Ai;

namespace StudyStash.App;

/// <summary>
/// A sign-in the library runs (started from Settings → Your AI, an AI card, or Connect) whose page ends on a code to
/// paste: Gemini's Google page says "Paste this code into your application". The AI store says when one waits for it
/// (<see cref="AiStatusStore.WantsCodeChanged"/>); the box for it opens in a small window of its own, whichever button
/// started it, and closes once the AI is signed in. Setup's and Get ready's own sign-ins have the box on their screens.
/// </summary>
public static partial class Shell
{
    const string SignInCodeKey = "sign-in-code";
    static (string Id, SignInCodeModel Box)? codeBox;

    static void SignInCodeWanted(string? id)
    {
        var store = host.AiStatus;
        var open = AppWindows.Find(SignInCodeKey);
        if (id is null)
        {
            if (open is null || codeBox is not { } was) return;
            // It stopped without signing in (the code didn't work, or it waited too long): the window says so, with Try
            // again. Signed in, or gone: it closes.
            string phase = store.For(was.Id) is { State: AiEngineState.NotSignedIn } e ? e.SignIn : "";
            if (SignInPhase.TryAgain(phase)) was.Box.Stop(AiWords.SignInAbout(was.Box.EngineName, phase));
            else open.Close();
            return;
        }
        if (open is not null && codeBox is { } same && same.Id == id)
        {
            // Sign in pressed again while it waits: the same window, in front. After Try again: ready for the new code.
            if (same.Box.Stopped) same.Box.Reset();
            AppWindows.Raise(open);
            return;
        }
        open?.Close();
        var cli = AgentCli.Get(id);
        var box = new SignInCodeModel(cli.Brand, cli.Maker,
            async (code, ct) => await store.SendCodeAsync(id, code, ct),
            openAgain: () => Run(() => store.ActAsync(id, AiAction.SignInAgain), "SignInAgain"),
            tryAgain: () => Run(() => store.ActAsync(id, AiAction.SignIn), "SignIn"));
        codeBox = (id, box);
        AppWindows.Open(new WindowSpec(SignInCodeKey, box.WindowTitle, new SignInCodePage { DataContext = box }), w =>
            w.Closed += (_, _) =>
            {
                if (codeBox is { } c && ReferenceEquals(c.Box, box)) codeBox = null;
            });
    }
}
