using System.Globalization;

namespace AeTool.Core;

/// <summary>Écart entre une donnée et le métamodèle. <see cref="Id"/> désigne un objet ou une relation, <see cref="Key"/> la propriété ou le type en cause.</summary>
public sealed record ConformityIssue(string Code, string Severity, string Message, string Id, string Kind, string? Key = null);

/// <summary>Contrôles de conformité des données au métamodèle : ils signalent, ils ne bloquent jamais l'enregistrement.</summary>
public static class Conformity
{
    public static List<ConformityIssue> Check(Repository repo)
    {
        var m = repo.Metamodel;
        var issues = new List<ConformityIssue>();
        var required = m.PropertyDefinitions.Where(p => p.Required).ToList();
        foreach (var o in repo.ActiveObjects)
        {
            var type = m.Type(o.TypeKey);
            if (type is null) issues.Add(new("type-inconnu", "erreur", $"« {o.Name} » a un type inconnu du métamodèle : {o.TypeKey}.", o.Id, "objet", o.TypeKey));
            else if (type.Archived) issues.Add(new("type-archive", "avertissement", $"« {o.Name} » a un type archivé : {type.Label}.", o.Id, "objet", o.TypeKey));
            foreach (var p in MissingRequired(m, o, required))
                issues.Add(new("propriete-obligatoire-absente", "avertissement", $"« {o.Name} » : la propriété obligatoire « {p.Label ?? p.Key} » est absente.", o.Id, "objet", p.Key));
            AddValueIssues(m, o.Properties, o.Id, "objet", o.Name, issues);
        }
        foreach (var r in repo.ActiveRelationships)
        {
            if (m.RelType(r.TypeKey) is null)
                issues.Add(new("type-inconnu", "erreur", $"Une relation a un type inconnu du métamodèle : {r.TypeKey}.", r.Id, "relation", r.TypeKey));
            else if (repo.Objects.TryGetValue(r.SourceId, out var s) && repo.Objects.TryGetValue(r.TargetId, out var t)
                     && m.CheckRelationship(r.TypeKey, s.TypeKey, t.TypeKey) is string problem)
                issues.Add(new("relation-non-autorisee", "avertissement", $"{problem} ({s.Name} → {t.Name}).", r.Id, "relation", r.TypeKey));
            AddValueIssues(m, r.Properties, r.Id, "relation", r.Name ?? r.TypeKey, issues);
        }
        return issues;
    }

    /// <summary>Propriétés obligatoires, applicables au type de l'objet, absentes ou vides.</summary>
    public static List<PropertyDefinition> MissingRequired(Metamodel m, ArchObject o, IEnumerable<PropertyDefinition>? required = null) =>
        (required ?? m.PropertyDefinitions.Where(p => p.Required))
            .Where(p => p.AppliesToType(o.TypeKey) && string.IsNullOrWhiteSpace(o.Get(p.Key))).ToList();

    private static void AddValueIssues(Metamodel m, IEnumerable<PropertyValue> values, string id, string kind, string owner, List<ConformityIssue> issues)
    {
        foreach (var v in values)
            if (m.Property(v.Key) is { } def && ValueProblem(def, v.Value) is { } problem)
                issues.Add(new(problem.Code, "avertissement", $"« {owner} » : {problem.Message}", id, kind, v.Key));
    }

    /// <summary>Problème d'une valeur au regard de la définition de sa propriété ; null si elle est conforme ou vide.</summary>
    public static (string Code, string Message)? ValueProblem(PropertyDefinition def, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var label = def.Label ?? def.Key;
        var v = value.Trim();
        switch (def.DataType)
        {
            case PropertyTypes.List when def.AllowedValues.Count > 0 && !def.AllowedValues.Contains(v):
                return ("valeur-hors-liste", $"la valeur « {v} » de « {label} » n'est pas dans la liste ({string.Join(", ", def.AllowedValues)}).");
            case PropertyTypes.Number when !double.TryParse(v.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out _):
                return ("valeur-nombre-invalide", $"la valeur « {v} » de « {label} » n'est pas un nombre.");
            case PropertyTypes.Date when !DateTime.TryParseExact(v, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _):
                return ("valeur-date-invalide", $"la valeur « {v} » de « {label} » n'est pas une date AAAA-MM-JJ.");
            case PropertyTypes.Boolean when !v.Equals("oui", StringComparison.OrdinalIgnoreCase) && !v.Equals("non", StringComparison.OrdinalIgnoreCase):
                return ("valeur-booleen-invalide", $"la valeur « {v} » de « {label} » doit être « oui » ou « non ».");
            default:
                return null;
        }
    }
}
