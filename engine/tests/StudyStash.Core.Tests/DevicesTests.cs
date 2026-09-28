using StudyStash.Library;

namespace StudyStash.Core.Tests;

/// <summary>Phones pairing with the library: a 6-digit code that lasts ten minutes and works once, guesses that run
/// out, and a removed phone locked out at once.</summary>
public class DevicesTests
{
    sealed class Clock
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);
    }

    static (Devices Devices, Clock Clock) Make(TempDir dir)
    {
        var clock = new Clock();
        return (new Devices(dir.Path, () => clock.Now), clock);
    }

    [Fact]
    public void A_code_pairs_a_phone_once_and_its_cookie_is_kept_only_as_a_hash()
    {
        using var dir = new TempDir();
        var (devices, _) = Make(dir);
        var (code, expires) = devices.NewCode();

        Assert.Matches("^[0-9]{6}$", code);
        Assert.Equal(new DateTimeOffset(2026, 9, 28, 12, 10, 0, TimeSpan.Zero), expires);
        var (device, token, why) = devices.Pair(code, "  Sam's\niPhone  ");
        Assert.Null(why);
        Assert.Equal("Sam's iPhone", device!.Name);
        Assert.Equal(device.Id, devices.Check(token)!.Id);
        Assert.DoesNotContain(token!, File.ReadAllText(dir["devices.json"]));
        Assert.Contains("Sam's iPhone", File.ReadAllText(dir["devices.json"]));

        Assert.Equal(Devices.Refusal.WrongCode, devices.Pair(code, "Again").Why);
        Assert.Single(devices.List());
    }

    [Fact]
    public void A_code_typed_with_spaces_still_pairs()
    {
        using var dir = new TempDir();
        var (devices, _) = Make(dir);
        var (code, _) = devices.NewCode();
        Assert.NotNull(devices.Pair($"{code[..3]} {code[3..]}", null).Device);
        Assert.Equal("Phone", devices.List().Single().Name);
    }

    [Fact]
    public void A_code_stops_working_after_ten_minutes()
    {
        using var dir = new TempDir();
        var (devices, clock) = Make(dir);
        var (code, _) = devices.NewCode();
        clock.Now += Devices.CodeLife;
        Assert.Equal(Devices.Refusal.WrongCode, devices.Pair(code, "Late").Why);
        Assert.Empty(devices.List());
    }

    [Fact]
    public void A_new_code_replaces_the_one_on_show()
    {
        using var dir = new TempDir();
        var (devices, _) = Make(dir);
        var (first, _) = devices.NewCode();
        string second;
        do second = devices.NewCode().Code; while (second == first);
        Assert.Equal(Devices.Refusal.WrongCode, devices.Pair(first, "Old").Why);
        Assert.NotNull(devices.Pair(second, "New").Device);
    }

    [Fact]
    public void No_code_on_show_pairs_nothing()
    {
        using var dir = new TempDir();
        var (devices, _) = Make(dir);
        Assert.Equal(Devices.Refusal.WrongCode, devices.Pair("------", "Guess").Why);
        Assert.Equal(Devices.Refusal.WrongCode, devices.Pair("", "Guess").Why);
        Assert.Equal(Devices.Refusal.WrongCode, devices.Pair(null, "Guess").Why);
    }

    [Fact]
    public void Five_wrong_codes_use_a_code_up()
    {
        using var dir = new TempDir();
        var (devices, _) = Make(dir);
        var (code, _) = devices.NewCode();
        string wrong = code == "000000" ? "111111" : "000000";
        for (int i = 0; i < Devices.TriesPerCode; i++) Assert.Equal(Devices.Refusal.WrongCode, devices.Pair(wrong, "Guess").Why);
        Assert.Equal(Devices.Refusal.WrongCode, devices.Pair(code, "Me").Why);
        Assert.Empty(devices.List());
    }

    [Fact]
    public void Too_many_wrong_codes_stop_pairing_for_a_while()
    {
        using var dir = new TempDir();
        var (devices, clock) = Make(dir);
        for (int i = 0; i < Devices.TriesPerWindow; i++) devices.Pair("123", "Guess");
        var (code, _) = devices.NewCode();
        Assert.Equal(Devices.Refusal.TooManyTries, devices.Pair(code, "Me").Why);

        clock.Now += Devices.TryWindow + TimeSpan.FromSeconds(1);
        Assert.NotNull(devices.Pair(devices.NewCode().Code, "Me").Device);
    }

    [Fact]
    public void A_removed_phone_is_locked_out_at_once_even_by_another_copy()
    {
        using var dir = new TempDir();
        var (devices, _) = Make(dir);
        var (code, _) = devices.NewCode();
        var (device, token, _) = devices.Pair(code, "Phone");
        var other = new Devices(dir.Path);
        Assert.NotNull(other.Check(token));

        Assert.True(devices.Remove(device!.Id));
        Assert.Null(devices.Check(token));
        Assert.Null(other.Check(token));
        Assert.False(devices.Remove(device.Id));
    }

    [Fact]
    public void Last_seen_is_written_down_at_most_once_a_minute()
    {
        using var dir = new TempDir();
        var (devices, clock) = Make(dir);
        var (code, _) = devices.NewCode();
        var (device, token, _) = devices.Pair(code, "Phone");
        var added = device!.LastSeen;

        clock.Now += TimeSpan.FromSeconds(30);
        var written = File.GetLastWriteTimeUtc(dir["devices.json"]);
        Assert.Equal(added, devices.Check(token)!.LastSeen);
        Assert.Equal(written, File.GetLastWriteTimeUtc(dir["devices.json"]));

        clock.Now += TimeSpan.FromSeconds(31);
        Assert.Equal(clock.Now, devices.Check(token)!.LastSeen);
        Assert.Equal(clock.Now, new Devices(dir.Path).List().Single().LastSeen);
    }

    [Fact]
    public void Nonsense_cookies_find_no_phone()
    {
        using var dir = new TempDir();
        var (devices, _) = Make(dir);
        Assert.Null(devices.Check(null));
        Assert.Null(devices.Check(""));
        Assert.Null(devices.Check(new string('a', 5000)));
    }

    [Theory]
    [InlineData(null, "Phone")]
    [InlineData("   ", "Phone")]
    [InlineData("Pixel\t8", "Pixel 8")]
    [InlineData("a\u0007b", "ab")]
    public void Names_are_kept_tidy(string? given, string kept) => Assert.Equal(kept, Devices.CleanName(given));

    [Fact]
    public void A_long_name_is_cut()
    {
        Assert.Equal(60, Devices.CleanName(new string('x', 200)).Length);
    }
}
