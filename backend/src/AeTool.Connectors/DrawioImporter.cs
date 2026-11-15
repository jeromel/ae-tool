using AeTool.Core;

namespace AeTool.Connectors;

/// <summary>Objet ou relation du référentiel proposé pour une forme ou un lien.</summary>
public sealed record DrawioCandidate(string Id, string Name, string? TypeLabel);

/// <summary>Une forme ou un lien du dessin, et ce que l'import propose d'en faire.</summary>
public sealed class DrawioItem
{
    public const string Link = "lier";
    public const string Create = "créer";
    public const string Drawing = "dessin";

    /// <summary>Clé stable : identifiant de page et identifiant de forme.</summary>
    public string Key { get; set; } = "";
    public string Page { get; set; } = "";
    /// <summary>« objet » ou « relation ».</summary>
    public string Kind { get; set; } = "objet";
    public string Label { get; set; } = "";
    public string? Concept { get; set; }
    /// <summary>Comment le concept a été reconnu : forme, couleur, donnée de la forme, style du lien.</summary>
    public string? ConceptHow { get; set; }
    public string? TypeKey { get; set; }
    public string? TypeLabel { get; set; }
    /// <summary>lier | créer | dessin.</summary>
    public string Proposal { get; set; } = Drawing;
    public string? TargetId { get; set; }
    /// <summary>Pourquoi cette proposition, en clair.</summary>
    public string Reason { get; set; } = "";
    public List<DrawioCandidate> Candidates { get; set; } = new();
    public string? SourceKey { get; set; }
    public string? TargetKey { get; set; }
}

public sealed record DrawioPageSummary(string Id, string Name, int Shapes, int Links);

public sealed class DrawioAnalysis
{
    public string File { get; set; } = "";
    public List<DrawioPageSummary> Pages { get; set; } = new();
    public List<DrawioItem> Items { get; set; } = new();
    public List<Anomaly> Anomalies { get; set; } = new();
}

/// <summary>Choix de l'utilisateur pour une forme ou un lien : lier à un objet, créer (avec un type), ou garder comme dessin.</summary>
public sealed class DrawioDecision
{
    public string Action { get; set; } = DrawioItem.Drawing;
    public string? TargetId { get; set; }
    public string? TypeKey { get; set; }
}

/// <summary>
/// Import d'un dessin draw.io en deux temps, comme le rapprochement des sources externes : l'analyse lit le fichier
/// et propose, sans rien écrire ; l'application reprend l'analyse avec les choix de l'utilisateur.
/// Chaque page devient une vue de l'outil. Les formes rapprochées d'un objet du référentiel en deviennent des
/// occurrences ; les autres restent du dessin. Rien n'est créé au référentiel sans avoir été proposé et accepté.
/// </summary>
public static class DrawioImporter
{
    public const string SourceTag = "drawio";
    public const string FolderLabel = "Schémas draw.io";
    public const string FileProperty = "drawio.file";
    public const string PageProperty = "drawio.page";

    /// <summary>Données de forme reconnues : identifiant d'un objet du référentiel, concept ArchiMate ou type de l'outil.</summary>
    private static readonly string[] IdKeys = { "ae.id", "ae_id", "referentiel.id", "objectId" };
    private static readonly string[] TypeKeys = { "ae.type", "ae_type", "archimate.type", "archimateType", "archimate" };

    // ------------------------------------------------------------------ analyse

    public static DrawioAnalysis Analyse(Stream file, string fileName, Repository repo)
    {
        var pages = DrawioReader.Read(file);
        return Analyse(pages, fileName, repo);
    }

    private static string RefKey(string fileName, string pageId) => $"{SourceTag}:{FileSlug(fileName)}:{pageId}";

    /// <summary>Nom de fichier sans ses extensions de dessin : « si.drawio.svg » donne « si ».</summary>
    private static string FileSlug(string fileName)
    {
        var name = fileName;
        foreach (var ext in new[] { ".svg", ".xml", ".drawio", ".dio" })
            if (name.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) name = name[..^ext.Length];
        return Text.Slug(name);
    }

