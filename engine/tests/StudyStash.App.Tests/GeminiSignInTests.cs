using Avalonia.Headless.XUnit;
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
        await row.OpenCommand.ExecuteAsync(null);
        Assert.Equal(["gemini"], asked);
        Assert.Equal("", notes.KeyFor); // not the key box: that's for a Gemini that isn't here at all

        // A host with no guided screens: the library's own sign-in, hidden, in that home; nothing to type.
        var settings = new AiSetupModel(SignedOut()) { Windows = false, TakesKeys = true };
        await settings.Load();
        var signedOut = settings.Engines.Single(r => r.Id == "gemini");
        Assert.True(signedOut.ShowSignIn);
        Assert.DoesNotContain("agy", signedOut.About, StringComparison.Ordinal);
    }

    /// <summary>Finding Gemini (each time guided setup opens) asks agy its version in Syft Notes' own home for it.</summary>
    [Fact]
    public void Finding_agy_asks_its_version_in_study_stashs_own_home()
    {
        using var home = new TempHome();
        using var agyHome = Antigravity.Use(home["agy"]);
        StudyStash.Core.Tests.FakeAgents.WriteCli(home["bin"], "gemini");
        var found = StudyStash.App.Services.AgentInstall.Find(AgentCli.Gemini, StudyStash.Core.Tests.FakeAgents.Which(home["bin"]));
        Assert.Equal(new Version(1, 4, 4), found.Version);
        Assert.Equal(Antigravity.Home(), File.ReadAllText(Path.Combine(home["bin"], "version-home.txt")).Trim());
    }

    /// <summary>Google turning the country away, at the sign-in or the check after it, says so in plain words, with
    /// the way on (another AI, or by hand), instead of "Gemini didn't answer the check".</summary>
    [AvaloniaFact]
    public async Task A_country_google_doesnt_serve_says_so_at_sign_in_and_at_the_check()
    {
        const string words = "Gemini isn't available in your country yet: pick another AI or set up by hand.";
        // At the check (signed in already).
        await using (var rig = new GuidedSetupTests.Rig(cli: "gemini", installed: true, signedIn: true))
        {
            using var agyHome = Antigravity.Use(rig.Home["agy"]);
            File.WriteAllText(Path.Combine(rig.Bin, "region"), "");
            await rig.OpenAsync();
            var g = rig.Guided;
            g.PickCommand.Execute("gemini");
            await g.ContinueCommand.ExecuteAsync(null);
            await GuidedSetupTests.Until(() => g.HasPlanProblem, "the check's answer");
            Assert.Equal((ChatProblem.Region, words), (g.PlanProblem, g.PlanProblemTitle));
            Assert.Equal("Use ChatGPT instead", g.UseOtherLabel);
        }
        // At the sign-in.
        await using (var rig = new GuidedSetupTests.Rig(cli: "gemini", installed: true))
        {
            using var agyHome = Antigravity.Use(rig.Home["agy"]);
            File.WriteAllText(Path.Combine(rig.Bin, "region"), "");
            await rig.OpenAsync();
            var g = rig.Guided;
            g.PickCommand.Execute("gemini");
            await g.ContinueCommand.ExecuteAsync(null);
            await g.OpenSignInCommand.ExecuteAsync(null);
            Assert.Equal(words, g.SignInProblem);
            Assert.False(g.SignedIn);
        }
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
