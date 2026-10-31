using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace StudyStash.Core.Tests;

/// <summary>What Syft Notes is for: kept in config.toml, said in /api/v2/library, set with POST /api/v2/uses.</summary>
public class UsesTests
{
    [Fact]
    public void Not_asked_reads_as_school_and_what_was_chosen_comes_back()
    {
        using var dir = new TempDir();
        var cfg = new Config(dir["home"], dir["pool"]);
        Directory.CreateDirectory(cfg.Home);
        Assert.True(cfg.ForSchool);
        Configs.Save(cfg);
        Assert.DoesNotContain("uses", File.ReadAllText(cfg.ConfigPath));

        cfg.Uses = ["work", "research"];
        Configs.Save(cfg);
        var back = Configs.Load(cfg.Home);
        Assert.Equal(["work", "research"], back.Uses);
        Assert.False(back.ForSchool);
        back.Uses = ["work", "student"];
        Assert.True(back.ForSchool);
    }

    [Fact]
    public async Task The_phone_or_setup_sets_it_and_the_library_says_so()
    {
        using var dir = new TempDir();
        var (cfg, store) = PhoneApiTests.Library(dir);
        using var _s = store;
        await using var site = await PhoneApiTests.Site(cfg, store);
        var r = new HttpRequestMessage(HttpMethod.Post, "/api/v2/uses") { Content = JsonContent.Create(new { uses = new[] { "Work", "nonsense", "creative" } }) };
        r.Headers.Authorization = new("Bearer", "pw");
        var reply = await site.Client.SendAsync(r);
        Assert.Equal(HttpStatusCode.OK, reply.StatusCode);
        var o = JsonNode.Parse(await reply.Content.ReadAsStringAsync())!;
        Assert.Equal(["work", "creative"], o["uses"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.False(o["for_school"]!.GetValue<bool>());
        Assert.Equal(["work", "creative"], Configs.Load(cfg.Home).Uses);
    }
}
