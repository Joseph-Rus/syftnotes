using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace StudyStash.Core.Ai;

public static partial class DocumentText
{
    static readonly XNamespace PresentationNs = "http://schemas.openxmlformats.org/presentationml/2006/main";
    static readonly XNamespace OfficeRelationshipNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    static readonly XNamespace PackageRelationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    static readonly XmlReaderSettings OfficeXml = new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };

    [GeneratedRegex(@"^ppt/slides/slide(\d+)\.xml$")]
    private static partial Regex SlideName();

    /// <summary>
    /// A PowerPoint's slides in the order the deck shows them, each slide's words followed by its speaker notes ("" for
    /// a slide with none, so the Nth string is always slide N); null when the deck has no slides.
    /// </summary>
    static List<string>? Pptx(string path)
    {
        using var z = ZipFile.OpenRead(path);
        var slides = DeckOrder(z) ?? NumberOrder(z);
        if (slides.Count == 0) return null;
        return slides.Select(slide => string.Join("\n\n", new[] { slide, NotesOf(z, slide) }.Select(part => Words(z, part)).Where(t => t.Length > 0))).ToList();
    }

    static string Words(ZipArchive z, string? part) => part is not null && z.GetEntry(part) is { } e ? Py.Strip(XmlWords(Entry(e), "a:p")) : "";

    /// <summary>The slides in the order ppt/presentation.xml lists them (the number in a slide's file name is only the
    /// order it was made in); null when that list, or what it points to, is missing or odd.</summary>
    static List<string>? DeckOrder(ZipArchive z)
    {
        var listed = Xml(z, "ppt/presentation.xml")?.Root?.Element(PresentationNs + "sldIdLst")?.Elements(PresentationNs + "sldId");
        var rels = Relationships(z, "ppt/presentation.xml");
        if (listed is null || rels is null) return null;
        var slides = listed.Select(s => rels.FirstOrDefault(r => r.Id == (string?)s.Attribute(OfficeRelationshipNs + "id")).Target).ToList();
        return slides.All(s => s is not null && z.GetEntry(s) is not null) ? slides! : null;
    }

    /// <summary>The slides by the number in their file names: the order for a file with no usable list.</summary>
    static List<string> NumberOrder(ZipArchive z) =>
        [.. z.Entries.Select(e => SlideName().Match(e.FullName)).Where(m => m.Success)
            .OrderBy(m => int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)).Select(m => m.Value)];

    /// <summary>The part holding a slide's speaker notes: the one the slide points to (notes are numbered in the order
    /// they were made, not like their slide), or without a list of what it points to, the one numbered like it.</summary>
    static string? NotesOf(ZipArchive z, string slide) =>
        Relationships(z, slide) is { } rels
            ? rels.Where(r => r.Type.EndsWith("/notesSlide", StringComparison.Ordinal)).Select(r => r.Target).FirstOrDefault()
            : SlideName().IsMatch(slide) ? slide.Replace("ppt/slides/slide", "ppt/notesSlides/notesSlide", StringComparison.Ordinal) : null;

    /// <summary>What a part of the file points to, from its _rels part, as names in the zip; null where it has none.</summary>
    static List<(string Id, string Type, string Target)>? Relationships(ZipArchive z, string part)
    {
        int slash = part.LastIndexOf('/');
        string folder = part[..(slash + 1)];
        if (Xml(z, $"{folder}_rels/{part[(slash + 1)..]}.rels") is not { } doc) return null;
        return [.. doc.Descendants(PackageRelationshipNs + "Relationship").Where(r => (string?)r.Attribute("TargetMode") != "External")
            .Select(r => ((string?)r.Attribute("Id") ?? "", (string?)r.Attribute("Type") ?? "", Resolve(folder, (string?)r.Attribute("Target") ?? "")))];
    }

    /// <summary>A target as a name in the zip: from the top when it starts with a slash, else from the part's folder,
    /// each ".." going up one.</summary>
    static string Resolve(string folder, string target)
    {
        var names = new List<string>(target.StartsWith('/') ? [] : folder.Split('/', StringSplitOptions.RemoveEmptyEntries));
        foreach (string name in target.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (name == ".." && names.Count > 0) names.RemoveAt(names.Count - 1);
            else if (name is not ("." or "..")) names.Add(name);
        }
        return string.Join('/', names);
    }

    /// <summary>A part of the file read as XML; null when it isn't there or isn't well formed.</summary>
    static XDocument? Xml(ZipArchive z, string name)
    {
        if (z.GetEntry(name) is not { } e) return null;
        try
        {
            using var reader = XmlReader.Create(e.Open(), OfficeXml);
            return XDocument.Load(reader);
        }
        catch (XmlException)
        {
            return null;
        }
    }
}
