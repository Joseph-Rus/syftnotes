using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using StudyStash.Core.Calendar;

namespace StudyStash.Core.Tests;

/// <summary>POST /api/v2/classes/alias: a recurring calendar event's title given to a class, so every week's is that class's.</summary>
public class ClassAliasTests
{
    static HttpRequestMessage Post(object body)
    {
        var r = new HttpRequestMessage(HttpMethod.Post, "/api/v2/classes/alias") { Content = JsonContent.Create(body) };
        r.Headers.Authorization = new("Bearer", "pw");
        return r;
    }

    [Fact]
    public async Task An_events_title_becomes_one_of_the_class_names_and_matches_from_then_on()
    {
        using var dir = new TempDir();
        var (cfg, store) = PhoneApiTests.Library(dir);
        using var _s = store;
        cfg.Classes.Add(new ClassDef("Machine Learning"));
        Configs.Save(cfg);
        await using var site = await PhoneApiTests.Site(cfg, store);

        Assert.Null(EventClass.For("Machine & Deep Learning", EventClass.Of(cfg)));
        var r = await site.Client.SendAsync(Post(new { @class = "Machine Learning", alias = "Machine & Deep Learning" }));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains("Machine & Deep Learning", cfg.Classes.Single(c => c.Name == "Machine Learning").Aliases);
        Assert.Equal("Machine Learning", EventClass.For("Machine & Deep Learning", EventClass.Of(cfg)));
        var overview = JsonNode.Parse(await r.Content.ReadAsStringAsync())!;
        Assert.Contains(overview["classes"]!.AsArray(), c => c!["name"]!.GetValue<string>() == "Machine Learning"
            && c["aliases"]!.AsArray().Any(a => a!.GetValue<string>() == "Machine & Deep Learning"));

        // Given to another class, it moves there; removed, it's gone.
        await site.Client.SendAsync(Post(new { @class = "CS 101", alias = "Machine & Deep Learning" }));
        Assert.DoesNotContain("Machine & Deep Learning", cfg.Classes.Single(c => c.Name == "Machine Learning").Aliases);
        Assert.Contains("Machine & Deep Learning", cfg.Classes.Single(c => c.Name == "CS 101").Aliases);
        await site.Client.SendAsync(Post(new { @class = "CS 101", alias = "Machine & Deep Learning", remove = true }));
        Assert.All(cfg.Classes, c => Assert.DoesNotContain("Machine & Deep Learning", c.Aliases));
    }

    [Fact]
    public async Task An_unknown_class_or_no_name_is_refused()
    {
        using var dir = new TempDir();
        var (cfg, store) = PhoneApiTests.Library(dir);
        using var _s = store;
        await using var site = await PhoneApiTests.Site(cfg, store);
        Assert.Equal(HttpStatusCode.NotFound, (await site.Client.SendAsync(Post(new { @class = "Nope", alias = "x" }))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await site.Client.SendAsync(Post(new { @class = "CS 101" }))).StatusCode);
    }
}
