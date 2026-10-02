using System.Text;
using System.Xml.Linq;
using StudyStash.Core.Rich;

namespace StudyStash.Core.Tests;

/// <summary>Drawings an AI wrote, made safe to show: nothing that runs, loads or expands survives; the colours follow
/// the look, light and dark; the words are kept for search and screen readers.</summary>
public class SafeSvgTests
{
    const string Open = """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 100">""";

    /// <summary>The four chambers of the heart, in the palette the writers are given (the notes' own example).</summary>
    public const string Heart = """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 640 300">
          <title>The four chambers of the heart</title>
          <defs><marker id="arrow" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse"><path d="M0 0L10 5L0 10z" fill="#1D1D1F"/></marker></defs>
          <rect x="120" y="40" width="190" height="100" rx="14" fill="#E8F0FE" stroke="#1A73E8" stroke-width="2"/>
          <rect x="330" y="40" width="190" height="100" rx="14" fill="#FCE8E6" stroke="#D93025" stroke-width="2"/>
          <rect x="120" y="160" width="190" height="110" rx="14" fill="#E8F0FE" stroke="#1A73E8" stroke-width="2"/>
          <rect x="330" y="160" width="190" height="110" rx="14" fill="#FCE8E6" stroke="#D93025" stroke-width="2"/>
          <text x="215" y="95" text-anchor="middle" font-size="15" fill="#1D1D1F">Right atrium</text>
          <text x="425" y="95" text-anchor="middle" font-size="15" fill="#1D1D1F">Left atrium</text>
          <text x="215" y="220" text-anchor="middle" font-size="15" fill="#1D1D1F">Right ventricle</text>
          <text x="425" y="220" text-anchor="middle" font-size="15" fill="#1D1D1F">Left ventricle</text>
          <line x1="215" y1="140" x2="215" y2="158" stroke="#1D1D1F" stroke-width="1.5" marker-end="url(#arrow)"/>
          <line x1="425" y1="140" x2="425" y2="158" stroke="#1D1D1F" stroke-width="1.5" marker-end="url(#arrow)"/>
          <text x="320" y="292" text-anchor="middle" font-size="13" fill="#6E6E73">Septum between the two sides</text>
        </svg>
        """;

    static SafeSvgResult Clean(string svg, SafeSvgOptions? options = null) => SafeSvg.Clean(svg, options);

    /// <summary>What must never come out, whatever went in (the SVG namespace itself is the one web address allowed).</summary>
    static void Harmless(SafeSvgResult r)
    {
        Assert.True(r.Svg is not null || r.Problem is not null, "either a drawing or a reason");
        if (r.Svg is not { } svg) return;
        string text = svg.Replace("http://www.w3.org/2000/svg", "");
        foreach (var bad in new[] { "script", "http", "foreignObject", "<image", "DOCTYPE", "@import", "javascript", "data:", "<style", "feImage", "<a ", "<a>", "iframe", "animate", "<set" })
            Assert.DoesNotContain(bad, text, StringComparison.OrdinalIgnoreCase);
        var doc = XDocument.Parse(svg);
        foreach (var a in doc.Descendants().Attributes())
            Assert.False(a.Name.LocalName.StartsWith("on", StringComparison.OrdinalIgnoreCase), $"{a.Name} survived");
    }

