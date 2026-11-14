using System.Text.Json.Serialization;

namespace AeTool.Core;

// Modèle du référentiel. Principe : le référentiel est un graphe (objets + relations) ;
// les dossiers classent, les diagrammes projettent. Rien ici ne dépend du web, du stockage ou d'Archi.

/// <summary>Valeur d'une propriété, rattachée à sa définition par la clé (le nom) de celle-ci.</summary>
public sealed class PropertyValue
{
    public string Key { get; set; } = "";
    public string? Value { get; set; }
}

/// <summary>Apparence d'un élément : portée par le type, redéfinissable pour un objet précis.</summary>
public sealed class Visual
{
    public string? Shape { get; set; }      // rectangle | arrondi | ellipse | hexagone
    public string? Fill { get; set; }       // #rrggbb
    public string? Stroke { get; set; }
    public string? TextColor { get; set; }
    public string? Icon { get; set; }       // clé du pictogramme (application, acteur, noeud…) ; vide = celui du type
    /// <summary>Gabarit du rendu soigné : carte | rond | zone. Vide = gabarit par défaut du type.</summary>
    public string? Template { get; set; }
}

public sealed class ArchObject
{
    public string Id { get; set; } = "";
    public string TypeKey { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Documentation { get; set; }
    public List<PropertyValue> Properties { get; set; } = new();
    public Visual? VisualOverride { get; set; }
    public string Source { get; set; } = "saisie";            // archi | urba | saisie
    public string ValidationStatus { get; set; } = "Brouillon"; // Brouillon | Proposé | Validé
    public string? Owner { get; set; }
    public bool Archived { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    /// <summary>Concept ArchiMate d'origine quand l'objet vient d'un import Archi ; sert à le réexporter à l'identique.</summary>
    public string? ArchimateType { get; set; }
    /// <summary>Identifiant de l'objet dans chaque source externe rapprochée : clé de la source → identifiant chez elle.</summary>
    public Dictionary<string, string> ExternalRefs { get; set; } = new();

    // Circuit de validation (Brouillon → Proposé → Validé) : qui a proposé, qui a validé, et le commentaire d'un refus.
    public string? ProposedBy { get; set; }
    public DateTime? ProposedAt { get; set; }
    public string? ValidatedBy { get; set; }
    public DateTime? ValidatedAt { get; set; }
    public string? ValidationComment { get; set; }

    public string? Get(string key) => Properties.FirstOrDefault(p => p.Key == key)?.Value;

    public void Set(string key, string? value)
    {
        var existing = Properties.FirstOrDefault(p => p.Key == key);
        if (existing is null) Properties.Add(new PropertyValue { Key = key, Value = value });
        else existing.Value = value;
    }
}

public sealed class Relationship
{
    public string Id { get; set; } = "";
    public string TypeKey { get; set; } = "";
    /// <summary>Identifiant d'un objet ou, comme ArchiMate l'autorise, d'une autre relation.</summary>
    public string SourceId { get; set; } = "";
    public string TargetId { get; set; } = "";
    public string? Name { get; set; }
    public string? Documentation { get; set; }
    public List<PropertyValue> Properties { get; set; } = new();
    public string? AccessType { get; set; }
    public bool? IsDirected { get; set; }
    public string? Modifier { get; set; }
    public string Source { get; set; } = "saisie";
    public bool Archived { get; set; }
    /// <summary>Type de relation ArchiMate d'origine quand la relation vient d'un import Archi.</summary>
    public string? ArchimateType { get; set; }
}

/// <summary>Dossier de classement. Ce n'est pas une relation d'architecture.</summary>
public sealed class Folder
{
    public string Label { get; set; } = "";
    public string? Documentation { get; set; }
    public List<Folder> Children { get; set; } = new();
    /// <summary>Identifiants d'objets, de relations ou de diagrammes rangés dans ce dossier.</summary>
    public List<string> Entries { get; set; } = new();
}

public sealed class Rgba
{
    public int R { get; set; }
    public int G { get; set; }
    public int B { get; set; }
    public int? A { get; set; }
    [JsonIgnore] public string Hex => $"#{R:x2}{G:x2}{B:x2}";
}

public sealed class FontSpec
{
    public string? Name { get; set; }
    public string? Size { get; set; }
    public string? Style { get; set; }
    public Rgba? Color { get; set; }
}

public sealed class DiagramStyle
{
    public Rgba? Fill { get; set; }
    public Rgba? Line { get; set; }
    public string? LineWidth { get; set; }
    public FontSpec? Font { get; set; }
}

/// <summary>Occurrence graphique : un même objet peut en avoir plusieurs, il n'est jamais dupliqué.</summary>
public sealed class DiagramNode
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "Element"; // Element | Label | Container
    public string? ElementId { get; set; }
    public string? Label { get; set; }
    public string? ViewRefId { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int W { get; set; }
    public int H { get; set; }
    public DiagramStyle? Style { get; set; }
    public List<DiagramNode> Children { get; set; } = new();
}

public sealed class Point
{
    public int X { get; set; }
    public int Y { get; set; }
}

public sealed class DiagramConnection
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "Relationship";
    public string? RelationshipId { get; set; }
    public string SourceNodeId { get; set; } = "";
    public string TargetNodeId { get; set; } = "";
    public DiagramStyle? Style { get; set; }
    public List<Point> Bendpoints { get; set; } = new();
}

public sealed class Diagram
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Documentation { get; set; }
    public string? Viewpoint { get; set; }
    public List<PropertyValue> Properties { get; set; } = new();
    public List<DiagramNode> Nodes { get; set; } = new();
    public List<DiagramConnection> Connections { get; set; } = new();
    public string Source { get; set; } = "saisie";
    public bool Archived { get; set; }

