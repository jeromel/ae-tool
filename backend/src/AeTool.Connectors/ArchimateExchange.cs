using System.Text.Json;
using System.Xml.Linq;
using AeTool.Core;

namespace AeTool.Connectors;

/// <summary>
/// Import et export au format d'échange ArchiMate 3 (celui d'Archi : Fichier &gt; Exporter &gt; Modèle au format Open Exchange).
/// Les identifiants XML sont la clé d'intégration : ils sont conservés dans les deux sens et l'import est rejouable.
/// </summary>
public static class ArchimateExchange
{
    private static readonly XNamespace A = "http://www.opengroup.org/xsd/archimate/3.0/";
    private static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";
    private const string SourceTag = "archi";

    // ------------------------------------------------------------------ Import

    public static ImportReport Import(Stream xml, Repository repo, string user = "import")
    {
        var report = new ImportReport { Source = "Archi (format d'échange ArchiMate)" };
        XDocument doc;
        try { doc = XDocument.Load(xml); }
        catch (Exception e) { report.Error("xml-invalide", $"Fichier XML illisible : {e.Message}"); return report; }

        var model = doc.Root;
        if (model is null || model.Name != A + "model")
        {
            report.Error("format-inconnu", "La racine attendue est <model> dans l'espace de noms ArchiMate 3.0.");
            return report;
        }

        var modelId = (string?)model.Attribute("identifier") ?? "";
        // On n'archive les objets absents du fichier que si l'on réimporte le même modèle.
        var sameModel = repo.ModelId != "" && repo.ModelId == modelId;
        repo.ModelId = modelId;
        repo.ModelName = Text(model.Element(A + "name")) ?? repo.ModelName;
        repo.Language = (string?)model.Element(A + "name")?.Attribute(XNamespace.Xml + "lang") ?? repo.Language;
        repo.SchemaLocation = (string?)model.Attribute(Xsi + "schemaLocation");

        var transcoder = new Transcoder(repo.Metamodel);
        var propertyNames = ImportPropertyDefinitions(model, report, transcoder);
        var seenObjects = ImportElements(model, repo, report, propertyNames, transcoder, user);
        var seenRelations = ImportRelationships(model, repo, report, propertyNames, transcoder);
        ImportFolders(model, repo, report);
        var seenDiagrams = ImportDiagrams(model, repo, report, propertyNames, transcoder);

        if (sameModel)
        {
            report.For("Objets").Archived = ArchiveMissing(repo.Objects.Values.Where(o => o.Source == SourceTag && !o.Archived),
                o => o.Id, seenObjects, o => o.Archived = true);
            report.For("Relations").Archived = ArchiveMissing(repo.Relationships.Values.Where(r => r.Source == SourceTag && !r.Archived),
                r => r.Id, seenRelations, r => r.Archived = true);
            report.For("Vues").Archived = ArchiveMissing(repo.Diagrams.Values.Where(d => d.Source == SourceTag && !d.Archived),
                d => d.Id, seenDiagrams, d => d.Archived = true);
        }
        else report.Notes.Add("Premier import de ce modèle : aucun objet existant n'a été archivé.");

        var deduced = new Analyzer(repo).RefreshDeducedImpacts();
        report.Notes.Add($"{deduced} impact(s) projet déduit(s) des vues rangées dans « {repo.Metamodel.Settings.ProjectFolderLabel} », à qualifier.");
        repo.Log(user, "Import Archi", modelId, Summary(report));
        return report;
    }

    private static int ArchiveMissing<T>(IEnumerable<T> items, Func<T, string> id, HashSet<string> seen, Action<T> archive)
    {
        var n = 0;
        foreach (var item in items.ToList())
            if (!seen.Contains(id(item))) { archive(item); n++; }
        return n;
    }

