namespace AeTool.Core;

/// <summary>
/// Enregistre un parcours comme vue du référentiel : une bande par couche (données, applications, étapes techniques, hébergement,
/// profils de poste), avec les relations qui existent entre les éléments montrés. La vue s'ouvre dans l'outil et s'exporte vers Archi.
/// Elle est régénérée à chaque enregistrement ; une disposition retouchée à la main est gardée pour les éléments qui restent.
/// </summary>
public static class LineageView
{
    public const string SourceTag = "parcours";
    public const string FolderLabel = "Parcours des données";
    private const int W = 180, H = 55, GapX = 30, GapY = 40, Margin = 24, PerRow = 6;

    public static string IdFor(string focusId) => "parcours-" + Text.Slug(focusId);

    public static Diagram Save(Repository repo, Lineage l, string user)
    {
        var id = IdFor(l.Focus.Id);
        var diagram = new Diagram
        {
            Id = id, Name = $"Parcours : {l.Focus.Name}", Source = SourceTag, Viewpoint = null,
            Documentation = $"Vue générée depuis le parcours de « {l.Focus.Name} » ({l.Focus.TypeLabel}). Régénérée à chaque enregistrement.",
        };
        var placed = new Dictionary<string, DiagramNode>();
        var y = Margin;
        void Band(IEnumerable<string> ids)
        {
            var list = ids.Where(i => repo.Objects.ContainsKey(i) && !placed.ContainsKey(i)).Distinct().ToList();
            if (list.Count == 0) return;
            for (var i = 0; i < list.Count; i++)
            {
                if (i > 0 && i % PerRow == 0) y += H + GapY / 2;
                var node = new DiagramNode { Id = $"{id}-n-{placed.Count}", Kind = "Element", ElementId = list[i], X = Margin + (i % PerRow) * (W + GapX), Y = y, W = W, H = H };
                diagram.Nodes.Add(node);
                placed[list[i]] = node;
            }
            y += H + GapY;
        }
        Band(l.Data.Select(d => d.Id));
        Band(l.Applications.Select(a => a.Id));
        Band(l.Technical.Select(t => t.Id));
        Band(l.Hosts.Select(h => h.Id));
        Band(l.Profiles.Select(p => p.Id));

        // Relations entre éléments montrés ; une relation dont une extrémité est une autre relation n'a pas de trait ici.
        var n = 0;
        foreach (var r in repo.ActiveRelationships)
            if (placed.TryGetValue(r.SourceId, out var s) && placed.TryGetValue(r.TargetId, out var t) && s != t)
                diagram.Connections.Add(new DiagramConnection { Id = $"{id}-c-{n++}", RelationshipId = r.Id, SourceNodeId = s.Id, TargetNodeId = t.Id });

        if (repo.Diagrams.TryGetValue(id, out var previous)) diagram.KeepLayoutFrom(previous);
        repo.Diagrams[id] = diagram;
        var folder = repo.Folders.FirstOrDefault(f => f.Source == SourceTag);
        if (folder is null) { folder = new Folder { Label = FolderLabel, Source = SourceTag }; repo.Folders.Add(folder); }
        if (!folder.Entries.Contains(id)) folder.Entries.Add(id);
        repo.Log(user, previous is null ? "Vue créée depuis un parcours" : "Vue régénérée depuis un parcours", id, diagram.Name);
        return diagram;
    }
}