    private static DrawioAnalysis Analyse(List<DrawioPage> pages, string fileName, Repository repo)
    {
        var meta = repo.Metamodel;
        var transcoder = new Transcoder(meta);
        var result = new DrawioAnalysis { File = fileName };
        var byName = repo.ActiveObjects.GroupBy(o => Text.Key(o.Name)).ToDictionary(g => g.Key, g => g.ToList());

        foreach (var page in pages)
        {
            var cells = page.Cells.ToDictionary(c => c.Id, c => c, StringComparer.Ordinal);
            var shapes = page.Cells.Where(c => IsShape(c, page, cells)).ToList();
            var links = page.Cells.Where(c => c.Edge).ToList();
            result.Pages.Add(new DrawioPageSummary(page.Id, page.Name, shapes.Count, links.Count));
            var refKey = RefKey(fileName, page.Id);
            var items = new Dictionary<string, DrawioItem>();

            foreach (var c in shapes)
            {
                var item = new DrawioItem { Key = page.Id + "/" + c.Id, Page = page.Name, Kind = "objet", Label = Name(c) };
                items[c.Id] = item;
                var (concept, how, explicitType) = ConceptOf(c, meta);
                item.Concept = concept; item.ConceptHow = how;
                if (explicitType is not null) item.TypeKey = explicitType;
                else if (concept is not null) item.TypeKey = transcoder.ElementType(concept, Raw(c)).TypeKey;
                item.TypeLabel = item.TypeKey is null ? null : meta.Type(item.TypeKey)?.Label ?? item.TypeKey;

                // 1. Identifiant explicite porté par la forme. 2. Lien gardé d'un import précédent. 3. Nom.
                var explicitId = IdKeys.Select(k => c.Data.GetValueOrDefault(k)).FirstOrDefault(v => !string.IsNullOrEmpty(v));
                if (explicitId is not null && repo.Objects.TryGetValue(explicitId, out var byId) && !byId.Archived)
                {
                    Propose(item, DrawioItem.Link, byId, $"identifiant « {explicitId} » porté par la forme", meta);
                    continue;
                }
                var remembered = repo.ActiveObjects.FirstOrDefault(o => o.ExternalRefs.TryGetValue(refKey, out var cell) && cell == c.Id);
                if (remembered is not null)
                {
                    Propose(item, DrawioItem.Link, remembered, "lien gardé depuis un import précédent de ce fichier", meta);
                    continue;
                }
                if (item.Label.Length == 0) { item.Reason = "forme sans libellé"; continue; }
                var matches = Matches(item.Label, byName);
                var sameType = item.TypeKey is null ? matches : matches.Where(o => o.TypeKey == item.TypeKey).ToList();
                if (sameType.Count == 1) { Propose(item, DrawioItem.Link, sameType[0], item.TypeKey is null ? "même nom" : "même nom et même type", meta); continue; }
                if (sameType.Count == 0 && matches.Count == 1 && item.TypeKey is null) { Propose(item, DrawioItem.Link, matches[0], "même nom", meta); continue; }
                var candidates = (sameType.Count > 1 ? sameType : matches).ToList();
                item.Candidates = candidates.Select(o => new DrawioCandidate(o.Id, o.Name, meta.Type(o.TypeKey)?.Label)).ToList();
                if (candidates.Count > 1) { item.Reason = $"{candidates.Count} objets portent ce nom : à choisir"; continue; }
                if (item.TypeKey is not null && concept is not null)
                {
                    item.Proposal = DrawioItem.Create;
                    item.Reason = matches.Count == 1 ? $"un objet de ce nom existe mais d'un autre type ({meta.Type(matches[0].TypeKey)?.Label})" : "aucun objet de ce nom au référentiel";
                }
                else item.Reason = item.TypeKey is null ? "forme sans concept ArchiMate et sans objet du même nom" : "aucun objet de ce nom au référentiel";
            }

            foreach (var e in links)
            {
                if (e.Source is null || e.Target is null || !items.TryGetValue(e.Source, out var from) || !items.TryGetValue(e.Target, out var to)) continue;
                var concept = DrawioConcepts.Relation(e);
                var item = new DrawioItem
                {
                    Key = page.Id + "/" + e.Id, Page = page.Name, Kind = "relation", Label = LinkLabel(e, page),
                    Concept = concept?.Concept, ConceptHow = concept?.How, SourceKey = from.Key, TargetKey = to.Key,
                };
                items[e.Id] = item;
                var fromId = from.Proposal == DrawioItem.Link ? from.TargetId : null;
                var toId = to.Proposal == DrawioItem.Link ? to.TargetId : null;
                if (concept is not null)
                {
                    var choice = transcoder.RelationType(concept.Concept, Array.Empty<ArchiProperty>(),
                        fromId is null ? from.TypeKey : repo.Objects[fromId].TypeKey, toId is null ? to.TypeKey : repo.Objects[toId].TypeKey);
                    item.TypeKey = choice.TypeKey;
                    item.TypeLabel = meta.RelType(choice.TypeKey)?.Label ?? choice.TypeKey;
                }
                if (fromId is not null && toId is not null)
                {
                    var existing = repo.ActiveRelationships.Where(r => r.SourceId == fromId && r.TargetId == toId
                        || (concept is null || concept.Concept == "Association") && r.SourceId == toId && r.TargetId == fromId).ToList();
                    var typed = item.TypeKey is null ? existing : existing.Where(r => r.TypeKey == item.TypeKey).ToList();
                    if (typed.Count == 1)
                    {
                        item.Proposal = DrawioItem.Link; item.TargetId = typed[0].Id;
                        item.Reason = item.TypeKey is null ? "seule relation entre ces deux objets" : "relation de même type entre ces deux objets";
                        continue;
                    }
                    item.Candidates = typed.Select(r => new DrawioCandidate(r.Id, r.Name ?? meta.RelType(r.TypeKey)?.Label ?? r.TypeKey, meta.RelType(r.TypeKey)?.Label)).ToList();
                    if (typed.Count > 1) { item.Reason = $"{typed.Count} relations possibles : à choisir"; continue; }
                }
                var endsKnown = from.Proposal != DrawioItem.Drawing && to.Proposal != DrawioItem.Drawing;
                var worthCreating = concept is not null && (concept.Concept != "Association" || from.Concept is not null && to.Concept is not null);
                if (endsKnown && worthCreating && item.TypeKey is not null)
                {
                    item.Proposal = DrawioItem.Create; item.Reason = "relation absente du référentiel";
                }
                else item.Reason = !endsKnown ? "une extrémité reste du dessin" : concept is null ? "lien sans concept ArchiMate (flèche ordinaire)" : "lien ordinaire entre deux formes sans concept";
            }
            result.Items.AddRange(items.Values);
            foreach (var e in links.Where(e => e.Source is null || e.Target is null))
                result.Anomalies.Add(new Anomaly("avertissement", "lien-flottant", $"Page « {page.Name} » : un lien n'est pas accroché à deux formes ; il reste du dessin.", page.Id + "/" + e.Id));
        }
        foreach (var item in result.Items.Where(i => i.ConceptHow == DrawioConcepts.Guessed))
            result.Anomalies.Add(new Anomaly("avertissement", "couche-supposee",
                $"« {item.Label} » : la couleur de la forme ne dit pas sa couche ; {item.Concept} est supposé. Vérifiez le type proposé.", item.Key));
        return result;
    }