    private static Dictionary<string, string> ImportPropertyDefinitions(XElement model, ImportReport report, Transcoder transcoder)
    {
        // Archi renumérote les « propid-N » à chaque export : la clé stable est le nom Archi de la propriété.
        var names = new Dictionary<string, string>();
        var marker = transcoder.Marker;
        var tally = report.For("Définitions de propriétés");
        foreach (var def in model.Element(A + "propertyDefinitions")?.Elements(A + "propertyDefinition") ?? Enumerable.Empty<XElement>())
        {
            var id = (string?)def.Attribute("identifier");
            if (string.IsNullOrEmpty(id)) { report.Error("identifiant-absent", "Définition de propriété sans identifiant."); continue; }
            var name = Text(def.Element(A + "name")) ?? "";
            if (name == "") report.Warn("propriete-sans-nom", "Définition de propriété sans nom ; importée sous la clé vide.", id);
            if (!names.TryAdd(id, name)) { report.Error("identifiant-duplique", "Définition de propriété en double.", id); continue; }
            if (name == marker) continue; // le marqueur de type n'est pas une propriété de l'objet
            if (transcoder.EnsureProperty(name, (string?)def.Attribute("type"), SourceTag)) tally.Added++; else tally.Unchanged++;
        }
        return names;
    }

    private static HashSet<string> ImportElements(XElement model, Repository repo, ImportReport report, Dictionary<string, string> propertyNames, Transcoder transcoder, string user)
    {
        var seen = new HashSet<string>();
        var tally = report.For("Objets");
        foreach (var el in model.Element(A + "elements")?.Elements(A + "element") ?? Enumerable.Empty<XElement>())
        {
            var id = (string?)el.Attribute("identifier");
            if (string.IsNullOrEmpty(id)) { report.Error("identifiant-absent", $"Élément sans identifiant : {Text(el.Element(A + "name"))}"); continue; }
            if (!seen.Add(id)) { report.Error("identifiant-duplique", "Élément en double dans le fichier ; seule la première occurrence est importée.", id); continue; }
            var concept = (string?)el.Attribute(Xsi + "type") ?? "";
            var raw = ReadProperties(el, propertyNames, report, id);
            repo.Objects.TryGetValue(id, out var existing);
            var typeKey = ChooseObjectType(repo, report, transcoder, existing, concept, raw, id);
            var incoming = new ArchObject
            {
                Id = id, TypeKey = typeKey, ArchimateType = concept, Name = Text(el.Element(A + "name")) ?? "",
                Documentation = Text(el.Element(A + "documentation")),
                Properties = transcoder.Values(raw), Source = SourceTag,
            };
            if (existing is null)
            {
                incoming.UpdatedAt = DateTime.UtcNow;
                Validation.Enter(incoming, Validation.ArchiImportStatus(repo.Metamodel), user, incoming.UpdatedAt);
                repo.Objects[id] = incoming;
                tally.Added++;
                continue;
            }
            // Les propriétés qui ne viennent pas d'Archi (reprise urbanisation, saisie) sont conservées.
            var local = existing.Properties.Where(p => repo.Metamodel.Property(p.Key)?.Source != SourceTag
                                                       && incoming.Properties.All(i => i.Key != p.Key)).ToList();
            incoming.Properties.AddRange(local);
            var changed = existing.Archived || existing.TypeKey != incoming.TypeKey || existing.ArchimateType != incoming.ArchimateType
                          || existing.Name != incoming.Name
                          || existing.Documentation != incoming.Documentation || !Same(existing.Properties, incoming.Properties);
            if (!changed) { tally.Unchanged++; continue; }
            existing.TypeKey = incoming.TypeKey;
            existing.ArchimateType = incoming.ArchimateType;
            existing.Name = incoming.Name;
            existing.Documentation = incoming.Documentation;
            existing.Properties = incoming.Properties;
            existing.Archived = false;
            existing.UpdatedAt = DateTime.UtcNow;
            tally.Updated++;
        }
        return seen;
    }

    /// <summary>
    /// Un objet déjà présent garde son type tant que son concept Archi n'a pas changé : un retypage fait dans l'outil survit au réimport.
    /// Sinon la matrice décide ; un concept sans règle tombe dans le type « non classé » et l'anomalie est signalée.
    /// </summary>
    private static string ChooseObjectType(Repository repo, ImportReport report, Transcoder transcoder, ArchObject? existing, string concept,
        List<ArchiProperty> raw, string id)
    {
        if (existing is not null && existing.ArchimateType == concept && repo.Metamodel.Type(existing.TypeKey) is not null) return existing.TypeKey;
        var choice = transcoder.ElementType(concept, raw);
        if (choice.Mapped) return choice.TypeKey;
        report.Warn("concept-non-transcode", $"Concept « {concept} » sans règle de transcodification : classé dans le type « {repo.Metamodel.Transcoding.UnmappedTypeKey} ».", id);
        return transcoder.EnsureUnmappedType();
    }

