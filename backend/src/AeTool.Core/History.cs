namespace AeTool.Core;

/// <summary>
/// Copie figée des champs d'un objet qui comptent pour l'historique. On la prend avant une écriture, puis on la compare
/// à l'objet modifié : l'objet vivant n'est jamais conservé, il serait déjà changé.
/// </summary>
public sealed class ObjectState
{
    public string Name { get; init; } = "";
    public string TypeKey { get; init; } = "";
    public string? Documentation { get; init; }
    public string? Owner { get; init; }
    public string ValidationStatus { get; init; } = "";
    public bool Archived { get; init; }
    public IReadOnlyList<(string Key, string? Value)> Properties { get; init; } = Array.Empty<(string, string?)>();
    public Visual? Visual { get; init; }
    public IReadOnlyDictionary<string, string> ExternalRefs { get; init; } = new Dictionary<string, string>();
}

/// <summary>Copie figée d'une relation (voir <see cref="ObjectState"/>). Source et cible sont résolues en noms, plus lisibles qu'un identifiant.</summary>
public sealed class RelationState
{
    public string TypeKey { get; init; } = "";
    public string? Name { get; init; }
    public string SourceName { get; init; } = "";
    public string TargetName { get; init; } = "";
    public bool Archived { get; init; }
    public IReadOnlyList<(string Key, string? Value)> Properties { get; init; } = Array.Empty<(string, string?)>();
}

/// <summary>
/// Calcul de l'historique avant/après (exigence EF-52). Pur : il compare deux copies et produit des champs lisibles,
/// avec le libellé de la propriété et du type plutôt que leur clé. Un champ identique avant et après n'apparaît pas.
/// </summary>
public static class ChangeSet
{
    public const string NameField = "Nom";
    public const string TypeField = "Type";
    public const string DescriptionField = "Description";
    public const string OwnerField = "Responsable";
    public const string VisualField = "Visuel propre";
    public const string ArchiveField = "Archivage";
    public const string StatusField = "État de validation";

    public static ObjectState Capture(ArchObject o) => new()
    {
        Name = o.Name, TypeKey = o.TypeKey, Documentation = o.Documentation, Owner = o.Owner, ValidationStatus = o.ValidationStatus,
        Archived = o.Archived, Properties = o.Properties.Select(p => (p.Key, p.Value)).ToList(),
        Visual = o.VisualOverride is null ? null : CopyOf(o.VisualOverride), ExternalRefs = new Dictionary<string, string>(o.ExternalRefs),
    };

    public static RelationState Capture(Repository repo, Relationship r) => new()
    {
        TypeKey = r.TypeKey, Name = r.Name, Archived = r.Archived, Properties = r.Properties.Select(p => (p.Key, p.Value)).ToList(),
        SourceName = NameOf(repo, r.SourceId), TargetName = NameOf(repo, r.TargetId),
    };

    private static string NameOf(Repository repo, string id) =>
        repo.Objects.TryGetValue(id, out var o) ? o.Name : repo.Relationships.ContainsKey(id) ? "(relation)" : id;

    private static Visual CopyOf(Visual v) => new()
    {
        Shape = v.Shape, Fill = v.Fill, Stroke = v.Stroke, TextColor = v.TextColor, Icon = v.Icon, Template = v.Template,
    };

    // ------------------------------------------------------------ Objets