    private static void Propose(DrawioItem item, string action, ArchObject o, string reason, Metamodel meta)
    {
        item.Proposal = action; item.TargetId = o.Id; item.Reason = reason;
        item.Candidates = new List<DrawioCandidate> { new(o.Id, o.Name, meta.Type(o.TypeKey)?.Label) };
    }

    private static List<ArchObject> Matches(string label, Dictionary<string, List<ArchObject>> byName)
    {
        if (byName.TryGetValue(Text.Key(label), out var list)) return list;
        // Un libellé sur plusieurs lignes porte souvent le nom sur la première (« ONDINE / Facturation »).
        var first = label.Split('\n')[0];
        return first != label && byName.TryGetValue(Text.Key(first), out var firstList) ? firstList : new List<ArchObject>();
    }

    private static (string? Concept, string? How, string? TypeKey) ConceptOf(DrawioCell c, Metamodel meta)
    {
        var declared = TypeKeys.Select(k => c.Data.GetValueOrDefault(k)).FirstOrDefault(v => !string.IsNullOrEmpty(v));
        if (declared is not null)
        {
            if (meta.Type(declared) is not null) return (null, DrawioConcepts.ByData, declared);
            if (Archimate.IsElement(declared)) return (declared, DrawioConcepts.ByData, null);
        }
        var found = DrawioConcepts.Element(c);
        return (found?.Concept, found?.How, null);
    }