    private static HashSet<string> ImportRelationships(XElement model, Repository repo, ImportReport report, Dictionary<string, string> propertyNames, Transcoder transcoder)
    {
        var seen = new HashSet<string>();
        var tally = report.For("Relations");
        var elements = model.Element(A + "relationships")?.Elements(A + "relationship").ToList() ?? new List<XElement>();
        var relationIds = elements.Select(e => (string?)e.Attribute("identifier")).Where(i => i is not null).ToHashSet();
        foreach (var el in elements)
        {
            var id = (string?)el.Attribute("identifier");
            if (string.IsNullOrEmpty(id)) { report.Error("identifiant-absent", "Relation sans identifiant."); continue; }
            if (!seen.Add(id)) { report.Error("identifiant-duplique", "Relation en double dans le fichier.", id); continue; }
            var concept = (string?)el.Attribute(Xsi + "type") ?? "";
            var raw = ReadProperties(el, propertyNames, report, id);
            var sourceId = (string?)el.Attribute("source") ?? "";
            var targetId = (string?)el.Attribute("target") ?? "";
            repo.Relationships.TryGetValue(id, out var existing);
            var incoming = new Relationship
            {
                Id = id, TypeKey = ChooseRelationType(repo, report, transcoder, existing, concept, raw, sourceId, targetId, id),
                ArchimateType = concept, SourceId = sourceId, TargetId = targetId,
                Name = Text(el.Element(A + "name")), Documentation = Text(el.Element(A + "documentation")),
                Properties = transcoder.Values(raw),
                AccessType = (string?)el.Attribute("accessType"), Modifier = (string?)el.Attribute("modifier"),
                IsDirected = (bool?)el.Attribute("isDirected"), Source = SourceTag,
            };
            foreach (var (end, label) in new[] { (incoming.SourceId, "source"), (incoming.TargetId, "cible") })
                if (!repo.Objects.ContainsKey(end) && !relationIds.Contains(end))
                    report.Error("reference-cassee", $"La {label} « {end} » de la relation n'existe pas dans le fichier. Relation conservée telle quelle.", id);
            if (repo.Objects.TryGetValue(incoming.SourceId, out var s) && repo.Objects.TryGetValue(incoming.TargetId, out var t)
                && repo.Metamodel.CheckRelationship(incoming.TypeKey, s.TypeKey, t.TypeKey) is string problem)
                report.Warn("relation-non-conforme", problem, id);

            if (existing is null) { repo.Relationships[id] = incoming; tally.Added++; }
            else if (Json(existing) == Json(incoming)) tally.Unchanged++;
            else { repo.Relationships[id] = incoming; tally.Updated++; }
        }
        return seen;
    }

    /// <summary>Même règle que pour les objets : le type d'une relation déjà connue survit au réimport tant que son concept Archi ne change pas.</summary>
    private static string ChooseRelationType(Repository repo, ImportReport report, Transcoder transcoder, Relationship? existing, string concept,
        List<ArchiProperty> raw, string sourceId, string targetId, string id)
    {
        if (existing is not null && existing.ArchimateType == concept && repo.Metamodel.RelType(existing.TypeKey) is not null) return existing.TypeKey;
        // Les extrémités qui sont elles-mêmes des relations n'ont pas de type d'objet.
        var choice = transcoder.RelationType(concept, raw, repo.Objects.GetValueOrDefault(sourceId)?.TypeKey, repo.Objects.GetValueOrDefault(targetId)?.TypeKey);
        if (choice.Mapped) return choice.TypeKey;
        report.Warn("concept-non-transcode", $"Concept de relation « {concept} » sans règle de transcodification : classé dans le type « {repo.Metamodel.Transcoding.UnmappedRelationTypeKey} ».", id);
        return transcoder.EnsureUnmappedRelationType();
    }

