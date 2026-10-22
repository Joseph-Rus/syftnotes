using StudyStash.App.ViewModels;
using StudyStash.Core.Ai;

namespace StudyStash.App.Tests;

/// <summary>Gemini signed out, on the notes step and in Settings → Your AI: a Sign in button that signs in to Study
/// Stash's own home for agy (the one its notes use), never a bare <c>agy</c> to type, which would sign in to the
/// student's own ~/.gemini and leave the row signed out for good.</summary>
public sealed class GeminiSignInTests
{
    static FakeAiLibrary SignedOut() => new()
    {
        Overview = AiTestData.MixedOverview() with
        {
            Engines = [.. AiTestData.MixedOverview().Engines.Select(e => e.Id == "gemini" ? e with { State = "not_signed_in", Installed = true } : e)],
        },
    };

    [Fact]
    public async Task Signed_out_gemini_has_a_sign_in_button_and_no_bare_agy_to_type()
    {
        // Setup's notes step: its button borrows guided setup's sign-in screen.
        var lib = SignedOut();
        var asked = new List<string>();
        var notes = new AiSetupModel(lib) { Windows = false, TakesKeys = true, SetUpPaid = id => { asked.Add(id); return Task.CompletedTask; } };
        await notes.Load();
        var row = notes.Engines.Single(r => r.Id == "gemini");
        Assert.Equal(("Sign in", true), (row.OpenLabel, row.OneButton));
        Assert.Empty(row.Help);
        await row.OpenCommand.ExecuteAsync(null);
        Assert.Equal(["gemini"], asked);
        Assert.Equal("", notes.KeyFor); // not the key box: that's for a Gemini that isn't here at all

        // Settings → Your AI (no guided screens): the library's own sign-in, which opens a terminal in that home.
        var settings = new AiSetupModel(SignedOut()) { Windows = false, TakesKeys = true };
        await settings.Load();
        var signedOut = settings.Engines.Single(r => r.Id == "gemini");
        Assert.True(signedOut.ShowSignIn);
        Assert.DoesNotContain(signedOut.Help, h => h.Command.Contains("agy", StringComparison.Ordinal));
        Assert.Empty(EngineSetupWords.Steps("gemini", "not_signed_in", windows: false));
    }

    [Fact]
    public void Where_only_words_can_be_given_the_sign_in_line_names_study_stashs_own_home()
    {
        string home = Antigravity.Home();
        Assert.Equal($"HOME=\"{home}\" agy -p=/usage", Antigravity.SignInLine(windows: false));
        Assert.Equal($"$env:USERPROFILE = '{home}'; $env:HOME = '{home}'; agy '-p=/usage'", Antigravity.SignInLine(windows: true));
        Assert.Equal(Antigravity.SignInLine(OperatingSystem.IsWindows()), Engines.SignInWords("gemini"));
        var (exe, args) = Antigravity.InTerminal("/Users/student/.local/bin/agy", windows: false);
        Assert.Equal(("/usr/bin/env", $"HOME={home}"), (exe, args[0]));
    }
}