    public static TheoryData<string, string> Malicious => new()
    {
        { "script", Open + """<script>alert(1)</script><rect width="10" height="10"/></svg>""" },
        { "script in a namespace", Open.Replace("<svg ", """<svg xmlns:h="http://www.w3.org/1999/xhtml" """) + """<h:script>alert(1)</h:script><rect width="10" height="10"/></svg>""" },
        { "onload", """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10" onload="alert(1)"><rect width="10" height="10" onclick="alert(2)" onmouseover="x()"/></svg>""" },
        { "javascript link", Open + """<a href="javascript:alert(1)"><text x="5" y="20">Click</text></a><rect width="1" height="1"/></svg>""" },
        { "javascript use", Open + """<use href="javascript:alert(1)"/><use xlink:href="java&#x09;script:alert(1)" xmlns:xlink="http://www.w3.org/1999/xlink"/></svg>""" },
        { "external image", Open + """<image href="http://example.com/tracker.png" width="10" height="10"/><rect width="1" height="1"/></svg>""" },
        { "external use", Open + """<use href="http://example.com/sprite.svg#icon"/><rect width="1" height="1"/></svg>""" },
        { "foreignObject", Open + """<foreignObject width="100" height="50"><div xmlns="http://www.w3.org/1999/xhtml"><iframe src="http://example.com"></iframe>Hi</div></foreignObject><rect width="1" height="1"/></svg>""" },
        { "style import", Open + """<style>@import url(http://example.com/x.css); rect { fill: red }</style><rect width="10" height="10"/></svg>""" },
        { "feImage", Open + """<filter id="f"><feImage href="http://example.com/x.png"/></filter><rect width="10" height="10" filter="url(#f)"/></svg>""" },
        { "animate", Open + """<rect width="10" height="10"><animate attributeName="fill" to="red" dur="1s"/><set attributeName="onclick" to="alert(1)"/></rect></svg>""" },
        { "style url", Open + """<rect width="10" height="10" style="fill:url(http://example.com/x.svg#g);stroke:#1D1D1F"/></svg>""" },
        { "style escape", Open + """<rect width="10" height="10" style="fill:url(ht\74p://example.com/#g)"/></svg>""" },
        { "fill url", Open + """<rect width="10" height="10" fill="url('http://example.com/x.svg#g')" stroke="url(data:image/svg+xml;base64,AAAA)"/></svg>""" },
        { "expression", Open + """<rect width="10" height="10" style="fill: expression(alert(1))"/></svg>""" },
        { "doctype and entity", """<?xml version="1.0"?><!DOCTYPE svg [<!ENTITY xxe SYSTEM "file:///etc/passwd">]><svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10"><text>&xxe;</text></svg>""" },
        { "billion laughs", """<!DOCTYPE lolz [<!ENTITY lol "lol"><!ENTITY lol2 "&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;"><!ENTITY lol3 "&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;">]><svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10"><text>&lol3;</text></svg>""" },
        { "a link", Open + """<a href="http://example.com"><rect width="10" height="10"/></a><rect width="1" height="1"/></svg>""" },
        { "font face", Open + """<font><font-face font-family="x"><font-face-src><font-face-uri href="http://example.com/x.svg"/></font-face-src></font-face></font><rect width="1" height="1"/></svg>""" },
        { "textPath", Open + """<path id="p" d="M0 0L10 10"/><text><textPath href="http://example.com/x#p">Hi</textPath></text></svg>""" },
        { "pattern and mask", Open + """<pattern id="p"><image href="http://example.com/x.png"/></pattern><mask id="m"><rect width="1" height="1"/></mask><rect width="10" height="10" fill="url(#p)" mask="url(#m)"/></svg>""" },
        { "iframe and media", Open + """<iframe src="http://example.com"/><video src="http://example.com/v.mp4"/><audio src="http://example.com/a.mp3"/><rect width="1" height="1"/></svg>""" },
        { "cdata script", Open + """<script><![CDATA[ fetch("http://example.com") ]]></script><rect width="1" height="1"/></svg>""" },
        { "switch", Open + """<switch><foreignObject><p xmlns="http://www.w3.org/1999/xhtml">x</p></foreignObject><text>x</text></switch><rect width="1" height="1"/></svg>""" },
        { "huge width", """<svg xmlns="http://www.w3.org/2000/svg" width="1e9" height="100"><rect width="10" height="10"/></svg>""" },
        { "infinite viewBox", """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1e400 10"><rect width="10" height="10"/></svg>""" },
        { "not svg", """<html><body><script>alert(1)</script></body></html>""" },
        { "garbage", """<svg viewBox="0 0 10 10"><rect""" },
        { "empty", "" },
        { "processing instruction", """<?xml-stylesheet href="http://example.com/x.css"?>""" + Open + """<rect width="1" height="1"/></svg>""" },
        { "a part's name and line", Open + """<g id="m"><title>Motor<script>alert(1)</script></title><desc onload="alert(2)">Spins <a href="http://example.com">here</a></desc><rect width="5" height="5" onclick="x()"/></g></svg>""" },
        { "base href", """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10" xml:base="http://example.com/"><rect width="1" height="1"/></svg>""" },
    };