    private static void ImportFolders(XElement model, Repository repo, ImportReport report)
    {
        Folder Read(XElement item)
        {
            var folder = new Folder { Label = Text(item.Element(A + "label")) ?? "", Documentation = Text(item.Element(A + "documentation")) };
            foreach (var child in item.Elements(A + "item"))
            {
                var reference = (string?)child.Attribute("identifierRef");
                if (reference is not null) folder.Entries.Add(reference);
                else folder.Children.Add(Read(child));
            }
            return folder;
        }
        var organizations = model.Elements(A + "organizations").SelectMany(o => o.Elements(A + "item")).ToList();
        if (organizations.Count == 0) return;
        var folders = new List<Folder>();
        foreach (var item in organizations)
        {
            // Un item racine peut être un dossier ou, plus rarement, une référence directe.
            if (item.Attribute("identifierRef") is not null)
            {
                var root = folders.FirstOrDefault(f => f.Label == "") ?? new Folder();
                if (!folders.Contains(root)) folders.Add(root);
                root.Entries.Add((string)item.Attribute("identifierRef")!);
            }
            else folders.Add(Read(item));
        }
        var first = repo.Folders.Count == 0;
        var changed = Json(repo.Folders) != Json(folders);
        repo.Folders = folders;
        var tally = report.For("Dossiers");
        var count = CountFolders(folders);
        if (first) tally.Added = count; else if (changed) tally.Updated = count; else tally.Unchanged = count;
    }

    private static int CountFolders(IEnumerable<Folder> folders) => folders.Sum(f => 1 + CountFolders(f.Children));

    private static HashSet<string> ImportDiagrams(XElement model, Repository repo, ImportReport report, Dictionary<string, string> propertyNames, Transcoder transcoder)
    {
        var seen = new HashSet<string>();
        var tally = report.For("Vues");
        var nodeTally = report.For("Occurrences graphiques");
        var connectionTally = report.For("Connexions graphiques");
        var views = model.Element(A + "views")?.Element(A + "diagrams")?.Elements(A + "view") ?? Enumerable.Empty<XElement>();
        foreach (var view in views)
        {
            var id = (string?)view.Attribute("identifier");
            if (string.IsNullOrEmpty(id)) { report.Error("identifiant-absent", "Vue sans identifiant."); continue; }
            if (!seen.Add(id)) { report.Error("identifiant-duplique", "Vue en double dans le fichier.", id); continue; }
            var diagram = new Diagram
            {
                Id = id, Name = Text(view.Element(A + "name")) ?? "", Documentation = Text(view.Element(A + "documentation")),
                Viewpoint = (string?)view.Attribute("viewpoint"), Properties = transcoder.Values(ReadProperties(view, propertyNames, report, id)),
                Nodes = view.Elements(A + "node").Select(ReadNode).ToList(),
                Connections = view.Elements(A + "connection").Select(ReadConnection).ToList(), Source = SourceTag,
            };
            var nodeIds = new HashSet<string>();
            foreach (var node in diagram.AllNodes())
            {
                nodeIds.Add(node.Id);
                if (node.Kind == "Element" && (node.ElementId is null || !repo.Objects.ContainsKey(node.ElementId)))
                    report.Error("occurrence-orpheline", $"Dans la vue « {diagram.Name} », un nœud référence l'élément inexistant « {node.ElementId} ».", node.Id);
            }
            foreach (var c in diagram.Connections)
            {
                if (c.RelationshipId is not null && !repo.Relationships.ContainsKey(c.RelationshipId))
                    report.Error("occurrence-orpheline", $"Dans la vue « {diagram.Name} », une connexion référence la relation inexistante « {c.RelationshipId} ».", c.Id);
                if (!nodeIds.Contains(c.SourceNodeId) && !diagram.Connections.Any(x => x.Id == c.SourceNodeId)
                    || !nodeIds.Contains(c.TargetNodeId) && !diagram.Connections.Any(x => x.Id == c.TargetNodeId))
                    report.Error("reference-cassee", $"Dans la vue « {diagram.Name} », une connexion relie un nœud absent de la vue.", c.Id);
            }
            var nodeCount = diagram.AllNodes().Count();
            if (!repo.Diagrams.TryGetValue(id, out var existing)) { repo.Diagrams[id] = diagram; tally.Added++; nodeTally.Added += nodeCount; connectionTally.Added += diagram.Connections.Count; }
            else if (Json(existing) == Json(diagram)) { tally.Unchanged++; nodeTally.Unchanged += nodeCount; connectionTally.Unchanged += diagram.Connections.Count; }
            else { repo.Diagrams[id] = diagram; tally.Updated++; nodeTally.Updated += nodeCount; connectionTally.Updated += diagram.Connections.Count; }
        }
        return seen;
    }

