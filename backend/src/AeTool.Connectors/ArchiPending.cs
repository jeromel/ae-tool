using AeTool.Core;

namespace AeTool.Connectors;

/// <summary>
/// Ce qui n'est pas encore dans Archi (export B, maquette de Camille « Pas encore dans Archi », décisions 014 et 015).
/// Un objet ou une relation revenu d'Archi passe en <c>Source = archi</c> : tout le reste n'y est pas encore.
/// <list type="bullet">
/// <item>Objets : actifs, hors Archi, au statut « Validé ». Les objets proposés ou brouillons attendent la validation.</item>
/// <item>Relations : actives, hors Archi, dont chaque extrémité part aussi ou est déjà dans Archi. Une relation qui touche un
/// objet non validé attend avec lui (une relation n'a pas de statut de validation).</item>
/// <item>Objets et relations d'Archi cités par une relation qui part : écrits avec l'identifiant, le type et le nom
/// seulement, pour que la fusion d'Archi ne touche pas leurs valeurs (essai du PO du 27/11).</item>
/// </list>
/// Les vues ne partent pas : Archi reste maître de ses vues.
/// </summary>
public sealed class ArchiPending
{
    public const string ImportAction = "Import Archi";

    /// <summary>Date du dernier import du modèle Archi, lue dans le journal ; null s'il n'y en a jamais eu.</summary>
    public DateTime? LastImport { get; private init; }
    /// <summary>Aucun modèle Archi importé : tout le référentiel est nouveau pour Archi, l'export complet s'applique.</summary>
    public bool NeverImported { get; private init; }
    public List<ArchObject> Objects { get; } = new();
    public List<Relationship> Relations { get; } = new();
    /// <summary>Impacts qualifiés dont l'association n'est pas encore dans Archi, entre deux objets du fichier.</summary>
    public List<ProjectImpact> Impacts { get; } = new();
    /// <summary>Objets d'Archi cités par une relation qui part : identifiant, type et nom seulement.</summary>
    public List<ArchObject> CitedObjects { get; } = new();
    /// <summary>Relations d'Archi visées par une relation qui part (relation vers une relation) : identifiant, extrémités, type.</summary>
    public List<Relationship> CitedRelations { get; } = new();
    public int ExcludedProposed { get; private set; }
    public int ExcludedDrafts { get; private set; }
    /// <summary>Relations qui attendent, parce qu'elles touchent un objet non validé (ou une relation qui attend).</summary>
    public int ExcludedRelations { get; private set; }

    public int RelationCount => Relations.Count + Impacts.Count;
    public bool IsEmpty => Objects.Count == 0 && RelationCount == 0;

    public static string ImpactId(ProjectImpact i) => $"id-impact-{Short(i.ProjectId)}-{Short(i.ObjectId)}";
    private static string Short(string id) => id.StartsWith("id-") ? id[3..] : id;

    public static ArchiPending Of(Repository repo)
    {
        var archi = ArchimateExchange.SourceTag;
        var last = repo.Journal.LastOrDefault(e => e.Action == ImportAction)?.At;
        var p = new ArchiPending
        {
            LastImport = last,
            NeverImported = last is null && !repo.Objects.Values.Any(o => o.Source == archi),
        };
        if (p.NeverImported) return p;

        foreach (var o in repo.ActiveObjects.Where(o => o.Source != archi).OrderBy(o => o.Name, StringComparer.CurrentCulture))
        {
            if (o.ValidationStatus == Validation.Validated) p.Objects.Add(o);
            else if (o.ValidationStatus == Validation.Proposed) p.ExcludedProposed++;
            else p.ExcludedDrafts++;
        }

        // Extrémités admises : ce qui part, ou ce qui est déjà dans Archi. Une relation peut viser une relation : on itère
        // jusqu'à ce que plus rien ne bouge, pour qu'une relation vers une relation qui attend attende aussi.
        var sent = p.Objects.Select(o => o.Id).ToHashSet();
        bool InArchi(string id) =>
            repo.Objects.TryGetValue(id, out var o) ? !o.Archived && o.Source == archi
            : repo.Relationships.TryGetValue(id, out var r) && !r.Archived && r.Source == archi;
        var candidates = repo.ActiveRelationships.Where(r => r.Source != archi).ToList();
        var accepted = new HashSet<string>();
        bool changed;
        do
        {
            changed = false;
            foreach (var r in candidates.Where(r => !accepted.Contains(r.Id)))
            {
                bool Ok(string end) => sent.Contains(end) || accepted.Contains(end) || InArchi(end);
                if (Ok(r.SourceId) && Ok(r.TargetId)) { accepted.Add(r.Id); changed = true; }
            }
        } while (changed);
        p.Relations.AddRange(candidates.Where(r => accepted.Contains(r.Id)));
        p.ExcludedRelations = candidates.Count - p.Relations.Count;

        // Objets et relations d'Archi cités. Une relation d'Archi citée apporte ses propres extrémités, citées à leur tour.
        var citedObjects = new HashSet<string>();
        var citedRelations = new HashSet<string>();
        void Cite(string id)
        {
            if (sent.Contains(id) || accepted.Contains(id)) return;
            if (repo.Objects.ContainsKey(id)) { citedObjects.Add(id); return; }
            if (citedRelations.Add(id) && repo.Relationships.TryGetValue(id, out var r)) { Cite(r.SourceId); Cite(r.TargetId); }
        }
        foreach (var r in p.Relations) { Cite(r.SourceId); Cite(r.TargetId); }

        // Impacts qualifiés : leur association part tant qu'elle n'est pas revenue d'Archi, si ses deux extrémités sont dans le fichier.
        foreach (var i in repo.Impacts.Where(i => i.Nature != ImpactNature.ToQualify && i.Nature != "Hors périmètre"))
        {
            if (repo.Relationships.ContainsKey(ImpactId(i))) continue;
            bool Present(string id) => sent.Contains(id) || InArchi(id);
            if (!Present(i.ProjectId) || !Present(i.ObjectId)) continue;
            p.Impacts.Add(i);
            Cite(i.ProjectId);
            Cite(i.ObjectId);
        }

        p.CitedObjects.AddRange(citedObjects.Select(id => repo.Objects[id]).OrderBy(o => o.Name, StringComparer.CurrentCulture));
        p.CitedRelations.AddRange(citedRelations.Select(id => repo.Relationships[id]));
        return p;
    }
}