    /// <summary>Champs modifiés d'un objet. Avec <paramref name="before"/> nul, c'est une création : chaque champ renseigné est listé.</summary>
    public static List<FieldChange> Between(Metamodel m, ObjectState? before, ObjectState after)
    {
        var list = new List<FieldChange>();
        void Add(string field, string? b, string? a)
        {
            b = Blank(b); a = Blank(a);
            if (b != a) list.Add(new FieldChange(field, b, a));
        }

        Add(NameField, before?.Name, after.Name);
        Add(TypeField, before is null ? null : TypeLabel(m, before.TypeKey), TypeLabel(m, after.TypeKey));
        Add(DescriptionField, before?.Documentation, after.Documentation);
        Add(OwnerField, before?.Owner, after.Owner);
        foreach (var key in PropertyKeys(before?.Properties, after.Properties))
            Add(PropertyLabel(m, key), Value(before?.Properties, key), Value(after.Properties, key));
        Add(VisualField, Describe(before?.Visual), Describe(after.Visual));
        foreach (var source in (before?.ExternalRefs.Keys ?? Enumerable.Empty<string>()).Union(after.ExternalRefs.Keys).Order())
            Add($"Lien avec la source « {source} »", before?.ExternalRefs.GetValueOrDefault(source), after.ExternalRefs.GetValueOrDefault(source));
        if (before is not null || after.Archived) Add(ArchiveField, before is null ? null : ArchiveLabel(before.Archived), ArchiveLabel(after.Archived));
        Add(StatusField, before?.ValidationStatus, after.ValidationStatus);
        return list;
    }

    /// <summary>
    /// Vrai si le fond de l'objet a changé : nom, type, description, responsable, propriétés. Le visuel, l'archivage et
    /// l'état de validation n'en font pas partie : seul le fond demande une nouvelle validation.
    /// </summary>
    public static bool IsSubstantive(ObjectState before, ObjectState after) =>
        Blank(before.Name) != Blank(after.Name) || before.TypeKey != after.TypeKey
        || Blank(before.Documentation) != Blank(after.Documentation) || Blank(before.Owner) != Blank(after.Owner)
        || PropertyKeys(before.Properties, after.Properties).Any(k => Blank(Value(before.Properties, k)) != Blank(Value(after.Properties, k)));

    private static string ArchiveLabel(bool archived) => archived ? "Archivé" : "Actif";

    // ------------------------------------------------------------ Relations

    public static List<FieldChange> Between(Metamodel m, RelationState? before, RelationState after)
    {
        var list = new List<FieldChange>();
        void Add(string field, string? b, string? a)
        {
            b = Blank(b); a = Blank(a);
            if (b != a) list.Add(new FieldChange(field, b, a));
        }

        // Source et cible ne changent pas : on ne les donne qu'à la création, pour lire l'entrée seule.
        if (before is null)
        {
            Add("Source", null, after.SourceName);
            Add("Cible", null, after.TargetName);
        }
        Add(TypeField, before is null ? null : RelationTypeLabel(m, before.TypeKey), RelationTypeLabel(m, after.TypeKey));
        Add(NameField, before?.Name, after.Name);
        foreach (var key in PropertyKeys(before?.Properties, after.Properties))
            Add(PropertyLabel(m, key), Value(before?.Properties, key), Value(after.Properties, key));
        if (before is not null || after.Archived) Add(ArchiveField, before is null ? null : ArchiveLabel(before.Archived), ArchiveLabel(after.Archived));
        return list;
    }

    // ------------------------------------------------------------ Notations

    /// <summary>Champs modifiés d'une notation d'urbanisation ; les preuves sont comptées, pas détaillées (elles se comptent par dizaines).</summary>
    public static List<FieldChange> Between(Assessment? before, Assessment after)
    {
        var list = new List<FieldChange>();
        void Add(string field, string? b, string? a)
        {
            b = Blank(b); a = Blank(a);
            if (b != a) list.Add(new FieldChange(field, b, a));
        }

        Add("Criticité métier", before?.Cm?.ToString(), after.Cm?.ToString());
        Add("Besoin", before?.Need, after.Need);
        Add("Enjeu patrimonial", before?.HeritageStake, after.HeritageStake);
        Add("Décision retenue", before?.RetainedDecision, after.RetainedDecision);
        Add("Justification", before?.Justification, after.Justification);
        var changedEvidences = ChangedEvidences(before, after);
        if (changedEvidences > 0) list.Add(new FieldChange("Preuves modifiées", null, changedEvidences.ToString()));
        foreach (var code in (before?.RedTriggers.Keys ?? Enumerable.Empty<string>()).Union(after.RedTriggers.Keys).Order())
            Add($"Déclencheur rouge {code}", before?.RedTriggers.GetValueOrDefault(code), after.RedTriggers.GetValueOrDefault(code));
        return list;
    }