    [Theory]
    [MemberData(nameof(Malicious))]
    public void Nothing_that_runs_loads_or_expands_survives(string name, string svg)
    {
        var r = Clean(svg, new SafeSvgOptions { Palette = SvgPalette.Written, Dark = true });
        Harmless(r);
        Assert.False(r.Svg is null && string.IsNullOrWhiteSpace(r.Problem), name);
    }

    [Fact]
    public void Entities_and_doctypes_are_refused_outright()
    {
        foreach (var svg in new[] { Malicious_("doctype and entity"), Malicious_("billion laughs") })
        {
            var r = Clean(svg);
            Assert.Null(r.Svg);
            Assert.Equal("This drawing declares its own document type, which isn't allowed.", r.Problem);
        }
    }

    static string Malicious_(string name) => (string)Malicious.Single(row => (string)row[0] == name)[1];

    /// <summary>Each level copies the one below it ten times: a small file that would draw 10^levels rectangles.</summary>
    static string UseBomb(int levels)
    {
        var bomb = new StringBuilder(Open + "<defs><rect id=\"a0\" width=\"1\" height=\"1\"/>");
        for (int i = 1; i <= levels; i++) bomb.Append($"<g id=\"a{i}\">" + string.Concat(Enumerable.Repeat($"<use href=\"#a{i - 1}\"/>", 10)) + "</g>");
        return bomb.Append($"</defs><use href=\"#a{levels}\"/></svg>").ToString();
    }

    [Fact]
    public void A_use_bomb_is_defused_and_too_many_uses_refused()
    {
        Assert.Equal("This drawing repeats its parts too many times to show.", Clean(UseBomb(101)).Problem);
        // A board's pins: hundreds of copies of one small pin are fine; hundreds of copies of a big part are not.
        string Copies(int parts, int copies) => Open + "<defs><g id=\"p\">" + string.Concat(Enumerable.Repeat("<rect width=\"1\" height=\"1\"/>", parts))
            + "</g></defs>" + string.Concat(Enumerable.Range(0, copies).Select(i => $"<use href=\"#p\" x=\"{i}\"/>")) + "</svg>";
        Assert.Null(Clean(Copies(3, 400)).Problem);
        Assert.Equal("This drawing repeats its parts too many times to show.", Clean(Copies(200, 300)).Problem);

        // Under the count, only the copies of a plain part are left: a copy of anything with a copy inside goes.
        var defused = Clean(UseBomb(5));
        var left = XDocument.Parse(defused.Svg!).Descendants().Where(e => e.Name.LocalName == "use").Select(u => (string?)u.Attribute("href")).Distinct().ToList();
        Assert.Equal(["#a0"], left);

        var small = Clean(Open + """<defs><rect id="a" width="1" height="1"/><g id="b"><use href="#a"/></g><g id="c"><use href="#c"/></g></defs><use href="#a"/><use href="#b"/><use id="d" href="#d"/><use href="#nowhere"/></svg>""");
        var uses = XDocument.Parse(small.Svg!).Descendants().Where(e => e.Name.LocalName == "use").Select(u => (string?)u.Attribute("href")).ToList();
        Assert.Equal(["#a", "#a"], uses);
    }