    private static List<ArchiProperty> Raw(DrawioCell c) =>
        c.Data.Where(kv => !IdKeys.Contains(kv.Key, StringComparer.OrdinalIgnoreCase) && !TypeKeys.Contains(kv.Key, StringComparer.OrdinalIgnoreCase))
            .Select(kv => new ArchiProperty(kv.Key, kv.Value)).ToList();

    private static string Name(DrawioCell c) => c.Label.Replace('\n', ' ').Trim();

    /// <summary>Libellé d'un lien : sa valeur, ou le texte posé dessus (une cellule enfant du lien).</summary>
    private static string LinkLabel(DrawioCell e, DrawioPage page) =>
        e.Label.Length > 0 ? e.Label.Replace('\n', ' ')
            : string.Join(" ", page.Cells.Where(c => c.Parent == e.Id && c.Label.Length > 0).Select(c => c.Label.Replace('\n', ' ')));

    /// <summary>Une forme à rapprocher : ni calque, ni texte libre, ni libellé de lien, ni simple regroupement.</summary>
    private static bool IsShape(DrawioCell c, DrawioPage page, Dictionary<string, DrawioCell> cells)
    {
        if (!c.Vertex || page.Layers.Contains(c.Id)) return false;
        if (c.Parent is not null && cells.TryGetValue(c.Parent, out var parent) && parent.Edge) return false;
        if (c.Has("text") || c.Has("edgeLabel") || c.Has("group")) return false;
        if (c.StyleValue("shape") is "image" || c.Has("image")) return c.Label.Length > 0;
        return true;
    }

    // ------------------------------------------------------------------ application

