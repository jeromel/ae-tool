namespace AeTool.Core;

/// <summary>
/// « Une donnée a un seul maître. » Le nom, la description et les propriétés d'un objet venu d'Archi se modifient dans Archi ;
/// une propriété alimentée par une source externe se modifie dans cette source. L'outil refuse d'y toucher : le prochain
/// import écraserait la modification sans prévenir. L'écran grise ces champs ; cette règle est celle qui fait foi.
/// Le maître se juge objet par objet (décision 014) : une propriété déclarée par Archi n'est maîtrisée par Archi que sur un
/// objet venu d'Archi. Sur un objet créé ici, Archi n'a aucune valeur : l'outil fait foi.
/// </summary>
public static class Mastery
{
    public const string Archi = "archi";

    /// <summary>Sources dont les données se saisissent dans l'outil.</summary>
    private static readonly string[] Editable = { "saisie", "urba" };

    /// <summary>Source qui maîtrise cette propriété de cet objet, ou null si elle se saisit dans l'outil.</summary>
    public static string? MasterOfProperty(Metamodel metamodel, ArchObject o, string key)
    {
        // Propriété déclarée : sa définition dit d'où elle vient. Non déclarée : elle suit l'objet, venu d'Archi ou non.
        var source = metamodel.Property(key) is { } def
            ? (string.IsNullOrEmpty(def.Source) ? "saisie" : def.Source)
            : (o.Source == Archi ? Archi : null);
        if (source == Archi && o.Source != Archi) return null;
        return source is null || Editable.Contains(source) ? null : source;
    }

    /// <summary>
    /// Ce qu'une modification demandée changerait alors que la donnée est maîtrisée ailleurs ; vide s'il n'y a rien à redire.
    /// Un champ absent de la demande (null) n'est pas modifié, et renvoyer une valeur maîtrisée telle quelle est permis.
    /// Omettre une propriété maîtrisée n'est pas une faute : <see cref="Keep"/> la conserve.
    /// </summary>
    public static List<string> Violations(Metamodel metamodel, ArchObject current, string? name, string? documentation, IReadOnlyList<PropertyValue>? properties)
    {
        var problems = new List<string>();
        if (current.Source == Archi)
        {
            if (!string.IsNullOrWhiteSpace(name) && Plain(name) != Plain(current.Name)) problems.Add("Le nom est maîtrisé dans Archi : il se modifie dans Archi.");
            if (documentation is not null && Plain(documentation) != Plain(current.Documentation)) problems.Add("La description est maîtrisée dans Archi : elle se modifie dans Archi.");
        }
        foreach (var wanted in Last(properties))
        {
            if (MasterOfProperty(metamodel, current, wanted.Key) is not { } master) continue;
            // Archi admet plusieurs propriétés de même nom sur un élément : la valeur renvoyée doit être l'une d'elles.
            var existing = current.Properties.Where(p => p.Key == wanted.Key).Select(p => Plain(p.Value)).ToList();
            if (existing.Count == 0 ? Plain(wanted.Value) == "" : existing.Contains(Plain(wanted.Value))) continue;
            var label = metamodel.Property(wanted.Key)?.Label is { Length: > 0 } l ? l : wanted.Key;
            problems.Add($"« {label} » est maîtrisée dans {(master == Archi ? "Archi" : $"la source « {master} »")} : elle ne se modifie pas ici.");
        }
        return problems;
    }

    /// <summary>
    /// Liste de propriétés à enregistrer quand la demande remplace toute la liste : les propriétés maîtrisées ailleurs,
    /// telles qu'elles sont (doublons compris), puis celles de la demande qui se saisissent ici. Une demande ne peut donc
    /// ni effacer par omission, ni réécrire, ce dont l'outil n'est pas le maître. Null si elle ne touche pas aux propriétés.
    /// </summary>
    public static List<PropertyValue>? Keep(Metamodel metamodel, ArchObject current, IReadOnlyList<PropertyValue>? properties)
    {
        if (properties is null) return null;
        var mastered = current.Properties.Where(p => MasterOfProperty(metamodel, current, p.Key) is not null);
        var entered = Last(properties).Where(p => MasterOfProperty(metamodel, current, p.Key) is null);
        return mastered.Concat(entered).ToList();
    }

    /// <summary>Une clé citée deux fois ne compte qu'une fois : la dernière valeur l'emporte.</summary>
    private static List<PropertyValue> Last(IReadOnlyList<PropertyValue>? properties) =>
        (properties ?? Array.Empty<PropertyValue>()).GroupBy(p => p.Key).Select(g => g.Last()).ToList();

    /// <summary>Une valeur absente et une valeur vide se valent, et les espaces de bord ne comptent pas : l'écran les retire.</summary>
    private static string Plain(string? value) => (value ?? "").Trim();
}