    private static DiagramNode ReadNode(XElement n) => new()
    {
        Id = (string?)n.Attribute("identifier") ?? "", Kind = (string?)n.Attribute(Xsi + "type") ?? "Element",
        ElementId = (string?)n.Attribute("elementRef"), Label = Text(n.Element(A + "label")),
        ViewRefId = (string?)n.Element(A + "viewRef")?.Attribute("ref"),
        X = (int?)n.Attribute("x") ?? 0, Y = (int?)n.Attribute("y") ?? 0, W = (int?)n.Attribute("w") ?? 0, H = (int?)n.Attribute("h") ?? 0,
        Style = ReadStyle(n.Element(A + "style")), Children = n.Elements(A + "node").Select(ReadNode).ToList(),
    };

    private static DiagramConnection ReadConnection(XElement c) => new()
    {
        Id = (string?)c.Attribute("identifier") ?? "", Kind = (string?)c.Attribute(Xsi + "type") ?? "Relationship",
        RelationshipId = (string?)c.Attribute("relationshipRef"),
        SourceNodeId = (string?)c.Attribute("source") ?? "", TargetNodeId = (string?)c.Attribute("target") ?? "",
        Style = ReadStyle(c.Element(A + "style")),
        Bendpoints = c.Elements(A + "bendpoint").Select(b => new Point { X = (int?)b.Attribute("x") ?? 0, Y = (int?)b.Attribute("y") ?? 0 }).ToList(),
    };

    private static DiagramStyle? ReadStyle(XElement? s)
    {
        if (s is null) return null;
        var font = s.Element(A + "font");
        return new DiagramStyle
        {
            LineWidth = (string?)s.Attribute("lineWidth"),
            Fill = ReadColor(s.Element(A + "fillColor")), Line = ReadColor(s.Element(A + "lineColor")),
            Font = font is null ? null : new FontSpec
            {
                Name = (string?)font.Attribute("name"), Size = (string?)font.Attribute("size"),
                Style = (string?)font.Attribute("style"), Color = ReadColor(font.Element(A + "color")),
            },
        };
    }

    private static Rgba? ReadColor(XElement? c) => c is null ? null : new Rgba
        { R = (int?)c.Attribute("r") ?? 0, G = (int?)c.Attribute("g") ?? 0, B = (int?)c.Attribute("b") ?? 0, A = (int?)c.Attribute("a") };

    /// <summary>Propriétés d'un élément Archi, sous leur nom Archi : la conversion en clés du métamodèle revient au transcodeur.</summary>
    private static List<ArchiProperty> ReadProperties(XElement owner, Dictionary<string, string> names, ImportReport report, string ownerId)
    {
        var list = new List<ArchiProperty>();
        foreach (var p in owner.Element(A + "properties")?.Elements(A + "property") ?? Enumerable.Empty<XElement>())
        {
            var reference = (string?)p.Attribute("propertyDefinitionRef") ?? "";
            if (!names.TryGetValue(reference, out var name))
            {
                report.Error("definition-absente", $"Propriété rattachée à la définition inexistante « {reference} » ; valeur ignorée.", ownerId);
                continue;
            }
            list.Add(new ArchiProperty(name, Text(p.Element(A + "value"))));
        }
        return list;
    }

    private static string? Text(XElement? e) => e is null ? null : e.Value;
    private static string Json(object o) => JsonSerializer.Serialize(o);
    private static bool Same(List<PropertyValue> a, List<PropertyValue> b) => Json(a) == Json(b);

    private static string Summary(ImportReport r) => string.Join(" ; ",
        r.Counts.Select(c => $"{c.Key} +{c.Value.Added} ~{c.Value.Updated} ={c.Value.Unchanged} -{c.Value.Archived}"))
        + $" ; {r.Anomalies.Count} anomalie(s)";

    // ------------------------------------------------------------------ Export