    private static int ChangedEvidences(Assessment? before, Assessment after)
    {
        static string Key(Evidence e) => e.Code + "\u0001" + e.SubCriterion;
        static string Content(Evidence e) => string.Join("\u0001", e.Value, e.Reference, e.Date, e.VerifiedBy);
        var old = (before?.Evidences ?? new List<Evidence>()).GroupBy(Key).ToDictionary(g => g.Key, g => Content(g.First()));
        return after.Evidences.GroupBy(Key).Count(g => !old.TryGetValue(g.Key, out var c) ? Content(g.First()) != Content(new Evidence { Code = g.First().Code, SubCriterion = g.First().SubCriterion })
                                                         : c != Content(g.First()));
    }

    // ------------------------------------------------------------ Outils

    /// <summary>Texte vide et absence de texte se valent : passer de « rien » à « chaîne vide » n'est pas une modification.</summary>
    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string TypeLabel(Metamodel m, string key) => m.Type(key)?.Label is { Length: > 0 } label ? label : key;
    private static string RelationTypeLabel(Metamodel m, string key) => m.RelType(key)?.Label is { Length: > 0 } label ? label : key;
    private static string PropertyLabel(Metamodel m, string key) => m.Property(key)?.Label is { Length: > 0 } label ? label : key;

    private static IEnumerable<string> PropertyKeys(IReadOnlyList<(string Key, string? Value)>? before, IReadOnlyList<(string Key, string? Value)> after) =>
        after.Select(p => p.Key).Concat(before?.Select(p => p.Key) ?? Enumerable.Empty<string>()).Distinct();

    private static string? Value(IReadOnlyList<(string Key, string? Value)>? properties, string key) =>
        properties?.FirstOrDefault(p => p.Key == key).Value;

    /// <summary>Visuel propre lisible en une ligne ; null s'il n'y a aucun réglage propre.</summary>
    private static string? Describe(Visual? v)
    {
        if (v is null) return null;
        var parts = new List<string>();
        void Add(string label, string? value) { if (!string.IsNullOrWhiteSpace(value)) parts.Add($"{label} {value}"); }
        Add("forme", v.Shape);
        Add("fond", v.Fill);
        Add("contour", v.Stroke);
        Add("texte", v.TextColor);
        Add("pictogramme", v.Icon);
        Add("gabarit", v.Template);
        return parts.Count == 0 ? null : string.Join(", ", parts);
    }
}

/// <summary>Critères de recherche dans le journal. Tous sont facultatifs et se cumulent.</summary>
public sealed record JournalFilter(string? User, string? Kind, string? Text, DateTime? From, DateTime? To);

public static class JournalSearch
{
    /// <summary>Entrées correspondant au filtre, les plus récentes d'abord. <paramref name="to"/> inclut toute la journée indiquée.</summary>
    public static IEnumerable<JournalEntry> Apply(IEnumerable<JournalEntry> journal, JournalFilter f)
    {
        var needle = Text.Plain(f.Text?.Trim());
        var end = f.To?.Date.AddDays(1);
        foreach (var e in journal.Reverse())
        {
            if (!string.IsNullOrEmpty(f.User) && !string.Equals(e.User, f.User, StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.IsNullOrEmpty(f.Kind) && e.TargetKind != f.Kind) continue;
            if (f.From is { } from && e.At < from.Date) continue;
            if (end is { } limit && e.At >= limit) continue;
            if (needle.Length > 0 && !Matches(e, needle)) continue;
            yield return e;
        }
    }

    private static bool Matches(JournalEntry e, string needle) =>
        new[] { e.Action, e.Detail, e.User, e.TargetId }.Any(t => Text.Plain(t).Contains(needle))
        || (e.Changes?.Any(c => Text.Plain(c.Field).Contains(needle) || Text.Plain(c.Before).Contains(needle) || Text.Plain(c.After).Contains(needle)) ?? false);
}
