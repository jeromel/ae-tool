namespace AeTool.Core;

// Données déduites du graphe : dépendances, analyse d'impact, matrices, impacts et adhérences projet, qualité.

public sealed record Reached(string Id, string Name, string TypeKey, string Layer, int Depth, string Via, string Direction);
public sealed record GraphNode(string Id, string Name, string TypeKey, string Layer, int Depth);
public sealed record GraphEdge(string Id, string TypeKey, string Source, string Target, string? Name);
public sealed record Graph(List<GraphNode> Nodes, List<GraphEdge> Edges);
public sealed record MatrixHeader(string Id, string Name);
public sealed record Matrix(List<MatrixHeader> Rows, List<MatrixHeader> Columns, List<string[]> Cells);
public sealed record Adherence(string ProjectA, string ProjectB, List<string> SharedObjectIds, List<string[]> LinkedPairs);
public sealed record TypeQuality(string TypeKey, string Label, string Layer, int Count, int Documented, int WithProperties, int Orphans, int NotInDiagram);

public sealed class Analyzer
{
    private readonly Repository _repo;
    private readonly ILookup<string, Relationship> _bySource;
    private readonly ILookup<string, Relationship> _byTarget;

    public Analyzer(Repository repo)
    {
        _repo = repo;
        // Une relation dont une extrémité est une autre relation n'entre pas dans le graphe objet-objet.
        var usable = repo.ActiveRelationships.Where(r => IsActiveObject(r.SourceId) && IsActiveObject(r.TargetId)).ToList();
        _bySource = usable.ToLookup(r => r.SourceId);
        _byTarget = usable.ToLookup(r => r.TargetId);
    }

    private bool IsActiveObject(string id) => _repo.Objects.TryGetValue(id, out var o) && !o.Archived;

    public IEnumerable<Relationship> Outgoing(string id) => _bySource[id];
    public IEnumerable<Relationship> Incoming(string id) => _byTarget[id];

    /// <summary>
    /// Analyse d'impact. « aval » suit le sens des relations (ce que l'objet sert, réalise, alimente) ;
    /// « amont » le remonte (ce dont l'objet dépend).
    /// </summary>
    public List<Reached> Impact(string id, int depth, string direction)
    {
        var result = new List<Reached>();
        if (direction is "aval" or "tous") result.AddRange(Walk(id, depth, downstream: true));
        if (direction is "amont" or "tous") result.AddRange(Walk(id, depth, downstream: false));
        return result;
    }

    private IEnumerable<Reached> Walk(string start, int depth, bool downstream)
    {
        var seen = new HashSet<string> { start };
        var frontier = new List<string> { start };
        for (var d = 1; d <= depth && frontier.Count > 0; d++)
        {
            var next = new List<string>();
            foreach (var current in frontier)
            {
                foreach (var rel in downstream ? _bySource[current] : _byTarget[current])
                {
                    var other = downstream ? rel.TargetId : rel.SourceId;
                    if (!seen.Add(other)) continue;
                    var o = _repo.Objects[other];
                    next.Add(other);
                    yield return new Reached(o.Id, o.Name, o.TypeKey, _repo.Metamodel.LayerOf(o.TypeKey), d,
                        _repo.Metamodel.RelType(rel.TypeKey)?.Label ?? rel.TypeKey, downstream ? "aval" : "amont");
                }
            }
            frontier = next;
        }
    }

    /// <summary>Voisinage d'un objet jusqu'à une profondeur donnée, relations dans les deux sens.</summary>
    public Graph Neighborhood(string id, int depth)
    {
        var depths = new Dictionary<string, int> { [id] = 0 };
        var edges = new Dictionary<string, GraphEdge>();
        var frontier = new List<string> { id };
        for (var d = 1; d <= depth && frontier.Count > 0; d++)
        {
            var next = new List<string>();
            foreach (var current in frontier)
            {
                foreach (var rel in _bySource[current].Concat(_byTarget[current]))
                {
                    edges[rel.Id] = new GraphEdge(rel.Id, rel.TypeKey, rel.SourceId, rel.TargetId, rel.Name);
                    var other = rel.SourceId == current ? rel.TargetId : rel.SourceId;
                    if (depths.TryAdd(other, d)) next.Add(other);
                }
            }
            frontier = next;
        }
        var nodes = depths.Select(kv =>
        {
            var o = _repo.Objects[kv.Key];
            return new GraphNode(o.Id, o.Name, o.TypeKey, _repo.Metamodel.LayerOf(o.TypeKey), kv.Value);
        }).ToList();
        return new Graph(nodes, edges.Values.Where(e => depths.ContainsKey(e.Source) && depths.ContainsKey(e.Target)).ToList());
    }

