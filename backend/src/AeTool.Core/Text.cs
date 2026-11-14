using System.Text;

namespace AeTool.Core;

/// <summary>Outils de texte partagés : comparaison de libellés, recherche sans accents, clés dérivées d'un libellé.</summary>
public static class Text
{
    // Lettres accentuées courantes (Latin-1 et Latin étendu A) et leur équivalent sans accent. Une table explicite plutôt que la
    // décomposition Unicode : celle-ci ne fait rien quand la globalisation est invariante, ce qui est le cas de cette application.
    private static readonly string[] Groups =
    {
        "aàáâãäåāăą", "cçćĉč", "dďđ", "eèéêëēĕėęě", "gĝğģ", "hĥ", "iìíîïĩīĭįı", "jĵ", "kķ", "lĺļľł", "nñńņň",
        "oòóôõöøōŏő", "rŕŗř", "sśŝşš", "tţť", "uùúûüũūŭůűų", "wŵ", "yýÿŷ", "zźżž",
    };
    private static readonly Dictionary<char, char> Folding = Groups.SelectMany(g => g.Skip(1).Select(c => (c, g[0]))).ToDictionary(x => x.c, x => x.Item2);

    /// <summary>
    /// Clé de comparaison de deux libellés : minuscules, sans accents, ponctuation et espaces resserrés en un seul espace.
    /// « SRV-APP 01 », « srv app  01 » et « Srv_App_01 » donnent la même clé.
    /// </summary>
    public static string Key(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var sb = new StringBuilder();
        var pendingSpace = false;
        foreach (var ch in s)
        {
            if (!char.IsLetterOrDigit(ch)) { pendingSpace = true; continue; }
            if (pendingSpace && sb.Length > 0) sb.Append(' ');
            pendingSpace = false;
            sb.Append(Fold(char.ToLowerInvariant(ch)));
        }
        // Un libellé fait uniquement de ponctuation ne doit pas être confondu avec tous les autres.
        return sb.Length > 0 ? sb.ToString() : s.Trim().ToLowerInvariant();
    }

    /// <summary>Texte en minuscules et sans accents, ponctuation conservée : sert à la recherche (« Sécurité » se trouve en tapant « securite »).</summary>
    public static string Plain(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s) sb.Append(Fold(char.ToLowerInvariant(ch)));
        return sb.ToString();
    }

    /// <summary>Clé technique tirée d'un libellé : lettres sans accent et chiffres, tout le reste devient un tiret (« Bloc fonctionnel » donne « bloc-fonctionnel »).</summary>
    public static string Slug(string? s)
    {
        var sb = new StringBuilder();
        foreach (var ch in Plain(s))
        {
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9') sb.Append(ch);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        return sb.ToString().Trim('-');
    }

    private static string Fold(char ch)
    {
        switch (ch)
        {
            case 'œ': return "oe";
            case 'æ': return "ae";
            case 'ß': return "ss";
        }
        return Folding.TryGetValue(ch, out var plain) ? plain.ToString() : ch.ToString();
    }
}