    /// <summary>
    /// Produit un fichier réimportable dans Archi. Chaque type de l'outil est écrit avec le concept que désigne la matrice de
    /// transcodification ; un type sans règle prend le concept de repli, avec un marqueur pour le retrouver au réimport.
    /// Les impacts projet qualifiés deviennent des associations nommées.
    /// </summary>
    public static XDocument Export(Repository repo, IEnumerable<string>? onlyDiagramIds = null)
    {
        var lang = repo.Language;
        var transcoder = new Transcoder(repo.Metamodel);
        XElement Named(string tag, string? value) => new(A + tag, new XAttribute(XNamespace.Xml + "lang", lang), value ?? "");

        var definitions = new Dictionary<string, string>();
        string DefinitionId(string archiName)
        {
            if (!definitions.TryGetValue(archiName, out var id)) definitions[archiName] = id = $"propid-{definitions.Count + 1}";
            return id;
        }
        XElement? Properties(List<ArchiProperty> props) => props.Count == 0 ? null : new XElement(A + "properties",
            props.Select(p => new XElement(A + "property", new XAttribute("propertyDefinitionRef", DefinitionId(p.Name)), Named("value", p.Value))));
        XElement? Plain(List<PropertyValue> props) => Properties(transcoder.ArchiProperties(props));

        var diagrams = repo.ActiveDiagrams.Where(d => onlyDiagramIds is null || onlyDiagramIds.Contains(d.Id)).ToList();
        HashSet<string>? scope = null;
        if (onlyDiagramIds is not null)
        {
            scope = diagrams.SelectMany(d => d.AllNodes()).Where(n => n.ElementId is not null).Select(n => n.ElementId!).ToHashSet();
            foreach (var d in diagrams) foreach (var c in d.Connections) if (c.RelationshipId is not null) scope.Add(c.RelationshipId);
        }
        var objects = repo.ActiveObjects.Where(o => scope is null || scope.Contains(o.Id)).ToList();
        var objectIds = objects.Select(o => o.Id).ToHashSet();
        var relations = repo.ActiveRelationships.Where(r => scope is null || scope.Contains(r.Id)).ToList();

        var elements = new XElement(A + "elements", objects.Select(o =>
        {
            var choice = transcoder.ExportElement(o);
            return new XElement(A + "element",
                new XAttribute("identifier", o.Id), new XAttribute(Xsi + "type", choice.Concept),
                Named("name", o.Name),
                o.Documentation is null ? null : Named("documentation", o.Documentation),
                Properties(transcoder.ArchiProperties(o.Properties, choice.Extra)));
        }));

        var relationships = new XElement(A + "relationships", relations.Select(r =>
        {
            var choice = transcoder.ExportRelation(r);
            return new XElement(A + "relationship",
                new XAttribute("identifier", r.Id), new XAttribute("source", r.SourceId), new XAttribute("target", r.TargetId),
                new XAttribute(Xsi + "type", choice.Concept),
                r.AccessType is null ? null : new XAttribute("accessType", r.AccessType),
                r.Modifier is null ? null : new XAttribute("modifier", r.Modifier),
                r.IsDirected is null ? null : new XAttribute("isDirected", r.IsDirected.Value ? "true" : "false"),
                r.Name is null ? null : Named("name", r.Name),
                r.Documentation is null ? null : Named("documentation", r.Documentation),
                Properties(transcoder.ArchiProperties(r.Properties, choice.Extra)));
        }));

        // Impacts qualifiés : association nommée projet -> objet, identifiant dérivé donc stable d'un export à l'autre.
        foreach (var impact in repo.Impacts.Where(i => i.Nature != ImpactNature.ToQualify && i.Nature != "Hors périmètre"
                                                       && objectIds.Contains(i.ProjectId) && objectIds.Contains(i.ObjectId)))
        {
            var id = $"id-impact-{Short(impact.ProjectId)}-{Short(impact.ObjectId)}";
            if (repo.Relationships.ContainsKey(id)) continue;
            relationships.Add(new XElement(A + "relationship", new XAttribute("identifier", id),
                new XAttribute("source", impact.ProjectId), new XAttribute("target", impact.ObjectId),
                new XAttribute(Xsi + "type", "Association"), Named("name", $"Impact : {impact.Nature}")));
        }

        XElement Item(Folder f) => new(A + "item", Named("label", f.Label),
            f.Documentation is null ? null : Named("documentation", f.Documentation),
            f.Children.Select(Item),
            f.Entries.Where(e => scope is null || scope.Contains(e) || diagrams.Any(d => d.Id == e))
                .Select(e => new XElement(A + "item", new XAttribute("identifierRef", e))));
        var organizations = repo.Folders.Count == 0 ? null : new XElement(A + "organizations", repo.Folders.Select(Item));

        var views = diagrams.Count == 0 ? null : new XElement(A + "views", new XElement(A + "diagrams", diagrams.Select(d =>
            new XElement(A + "view", new XAttribute("identifier", d.Id), new XAttribute(Xsi + "type", "Diagram"),
                d.Viewpoint is null ? null : new XAttribute("viewpoint", d.Viewpoint),
                Named("name", d.Name),
                d.Documentation is null ? null : Named("documentation", d.Documentation),
                Plain(d.Properties),
                d.Nodes.Select(n => WriteNode(n, lang)),
                d.Connections.Select(WriteConnection)))));

        // Les définitions sont écrites après coup : seules celles réellement utilisées sont exportées.
        var propertyDefinitions = definitions.Count == 0 ? null : new XElement(A + "propertyDefinitions",
            definitions.Select(kv => new XElement(A + "propertyDefinition", new XAttribute("identifier", kv.Value),
                new XAttribute("type", transcoder.ArchiDataType(kv.Key)), new XElement(A + "name", kv.Key))));

        var root = new XElement(A + "model",
            new XAttribute(XNamespace.Xmlns + "xsi", Xsi.NamespaceName),
            new XAttribute(Xsi + "schemaLocation", repo.SchemaLocation
                ?? "http://www.opengroup.org/xsd/archimate/3.0/ http://www.opengroup.org/xsd/archimate/3.1/archimate3_Diagram.xsd"),
            new XAttribute("identifier", repo.ModelId == "" ? "id-ae-tool" : repo.ModelId),
            Named("name", repo.ModelName), elements, relationships, organizations, propertyDefinitions, views);
        return new XDocument(new XDeclaration("1.0", "UTF-8", null), root);
    }