    /// <summary>
    /// Matrice croisée entre deux types. Une cellule porte « R » si une relation directe lie les deux objets,
    /// « V » si l'un est imbriqué dans l'autre dans au moins une vue (lecture type POS), « RV » pour les deux.
    /// </summary>
    public Matrix CrossMatrix(string rowType, string colType, bool includeNesting)
    {
        var rows = _repo.ActiveObjects.Where(o => o.TypeKey == rowType).OrderBy(o => o.Name).ToList();
        var cols = _repo.ActiveObjects.Where(o => o.TypeKey == colType).OrderBy(o => o.Name).ToList();
        var colIndex = cols.Select((c, i) => (c.Id, i)).ToDictionary(x => x.Id, x => x.i);
        var rowIndex = rows.Select((r, i) => (r.Id, i)).ToDictionary(x => x.Id, x => x.i);
        var cells = rows.Select(_ => new string[cols.Count]).ToList();
        for (var i = 0; i < cells.Count; i++) Array.Fill(cells[i], "");

        void Mark(string a, string b, string mark)
        {
            if (rowIndex.TryGetValue(a, out var r) && colIndex.TryGetValue(b, out var c) && !cells[r][c].Contains(mark))
                cells[r][c] += mark;
        }
        foreach (var rel in _repo.ActiveRelationships)
        {
            Mark(rel.SourceId, rel.TargetId, "R");
            Mark(rel.TargetId, rel.SourceId, "R");
        }
        if (includeNesting)
            foreach (var (ancestor, descendant) in NestingPairs())
            {
                Mark(ancestor, descendant, "V");
                Mark(descendant, ancestor, "V");
            }

        // Ne garder que les lignes et colonnes utiles : une matrice 186 × 437 presque vide est illisible.
        var keptRows = Enumerable.Range(0, rows.Count).Where(r => cells[r].Any(x => x != "")).ToList();
        var keptCols = Enumerable.Range(0, cols.Count).Where(c => cells.Any(row => row[c] != "")).ToList();
        return new Matrix(
            keptRows.Select(r => new MatrixHeader(rows[r].Id, rows[r].Name)).ToList(),
            keptCols.Select(c => new MatrixHeader(cols[c].Id, cols[c].Name)).ToList(),
            keptRows.Select(r => keptCols.Select(c => cells[r][c]).ToArray()).ToList());
    }

    /// <summary>
    /// Couples (contenant, contenu) lus dans les vues. Archi n'imbrique pas toujours les nœuds dans le XML :
    /// on retient donc l'inclusion géométrique, qui couvre aussi les nœuds imbriqués.
    /// </summary>
    private IEnumerable<(string Ancestor, string Descendant)> NestingPairs()
    {
        var pairs = new HashSet<(string, string)>();
        foreach (var d in _repo.ActiveDiagrams)
        {
            var nodes = d.AllNodes().Where(n => n.ElementId is not null).ToList();
            foreach (var outer in nodes)
            foreach (var inner in nodes)
            {
                if (ReferenceEquals(outer, inner) || outer.ElementId == inner.ElementId) continue;
                var inside = inner.X >= outer.X && inner.Y >= outer.Y
                             && inner.X + inner.W <= outer.X + outer.W && inner.Y + inner.H <= outer.Y + outer.H
                             && (long)inner.W * inner.H < (long)outer.W * outer.H;
                if (inside) pairs.Add((outer.ElementId!, inner.ElementId!));
            }
        }
        return pairs;
    }

    // ---- Projets ----

    public IEnumerable<ArchObject> Projects() =>
        _repo.ActiveObjects.Where(o => o.TypeKey == _repo.Metamodel.Settings.ProjectTypeKey).OrderBy(o => o.Name);

    /// <summary>Diagrammes rangés dans le dossier « Projets / &lt;nom du projet&gt; », sous-dossiers compris.</summary>
    public List<string> ProjectDiagramIds(ArchObject project)
    {
        var result = new List<string>();
        void Collect(Folder f)
        {
            result.AddRange(f.Entries.Where(e => _repo.Diagrams.ContainsKey(e)));
            foreach (var c in f.Children) Collect(c);
        }
        void Search(Folder f, bool insideProjects)
        {
            if (insideProjects && f.Label == project.Name) { Collect(f); return; }
            foreach (var c in f.Children) Search(c, insideProjects || f.Label == _repo.Metamodel.Settings.ProjectFolderLabel);
        }
        foreach (var root in _repo.Folders) Search(root, false);
        return result.Distinct().ToList();
    }