    public static ImportReport Apply(Stream file, string fileName, Repository repo, string user, IReadOnlyDictionary<string, DrawioDecision>? decisions)
    {
        var report = new ImportReport { Source = $"Dessin draw.io « {fileName} »" };
        List<DrawioPage> pages;
        try { pages = DrawioReader.Read(file); }
        catch (InvalidDataException e) { report.Error("fichier-illisible", e.Message); return report; }
        var analysis = Analyse(pages, fileName, repo);
        report.Anomalies.AddRange(analysis.Anomalies);
        var meta = repo.Metamodel;
        var transcoder = new Transcoder(meta);
        var now = DateTime.UtcNow;
        var objects = report.For("Objets");
        var relations = report.For("Relations");
        var views = report.For("Vues");
        var byKey = analysis.Items.ToDictionary(i => i.Key);
        var resolved = new Dictionary<string, string>();   // clé de forme ou de lien -> objet ou relation
        var fileSlug = FileSlug(fileName);
        // Un même nom sur plusieurs pages (ou deux fois sur une page) ne crée qu'un objet.
        var created = new Dictionary<(string, string), ArchObject>();

        DrawioDecision Choice(DrawioItem i) =>
            decisions is not null && decisions.TryGetValue(i.Key, out var d) ? d
                : new DrawioDecision { Action = i.Proposal, TargetId = i.TargetId, TypeKey = i.TypeKey };

        foreach (var item in analysis.Items.Where(i => i.Kind == "objet"))
        {
            var choice = Choice(item);
            var page = pages.First(p => item.Key.StartsWith(p.Id + "/", StringComparison.Ordinal));
            var cellId = item.Key[(page.Id.Length + 1)..];
            var refKey = RefKey(fileName, page.Id);
            if (choice.Action == DrawioItem.Link)
            {
                if (choice.TargetId is null || !repo.Objects.TryGetValue(choice.TargetId, out var o) || o.Archived)
                {
                    report.Error("objet-introuvable", $"« {item.Label} » : l'objet choisi n'existe pas ou est archivé ; la forme reste du dessin.", item.Key);
                    continue;
                }
                resolved[item.Key] = o.Id;
                if (!o.ExternalRefs.TryGetValue(refKey, out var known) || known != cellId) { o.ExternalRefs[refKey] = cellId; objects.Updated++; }
                else objects.Unchanged++;
            }
            else if (choice.Action == DrawioItem.Create)
            {
                var typeKey = choice.TypeKey ?? item.TypeKey;
                if (typeKey is null || meta.Type(typeKey) is null)
                {
                    report.Error("type-absent", $"« {item.Label} » : aucun type valable pour créer l'objet ; la forme reste du dessin.", item.Key);
                    continue;
                }
                if (item.Label.Length == 0) { report.Error("nom-absent", "Une forme sans libellé ne peut pas devenir un objet.", item.Key); continue; }
                if (created.TryGetValue((Text.Key(item.Label), typeKey), out var twin))
                {
                    twin.ExternalRefs.TryAdd(refKey, cellId);
                    resolved[item.Key] = twin.Id;
                    continue;
                }
                var cell = page.Cells.First(c => c.Id == cellId);
                var o = new ArchObject
                {
                    Id = "id-" + Guid.NewGuid().ToString("N"), TypeKey = typeKey, Name = item.Label, Source = SourceTag,
                    ArchimateType = item.Concept, Properties = transcoder.Values(Raw(cell)),
                    ValidationStatus = "Proposé", ProposedBy = user, ProposedAt = now, UpdatedAt = now, UpdatedBy = user,
                };
                o.ExternalRefs[refKey] = cellId;
                repo.Objects[o.Id] = o;
                created[(Text.Key(item.Label), typeKey)] = o;
                resolved[item.Key] = o.Id;
                objects.Added++;
                repo.Log(user, "Objet créé depuis draw.io", "objet", o.Id, null, $"« {o.Name} » ({meta.Type(typeKey)?.Label ?? typeKey}), fichier « {fileName} », proposé à la validation");
            }
        }

        foreach (var item in analysis.Items.Where(i => i.Kind == "relation"))
        {
            var choice = Choice(item);
            if (choice.Action == DrawioItem.Drawing) continue;
            if (!resolved.TryGetValue(item.SourceKey!, out var from) || !resolved.TryGetValue(item.TargetKey!, out var to))
            {
                report.Warn("extremite-dessin", $"Lien « {item.Label} » : une extrémité est restée du dessin ; le lien aussi.", item.Key);
                continue;
            }
            if (choice.Action == DrawioItem.Link)
            {
                if (choice.TargetId is not null && repo.Relationships.TryGetValue(choice.TargetId, out var r) && !r.Archived) { resolved[item.Key] = r.Id; relations.Unchanged++; }
                else report.Error("relation-introuvable", $"Lien « {item.Label} » : la relation choisie n'existe pas ; le lien reste du dessin.", item.Key);
                continue;
            }
            var typeKey = choice.TypeKey ?? item.TypeKey;
            if (typeKey is null || meta.RelType(typeKey) is null)
            {
                report.Error("type-absent", $"Lien « {item.Label} » : aucun type de relation valable ; le lien reste du dessin.", item.Key);
                continue;
            }
            var same = repo.ActiveRelationships.FirstOrDefault(r => r.SourceId == from && r.TargetId == to && r.TypeKey == typeKey);
            if (same is not null) { resolved[item.Key] = same.Id; relations.Unchanged++; continue; }
            var relation = new Relationship
            {
                Id = "id-" + Guid.NewGuid().ToString("N"), TypeKey = typeKey, SourceId = from, TargetId = to,
                Name = item.Label.Length > 0 ? item.Label : null, Source = SourceTag,
                ArchimateType = item.Concept,
            };
            repo.Relationships[relation.Id] = relation;
            resolved[item.Key] = relation.Id;
            relations.Added++;
        }

        // Une vue par page, rangée dans « Schémas draw.io / fichier ».
        var folder = DrawioFolder(repo, fileName);
        var seen = new HashSet<string>();
        foreach (var page in pages)
        {
            var id = $"drawio-{fileSlug}-{Text.Slug(page.Id)}";
            seen.Add(id);
            var name = pages.Count == 1 && page.Name.StartsWith("Page", StringComparison.OrdinalIgnoreCase)
                ? Path.GetFileNameWithoutExtension(fileName.Replace(".drawio", "", StringComparison.OrdinalIgnoreCase)) : page.Name;
            var diagram = BuildDiagram(page, id, name, resolved, repo);
            diagram.Properties.Add(new PropertyValue { Key = FileProperty, Value = fileName });
            diagram.Properties.Add(new PropertyValue { Key = PageProperty, Value = page.Name });
            if (!repo.Diagrams.TryGetValue(id, out var existing)) { repo.Diagrams[id] = diagram; views.Added++; }
            else if (System.Text.Json.JsonSerializer.Serialize(existing) == System.Text.Json.JsonSerializer.Serialize(diagram)) views.Unchanged++;
            else { repo.Diagrams[id] = diagram; views.Updated++; }
            if (!folder.Entries.Contains(id)) folder.Entries.Add(id);
            (report.Views ??= new List<string>()).Add(id);
        }
        // Pages disparues du fichier : leur vue est archivée.
        foreach (var d in repo.Diagrams.Values.Where(d => !d.Archived && d.Source == SourceTag && !seen.Contains(d.Id)
                                                           && d.Properties.Any(p => p.Key == FileProperty && p.Value == fileName)))
        {
            d.Archived = true; views.Archived++;
            folder.Entries.Remove(d.Id);
        }
        var drawn = analysis.Items.Count(i => !resolved.ContainsKey(i.Key));
        if (drawn > 0) report.Notes.Add($"{drawn} forme(s) ou lien(s) gardés comme dessin, sans objet du référentiel.");
        if (objects.Added > 0) report.Notes.Add($"{objects.Added} objet(s) créé(s), proposés à la validation.");
        repo.Log(user, "Import draw.io", null, $"{fileName} : {pages.Count} page(s), {objects.Added} objet(s) créé(s), {resolved.Count(kv => byKey[kv.Key].Kind == "objet")} forme(s) rapprochée(s), {relations.Added} relation(s) créée(s)");
        return report;
    }