    [Fact]
    public void Too_many_parts_too_deep_or_too_big_is_a_plain_reason()
    {
        string many = Open + string.Concat(Enumerable.Repeat("<g/>", 10_000)) + "</svg>";
        Assert.Equal("This drawing has too many parts to show (over 4,000).", Clean(many).Problem);
        string deep = Open + string.Concat(Enumerable.Repeat("<g>", 100)) + "<rect width=\"1\" height=\"1\"/>" + string.Concat(Enumerable.Repeat("</g>", 100)) + "</svg>";
        Assert.Equal("This drawing is nested too deeply to show.", Clean(deep).Problem);
        string huge = Open + "<text>" + new string('x', 5 * 1024 * 1024) + "</text></svg>";
        Assert.Equal("This drawing is too big to show (over 192 KB).", Clean(huge).Problem);
        Assert.Equal("This drawing is too big to show (over 5,000 units).", Clean("""<svg xmlns="http://www.w3.org/2000/svg" width="1e9" height="100"/>""").Problem);
        Assert.Equal("This drawing has no size: it needs a viewBox, or a width and height.", Clean("""<svg xmlns="http://www.w3.org/2000/svg"><rect width="1" height="1"/></svg>""").Problem);
        Assert.Equal("This isn't an SVG drawing.", Clean("<html/>").Problem);
        Assert.StartsWith("This drawing isn't well-formed SVG", Clean("<svg viewBox=\"0 0 1 1\"><rect></svg>").Problem);
    }

    /// <summary>A detailed illustration is a few hundred curves: one of about 150 KB is shown, and whatever hides
    /// among its parts (a script, a handler, an embedded page, a link out, a style sheet) still goes.</summary>
    [Fact]
    public void A_detailed_illustration_is_shown_and_nothing_that_runs_survives_among_its_parts()
    {
        var sb = new StringBuilder(Open.Replace("0 0 200 100", "0 0 720 600") + "<title>A hand</title><g id='art'>");
        for (int i = 0; sb.Length < 150 * 1024; i++)
            sb.Append($"<g id='bone-{i}'><title>Bone {i}</title><desc>A bone</desc><path d='M{i % 700} 10 C {i % 700 + 5} 40 {i % 700 + 9} 80 {i % 700 + 3} 120 S {i % 700 + 7} 160 {i % 700} 200' fill='#F2E4C4' stroke='#A88C5C' stroke-width='1.5'/></g>");
        sb.Append("""<g id="evil"><title>Evil</title><script>alert(1)</script><rect width="5" height="5" onmouseover="steal()" style="fill:url(http://example.com/x)"/><foreignObject><iframe src="http://example.com"/></foreignObject><use href="http://example.com/x.svg#a"/><style>@import url(http://example.com/x.css);</style><image href="data:image/png;base64,AAAA"/></g>""");
        string svg = sb.Append("</g></svg>").ToString();
        Assert.InRange(Encoding.UTF8.GetByteCount(svg), 150 * 1024, SafeSvg.MaxBytes);
        var r = Clean(svg, new SafeSvgOptions { Palette = SvgPalette.Written, Dark = true });
        Assert.Null(r.Problem);
        Harmless(r);
        Assert.True(r.Parts.Count > 300);
        Assert.Equal(("evil", "Evil"), (r.Parts[^1].Id, r.Parts[^1].Name));
    }

