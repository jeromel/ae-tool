namespace AeTool.Core;

/// <summary>
/// Relation qu'un objet peut porter d'après le métamodèle. <see cref="AnyType"/> est vrai quand le type de relation n'a aucun
/// couple déclaré (aucun contrôle) ; sinon <see cref="ObjectTypes"/> liste les types admis à l'autre extrémité.
/// </summary>
public sealed record AllowedRelation(string TypeKey, string Label, string? ReverseLabel, bool AnyType, IReadOnlyList<string> ObjectTypes);

/// <summary>Lecture des couples autorisés (« TypeSource&gt;TypeCible ») du point de vue d'un objet : ce qu'il peut émettre, ce qu'il peut recevoir.</summary>
public static class RelationRules
{
    /// <summary>Relations que l'objet peut porter en tant que source (l'autre extrémité est la cible).</summary>
    public static List<AllowedRelation> Outgoing(Metamodel m, string objectTypeKey) => For(m, objectTypeKey, outgoing: true);

    /// <summary>Relations que l'objet peut porter en tant que cible (l'autre extrémité est la source).</summary>
    public static List<AllowedRelation> Incoming(Metamodel m, string objectTypeKey) => For(m, objectTypeKey, outgoing: false);

    private static List<AllowedRelation> For(Metamodel m, string objectTypeKey, bool outgoing)
    {
        // Les types d'objets archivés ne sont plus proposés : on n'en crée plus de nouveaux.
        var active = m.ObjectTypes.Where(t => !t.Archived).Select(t => t.Key).ToHashSet();
        var result = new List<AllowedRelation>();
        foreach (var rt in m.RelationshipTypes.Where(t => !t.Archived).OrderBy(t => t.Label, StringComparer.CurrentCultureIgnoreCase))
        {
            if (rt.AllowedPairs.Count == 0)
            {
                result.Add(new AllowedRelation(rt.Key, rt.Label, rt.ReverseLabel, true, Array.Empty<string>()));
                continue;
            }
            var others = rt.AllowedPairs.Select(Split).Where(p => p is not null).Select(p => p!.Value)
                .Where(p => (outgoing ? p.Source : p.Target) == objectTypeKey)
                .Select(p => outgoing ? p.Target : p.Source).Where(active.Contains).Distinct().OrderBy(k => k, StringComparer.Ordinal).ToList();
            if (others.Count > 0) result.Add(new AllowedRelation(rt.Key, rt.Label, rt.ReverseLabel, false, others));
        }
        return result;
    }

    private static (string Source, string Target)? Split(string pair)
    {
        var i = pair.IndexOf('>');
        return i <= 0 || i == pair.Length - 1 ? null : (pair[..i], pair[(i + 1)..]);
    }
}
