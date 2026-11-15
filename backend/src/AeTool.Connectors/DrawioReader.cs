using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace AeTool.Connectors;

/// <summary>Forme ou lien d'une page draw.io, lu tel quel : géométrie en coordonnées absolues, style en clés et valeurs.</summary>
public sealed class DrawioCell
{
    public string Id { get; init; } = "";
    public string? Parent { get; init; }
    public bool Vertex { get; init; }
    public bool Edge { get; init; }
    public string? Source { get; init; }
    public string? Target { get; init; }
    public string Label { get; set; } = "";
    public Dictionary<string, string> Style { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Données ajoutées à la forme dans draw.io (Modifier les données), hors libellé.</summary>
    public Dictionary<string, string> Data { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public double X { get; set; }
    public double Y { get; set; }
    public double W { get; init; }
    public double H { get; init; }
    public List<(double X, double Y)> Points { get; init; } = new();

    public string? StyleValue(string key) => Style.TryGetValue(key, out var v) ? v : null;
    public bool Has(string flag) => Style.ContainsKey(flag);
}

public sealed class DrawioPage
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public List<DrawioCell> Cells { get; init; } = new();
    /// <summary>Identifiants des calques : la racine (« 0 ») et ses enfants directs. Une forme posée sur un calque est au premier niveau.</summary>
    public HashSet<string> Layers { get; init; } = new();
}

/// <summary>
/// Lecture des fichiers draw.io (diagrams.net) : .drawio, .xml, et .drawio.svg qui embarque le dessin.
/// Les pages peuvent être stockées en clair ou compressées (deflate, base64, encodage d'URL), comme draw.io les écrit.
/// </summary>
public static class DrawioReader
{
    public static List<DrawioPage> Read(Stream stream)
    {
        XElement root;
        try { root = SafeXml.Load(stream).Root ?? throw new InvalidDataException("Fichier vide."); }
        catch (System.Xml.XmlException e) { throw new InvalidDataException("Ce fichier n'est pas un dessin draw.io lisible : " + e.Message); }

        // Un .drawio.svg porte le dessin dans son attribut « content ».
        if (root.Name.LocalName == "svg")
        {
            var content = (string?)root.Attribute("content")
                ?? throw new InvalidDataException("Cette image SVG ne contient pas de dessin draw.io modifiable (enregistrez-la depuis draw.io en « SVG modifiable »).");
            root = Parse(content);
        }
        return root.Name.LocalName switch
        {
            "mxfile" => root.Elements("diagram").Select((d, i) => Page(d, i)).ToList(),
            "mxGraphModel" => new List<DrawioPage> { Model(root, "page-1", "Page 1") },
            _ => throw new InvalidDataException($"Racine « {root.Name.LocalName} » inattendue : ce fichier n'est pas un dessin draw.io."),
        };
    }

    private static XElement Parse(string xml)
    {
        using var reader = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        return SafeXml.Load(reader).Root ?? throw new InvalidDataException("Dessin vide.");
    }

    private static DrawioPage Page(XElement diagram, int index)
    {
        var id = (string?)diagram.Attribute("id") is { Length: > 0 } i ? i : $"page-{index + 1}";
        var name = (string?)diagram.Attribute("name") is { Length: > 0 } n ? n : $"Page {index + 1}";
        var model = diagram.Element("mxGraphModel");
        if (model is null)
        {
            var packed = diagram.Value.Trim();
            if (packed.Length == 0) return new DrawioPage { Id = id, Name = name };
            model = Parse(Unpack(packed));
        }
        return Model(model, id, name);
    }

    /// <summary>Page compressée : base64, puis deflate brut, puis encodage d'URL.</summary>
    public static string Unpack(string packed)
    {
        byte[] bytes;
        try { bytes = Convert.FromBase64String(packed); }
        catch (FormatException) { throw new InvalidDataException("Page draw.io compressée illisible (base64 invalide)."); }
        using var input = new DeflateStream(new MemoryStream(bytes), CompressionMode.Decompress);
        using var text = new StreamReader(input, Encoding.UTF8);
        string inflated;
        try { inflated = text.ReadToEnd(); }
        catch (InvalidDataException) { throw new InvalidDataException("Page draw.io compressée illisible (données compressées invalides)."); }
        return Uri.UnescapeDataString(inflated);
    }