    /// <summary>On a dark page an illustration keeps its own colours (a hand still skin-coloured, a frame still
    /// dark), lifted clear of the dark paper and held back from glaring; a background left in becomes the paper. A
    /// plain drawing keeps the old rule (see the test above).</summary>
    [Fact]
    public void An_illustration_keeps_its_colours_in_dark_moved_into_what_a_dark_page_carries()
    {
        string art = Open + """<g id="hand"><title>Hand</title><rect width="5" height="5" fill="#F2C6A6" stroke="#A86F52"/></g><rect width="5" height="5" fill="#000000" stroke="#24272B"/><rect width="200" height="100" fill="#FAFAFA" stroke="#FFFFFF"/><linearGradient id="g"><stop offset="0" stop-color="#4A9AD1"/></linearGradient><text fill="#1D1D1F">Hand</text><ellipse rx="3" ry="2" fill="#FCF6E8" fill-opacity="0.6"/></svg>""";
        var r = Clean(art, new SafeSvgOptions { Palette = Dark, Dark = true });
        var doc = XDocument.Parse(r.Svg!);
        var rects = doc.Descendants().Where(e => e.Name.LocalName == "rect").ToList();
        string Fill(int i) => (string)rects[i].Attribute("fill")!;
        string Stroke(int i) => (string)rects[i].Attribute("stroke")!;
        double L(string hex) => SvgColour.Lab(hex)!.Value.L;
        double Hue(string hex) => Math.Atan2(SvgColour.Lab(hex)!.Value.B, SvgColour.Lab(hex)!.Value.A);
        // Skin stays skin: about the same hue and lightness; its outline still darker than it.
        Assert.InRange(Math.Abs(Hue(Fill(0)) - Hue("#f2c6a6")), 0, 0.15);
        Assert.InRange(L(Fill(0)), 0.75, 0.9);
        Assert.True(L(Stroke(0)) < L(Fill(0)));
        // Black and near-black are lifted clear of the dark paper; a near-white background over the whole canvas is the
        // paper, but a highlight (a light tint, or white) stays light.
        Assert.InRange(L(Fill(1)), 0.43, 0.5);
        Assert.True(L(Stroke(1)) > L(SvgPalette.DarkPaper.ToLowerInvariant()) + 0.15);
        Assert.Equal(SvgPalette.DarkPaper, Fill(2));
        Assert.True(L(Stroke(2)) > 0.85);
        Assert.True(L((string)doc.Descendants().Single(e => e.Name.LocalName == "ellipse").Attribute("fill")!) > 0.85);
        // Gradients follow, and the words keep the look's ink.
        Assert.NotEqual("#4A9AD1", (string?)doc.Descendants().Single(e => e.Name.LocalName == "stop").Attribute("stop-color"));
        Assert.Equal("#F5F5F7", (string?)doc.Descendants().Single(e => e.Name.LocalName == "text").Attribute("fill"));
        // Light keeps every colour as written.
        Assert.Contains("fill=\"#F2C6A6\"", Clean(art, new SafeSvgOptions { Palette = SvgPalette.Written }).Svg);
    }

    [Fact]
    public void The_size_comes_from_the_view_box_or_else_the_width_and_height()
    {
        var sized = Clean("""<svg xmlns="http://www.w3.org/2000/svg" width="320px" height="180"><rect width="1" height="1"/></svg>""");
        Assert.Equal((320, 180), (sized.Width, sized.Height));
        var root = XDocument.Parse(sized.Svg!).Root!;
        Assert.Equal("0 0 320 180", (string?)root.Attribute("viewBox"));
        Assert.Null(root.Attribute("width"));
        Assert.Null(root.Attribute("height"));
        var boxed = Clean("""<svg xmlns="http://www.w3.org/2000/svg" viewBox="10,20 640 300" width="1280" height="600"/>""");
        Assert.Equal((640, 300), (boxed.Width, boxed.Height));
        Assert.Equal("10 20 640 300", (string?)XDocument.Parse(boxed.Svg!).Root!.Attribute("viewBox"));
    }

    [Fact]
    public void A_drawing_without_a_namespace_or_with_loose_ampersands_still_reads()
    {
        var r = Clean("""<svg viewBox="0 0 100 40"><text x="4" y="20">Salt & water</text></svg>""");
        Assert.Null(r.Problem);
        Assert.Equal(["Salt & water"], r.Texts);
        Assert.Equal("http://www.w3.org/2000/svg", XDocument.Parse(r.Svg!).Root!.Name.NamespaceName);
    }

    [Fact]
    public void Styles_become_checked_attributes_and_outrank_the_plain_ones()
    {
        var r = Clean(Open + """<rect width="10" height="10" fill="#1A73E8" class="x" style="fill: #D93025 !important; stroke-width: 2; cursor: pointer; behavior: url(x.htc)"/></svg>""");
        var rect = XDocument.Parse(r.Svg!).Descendants().Single(e => e.Name.LocalName == "rect");
        Assert.Equal("#D93025", (string?)rect.Attribute("fill"));
        Assert.Equal("2", (string?)rect.Attribute("stroke-width"));
        Assert.Null(rect.Attribute("style"));
        Assert.Null(rect.Attribute("class"));
        Assert.Null(rect.Attribute("cursor"));
    }