    private static string Short(string id) => id.StartsWith("id-") ? id[3..] : id;

    private static XElement WriteNode(DiagramNode n, string lang) => new(A + "node",
        new XAttribute("identifier", n.Id),
        n.ElementId is null ? null : new XAttribute("elementRef", n.ElementId),
        new XAttribute(Xsi + "type", n.Kind),
        new XAttribute("x", n.X), new XAttribute("y", n.Y), new XAttribute("w", n.W), new XAttribute("h", n.H),
        n.Label is null ? null : new XElement(A + "label", new XAttribute(XNamespace.Xml + "lang", lang), n.Label),
        WriteStyle(n.Style),
        n.ViewRefId is null ? null : new XElement(A + "viewRef", new XAttribute("ref", n.ViewRefId)),
        n.Children.Select(c => WriteNode(c, lang)));

    private static XElement WriteConnection(DiagramConnection c) => new(A + "connection",
        new XAttribute("identifier", c.Id),
        c.RelationshipId is null ? null : new XAttribute("relationshipRef", c.RelationshipId),
        new XAttribute(Xsi + "type", c.Kind), new XAttribute("source", c.SourceNodeId), new XAttribute("target", c.TargetNodeId),
        WriteStyle(c.Style),
        c.Bendpoints.Select(b => new XElement(A + "bendpoint", new XAttribute("x", b.X), new XAttribute("y", b.Y))));

    private static XElement? WriteStyle(DiagramStyle? s) => s is null ? null : new XElement(A + "style",
        s.LineWidth is null ? null : new XAttribute("lineWidth", s.LineWidth),
        WriteColor("fillColor", s.Fill), WriteColor("lineColor", s.Line),
        s.Font is null ? null : new XElement(A + "font",
            s.Font.Name is null ? null : new XAttribute("name", s.Font.Name),
            s.Font.Size is null ? null : new XAttribute("size", s.Font.Size),
            s.Font.Style is null ? null : new XAttribute("style", s.Font.Style),
            WriteColor("color", s.Font.Color)));

    private static XElement? WriteColor(string tag, Rgba? c) => c is null ? null : new XElement(A + tag,
        new XAttribute("r", c.R), new XAttribute("g", c.G), new XAttribute("b", c.B),
        c.A is null ? null : new XAttribute("a", c.A));
}
