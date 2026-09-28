using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using StudyStash.Library;

namespace StudyStash.Core.Tests;

/// <summary>A lecture's notes drawn for the phone: prose as HTML, flowcharts as SVG, SVG drawings cleaned, formulas
/// ready for KaTeX, and nothing in them that could run.</summary>
public class PhoneNotesTests
{
    const string Notes = """
        # Recursion

        A function that calls **itself**, like $f(n) = n \cdot f(n-1)$.

        ```mermaid
        flowchart TD
          A[Call f 3] --> B[Call f 2]
          B --> C[Base case]
        ```

        $$
        \sum_{i=1}^{n} i = \frac{n(n+1)}{2}
        $$

        ```python
        def f(n): return 1 if n == 0 else n * f(n - 1)  # costs $5
        ```

        <svg viewBox="0 0 10 10"><title>A dot</title><circle cx="5" cy="5" r="4" fill="#1a73e8"/></svg>
        """;

    [Fact]
    public void Prose_diagrams_formulas_and_code_each_come_out_as_the_phone_needs_them()
    {
        string html = PhoneNotes.Render(Notes);

        Assert.Contains("<h1", html);
        Assert.Contains("<strong>itself</strong>", html);
        Assert.Contains("<span class=\"math\" data-tex=\"f(n) = n \\cdot f(n-1)\">", html);
        Assert.Contains("<div class=\"math display\" data-tex=\"\\sum_{i=1}^{n} i = \\frac{n(n+1)}{2}\">", html);
        Assert.Contains("<figure class=\"diagram\"><svg", html);
        Assert.Contains("Base case", html);
        Assert.Contains("<pre><code class=\"language-python\">def f(n): return 1 if n == 0 else n * f(n - 1)  # costs $5", html);
        Assert.Contains("A dot", html);
        Assert.DoesNotContain("\uE002", html);
        Assert.DoesNotContain("```", html);
        Assert.Equal(2, html.Split("<figure class=\"diagram\">").Length - 1);
    }

    [Fact]
    public void A_formula_reads_in_plain_characters_before_katex_draws_it()
    {
        string html = PhoneNotes.Render(@"Speed is $\alpha \times \beta$.");
        Assert.Contains(">α × β</span>", html);
    }

    [Fact]
    public void A_flowchart_that_cant_be_read_is_shown_as_it_was_written()
    {
        string html = PhoneNotes.Render("```mermaid\nsequenceDiagram\n  A->>B: hi\n```");
        Assert.Contains("<pre><code class=\"language-mermaid\">sequenceDiagram", html);
        Assert.Contains("A-&gt;&gt;B: hi", html);
        Assert.DoesNotContain("<svg", html);
    }

    [Fact]
    public void A_block_inside_a_bullet_stays_in_its_bullet()
    {
        string html = PhoneNotes.Render("- First\n\n  ```js\n  let x = 1\n  ```\n- Second");
        Assert.Contains("<li>", html);
        Assert.Contains("let x = 1", html);
        Assert.Contains("Second", html);
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("<img src=x onerror=alert(1)>")]
    [InlineData("[click](javascript:alert(1))")]
    [InlineData("```svg\n<svg><script>alert(1)</script><circle r=\"3\" onload=\"alert(1)\"/></svg>\n```")]
    [InlineData("<svg onload=\"alert(1)\"><foreignObject><iframe src=\"https://evil.example\"></iframe></foreignObject></svg>")]
    [InlineData("$<script>alert(1)</script>$")]
    [InlineData("```mermaid\nflowchart TD\n  A[\"<script>alert(1)</script>\"] --> B[<img src=x onerror=alert(1)>]\n```")]
    [InlineData("```<script>\nx\n```")]
    public void Nothing_in_the_notes_can_run(string notes)
    {
        string html = PhoneNotes.Render(notes).ToLowerInvariant();
        Assert.DoesNotContain("<script", html);
        Assert.DoesNotContain("javascript:", html);
        Assert.DoesNotContain("<iframe", html);
        Assert.DoesNotContain("<img", html);
        Assert.DoesNotMatch(@"<[^>]*\son\w+\s*=", html);
    }

    [Fact]
    public async Task The_rendered_route_gives_the_lecture_and_its_notes_to_a_paired_phone()
    {
        using var dir = new TempDir();
        var (cfg, store) = PhoneApiTests.Library(dir);
        using var _s = store;
        store.Save(new Meeting("m1") { Title = "Recursion", Date = "2026-09-01", Transcript = "Today.", NotesMarkdown = Notes + "\n\n<script>alert(1)</script>" },
            new Classification("CS 101", 0.9, "folder"));
        await using var site = await PhoneApiTests.Site(cfg, store);
        string device = await PhoneApiTests.PairAsync(site);

        var ask = new HttpRequestMessage(HttpMethod.Get, "/api/v2/lectures/m1/rendered");
        ask.Headers.Add("Cookie", $"device={device}");
        var r = await site.Stranger().SendAsync(ask);

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var got = (JsonObject)JsonNode.Parse(await r.Content.ReadAsStringAsync())!;
        Assert.Equal(("m1", "Recursion", "CS 101", "2026-09-01"),
            (got["id"]!.GetValue<string>(), got["title"]!.GetValue<string>(), got["class"]!.GetValue<string>(), got["date"]!.GetValue<string>()));
        string html = got["html"]!.GetValue<string>();
        Assert.Contains("<figure class=\"diagram\"><svg", html);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(HttpStatusCode.Unauthorized, (await site.Stranger().GetAsync("/api/v2/lectures/m1/rendered")).StatusCode);
        var missing = new HttpRequestMessage(HttpMethod.Get, "/api/v2/lectures/nope/rendered");
        missing.Headers.Authorization = new("Bearer", "pw");
        Assert.Equal(HttpStatusCode.NotFound, (await site.Client.SendAsync(missing)).StatusCode);
    }
}