    [Fact]
    public void Every_font_is_the_callers()
    {
        var r = Clean(Open + """<text font-family="Comic Sans MS" style="font-family: Papyrus">A</text><text>B</text></svg>""", new SafeSvgOptions { FontFamily = "Segoe UI" });
        var doc = XDocument.Parse(r.Svg!);
        Assert.Equal("Segoe UI", (string?)doc.Root!.Attribute("font-family"));
        Assert.All(doc.Descendants().Where(e => e.Name.LocalName == "text").Attributes("font-family"), a => Assert.Equal("Segoe UI", a.Value));
        Assert.DoesNotContain("Comic", r.Svg);
        Assert.DoesNotContain("Papyrus", r.Svg);
    }

    [Fact]
    public void The_words_and_the_title_are_kept_for_search_and_screen_readers()
    {
        var r = Clean(Heart);
        Assert.Equal("The four chambers of the heart", r.Title);
        Assert.Equal(["Right atrium", "Left atrium", "Right ventricle", "Left ventricle", "Septum between the two sides"], r.Texts);
        var untitled = Clean(Open + """<text x="1" y="1">First <tspan>words</tspan></text><text>  </text><text>Second</text></svg>""");
        Assert.Equal("First words", untitled.Title);
        Assert.Equal(["First words", "Second"], untitled.Texts);
    }

    /// <summary>A dark palette the way the app makes one: the look's text colours and the tones' dark pairs.</summary>
    static readonly SvgPalette Dark = new("#F5F5F7", "rgba(245,245,247,0.62)", "rgba(245,245,247,0.34)", SvgPalette.DarkPaper, new Dictionary<Tone, (string, string)>
    {
        [Tone.Red] = ("#FF8A7E", "#4A2624"),
        [Tone.Blue] = ("#8DB2FF", "#23324A"),
        [Tone.Green] = ("#7FD49A", "#1F3A28"),
        [Tone.Amber] = ("#F2B866", "#433018"),
        [Tone.Purple] = ("#D7A6F0", "#3A2645"),
    });

    [Fact]
    public void The_palette_is_recoloured_for_dark_and_unknown_near_black_and_white_flip()
    {
        var r = Clean(Heart.Replace("</svg>", """<rect width="5" height="5" fill="#000" stroke="black"/><rect width="5" height="5" fill="#0a0a14" stroke="#FAFAFA"/><rect width="5" height="5" fill="#808080" stroke="currentColor"/><rect width="5" height="5" fill="white" stroke="rgb(20, 20, 20)"/><rect width="5" height="5" fill="none"/></svg>"""),
            new SafeSvgOptions { Palette = Dark, Dark = true });
        var rects = XDocument.Parse(r.Svg!).Descendants().Where(e => e.Name.LocalName == "rect").ToList();
        (string?, string?) Of(int i) => ((string?)rects[i].Attribute("fill"), (string?)rects[i].Attribute("stroke"));
        Assert.Equal(("#23324A", "#8DB2FF"), Of(0));
        Assert.Equal(("#4A2624", "#FF8A7E"), Of(1));
        Assert.Equal(("#F5F5F7", "#F5F5F7"), Of(4));
        Assert.Equal(("#F5F5F7", SvgPalette.DarkPaper), Of(5));
        Assert.Equal(("#808080", "#F5F5F7"), Of(6));
        Assert.Equal((SvgPalette.DarkPaper, "#F5F5F7"), Of(7));
        Assert.Equal(("none", null), Of(8));
        var words = XDocument.Parse(r.Svg!).Descendants().Where(e => e.Name.LocalName == "text").Select(t => (string?)t.Attribute("fill")).ToList();
        Assert.Equal(["#F5F5F7", "#F5F5F7", "#F5F5F7", "#F5F5F7", "rgba(245,245,247,0.62)"], words);
    }

