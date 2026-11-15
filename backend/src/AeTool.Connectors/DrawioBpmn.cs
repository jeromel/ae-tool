using System.Text.RegularExpressions;

namespace AeTool.Connectors;

public enum BpmnKind { None, Pool, Lane, Task, Gateway, Event, Data }

/// <summary>
/// Reconnaissance d'un schéma BPMN dans un dessin draw.io, qu'il utilise la bibliothèque BPMN de draw.io ou des formes
/// ordinaires : une pool est un couloir qui contient des couloirs, une forme posée dans un couloir est une tâche, un
/// losange est une passerelle, un petit cercle dans un couloir est un événement.
///
/// Correspondance ArchiMate retenue (ArchiMate 3.1, annexe sur BPMN) et ce que l'import en fait par défaut :
/// pool → processus ou acteur du même nom, sinon dessin ; couloir → rôle (création proposée) ; tâche → processus du
/// même nom, sinon dessin (le détail d'un BPMN ne charge pas le référentiel) ; passerelle → jonction, toujours dessin ;
/// événement → événement métier du même nom, sinon dessin ; objet de données → objet métier du même nom.
/// </summary>
public static class DrawioBpmn
{
    public const string How = "structure BPMN";

    public static BpmnKind Kind(DrawioCell c, IReadOnlyDictionary<string, DrawioCell> cells)
    {
        if (!c.Vertex) return BpmnKind.None;
        var shape = (c.StyleValue("shape") ?? "").ToLowerInvariant();
        if (shape.StartsWith("mxgraph.bpmn."))
        {
            if (shape.Contains("gateway") || (c.StyleValue("symbol") ?? "").EndsWith("Gw", StringComparison.OrdinalIgnoreCase)) return BpmnKind.Gateway;
            if (shape.Contains("event") || c.StyleValue("outline") is not null) return BpmnKind.Event;
            if (shape.Contains("data")) return BpmnKind.Data;
            return BpmnKind.Task;
        }
        if (IsSwimlane(c))
        {
            if (Parent(c, cells) is { } p && IsSwimlane(p)) return BpmnKind.Lane;
            if (cells.Values.Any(x => x.Parent == c.Id && IsSwimlane(x)) || c.StyleValue("childLayout") == "stackLayout") return BpmnKind.Pool;
            return BpmnKind.None;
        }
        if (c.Has("rhombus") || shape == "rhombus") return BpmnKind.Gateway;
        if (!InLane(c, cells) || c.Has("text")) return BpmnKind.None;
        if ((c.Has("ellipse") || shape == "ellipse") && c.W <= 60 && c.H <= 60) return BpmnKind.Event;
        if (shape is "note" or "document" || c.Has("shape=note")) return BpmnKind.None;
        return BpmnKind.Task;
    }

    /// <summary>Concept ArchiMate correspondant, utilisé pour le type proposé si l'utilisateur choisit de créer.</summary>
    public static string? Concept(BpmnKind kind) => kind switch
    {
        BpmnKind.Pool => "BusinessProcess",
        BpmnKind.Lane => "BusinessRole",
        BpmnKind.Task => "BusinessProcess",
        BpmnKind.Event => "BusinessEvent",
        BpmnKind.Data => "BusinessObject",
        BpmnKind.Gateway => "Junction",
        _ => null,
    };

    /// <summary>Concepts acceptés pour un rapprochement par nom : un couloir se lie à un rôle ou à un acteur, une pool à un processus ou à un acteur.</summary>
    public static string[] Accepts(BpmnKind kind) => kind switch
    {
        BpmnKind.Pool => new[] { "BusinessProcess", "BusinessActor", "BusinessCollaboration" },
        BpmnKind.Lane => new[] { "BusinessRole", "BusinessActor" },
        BpmnKind.Task => new[] { "BusinessProcess", "BusinessFunction", "BusinessInteraction" },
        BpmnKind.Event => new[] { "BusinessEvent" },
        BpmnKind.Data => new[] { "BusinessObject", "DataObject" },
        _ => Array.Empty<string>(),
    };

    public static string Label(BpmnKind kind) => kind switch
    {
        BpmnKind.Pool => "pool BPMN",
        BpmnKind.Lane => "couloir BPMN",
        BpmnKind.Task => "tâche BPMN",
        BpmnKind.Gateway => "passerelle BPMN",
        BpmnKind.Event => "événement BPMN",
        BpmnKind.Data => "objet de données BPMN",
        _ => "",
    };

    private static readonly Regex DefaultName = new(@"^(pool|lane|couloir|participant|task|tâche)\s*\d*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Nom laissé par draw.io (« Pool », « Lane 3 ») : il ne désigne rien.</summary>
    public static bool IsDefaultName(string label) => label.Length == 0 || DefaultName.IsMatch(label.Trim());

    public static bool IsSwimlane(DrawioCell c) => c.Has("swimlane") || string.Equals(c.StyleValue("shape"), "swimlane", StringComparison.OrdinalIgnoreCase);

    private static DrawioCell? Parent(DrawioCell c, IReadOnlyDictionary<string, DrawioCell> cells) =>
        c.Parent is not null && cells.TryGetValue(c.Parent, out var p) && p.Vertex ? p : null;

    /// <summary>
    /// Couloir ou pool BPMN qui contient la forme, en remontant ses parents. Un simple conteneur draw.io (un couloir
    /// isolé, sans couloirs voisins) n'en est pas un : ce qu'il contient garde le rapprochement ordinaire.
    /// </summary>
    public static DrawioCell? Lane(DrawioCell c, IReadOnlyDictionary<string, DrawioCell> cells)
    {
        var p = Parent(c, cells);
        for (var depth = 0; p is not null && depth < 50; depth++, p = Parent(p, cells))
            if (IsSwimlane(p) && Kind(p, cells) is BpmnKind.Lane or BpmnKind.Pool) return p;
        return null;
    }

    private static bool InLane(DrawioCell c, IReadOnlyDictionary<string, DrawioCell> cells) => Lane(c, cells) is not null;

    /// <summary>Un lien de schéma BPMN : flux de message (pointillés, rond au départ) ou flux de séquence (flèche).</summary>
    public static DrawioConcept? Relation(DrawioCell e)
    {
        var start = (e.StyleValue("startArrow") ?? "none").ToLowerInvariant();
        var end = (e.StyleValue("endArrow") ?? "classic").ToLowerInvariant();
        if (end == "none") return null;
        if (e.StyleValue("dashed") == "1") return start == "oval" ? new("Flow", How) : null;
        return new("Triggering", How);
    }
}