    /// <summary>
    /// Recalcule les impacts déduits des vues projet. Les natures déjà qualifiées et les impacts saisis sont conservés ;
    /// un impact déduit dont l'objet a quitté les vues du projet est retiré s'il n'a jamais été qualifié.
    /// </summary>
    public int RefreshDeducedImpacts()
    {
        var excluded = _repo.Metamodel.Settings.ImpactExcludedTypeKeys.ToHashSet();
        var existing = _repo.Impacts.ToDictionary(i => (i.ProjectId, i.ObjectId));
        var seen = new HashSet<(string, string)>();
        var created = 0;
        foreach (var project in Projects())
        {
            foreach (var diagramId in ProjectDiagramIds(project))
            {
                foreach (var node in _repo.Diagrams[diagramId].AllNodes())
                {
                    if (node.ElementId is null || !_repo.Objects.TryGetValue(node.ElementId, out var o)) continue;
                    if (o.Archived || o.Id == project.Id || excluded.Contains(o.TypeKey)) continue;
                    var key = (project.Id, o.Id);
                    seen.Add(key);
                    if (!existing.TryGetValue(key, out var impact))
                    {
                        impact = new ProjectImpact { ProjectId = project.Id, ObjectId = o.Id, Origin = "vue" };
                        _repo.Impacts.Add(impact);
                        existing[key] = impact;
                        created++;
                    }
                    if (!impact.DiagramIds.Contains(diagramId)) impact.DiagramIds.Add(diagramId);
                }
            }
        }
        _repo.Impacts.RemoveAll(i => i.Origin == "vue" && i.Nature == ImpactNature.ToQualify
            && !seen.Contains((i.ProjectId, i.ObjectId)));
        return created;
    }

    public IEnumerable<ProjectImpact> ImpactsOf(string projectId) =>
        _repo.Impacts.Where(i => i.ProjectId == projectId && i.Nature != "Hors périmètre"
            && _repo.Objects.TryGetValue(i.ObjectId, out var o) && !o.Archived);

    /// <summary>Deux projets sont adhérents s'ils touchent le même objet ou deux objets directement reliés.</summary>
    public List<Adherence> Adherences()
    {
        var projects = Projects().ToList();
        var touched = projects.ToDictionary(p => p.Id, p => ImpactsOf(p.Id).Select(i => i.ObjectId).ToHashSet());
        var result = new List<Adherence>();
        for (var i = 0; i < projects.Count; i++)
        for (var j = i + 1; j < projects.Count; j++)
        {
            var a = touched[projects[i].Id];
            var b = touched[projects[j].Id];
            var shared = a.Intersect(b).ToList();
            var linked = new List<string[]>();
            foreach (var objectId in a)
                foreach (var rel in _bySource[objectId].Concat(_byTarget[objectId]))
                {
                    var other = rel.SourceId == objectId ? rel.TargetId : rel.SourceId;
                    if (b.Contains(other) && !shared.Contains(objectId) && !shared.Contains(other))
                        linked.Add(new[] { objectId, other });
                }
            if (shared.Count > 0 || linked.Count > 0)
                result.Add(new Adherence(projects[i].Id, projects[j].Id, shared, linked));
        }
        return result;
    }

    // ---- Qualité ----

    public List<TypeQuality> Quality()
    {
        var inDiagram = _repo.ActiveDiagrams.SelectMany(d => d.AllNodes()).Where(n => n.ElementId is not null)
            .Select(n => n.ElementId!).ToHashSet();
        return _repo.ActiveObjects.GroupBy(o => o.TypeKey).Select(g =>
        {
            var type = _repo.Metamodel.Type(g.Key);
            return new TypeQuality(g.Key, type?.Label ?? g.Key, type?.LayerKey ?? "autre", g.Count(),
                g.Count(o => !string.IsNullOrWhiteSpace(o.Documentation)),
                g.Count(o => o.Properties.Count > 0),
                g.Count(o => !_bySource[o.Id].Any() && !_byTarget[o.Id].Any()),
                g.Count(o => !inDiagram.Contains(o.Id)));
        }).OrderByDescending(q => q.Count).ToList();
    }

    public bool IsRetired(ArchObject o)
    {
        var grid = _repo.Metamodel.Scoring;
        var state = o.Get(grid.StatePropertyKey);
        return state is not null && grid.RetiredStates.Contains(state);
    }

    /// <summary>Chemins de dossiers contenant l'identifiant donné, par exemple « Views / Entreprise / POS ».</summary>
    public List<string> FolderPaths(string id)
    {
        var result = new List<string>();
        void Visit(Folder f, string path)
        {
            var here = path == "" ? f.Label : $"{path} / {f.Label}";
            if (f.Entries.Contains(id)) result.Add(here);
            foreach (var c in f.Children) Visit(c, here);
        }
        foreach (var root in _repo.Folders) Visit(root, "");
        return result;
    }
}
