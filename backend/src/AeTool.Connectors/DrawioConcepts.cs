namespace AeTool.Connectors;

/// <summary>Concept ArchiMate reconnu sur une forme ou un lien draw.io, et la façon dont il l'a été.</summary>
public sealed record DrawioConcept(string Concept, string How);

/// <summary>
/// Reconnaissance des formes de la bibliothèque ArchiMate 3 de draw.io. Plusieurs formes sont communes aux couches
/// métier, application et technologie (processus, fonction, service…) : draw.io ne les distingue que par leur couleur
/// de fond par défaut. Quand la couleur a été changée, la couche applicative est retenue et l'analyse le signale.
/// </summary>
public static class DrawioConcepts
{
    public const string ByData = "donnée de la forme";
    public const string ByShape = "forme ArchiMate";
    public const string ByColor = "forme ArchiMate et couleur de couche";
    public const string Guessed = "forme ArchiMate, couche supposée";
    public const string ByLine = "style du lien";

    private enum Layer { Unknown, Strategy, Motivation, Business, Application, Technology, Implementation, Migration, Location }

    private static Layer LayerOf(string? fill) => (fill ?? "").Trim().TrimStart('#').ToUpperInvariant() switch
    {
        "F5DEAA" => Layer.Strategy,
        "CCCCFF" => Layer.Motivation,
        "FFFF99" or "FFFFB5" or "FFFFCC" => Layer.Business,
        "99FFFF" or "B5FFFF" or "CCFFFF" => Layer.Application,
        "AFFFAF" or "C9FFC9" or "CCFFCC" => Layer.Technology,
        "FFE0E0" => Layer.Implementation,
        "E0FFE0" => Layer.Migration,
        "FFB973" => Layer.Location,
        _ => Layer.Unknown,
    };

    /// <summary>Concept d'une forme, ou null pour une forme quelconque (rectangle, texte, conteneur…).</summary>
    public static DrawioConcept? Element(DrawioCell c)
    {
        var shape = c.StyleValue("shape") ?? "";
        if (!shape.StartsWith("mxgraph.archimate3.", StringComparison.OrdinalIgnoreCase)) return MotivationExtras(c, shape);
        var kind = shape["mxgraph.archimate3.".Length..].ToLowerInvariant();
        var layer = LayerOf(c.StyleValue("fillColor"));
        DrawioConcept Sure(string concept) => new(concept, ByShape);

        switch (kind)
        {
            case "businessobject": return layer == Layer.Application ? new("DataObject", ByColor) : layer == Layer.Business ? new("BusinessObject", ByColor) : new("DataObject", Guessed);
            case "product": return Sure("Product");
            case "contract": return Sure("Contract");
            case "representation": return Sure("Representation");
            case "device": return Sure("Device");
            case "node": return Sure("Node");
            case "deliverable": return Sure("Deliverable");
            case "gap": return Sure("Gap");
            case "tech":
                return (c.StyleValue("techType") ?? "").ToLowerInvariant() switch
                {
                    "equipment" => Sure("Equipment"),
                    "facility" => Sure("Facility"),
                    "plateau" => Sure("Plateau"),
                    "device" => Sure("Device"),
                    "sysSw" or "syssw" => Sure("SystemSoftware"),
                    _ => Sure("Node"),
                };
            case "application": break;
            default: return null;
        }

        var app = (c.StyleValue("appType") ?? "").ToLowerInvariant();
        switch (app)
        {
            case "capability": return Sure("Capability");
            case "course": return Sure("CourseOfAction");
            case "resource": return Sure("Resource");
            case "driver": return Sure("Driver");
            case "goal": return Sure("Goal");
            case "outcome": return Sure("Outcome");
            case "principle": return Sure("Principle");
            case "requirement": return Sure("Requirement");
            case "assess": return Sure("Assessment");
            case "constraint": return Sure("Constraint");
            case "actor": return Sure("BusinessActor");
            case "comp": return Sure("ApplicationComponent");
            case "artifact": return Sure("Artifact");
            case "netw": return Sure("CommunicationNetwork");
            case "syssw": return Sure("SystemSoftware");
            case "path": return Sure("Path");
            case "distribution": return Sure("DistributionNetwork");
            case "material": return Sure("Material");
            case "location": return Sure("Location");
            case "role": return layer == Layer.Motivation ? new("Stakeholder", ByColor) : Sure("BusinessRole");
            case "event":
                if (layer == Layer.Implementation) return new("ImplementationEvent", ByColor);
                return Layered("Event", layer);
            case "collab": return Layered("Collaboration", layer);
            case "func": return Layered("Function", layer);
            case "interaction": return Layered("Interaction", layer);
            case "interface": return Layered("Interface", layer);
            case "proc": return Layered("Process", layer);
            case "serv": return Layered("Service", layer);
            case "": return layer == Layer.Implementation ? new("WorkPackage", ByColor) : null;
            default: return null;
        }
    }

    private static DrawioConcept Layered(string suffix, Layer layer) => layer switch
    {
        Layer.Business => new("Business" + suffix, ByColor),
        Layer.Technology => new("Technology" + suffix, ByColor),
        Layer.Application => new("Application" + suffix, ByColor),
        _ => new("Application" + suffix, Guessed),
    };

    /// <summary>Signification et valeur : un nuage ou une ellipse, reconnus seulement à la couleur de la couche motivation.</summary>
    private static DrawioConcept? MotivationExtras(DrawioCell c, string shape)
    {
        if (LayerOf(c.StyleValue("fillColor")) != Layer.Motivation) return null;
        if (shape.Equals("cloud", StringComparison.OrdinalIgnoreCase)) return new("Meaning", ByColor);
        if (shape.Equals("ellipse", StringComparison.OrdinalIgnoreCase) || c.Has("ellipse")) return new("Value", ByColor);
        return null;
    }

    /// <summary>
    /// Concept d'un lien d'après ses extrémités et son tracé, selon la bibliothèque ArchiMate 3 de draw.io.
    /// Un lien ordinaire (flèche par défaut) ne porte aucun concept : il reste un trait de dessin, sauf rapprochement.
    /// </summary>
    public static DrawioConcept? Relation(DrawioCell c)
    {
        var start = (c.StyleValue("startArrow") ?? "none").ToLowerInvariant();
        var end = (c.StyleValue("endArrow") ?? "classic").ToLowerInvariant();
        var startFill = c.StyleValue("startFill") != "0";
        var endFill = c.StyleValue("endFill") != "0";
        var dashed = c.StyleValue("dashed") == "1";
        var dotted = dashed && (c.StyleValue("dashPattern") ?? "").Trim().StartsWith("1 ");
        DrawioConcept Line(string concept) => new(concept, ByLine);

        if (start is "diamond" or "diamondthin") return Line(startFill ? "Composition" : "Aggregation");
        if (start == "oval" && end is "block" or "classic") return Line("Assignment");
        if (end is "block" or "blockthin")
            return endFill ? Line(dashed ? "Flow" : "Triggering") : Line(dashed ? "Realization" : "Specialization");
        if (end is "open" or "openthin" or "openasync")
            return dotted ? Line("Access") : dashed ? Line("Influence") : Line("Serving");
        if (end == "none" && start == "none") return dotted ? Line("Access") : Line("Association");
        return null;
    }
}
