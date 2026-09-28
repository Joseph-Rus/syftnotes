using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using StudyStash.App.ViewModels;
using StudyStash.Core;

namespace StudyStash.App.Tests;

/// <summary>Settings → Phone: a code and its QR code from the library, the time it has left, the new phone showing up
/// once it pairs, and Remove.</summary>
public class PhonesModelTests
{
    /// <summary>The library's /api/v2/devices, in memory.</summary>
    sealed class FakeDevices
    {
        public List<JsonObject> Devices { get; } = [];
        public List<(string Method, string Path)> Calls { get; } = [];
        public string? Refuse { get; set; }
        public bool Down { get; set; }
        public bool Older { get; set; }

        JsonObject List() => new() { ["devices"] = new JsonArray([.. Devices.Select(d => (JsonNode)d.DeepClone())]) };

        public LibrarySettingsModel.Call Call => (method, path, body) =>
        {
            Calls.Add((method.Method, path));
            if (Down) throw new HttpRequestException("no route to host");
            if (Older) return Task.FromResult<JsonObject?>(null);
            if (Refuse is { } why) throw new LibraryRefusedException(409, why);
            JsonObject? answer = (method.Method, path) switch
            {
                ("GET", "") => List(),
                ("POST", "/code") => new JsonObject { ["code"] = "042917", ["url"] = "https://mini.tail1234.ts.net:8443/app/", ["expires"] = "2026-09-28T12:10:00+00:00" },
                ("DELETE", _) when Devices.RemoveAll(d => "/" + d["id"]!.GetValue<string>() == path) > 0 => List(),
                _ => null,
            };
            return Task.FromResult(answer);
        };
    }

    sealed class Clock
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);
    }

    static JsonObject Phone(string id, string name) =>
        new() { ["id"] = id, ["name"] = name, ["added"] = "2026-09-28T11:00:00+00:00", ["lastSeen"] = "2026-09-28T11:30:00+00:00" };

    [AvaloniaFact]
    public async Task The_page_lists_the_phones_already_added()
    {
        var fake = new FakeDevices();
        fake.Devices.Add(Phone("a", "Sam's iPhone"));
        var clock = new Clock();
        var model = new PhonesModel(() => fake.Call, () => clock.Now);

        await model.Load();

        Assert.True(model.IsReady);
        var row = Assert.Single(model.Phones);
        Assert.Equal("Sam's iPhone", row.Name);
        Assert.Equal("Added today · used today", row.Meta);
        Assert.Equal("Add another phone", model.AddTitle);
        Assert.False(model.NoPhones);
    }

    [AvaloniaFact]
    public async Task Add_a_phone_shows_the_code_its_qr_code_and_the_time_it_has_left()
    {
        var fake = new FakeDevices();
        var clock = new Clock();
        var ticking = new List<bool>();
        var model = new PhonesModel(() => fake.Call, () => clock.Now) { Ticking = ticking.Add };
        await model.Load();
        Assert.Equal("Read your notes and lectures on your phone", model.AddTitle);

        await model.AddPhoneCommand.ExecuteAsync(null);

        Assert.True(model.Adding);
        Assert.False(model.ShowAdd);
        Assert.Equal("042 917", model.Code);
        Assert.Equal("https://mini.tail1234.ts.net:8443/app/", model.Url);
        Assert.NotNull(model.Qr);
        Assert.Equal(Qr.Modules(model.Url).GetLength(0) + 2 * Qr.Margin, model.QrSize);
        Assert.Equal("Works for 10:00 more, once.", model.Left);
        Assert.Equal([true], ticking);

        clock.Now += TimeSpan.FromSeconds(19);
        model.Tick();
        Assert.Equal("Works for 9:41 more, once.", model.Left);

        clock.Now += TimeSpan.FromMinutes(10);
        model.Tick();
        Assert.True(model.RanOut);
        Assert.Equal("This code has run out. Make a new one.", model.Left);
        Assert.Equal([true, false], ticking);

        model.Done();
        Assert.False(model.Adding);
        Assert.Null(model.Qr);
    }

    [AvaloniaFact]
    public async Task Once_the_phone_pairs_the_code_goes_and_the_phone_is_named()
    {
        var fake = new FakeDevices();
        var clock = new Clock();
        var model = new PhonesModel(() => fake.Call, () => clock.Now);
        await model.Load();
        await model.AddPhoneCommand.ExecuteAsync(null); // tick 1

        fake.Devices.Add(Phone("new", "Pixel 10"));
        model.Tick();
        model.Tick(); // the third tick asks the library
        await Task.Yield();

        Assert.False(model.Adding);
        Assert.Equal("Pixel 10", Assert.Single(model.Phones).Name);
        Assert.Equal("Pixel 10 is added. It stays signed in until you remove it here.", model.Say);
    }

    [AvaloniaFact]
    public async Task Without_tailscale_the_librarys_words_say_what_to_do()
    {
        var fake = new FakeDevices();
        var model = new PhonesModel(() => fake.Call);
        await model.Load();
        fake.Refuse = "Your phone reaches the library over Tailscale, so it needs Tailscale here first. Tailscale isn't running on the library's computer. Open it and sign in.";

        await model.AddPhoneCommand.ExecuteAsync(null);

        Assert.False(model.Adding);
        Assert.Equal(fake.Refuse, model.Say);
    }

    [AvaloniaFact]
    public async Task Remove_takes_the_phone_off_the_list()
    {
        var fake = new FakeDevices();
        fake.Devices.Add(Phone("a", "Old phone"));
        var model = new PhonesModel(() => fake.Call);
        await model.Load();

        await model.RemoveCommand.ExecuteAsync(model.Phones[0]);

        Assert.Empty(model.Phones);
        Assert.True(model.NoPhones);
        Assert.Contains(("DELETE", "/a"), fake.Calls);
        Assert.Equal("Old phone is removed. It can't read your library any more.", model.Say);
    }

    [AvaloniaFact]
    public async Task No_library_an_unreachable_one_and_an_older_one_each_say_so()
    {
        var none = new PhonesModel(() => null);
        await none.Load();
        Assert.Equal(LibrarySettingsState.NoLibrary, none.State);

        var down = new FakeDevices { Down = true };
        var unreachable = new PhonesModel(() => down.Call);
        await unreachable.Load();
        Assert.True(unreachable.CanRetry);
        Assert.StartsWith("Can't reach your library", unreachable.Problem);

        var old = new FakeDevices { Older = true };
        var older = new PhonesModel(() => old.Call);
        await older.Load();
        Assert.Equal(LibrarySettingsState.Older, older.State);
        Assert.Contains("older Study Stash", older.Problem);
    }
}