    private static DrawioPage Model(XElement model, string id, string name)
    {
        var page = new DrawioPage { Id = id, Name = name };
        var rootElement = model.Element("root");
        if (rootElement is null) return page;
        foreach (var node in rootElement.Elements())
        {
            // Une forme qui porte des données est enveloppée : <object label="…" clé="…"><mxCell …/></object>.
            var cell = node.Name.LocalName == "mxCell" ? node : node.Element("mxCell");
            if (cell is null) continue;
            var wrapper = cell == node ? null : node;
            var cellId = (string?)wrapper?.Attribute("id") ?? (string?)cell.Attribute("id") ?? "";
            var geometry = cell.Element("mxGeometry");
            var data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (wrapper is not null)
                foreach (var a in wrapper.Attributes())
                    if (a.Name.LocalName is not ("id" or "label" or "placeholders" or "tooltip" or "link") && !string.IsNullOrWhiteSpace(a.Value))
                        data[a.Name.LocalName] = a.Value.Trim();
            var style = ParseStyle((string?)cell.Attribute("style"));
            var raw = (string?)wrapper?.Attribute("label") ?? (string?)cell.Attribute("value") ?? "";
            page.Cells.Add(new DrawioCell
            {
                Id = cellId, Parent = (string?)cell.Attribute("parent"),
                Vertex = (string?)cell.Attribute("vertex") == "1", Edge = (string?)cell.Attribute("edge") == "1",
                Source = (string?)cell.Attribute("source"), Target = (string?)cell.Attribute("target"),
                Label = PlainLabel(raw), Style = style, Data = data,
                X = Num(geometry, "x"), Y = Num(geometry, "y"), W = Num(geometry, "width"), H = Num(geometry, "height"),
                Points = geometry?.Elements("Array").Where(a => (string?)a.Attribute("as") == "points")
                    .SelectMany(a => a.Elements("mxPoint")).Select(p => (Num(p, "x"), Num(p, "y"))).ToList() ?? new(),
            });
        }
        // Calques : la cellule racine et ses enfants directs, sans géométrie.
        var all = page.Cells.ToDictionary(c => c.Id, c => c, StringComparer.Ordinal);
        foreach (var c in page.Cells.Where(c => c.Parent is null)) page.Layers.Add(c.Id);
        foreach (var c in page.Cells.Where(c => c.Parent is not null && page.Layers.Contains(c.Parent) && !c.Vertex && !c.Edge)) page.Layers.Add(c.Id);
        Absolutize(page, all);
        return page;
    }

    /// <summary>draw.io place un enfant relativement à son parent : on ramène tout en coordonnées de page.</summary>
    private static void Absolutize(DrawioPage page, Dictionary<string, DrawioCell> all)
    {
        var done = new HashSet<string>(page.Layers);
        void Fix(DrawioCell c, int depth)
        {
            if (done.Contains(c.Id) || depth > 50) return;
            done.Add(c.Id);
            if (c.Parent is null || page.Layers.Contains(c.Parent) || !all.TryGetValue(c.Parent, out var parent) || !parent.Vertex) return;
            Fix(parent, depth + 1);
            c.X += parent.X; c.Y += parent.Y;
            for (var i = 0; i < c.Points.Count; i++) c.Points[i] = (c.Points[i].X + parent.X, c.Points[i].Y + parent.Y);
        }
        foreach (var c in page.Cells) Fix(c, 0);
    }

    public static Dictionary<string, string> ParseStyle(string? style)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(style)) return map;
        foreach (var part in style.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = part.IndexOf('=');
            // Un mot seul (« text », « ellipse », « rounded ») est le nom d'un style prédéfini.
            if (eq < 0) map[part] = "";
            else map[part[..eq]] = part[(eq + 1)..];
        }
        return map;
    }

    private static readonly Regex Breaks = new(@"<\s*(br|/div|/p|/li)\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Tags = new(@"<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex Spaces = new(@"[ \t ]+", RegexOptions.Compiled);

    /// <summary>Libellé en texte brut : balises HTML retirées, retours à la ligne gardés, espaces resserrés.</summary>
    public static string PlainLabel(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        var text = Breaks.Replace(raw, "\n");
        text = Tags.Replace(text, "");
        text = WebUtility.HtmlDecode(text);
        var lines = text.Split('\n').Select(l => Spaces.Replace(l, " ").Trim()).Where(l => l.Length > 0);
        return string.Join("\n", lines);
    }

    private static double Num(XElement? e, string attribute) =>
        double.TryParse((string?)e?.Attribute(attribute), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
}
