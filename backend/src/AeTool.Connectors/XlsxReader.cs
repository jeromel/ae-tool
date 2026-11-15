using System.IO.Compression;
using System.Xml.Linq;

namespace AeTool.Connectors;

/// <summary>
/// Lecteur .xlsx minimal, écrit sur la bibliothèque standard pour éviter une dépendance tierce.
/// Il lit les valeurs (celles en cache pour les formules). Des styles, il ne retient que les formats de date : une date
/// est rendue en « aaaa-mm-jj » au lieu du nombre de jours qu'Excel stocke. Un classeur est un zip de fichiers XML.
/// </summary>
public static class XlsxReader
{
    private static readonly XNamespace M = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/package/2006/relationships";

    /// <summary>Retourne, par nom de feuille, les lignes sous forme de tableaux de cellules (null = cellule vide).</summary>
    public static Dictionary<string, List<string?[]>> Read(Stream stream)
    {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var shared = ReadSharedStrings(zip);
        var dateStyles = ReadDateStyles(zip);
        var workbook = Load(zip, "xl/workbook.xml") ?? throw new InvalidDataException("Classeur Excel invalide : xl/workbook.xml absent.");
        var rels = Load(zip, "xl/_rels/workbook.xml.rels")?.Root?.Elements(Rel + "Relationship")
            .ToDictionary(r => (string)r.Attribute("Id")!, r => (string)r.Attribute("Target")!) ?? new();

        var result = new Dictionary<string, List<string?[]>>();
        foreach (var sheet in workbook.Root!.Element(M + "sheets")!.Elements(M + "sheet"))
        {
            var name = (string)sheet.Attribute("name")!;
            var target = rels.GetValueOrDefault((string?)sheet.Attribute(R + "id") ?? "");
            if (target is null) continue;
            var path = target.StartsWith('/') ? target[1..] : "xl/" + target;
            var doc = Load(zip, path);
            if (doc is not null) result[name] = ReadSheet(doc, shared, dateStyles);
        }
        return result;
    }

    private static List<string?[]> ReadSheet(XDocument doc, List<string> shared, HashSet<int> dateStyles)
    {
        var rows = new List<string?[]>();
        foreach (var row in doc.Root!.Element(M + "sheetData")?.Elements(M + "row") ?? Enumerable.Empty<XElement>())
        {
            var index = (int?)row.Attribute("r") ?? rows.Count + 1;
            while (rows.Count < index - 1) rows.Add(Array.Empty<string?>());
            var cells = new List<string?>();
            foreach (var c in row.Elements(M + "c"))
            {
                var col = ColumnIndex((string?)c.Attribute("r"), cells.Count);
                while (cells.Count < col) cells.Add(null);
                cells.Add(CellValue(c, shared, dateStyles));
            }
            rows.Add(cells.ToArray());
        }
        return rows;
    }

    private static string? CellValue(XElement c, List<string> shared, HashSet<int> dateStyles)
    {
        var type = (string?)c.Attribute("t");
        if (type == "inlineStr") return string.Concat(c.Descendants(M + "t").Select(t => t.Value));
        var v = c.Element(M + "v")?.Value;
        if (v is null) return null;
        return type switch
        {
            "s" => int.TryParse(v, out var i) && i < shared.Count ? shared[i] : null,
            "b" => v == "1" ? "true" : "false",
            "e" => null, // erreur de formule
            "str" => v,
            _ => (int?)c.Attribute("s") is { } style && dateStyles.Contains(style) ? AsDate(v) ?? v : v,
        };
    }

    /// <summary>Numéro de série Excel (jours depuis le 30/12/1899) en « aaaa-mm-jj », suivi de l'heure s'il y en a une.</summary>
    private static string? AsDate(string serial)
    {
        if (!double.TryParse(serial, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var days)
            || days < 1 || days > 2958465) return null;
        var date = DateTime.FromOADate(days);
        return date.TimeOfDay == TimeSpan.Zero ? date.ToString("yyyy-MM-dd") : date.ToString("yyyy-MM-dd HH:mm");
    }

    /// <summary>
    /// Index des styles de cellule dont le format est une date : formats prédéfinis 14 à 22 et 45 à 47, ou format
    /// personnalisé contenant un jour ou une année hors des parties entre guillemets et entre crochets.
    /// </summary>
    private static HashSet<int> ReadDateStyles(ZipArchive zip)
    {
        var result = new HashSet<int>();
        var doc = Load(zip, "xl/styles.xml");
        if (doc?.Root is null) return result;
        var custom = doc.Root.Element(M + "numFmts")?.Elements(M + "numFmt")
            .ToDictionary(f => (int)f.Attribute("numFmtId")!, f => (string?)f.Attribute("formatCode") ?? "") ?? new();
        var index = 0;
        foreach (var xf in doc.Root.Element(M + "cellXfs")?.Elements(M + "xf") ?? Enumerable.Empty<XElement>())
        {
            var id = (int?)xf.Attribute("numFmtId") ?? 0;
            if (id is >= 14 and <= 22 or >= 45 and <= 47 || (custom.TryGetValue(id, out var code) && IsDateFormat(code))) result.Add(index);
            index++;
        }
        return result;
    }

    public static bool IsDateFormat(string code)
    {
        var plain = System.Text.RegularExpressions.Regex.Replace(code, "\"[^\"]*\"|\\[[^\\]]*\\]|\\\\.", "").ToLowerInvariant();
        return plain.Contains('d') || plain.Contains('y');
    }

    private static int ColumnIndex(string? reference, int fallback)
    {
        if (string.IsNullOrEmpty(reference)) return fallback;
        var index = 0;
        foreach (var ch in reference)
        {
            if (!char.IsLetter(ch)) break;
            index = index * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
        }
        return index - 1;
    }

    private static List<string> ReadSharedStrings(ZipArchive zip)
    {
        var doc = Load(zip, "xl/sharedStrings.xml");
        if (doc is null) return new List<string>();
        // Une chaîne peut être découpée en fragments de mise en forme ; les annotations phonétiques (rPh) sont ignorées.
        return doc.Root!.Elements(M + "si").Select(si => string.Concat(
            si.Descendants(M + "t").Where(t => t.Ancestors(M + "rPh").FirstOrDefault() is null).Select(t => t.Value))).ToList();
    }

    private static XDocument? Load(ZipArchive zip, string path)
    {
        var entry = zip.GetEntry(path);
        if (entry is null) return null;
        using var s = entry.Open();
        return SafeXml.Load(s);
    }
}