    private static Folder DrawioFolder(Repository repo, string fileName)
    {
        var root = repo.Folders.FirstOrDefault(f => f.Source == SourceTag);
        if (root is null) { root = new Folder { Label = FolderLabel, Source = SourceTag }; repo.Folders.Add(root); }
        var child = root.Children.FirstOrDefault(f => f.Label == fileName);
        if (child is null) { child = new Folder { Label = fileName, Source = SourceTag }; root.Children.Add(child); }
        return child;
    }

    private static Diagram BuildDiagram(DrawioPage page, string id, string name, Dictionary<string, string> resolved, Repository repo)
    {
        var cells = page.Cells.ToDictionary(c => c.Id, c => c, StringComparer.Ordinal);
        var diagram = new Diagram { Id = id, Name = name, Source = SourceTag };
        string NodeId(string cellId) => $"{id}-{Text.Slug(cellId)}";
        var nodes = new Dictionary<string, DiagramNode>();
        var visible = page.Cells.Where(c => c.Vertex && !page.Layers.Contains(c.Id)
                                            && !(c.Parent is not null && cells.TryGetValue(c.Parent, out var p) && p.Edge)).ToList();
        var parents = visible.Select(c => c.Parent).Where(p => p is not null).ToHashSet();
        foreach (var c in visible)
        {
            resolved.TryGetValue(page.Id + "/" + c.Id, out var objectId);
            var isGroup = c.Has("group");
            if (objectId is null && c.Label.Length == 0 && !parents.Contains(c.Id)) continue;   // forme vide : décor
            nodes[c.Id] = new DiagramNode
            {
                Id = NodeId(c.Id),
                Kind = objectId is not null ? "Element" : parents.Contains(c.Id) || isGroup || c.Has("swimlane") ? "Container" : "Label",
                ElementId = objectId, Label = objectId is null ? c.Label.Replace('\n', ' ') : null,
                X = (int)Math.Round(c.X), Y = (int)Math.Round(c.Y), W = Math.Max(1, (int)Math.Round(c.W)), H = Math.Max(1, (int)Math.Round(c.H)),
                Style = objectId is null ? StyleOf(c) : null,
            };
        }
        foreach (var c in visible.Where(c => nodes.ContainsKey(c.Id)))
        {
            if (c.Parent is not null && nodes.TryGetValue(c.Parent, out var parentNode)) parentNode.Children.Add(nodes[c.Id]);
            else diagram.Nodes.Add(nodes[c.Id]);
        }
        foreach (var e in page.Cells.Where(e => e.Edge && e.Source is not null && e.Target is not null))
        {
            var connectionId = NodeId(e.Id);
            string? End(string cellId) => nodes.ContainsKey(cellId) ? NodeId(cellId)
                : cells.TryGetValue(cellId, out var target) && target.Edge ? NodeId(cellId) : null;
            var source = End(e.Source!); var target = End(e.Target!);
            if (source is null || target is null) continue;
            resolved.TryGetValue(page.Id + "/" + e.Id, out var relationId);
            var label = LinkLabel(e, page);
            diagram.Connections.Add(new DiagramConnection
            {
                Id = connectionId, Kind = relationId is not null ? "Relationship" : "Line", RelationshipId = relationId,
                SourceNodeId = source, TargetNodeId = target, Label = relationId is null && label.Length > 0 ? label : null,
                Style = relationId is null ? StyleOf(e) : null,
                Bendpoints = e.Points.Select(p => new Point { X = (int)Math.Round(p.X), Y = (int)Math.Round(p.Y) }).ToList(),
            });
        }
        // Une connexion vers un lien suppose que ce lien soit gardé : on retire celles qui pointent dans le vide.
        var connectionIds = diagram.Connections.Select(c => c.Id).ToHashSet();
        var nodeIds = diagram.AllNodes().Select(n => n.Id).ToHashSet();
        diagram.Connections.RemoveAll(c => !(nodeIds.Contains(c.SourceNodeId) || connectionIds.Contains(c.SourceNodeId))
                                           || !(nodeIds.Contains(c.TargetNodeId) || connectionIds.Contains(c.TargetNodeId)));
        return diagram;
    }

    private static DiagramStyle? StyleOf(DrawioCell c)
    {
        var fill = Color(c.StyleValue("fillColor") ?? (c.Vertex && !c.Has("text") ? "#ffffff" : null));
        var line = Color(c.StyleValue("strokeColor"));
        var font = Color(c.StyleValue("fontColor"));
        var size = c.StyleValue("fontSize");
        var bold = int.TryParse(c.StyleValue("fontStyle"), out var fs) && (fs & 1) == 1;
        if (fill is null && line is null && font is null && size is null && !bold) return null;
        return new DiagramStyle
        {
            Fill = fill, Line = line,
            Font = font is null && size is null && !bold ? null : new FontSpec { Color = font, Size = size, Style = bold ? "bold" : null },
        };
    }

    private static Rgba? Color(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var hex = value.Trim().TrimStart('#');
        if (hex.Length == 3) hex = string.Concat(hex.Select(ch => $"{ch}{ch}"));
        if (hex.Length != 6 || !int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var rgb)) return null;
        return new Rgba { R = (rgb >> 16) & 0xff, G = (rgb >> 8) & 0xff, B = rgb & 0xff };
    }
}
