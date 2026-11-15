using AeTool.Core;

namespace AeTool.Api;

// Entrées des routes et projections renvoyées à l'interface.

public sealed record ObjectInput(string? Name, string? TypeKey, string? Documentation, string? Owner,
    List<PropertyValue>? Properties, Visual? VisualOverride, bool? ClearVisualOverride);
public sealed record RelationshipInput(string TypeKey, string SourceId, string TargetId, string? Name);
public sealed record RelationshipUpdate(string? Name, string? TypeKey, List<PropertyValue>? Properties);
public sealed record ObjectTypeInput(string? Label, string? LayerKey, string? Description, List<string>? Roles, Visual? Visual);
public sealed record RelationshipTypeInput(string? Label, string? ReverseLabel, string? Description, List<string>? Roles, string? LineStyle,
    string? SoftLine, string? Head, List<string>? AllowedPairs);
public sealed record DisplayableInput(string Key, bool Displayable);
public sealed record ImpactInput(string Nature, string? Comment);
public sealed record BriefDto(string Id, string Name, string TypeKey, string TypeLabel, string Layer, string LayerLabel, string Source, string Status, string? Owner, string? Mark = null);

public static class Dto
{
    public static BriefDto Brief(Repository repo, ArchObject o)
    {
        var type = repo.Metamodel.Type(o.TypeKey);
        var layer = type?.LayerKey ?? "autre";
        return new BriefDto(o.Id, o.Name, o.TypeKey, type?.Label ?? o.TypeKey, layer,
            repo.Metamodel.Layers.FirstOrDefault(l => l.Key == layer)?.Label ?? layer, o.Source, o.ValidationStatus, o.Owner,
            repo.Projection?.GetValueOrDefault(o.Id));
    }

    public static void Apply(ArchObject o, ObjectInput input)
    {
        if (input.Documentation is not null) o.Documentation = input.Documentation;
        if (input.Owner is not null) o.Owner = input.Owner;
        if (input.Properties is not null) o.Properties = input.Properties;
        if (input.VisualOverride is not null) o.VisualOverride = input.VisualOverride;
    }

    /// <summary>
    /// Vue prête à dessiner. Ordre de priorité du visuel : réglage du diagramme, puis visuel de l'élément, puis visuel du type.
    /// Avec <paramref name="metamodelVisuals"/>, le réglage du diagramme est ignoré.
    /// </summary>
    public static object Diagram(Repository repo, Diagram d, bool metamodelVisuals)
    {
        var meta = repo.Metamodel;
        var analyzer = new Analyzer(repo);
        var stateKey = meta.Scoring.StatePropertyKey;
        var displayable = meta.PropertyDefinitions.Where(p => p.Displayable && p.Key != stateKey).ToList();

        // Valeurs des calques pour un objet : notation d'urbanisation, état, puis propriétés marquées « affichables ».
        Dictionary<string, string> LayerValues(ArchObject o)
        {
            var values = new Dictionary<string, string>();
            if (repo.Assessments.TryGetValue(o.Id, out var a))
            {
                if (a.Cm is not null) values["cm"] = a.Cm.Value.ToString();
                var decision = a.RetainedDecision ?? ScoringEngine.Evaluate(meta.Scoring, a, analyzer.IsRetired(o)).ComputedDecision;
                if (!string.IsNullOrWhiteSpace(decision)) values["decision"] = decision;
            }
            if (o.Get(stateKey) is { Length: > 0 } state) values["etat"] = state;
            foreach (var p in displayable)
                if (o.Get(p.Key) is { Length: > 0 } v) values["p:" + p.Key] = v;
            return values;
        }

