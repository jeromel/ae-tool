namespace AeTool.Core;

/// <summary>
/// Périmètre de lecture d'un compte (décision 012) : « tout » ou « métier ». Le périmètre « métier » retire l'infrastructure :
/// les objets de la couche « technique » et ceux dont le type porte le rôle « infrastructure », les relations qui y
/// aboutissent, et leurs formes dans les vues. Le filtre est une projection du référentiel, appliquée à la lecture comme
/// l'état projeté d'un scénario : tous les espaces, vues et analyses la reçoivent sans code propre.
/// </summary>
public static class ReadScope
{
    public const string All = "tout";
    public const string Business = "metier";
    public const string TechnicalLayer = "technique";

    public static bool IsKnown(string? scope) => scope is All or Business;

    /// <summary>Vrai si les objets de ce type sont de l'infrastructure.</summary>
    public static bool IsInfrastructureType(Metamodel metamodel, string typeKey) =>
        metamodel.Type(typeKey) is { } t && (t.LayerKey == TechnicalLayer || t.Roles.Contains(Roles.Infrastructure));

    /// <summary>
    /// Référentiel vu au périmètre « métier ». Les objets gardés sont partagés avec la source (lecture seule) ; les
    /// collections et les vues sont des copies filtrées. Les scénarios, le journal et le comptage des postes n'y sont pas :
    /// les routes qui en ont besoin sont fermées à ce périmètre.
    /// </summary>
    public static Repository Project(Repository source)
    {
        var removedObjects = source.Objects.Values.Where(o => IsInfrastructureType(source.Metamodel, o.TypeKey)).Select(o => o.Id).ToHashSet();
        // Une relation peut viser une autre relation (ArchiMate le permet) : on retire jusqu'à ce que plus rien ne change.
        var removedRelations = new HashSet<string>();
        bool changed;
        do
        {
            changed = false;
            foreach (var r in source.Relationships.Values)
                if (!removedRelations.Contains(r.Id) && (Gone(r.SourceId) || Gone(r.TargetId))) { removedRelations.Add(r.Id); changed = true; }
        } while (changed);
        bool Gone(string id) => removedObjects.Contains(id) || removedRelations.Contains(id);
        // Cadre d'une vue : forme sans objet, regroupement ou lieu ArchiMate, fond de carte. Vidé par le filtre, il disparaît.
        bool IsFrame(string? elementId) => elementId is null || source.Objects.TryGetValue(elementId, out var o)
            && (o.ArchimateType is "Grouping" or "Location" || source.Metamodel.Type(o.TypeKey)?.Roles.Contains(Roles.Backdrop) == true);

        var trim = new ScopeTrim();
        foreach (var r in source.Relationships.Values.Where(r => removedRelations.Contains(r.Id)))
            foreach (var end in new[] { r.SourceId, r.TargetId }.Where(e => !Gone(e))) trim.Objects.Add(end);

        var diagrams = new Dictionary<string, Diagram>();
        foreach (var d in source.Diagrams.Values)
        {
            var kept = Filter(d, Gone, IsFrame, out var lost);
            if (kept is null) continue; // vue entièrement technique : elle ne se liste pas
            if (lost) trim.Diagrams.Add(d.Id);
            diagrams[d.Id] = kept;
        }
        // Une forme qui renvoie vers une vue retirée garde sa place, sans le renvoi.
        foreach (var d in diagrams.Values)
            foreach (var n in d.AllNodes().Where(n => n.ViewRefId is not null && !diagrams.ContainsKey(n.ViewRefId))) n.ViewRefId = null;

        var keep = (string id) => !Gone(id) && (!source.Diagrams.ContainsKey(id) || diagrams.ContainsKey(id));
        return new Repository
        {
            SchemaVersion = source.SchemaVersion, ModelId = source.ModelId, ModelName = source.ModelName, Language = source.Language,
            SchemaLocation = source.SchemaLocation, Metamodel = source.Metamodel,
            Objects = source.Objects.Where(kv => !removedObjects.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value),
            Relationships = source.Relationships.Where(kv => !removedRelations.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value),
            Diagrams = diagrams,
            Folders = source.Folders.Select(f => Filter(f, keep)).ToList(),
            Impacts = source.Impacts.Where(i => keep(i.ObjectId) && keep(i.ProjectId))
                .Select(i => new ProjectImpact { ProjectId = i.ProjectId, ObjectId = i.ObjectId, Nature = i.Nature, Origin = i.Origin,
                    DiagramIds = i.DiagramIds.Where(keep).ToList(), Comment = i.Comment }).ToList(),
            Assessments = source.Assessments.Where(kv => keep(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value),
            DataAttributes = source.DataAttributes.Where(kv => keep(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value),
            CatalogueLists = source.CatalogueLists,
            Projection = source.Projection?.Where(kv => keep(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value),
            Replacements = source.Replacements?.Where(kv => keep(kv.Key) && keep(kv.Value)).ToDictionary(kv => kv.Key, kv => kv.Value),
            Scope = trim,
        };
    }

    /// <summary>Copie d'une vue sans les formes d'infrastructure ni les traits qui y aboutissent ; null si rien n'en reste.</summary>
    private static Diagram? Filter(Diagram d, Func<string, bool> gone, Func<string?, bool> isFrame, out bool lost)
    {
        var removedNodes = new HashSet<string>();
        var anyLost = false;
        List<DiagramNode> Nodes(IEnumerable<DiagramNode> nodes)
        {
            var list = new List<DiagramNode>();
            foreach (var n in nodes)
            {
                if (n.ElementId is { } e && gone(e)) { Remove(n); anyLost = true; continue; }
                var children = Nodes(n.Children);
                // Une zone qui ne contient plus rien après le filtre disparaît : un cadre « Socle » vide trahit ce qu'on retire.
                if (n.Children.Count > 0 && children.Count == 0 && (n.Kind == "Container" || isFrame(n.ElementId))) { Remove(n); continue; }
                list.Add(new DiagramNode
                {
                    Shape = n.Shape, Id = n.Id, Kind = n.Kind, ElementId = n.ElementId, Label = n.Label, ViewRefId = n.ViewRefId,
                    X = n.X, Y = n.Y, W = n.W, H = n.H, Style = n.Style, Children = children,
                });
            }
            return list;
        }
        void Remove(DiagramNode n) { removedNodes.Add(n.Id); foreach (var c in n.Children) Remove(c); }

        var nodes = Nodes(d.Nodes);
        var connections = d.Connections.Where(c => !removedNodes.Contains(c.SourceNodeId) && !removedNodes.Contains(c.TargetNodeId)
                                                   && (c.RelationshipId is null || !gone(c.RelationshipId))).ToList();
        if (connections.Count < d.Connections.Count) anyLost = true;
        lost = anyLost;
        var hadContent = d.Nodes.Count > 0;
        if (hadContent && nodes.Count == 0) return null;
        return new Diagram
        {
            Id = d.Id, Name = d.Name, Documentation = d.Documentation, Viewpoint = d.Viewpoint, Properties = d.Properties,
            Nodes = nodes, Connections = connections, Source = d.Source, Archived = d.Archived,
            Layout = d.Layout?.Where(kv => !removedNodes.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value),
            LayoutBy = d.LayoutBy, LayoutAt = d.LayoutAt,
        };
    }

    private static Folder Filter(Folder f, Func<string, bool> keep) => new()
    {
        Label = f.Label, Documentation = f.Documentation, Source = f.Source,
        Entries = f.Entries.Where(keep).ToList(),
        Children = f.Children.Select(c => Filter(c, keep)).ToList(),
    };
}

/// <summary>
/// Ce que le périmètre a retiré, sans rien en dire : seulement quelles vues et quels objets ont perdu quelque chose, pour
/// que l'écran l'annonce (« des éléments d'infrastructure ne sont pas affichés »). Jamais de nombre ni d'identifiant retiré.
/// </summary>
public sealed class ScopeTrim
{
    /// <summary>Vues qui ont perdu au moins une forme ou un trait.</summary>
    public HashSet<string> Diagrams { get; } = new();
    /// <summary>Objets gardés qui ont perdu au moins une relation.</summary>
    public HashSet<string> Objects { get; } = new();
}