    [Fact]
    public void Light_without_a_palette_keeps_the_written_colours_and_unknown_colours_stay_in_light()
    {
        var kept = Clean(Heart);
        Assert.Contains("fill=\"#E8F0FE\"", kept.Svg);
        Assert.Contains("stroke=\"#D93025\"", kept.Svg);
        var light = Clean(Open + """<rect width="5" height="5" fill="#0a0a14" stroke="#123456"/></svg>""", new SafeSvgOptions { Palette = SvgPalette.Written });
        Assert.Contains("fill=\"#0a0a14\"", light.Svg);
        Assert.Contains("stroke=\"#123456\"", light.Svg);
    }

    [Fact]
    public void The_notes_own_example_passes_untouched_but_for_its_colours()
    {
        var r = Clean(Heart);
        Assert.Null(r.Problem);
        var before = XDocument.Parse(Heart).Root!;
        var after = XDocument.Parse(r.Svg!).Root!;
        Assert.Equal(before.Descendants().Select(e => e.Name.LocalName), after.Descendants().Select(e => e.Name.LocalName));
        foreach (var (b, a) in before.Descendants().Zip(after.Descendants()))
            foreach (var attr in b.Attributes().Where(x => !x.IsNamespaceDeclaration))
                Assert.Equal(attr.Value, (string?)a.Attribute(attr.Name));
    }

    /// <summary>The drawing the notes' prompt shows the CLI engines is what they imitate: it must come through as
    /// written, and every colour in it must have a partner for dark.</summary>
    [Fact]
    public void The_prompts_own_example_passes_untouched_but_for_its_colours()
    {
        string svg = Summarize.SvgExample;
        var r = Clean(svg);
        Assert.Null(r.Problem);
        var before = XDocument.Parse(svg).Root!;
        var after = XDocument.Parse(r.Svg!).Root!;
        foreach (string name in new[] { "viewBox", "font-size", "fill" })
            Assert.Equal((string?)before.Attribute(name), (string?)after.Attribute(name));
        Assert.Equal(before.Descendants().Select(e => e.Name.LocalName), after.Descendants().Select(e => e.Name.LocalName));
        foreach (var (b, a) in before.Descendants().Zip(after.Descendants()))
        {
            foreach (var attr in b.Attributes().Where(x => !x.IsNamespaceDeclaration))
                Assert.Equal(attr.Value, (string?)a.Attribute(attr.Name));
            Assert.Equal(b.Nodes().OfType<XText>().Select(t => t.Value), a.Nodes().OfType<XText>().Select(t => t.Value));
        }

        var dark = Clean(svg, new SafeSvgOptions { Palette = Dark, Dark = true });
        Harmless(dark);
        var written = SvgPalette.Written.Tones.Values.SelectMany(t => new[] { t.Stroke, t.Fill })
            .Concat([SvgPalette.Written.Ink, SvgPalette.Written.Secondary, SvgPalette.Written.LightLines]);
        foreach (string hex in written) Assert.DoesNotContain(hex, dark.Svg!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("#F5F5F7", (string?)XDocument.Parse(dark.Svg!).Root!.Attribute("fill"));
    }

    [Fact]
    public void Random_and_garbled_input_never_throws()
    {
        var random = new Random(7);
        string[] pieces = ["<svg", ">", "</svg>", "<rect", "/>", "viewBox=\"0 0 9 9\"", "<!DOCTYPE", "&", "&lt;", "<text>", "</text>", "\"", "'", "<use href=\"#a\"/>", "id=\"a\"", "style=\"fill:", "url(", "<![CDATA[", "]]>", "<!--", "-->", " ", "\n", "é", "\u0000", "<?", "?>"];
        for (int i = 0; i < 300; i++)
        {
            var s = string.Concat(Enumerable.Range(0, random.Next(1, 30)).Select(_ => pieces[random.Next(pieces.Length)]));
            Harmless(Clean(s, new SafeSvgOptions { Palette = Dark, Dark = true }));
        }
        Harmless(Clean(Heart[..(Heart.Length / 2)]));
    }
}
