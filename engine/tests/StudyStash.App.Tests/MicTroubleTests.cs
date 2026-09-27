using System.Text.RegularExpressions;
using StudyStash.Audio;
using StudyStash.Core;

namespace StudyStash.App.Tests;

public class MicTroubleTests
{
    const string MacPrivacy = "x-apple.systempreferences:com.apple.preference.security?Privacy_Microphone";

    [Theory]
    [InlineData(MicAccess.Denied)]
    [InlineData(MicAccess.Restricted)]
    public void Not_allowed_says_where_to_allow_it(MicAccess access)
    {
        var t = MicTrouble.From(MicTrouble.InvalidDevice, access);
        Assert.Equal(MicTroubleKind.Denied, t.Kind);
        Assert.Equal("Study Stash can't use the microphone", t.Title);
        Assert.Equal("Allow it in System Settings → Privacy & Security → Microphone.", t.Detail);
        Assert.Equal("Open System Settings", t.ActionLabel);
        Assert.Equal(MacPrivacy, t.ActionUrl);
    }

    [Fact]
    public void Allowed_but_refused_says_to_switch_study_stash_off_and_on()
    {
        var t = MicTrouble.From(MicTrouble.InvalidDevice, MicAccess.Allowed);
        Assert.Equal(MicTroubleKind.NotHandedOver, t.Kind);
        Assert.Equal("macOS didn't hand over the microphone", t.Title);
        Assert.Equal("In System Settings → Privacy & Security → Microphone, turn Study Stash off and on again, then press Record.", t.Detail);
        Assert.Equal("Open System Settings", t.ActionLabel);
        Assert.Equal(MacPrivacy, t.ActionUrl);
    }

    [Theory]
    [InlineData(-50, MicAccess.Allowed)]
    [InlineData(MicTrouble.InvalidDevice, MicAccess.Unknown)]
    [InlineData(MicTrouble.InvalidDevice, MicAccess.NotAsked)]
    [InlineData(1718449215, MicAccess.Allowed)]
    public void Anything_else_says_to_try_again(int status, MicAccess access)
    {
        var t = MicTrouble.From(status, access);
        Assert.Equal(MicTroubleKind.Other, t.Kind);
        Assert.Equal("The microphone didn't start", t.Title);
        Assert.Equal("Press Record to try again. If it keeps happening, restart your Mac.", t.Detail);
        Assert.False(t.HasAction);
    }

    [Fact]
    public void No_microphone_points_at_the_sound_settings()
    {
        var mac = MicTrouble.NoDevice(windows: false);
        Assert.Equal("No microphone found", mac.Title);
        Assert.Equal("Plug one in, or pick one in System Settings → Sound → Input.", mac.Detail);
        Assert.Equal("Open Sound settings", mac.ActionLabel);
        Assert.Equal("x-apple.systempreferences:com.apple.Sound-Settings.extension", mac.ActionUrl);

        var win = MicTrouble.NoDevice(windows: true);
        Assert.Equal("No microphone found", win.Title);
        Assert.Equal("Open Sound settings", win.ActionLabel);
        Assert.Equal("ms-settings:sound", win.ActionUrl);
    }

    [Fact]
    public void Windows_keeps_its_own_words()
    {
        var t = MicTrouble.Denied(windows: true);
        Assert.Equal("Study Stash can't use the microphone", t.Title);
        Assert.Equal(RecordingWords.CantHearWindows, t.Detail);
        Assert.Equal("Open Settings", t.ActionLabel);
        Assert.Equal("ms-settings:privacy-microphone", t.ActionUrl);
        Assert.Equal("Press Record to try again. If it keeps happening, restart your PC.", MicTrouble.Other(windows: true).Detail);
    }

    public static TheoryData<MicTrouble> Every() =>
    [
        MicTrouble.Denied(false), MicTrouble.Denied(true), MicTrouble.NoDevice(false), MicTrouble.NoDevice(true),
        MicTrouble.NotHandedOver(), MicTrouble.Other(false), MicTrouble.Other(true),
    ];

    [Theory]
    [MemberData(nameof(Every))]
    public void No_words_carry_an_error_code(MicTrouble t)
    {
        Assert.DoesNotMatch(new Regex(@"\d{3,}|error", RegexOptions.IgnoreCase), t.Title + " " + t.Detail + " " + t.ActionLabel);
        Assert.Equal(t.HasAction, t.ActionUrl.Length > 0);
    }

    [Fact]
    public void The_exception_keeps_the_code_for_the_log_and_the_words_for_the_student()
    {
        var e = new MicrophoneException(MicTrouble.NotHandedOver(), MicTrouble.InvalidDevice);
        Assert.IsAssignableFrom<InvalidOperationException>(e);
        Assert.Contains("-66680", e.Message);
        Assert.Equal(MicTrouble.InvalidDevice, e.Status);
        Assert.Equal(MicTroubleKind.NotHandedOver, e.Trouble.Kind);
    }
}
