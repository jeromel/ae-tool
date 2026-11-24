using System.Text;
using AeTool.Core;

namespace AeTool.Connectors;

/// <summary>
/// Adaptateur « fichier » : lit un CSV ou un classeur Excel (export d'une CMDB, d'un outil de gestion de projet ou de tout autre outil)
/// et en tire des lignes colonne → valeur. Il ne connaît rien du rapprochement.
/// </summary>
public static class TabularReader
{
    public const int MaxRows = 100_000;
    public const int MaxColumns = 200;
    private static readonly char[] Delimiters = { ';', ',', '\t', '|' };

    /// <summary>Un fichier commençant par « PK » est un classeur xlsx (un zip) ; sinon c'est du texte CSV.</summary>
    public static SourceTable Read(Stream stream, string? fileName = null)
    {
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var bytes = buffer.ToArray();
        if (bytes.Length == 0) return Failed("fichier-vide", "Le fichier est vide.");
        string? error = null;
        var grid = bytes.Length >= 2 && bytes[0] == 'P' && bytes[1] == 'K' ? ReadWorkbook(bytes, out error) : ReadCsv(Decode(bytes));
        return grid is null ? Failed("classeur-illisible", error ?? "Fichier illisible.") : ToTable(grid);
    }

    private static SourceTable Failed(string code, string message) =>
        new() { Issues = { new SourceIssue("erreur", code, message) } };

    // ---------- Classeur ----------

    private static List<string?[]>? ReadWorkbook(byte[] bytes, out string? error)
    {
        error = null;
        try
        {
            using var ms = new MemoryStream(bytes);
            var book = XlsxReader.Read(ms);
            // Première feuille contenant au moins une cellule renseignée.
            var sheet = book.Values.FirstOrDefault(rows => rows.Any(r => r.Any(c => !string.IsNullOrWhiteSpace(c))));
            if (sheet is not null) return sheet;
            error = "Le classeur ne contient aucune feuille renseignée.";
        }
        catch (Exception e) when (e is InvalidDataException or System.Xml.XmlException or NullReferenceException or FormatException)
        {
            error = "Classeur Excel illisible : le fichier est corrompu ou n'est pas un classeur .xlsx.";
        }
        return null;
    }

    // ---------- CSV ----------

    /// <summary>UTF-8 avec ou sans BOM ; si les octets ne sont pas de l'UTF-8 valide, repli sur Latin-1 (export Excel français ancien).</summary>
    private static string Decode(byte[] bytes)
    {
        var start = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
        try { return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes, start, bytes.Length - start); }
        catch (DecoderFallbackException) { return Encoding.Latin1.GetString(bytes, start, bytes.Length - start); }
    }

    private static List<string?[]> ReadCsv(string text)
    {
        var delimiter = DetectDelimiter(text);
        var rows = new List<string?[]>();
        var fields = new List<string?>();
        var field = new StringBuilder();
        var inQuotes = false;
        var quoted = false;

        void EndField() { fields.Add(quoted || field.Length > 0 ? field.ToString() : null); field.Clear(); quoted = false; }
        void EndRow() { EndField(); rows.Add(fields.ToArray()); fields.Clear(); }

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (inQuotes)
            {
                if (ch != '"') field.Append(ch);
                else if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else inQuotes = false;
            }
            else if (ch == '"' && field.Length == 0) { inQuotes = true; quoted = true; }
            else if (ch == delimiter) EndField();
            else if (ch == '\r') { if (i + 1 < text.Length && text[i + 1] == '\n') i++; EndRow(); }
            else if (ch == '\n') EndRow();
            else field.Append(ch);
            // Au-delà de la limite, inutile de tout lire : la ligne en trop suffit à déclencher l'anomalie.
            if (rows.Count > MaxRows + 1) return rows;
        }
        if (field.Length > 0 || quoted || fields.Count > 0) EndRow();
        return rows;
    }

    /// <summary>Séparateur le plus fréquent, hors guillemets, sur la ligne d'en-têtes.</summary>
    private static char DetectDelimiter(string text)
    {
        var counts = new int[Delimiters.Length];
        var inQuotes = false;
        foreach (var ch in text)
        {
            if (ch == '"') inQuotes = !inQuotes;
            else if (!inQuotes && (ch == '\n' || ch == '\r')) break;
            else if (!inQuotes) { var d = Array.IndexOf(Delimiters, ch); if (d >= 0) counts[d]++; }
        }
        var best = Array.IndexOf(counts, counts.Max());
        return counts[best] == 0 ? ',' : Delimiters[best];
    }

    // ---------- Commun ----------

    /// <summary>Première ligne = en-têtes ; les lignes entièrement vides sont ignorées.</summary>
    private static SourceTable ToTable(List<string?[]> grid)
    {
        var table = new SourceTable();
        var headerRow = grid.FindIndex(r => r.Any(c => !string.IsNullOrWhiteSpace(c)));
        if (headerRow < 0) return Failed("fichier-vide", "Le fichier ne contient aucune ligne.");
        var headers = Headers(grid[headerRow], table);
        var irregular = 0;
        foreach (var row in grid.Skip(headerRow + 1))
        {
            if (row.All(c => string.IsNullOrWhiteSpace(c))) continue;
            if (table.Rows.Count >= MaxRows)
            {
                table.Truncated = true;
                table.Issues.Add(new SourceIssue("erreur", "limite-lignes", $"Le fichier dépasse {MaxRows} lignes ; seules les {MaxRows} premières sont lues."));
                break;
            }
            if (row.Length > headers.Count && row.Skip(headers.Count).Any(c => !string.IsNullOrWhiteSpace(c))) irregular++;
            var values = new Dictionary<string, string?>();
            for (var c = 0; c < headers.Count; c++)
                values[headers[c]] = c < row.Length && !string.IsNullOrWhiteSpace(row[c]) ? row[c]!.Trim() : null;
            table.Rows.Add(values);
        }
        if (irregular > 0)
            table.Issues.Add(new SourceIssue("avertissement", "lignes-irregulieres", $"{irregular} ligne(s) comptent plus de valeurs que d'en-têtes ; les valeurs en trop sont ignorées."));
        table.Columns = headers;
        table.Total = table.Rows.Count;
        return table;
    }

    /// <summary>En-têtes sans espaces superflus ; un doublon est suffixé « (2) », « (3) »… et une colonne sans titre reçoit « Colonne N ».</summary>
    private static List<string> Headers(string?[] row, SourceTable table)
    {
        var headers = new List<string>();
        var used = new HashSet<string>();
        for (var c = 0; c < row.Length; c++)
        {
            if (c >= MaxColumns)
            {
                table.Issues.Add(new SourceIssue("erreur", "limite-colonnes", $"Le fichier dépasse {MaxColumns} colonnes ; les suivantes sont ignorées."));
                break;
            }
            var name = row[c]?.Trim();
            if (string.IsNullOrEmpty(name)) name = $"Colonne {c + 1}";
            var unique = name;
            for (var n = 2; !used.Add(unique); n++) unique = $"{name} ({n})";
            headers.Add(unique);
        }
        return headers;
    }
}