    public IEnumerable<DiagramNode> AllNodes() => Flatten(Nodes);

    private static IEnumerable<DiagramNode> Flatten(IEnumerable<DiagramNode> nodes)
    {
        foreach (var n in nodes)
        {
            yield return n;
            foreach (var c in Flatten(n.Children)) yield return c;
        }
    }
}

/// <summary>Impact d'un projet sur un objet. Entité dédiée, exportable en association ArchiMate.</summary>
public sealed class ProjectImpact
{
    public string ProjectId { get; set; } = "";
    public string ObjectId { get; set; } = "";
    /// <summary>À qualifier | Création | Évolution | Remplacement | Retrait.</summary>
    public string Nature { get; set; } = ImpactNature.ToQualify;
    /// <summary>« vue » si déduit d'un diagramme du dossier projet, « saisie » sinon.</summary>
    public string Origin { get; set; } = "saisie";
    public List<string> DiagramIds { get; set; } = new();
    public string? Comment { get; set; }
}

public static class ImpactNature
{
    public const string ToQualify = "À qualifier";
    public static readonly string[] All = { ToQualify, "Création", "Évolution", "Remplacement", "Retrait", "Hors périmètre" };
}

/// <summary>Modification d'un champ : valeur avant et valeur après (exigence EF-52).</summary>
public sealed record FieldChange(string Field, string? Before, string? After);

public sealed class JournalEntry
{
    public DateTime At { get; set; }
    public string User { get; set; } = "";
    public string Action { get; set; } = "";
    public string? TargetId { get; set; }
    /// <summary>Nature de la cible : objet, relation, impact, notation, métamodèle, import…</summary>
    public string? TargetKind { get; set; }
    public string? Detail { get; set; }
    /// <summary>Champs modifiés, avec leur valeur avant et après. Vide pour une action sans modification de champ.</summary>
    public List<FieldChange>? Changes { get; set; }
}

/// <summary>Tout l'état de l'outil. Sérialisable en un seul fichier JSON (réversibilité, exigence ED-01).</summary>
public sealed class Repository
{
    public string ModelId { get; set; } = "";
    public string ModelName { get; set; } = "Référentiel";
    public string Language { get; set; } = "fr";
    public string? SchemaLocation { get; set; }
    public Metamodel Metamodel { get; set; } = Metamodel.Default();
    public Dictionary<string, ArchObject> Objects { get; set; } = new();
    public Dictionary<string, Relationship> Relationships { get; set; } = new();
    public Dictionary<string, Diagram> Diagrams { get; set; } = new();
    public List<Folder> Folders { get; set; } = new();
    public List<ProjectImpact> Impacts { get; set; } = new();
    public Dictionary<string, Assessment> Assessments { get; set; } = new();
    public List<JournalEntry> Journal { get; set; } = new();

    [JsonIgnore] public IEnumerable<ArchObject> ActiveObjects => Objects.Values.Where(o => !o.Archived);
    [JsonIgnore] public IEnumerable<Relationship> ActiveRelationships => Relationships.Values.Where(r => !r.Archived);
    [JsonIgnore] public IEnumerable<Diagram> ActiveDiagrams => Diagrams.Values.Where(d => !d.Archived);

    public void Log(string user, string action, string? targetId = null, string? detail = null)
    {
        Journal.Add(new JournalEntry { At = DateTime.UtcNow, User = user, Action = action, TargetId = targetId, Detail = detail });
        if (Journal.Count > 5000) Journal.RemoveRange(0, Journal.Count - 5000);
    }
}