        var nodes = new List<object>();
        void Visit(DiagramNode n, int depth, string? parentId)
        {
            var b = d.Placed(n);
            // État projeté : un objet remplacé est dessiné sous les traits de son remplaçant, avec les liens qui lui ont été reportés.
            var elementId = n.ElementId is null ? null : repo.ReplacedBy(n.ElementId);
            var replaced = elementId is not null && elementId != n.ElementId ? repo.Objects.GetValueOrDefault(n.ElementId!)?.Name : null;
            repo.Objects.TryGetValue(elementId ?? "", out var o);
            var visual = o is null ? new Visual() : meta.EffectiveVisual(o);
            var useStyle = !metamodelVisuals || o is null;
            nodes.Add(new
            {
                n.Id, n.Kind, ElementId = elementId, Name = o?.Name ?? n.Label ?? "", Replaces = replaced,
                Mark = elementId is null ? null : repo.Projection?.GetValueOrDefault(elementId), TypeKey = o?.TypeKey,
                TypeLabel = o is null ? null : meta.Type(o.TypeKey)?.Label, n.ViewRefId,
                b.X, b.Y, b.W, b.H, Depth = depth, ParentId = parentId,
                Fill = (useStyle ? n.Style?.Fill?.Hex : null) ?? visual.Fill ?? "#ffffff",
                Stroke = (useStyle ? n.Style?.Line?.Hex : null) ?? visual.Stroke ?? "#5c5c5c",
                TextColor = (useStyle ? n.Style?.Font?.Color?.Hex : null) ?? visual.TextColor ?? "#000000",
                FontSize = int.TryParse(n.Style?.Font?.Size, out var size) ? size : 9,
                Bold = n.Style?.Font?.Style?.Contains("bold") == true,
                Shape = n.Shape ?? visual.Shape ?? "rectangle",
                Outlined = o is not null || n.Kind != "Label" || n.Style?.Line is not null,
                Template = o is null ? null : visual.Template ?? DrawingDefaults.FallbackTemplate,
                Glyph = o is null ? null : visual.Icon ?? DrawingDefaults.FallbackGlyph,
                Layers = o is null ? null : LayerValues(o),
                HasChildren = n.Children.Count > 0,
            });
            foreach (var c in n.Children) Visit(c, depth + 1, n.Id);
        }
        foreach (var n in d.Nodes) Visit(n, 0, null);

        // Objets rattachés à une relation (ArchiMate autorise une relation vers une relation) : les données qu'un flux transporte.
        var shown = d.Connections.Select(c => c.RelationshipId).Where(id => id is not null).ToHashSet();
        var carried = new Dictionary<string, List<string>>();
        foreach (var r in repo.ActiveRelationships)
        {
            var (relationId, objectId) = shown.Contains(r.TargetId) ? (r.TargetId, r.SourceId) : shown.Contains(r.SourceId) ? (r.SourceId, r.TargetId) : (null, null);
            if (relationId is null || !repo.Objects.TryGetValue(objectId!, out var o)) continue;
            if (!carried.TryGetValue(relationId, out var list)) carried[relationId] = list = new List<string>();
            list.Add(o.Name);
        }

        var layers = new List<object>
        {
            new { Key = "cm", Label = "Criticité métier", Kind = "note" },
            new { Key = "decision", Label = "Décision d'urbanisation", Kind = "texte" },
            new { Key = "etat", Label = "État", Kind = "texte" },
        };
        layers.AddRange(displayable.Select(p => new { Key = "p:" + p.Key, Label = p.Label ?? p.Key, Kind = "texte" }));

        return new
        {
            d.Id, d.Name, d.Documentation, d.Viewpoint, d.Source, Nodes = nodes, Layers = layers,
            Layout = d.Layout is null ? null : new { Count = d.Layout.Count, By = d.LayoutBy, At = d.LayoutAt },
            Connections = d.Connections.Select(c =>
            {
                repo.Relationships.TryGetValue(c.RelationshipId ?? "", out var r);
                var type = r is null ? null : meta.RelType(r.TypeKey);
                return new
                {
                    c.Id, c.SourceNodeId, c.TargetNodeId, Mark = c.RelationshipId is null ? null : repo.Projection?.GetValueOrDefault(c.RelationshipId),
                    Bendpoints = d.Moved(c.SourceNodeId) || d.Moved(c.TargetNodeId) ? new List<Point>() : c.Bendpoints, TypeKey = r?.TypeKey, TypeLabel = type?.Label, Name = r?.Name ?? c.Label,
                    LineStyle = type?.LineStyle ?? (c.Dashed ? "tirets" : "plein"),
                    SoftLine = type?.SoftLine ?? (c.Head is null ? DrawingDefaults.FallbackLine : c.Dashed ? "tirets" : "plein"),
                    Head = type?.Head ?? c.Head ?? DrawingDefaults.FallbackHead, c.Orthogonal,
                    Flow = r is not null && meta.RelIsA(r.TypeKey, Roles.Flow),
                    Protocol = r?.Properties.FirstOrDefault(p => p.Key == meta.Settings.FlowProtocolKey)?.Value,
                    Encryption = r?.Properties.FirstOrDefault(p => p.Key == meta.Settings.FlowEncryptionKey)?.Value,
                    Carried = r is not null && carried.TryGetValue(r.Id, out var list) ? list : null,
                    Color = c.Style?.Line?.Hex ?? "#000000",
                };
            }),
        };
    }
}
