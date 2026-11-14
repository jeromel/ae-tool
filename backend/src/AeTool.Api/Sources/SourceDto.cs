using System.Text.RegularExpressions;
using AeTool.Connectors;
using AeTool.Core;

namespace AeTool.Api.Sources;

/// <summary>Corps de PUT /api/sources/{key}. Secret : absent = inchangé, chaîne vide = effacé. IgnoredIds absent = inchangé.</summary>
public sealed record SourceInput(string? Label, string? Kind, Dictionary<string, string>? Connection, SourceMapping? Mapping,
    bool? SourceIsMaster, List<string>? IgnoredIds, string? Secret);

public sealed record SourceTemplate(string Name, string Description, SourceProfile Profile);

/// <summary>Profil tel que l'interface le voit : jamais de secret, seulement le fait qu'il y en a un.</summary>
public sealed record SourceView(string Key, string Label, string Kind, Dictionary<string, string> Connection, SourceMapping Mapping,
    bool SourceIsMaster, List<string>? IgnoredIds, int IgnoredCount, DateTime? LastRunAt, string? LastRunBy, bool HasSecret, bool HasReport)
{
    public static SourceView Of(SourceProfile p, bool hasSecret, bool hasReport, bool withIgnored) =>
        new(p.Key, p.Label, p.Kind, p.Connection, p.Mapping, p.SourceIsMaster, withIgnored ? p.IgnoredIds : null, p.IgnoredIds.Count,
            p.LastRunAt, p.LastRunBy, hasSecret, hasReport);
}

/// <summary>Contrôle d'un profil reçu de l'interface ; renvoie le profil à enregistrer ou le message d'erreur.</summary>
public static partial class SourceValidator
{
    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,39}$")] private static partial Regex KeyPattern();

    public static bool IsValidKey(string key) => KeyPattern().IsMatch(key);

    public static (SourceProfile? Profile, string? Error) Build(string key, SourceInput input, SourceProfile? existing, Metamodel metamodel)
    {
        if (!IsValidKey(key)) return Fail("La clé de la source doit tenir en 40 caractères : minuscules, chiffres et tirets.");
        var label = input.Label?.Trim();
        if (string.IsNullOrEmpty(label) || label.Length > 120) return Fail("Le libellé est obligatoire (120 caractères au plus).");
        var kind = input.Kind ?? existing?.Kind ?? "";
        if (kind is not (SourceProfile.KindFile or SourceProfile.KindEasyVista)) return Fail("Le type de source doit être « fichier » ou « easyvista ».");
        var mapping = input.Mapping ?? existing?.Mapping;
        if (mapping is null) return Fail("Le mappage est obligatoire.");
        if (MappingError(mapping, metamodel) is { } mappingError) return Fail(mappingError);
        var connection = KnownSettings(kind, input.Connection ?? existing?.Connection ?? new());
        if (kind == SourceProfile.KindEasyVista && EasyVistaClient.Validate(connection) is { } connectionError) return Fail(connectionError);
        if (kind == SourceProfile.KindEasyVista && !string.IsNullOrEmpty(input.Secret) && input.Secret.Length > 4096) return Fail("Le secret est trop long.");
        var profile = new SourceProfile
        {
            Key = key, Label = label, Kind = kind, Connection = connection, Mapping = Clean(mapping),
            SourceIsMaster = input.SourceIsMaster ?? existing?.SourceIsMaster ?? false,
            IgnoredIds = input.IgnoredIds?.Distinct().ToList() ?? existing?.IgnoredIds ?? new(),
            LastRunAt = existing?.LastRunAt, LastRunBy = existing?.LastRunBy,
        };
        return (profile, null);
    }

    private static (SourceProfile?, string?) Fail(string error) => (null, error);

    /// <summary>Seuls les réglages connus de l'adaptateur sont gardés, limités en taille : un mot de passe glissé ici ne serait pas stocké en clair.</summary>
    private static Dictionary<string, string> KnownSettings(string kind, Dictionary<string, string> connection) =>
        kind == SourceProfile.KindEasyVista
            ? connection.Where(c => EasyVistaClient.ConnectionKeys.Contains(c.Key) && c.Value.Length <= 500).ToDictionary(c => c.Key, c => c.Value.Trim())
            : new();

    private static string? MappingError(SourceMapping m, Metamodel metamodel)
    {
        if (string.IsNullOrWhiteSpace(m.IdField) || string.IsNullOrWhiteSpace(m.NameField)) return "Les colonnes de l'identifiant et du nom sont obligatoires.";
        // Contrôlé avant la recherche du type : une clé absente passerait sinon pour « aucun type inconnu ».
        if (m.Types.Any(t => string.IsNullOrWhiteSpace(t.TypeKey))) return "Chaque valeur de nature doit être associée à un type du métamodèle.";
        var keys = m.Types.Select(t => t.TypeKey).Concat(string.IsNullOrWhiteSpace(m.DefaultTypeKey) ? Array.Empty<string>() : new[] { m.DefaultTypeKey });
        if (keys.FirstOrDefault(k => metamodel.Type(k) is null) is { } unknown) return $"Type inconnu du métamodèle : {unknown}";
        if (m.Types.Any(t => string.IsNullOrWhiteSpace(t.SourceValue))) return "Chaque type du mappage doit indiquer la valeur de la source qu'il remplace.";
        if (m.Types.Count > 0 && string.IsNullOrWhiteSpace(m.TypeField)) return "Des valeurs de nature sont associées à des types, mais la colonne de nature n'est pas indiquée.";
        if (m.Fields.Count > 100) return "Trop de propriétés reprises (100 au plus).";
        if (m.Fields.Any(f => string.IsNullOrWhiteSpace(f.SourceField) || string.IsNullOrWhiteSpace(f.PropertyKey) || f.PropertyKey.Length > 100))
            return "Chaque propriété reprise doit indiquer une colonne de la source et une clé de propriété.";
        if (m.Fields.GroupBy(f => f.PropertyKey).Any(g => g.Count() > 1)) return "Une même propriété ne peut être alimentée que par une colonne.";
        return null;
    }

    private static SourceMapping Clean(SourceMapping m) => new()
    {
        IdField = m.IdField.Trim(), NameField = m.NameField.Trim(),
        TypeField = string.IsNullOrWhiteSpace(m.TypeField) ? null : m.TypeField.Trim(),
        DefaultTypeKey = string.IsNullOrWhiteSpace(m.DefaultTypeKey) ? null : m.DefaultTypeKey,
        Types = m.Types.Select(t => new TypeMapping { SourceValue = t.SourceValue.Trim(), TypeKey = t.TypeKey }).ToList(),
        Fields = m.Fields.Select(f => new FieldMapping
        { SourceField = f.SourceField.Trim(), PropertyKey = f.PropertyKey.Trim(), Label = string.IsNullOrWhiteSpace(f.Label) ? null : f.Label.Trim() }).ToList(),
    };
}
