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
    /// <summary>Origine du dossier quand il ne vient pas d'Archi (« drawio ») : un import Archi le garde tel quel.</summary>
    public string? Source { get; set; }
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
    /// <summary>Forme d'un élément de dessin sans objet (« ellipse », « losange », « arrondi ») : un dessin draw.io garde ses formes.</summary>
    public string? Shape { get; set; }
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
    /// <summary>Texte d'un trait de dessin qui ne porte pas de relation (lien draw.io non rapproché).</summary>
    public string? Label { get; set; }
    /// <summary>Trait de dessin : extrémité (« fleche » ou « aucune »), tirets, tracé à angles droits.</summary>
    public string? Head { get; set; }
    public bool Dashed { get; set; }
    public bool Orthogonal { get; set; }
}

/// <summary>Position et taille d'un nœud, en coordonnées de vue.</summary>
public sealed record Box(int X, int Y, int W, int H);

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
    /// <summary>
    /// Disposition retouchée dans l'outil : position et taille de nœuds, par identifiant. Elle se superpose à la géométrie
    /// venue d'Archi ou de draw.io et survit à leurs réimports ; l'effacer rend la disposition d'origine.
    /// </summary>
    public Dictionary<string, Box>? Layout { get; set; }
    public string? LayoutBy { get; set; }
    public DateTime? LayoutAt { get; set; }

    public IEnumerable<DiagramNode> AllNodes() => Flatten(Nodes);

    /// <summary>Position et taille affichées d'un nœud : la disposition retouchée si elle existe, sinon l'origine.</summary>
    public Box Placed(DiagramNode n) => Layout is not null && Layout.TryGetValue(n.Id, out var b) ? b : new Box(n.X, n.Y, n.W, n.H);

    /// <summary>Un nœud déplacé rend caducs les points de passage des liens qui y aboutissent.</summary>
    public bool Moved(string nodeId) => Layout?.ContainsKey(nodeId) == true;

    /// <summary>Reprend la disposition retouchée d'une version précédente de la vue, pour les nœuds qui existent encore.</summary>
    public void KeepLayoutFrom(Diagram? previous)
    {
        if (previous?.Layout is null) return;
        var ids = AllNodes().Select(n => n.Id).ToHashSet();
        var kept = previous.Layout.Where(kv => ids.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
        if (kept.Count == 0) return;
        Layout = kept; LayoutBy = previous.LayoutBy; LayoutAt = previous.LayoutAt;
    }

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
    public const int CurrentSchemaVersion = 4;

    /// <summary>Version du format de stockage. Un fichier sans cette propriété est en version 1 (voir <see cref="RepositoryUpgrader"/>).</summary>
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
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
    /// <summary>Scénarios et étapes de transition des projets.</summary>
    public Dictionary<string, Scenario> Scenarios { get; set; } = new();
    /// <summary>Attributs métier des objets de données (catalogue) : identifiant de l'objet de données → attributs.</summary>
    public Dictionary<string, List<DataAttribute>> DataAttributes { get; set; } = new();
    /// <summary>Listes de valeurs de référence du catalogue (onglet « Listes ») : nom de liste → valeurs.</summary>
    public Dictionary<string, List<string>> CatalogueLists { get; set; } = new();
    /// <summary>Dernier comptage des postes de travail lus dans la CMDB, par profil de poste.</summary>
    public WorkstationCensus? Workstations { get; set; }

    /// <summary>Référentiel projeté seulement : ce qui a changé à l'étape lue (identifiant → ajouté, modifié, retiré).</summary>
    [JsonIgnore] public Dictionary<string, string>? Projection { get; set; }
    /// <summary>Référentiel projeté seulement : objet remplacé → remplaçant. Les vues dessinent le remplaçant à la place.</summary>
    [JsonIgnore] public Dictionary<string, string>? Replacements { get; set; }

    /// <summary>Remplaçant final d'un objet dans un état projeté (en suivant les remplacements successifs), sinon l'objet lui-même.</summary>
    public string ReplacedBy(string id)
    {
        for (var i = 0; i < 20 && Replacements is not null && Replacements.TryGetValue(id, out var next); i++) id = next;
        return id;
    }

    [JsonIgnore] public IEnumerable<ArchObject> ActiveObjects => Objects.Values.Where(o => !o.Archived);
    [JsonIgnore] public IEnumerable<Relationship> ActiveRelationships => Relationships.Values.Where(r => !r.Archived);
    [JsonIgnore] public IEnumerable<Diagram> ActiveDiagrams => Diagrams.Values.Where(d => !d.Archived);

    /// <summary>Entrée de journal sans champ modifié (import, métamodèle…). La nature de la cible se déduit de l'action.</summary>
    public void Log(string user, string action, string? targetId = null, string? detail = null) =>
        Journal.Add(new JournalEntry { At = DateTime.UtcNow, User = user, Action = action, TargetId = targetId, TargetKind = JournalKinds.Infer(action), Detail = detail });

    /// <summary>Entrée de journal portant les champs modifiés avec leur valeur avant et après (EF-52). Le journal n'est jamais tronqué.</summary>
    public void Log(string user, string action, string kind, string? targetId, IReadOnlyList<FieldChange>? changes, string? detail = null) =>
        Journal.Add(new JournalEntry
        {
            At = DateTime.UtcNow, User = user, Action = action, TargetKind = kind, TargetId = targetId, Detail = detail,
            Changes = changes is { Count: > 0 } ? changes.ToList() : null,
        });
}

/// <summary>Natures de cible du journal.</summary>
public static class JournalKinds
{
    public const string Object = "objet";
    public const string Relationship = "relation";
    public const string Impact = "impact";
    public const string Assessment = "notation";
    public const string Metamodel = "métamodèle";
    public const string Import = "import";
    public const string Account = "compte";

    public static readonly string[] All = { Object, Relationship, Impact, Assessment, Metamodel, Import, Account };

    /// <summary>Nature déduite du libellé d'une action écrite sans nature explicite (métamodèle, imports, rapprochements).</summary>
    public static string? Infer(string action) =>
        action.StartsWith("Métamodèle", StringComparison.Ordinal) ? Metamodel
        : action.StartsWith("Import", StringComparison.Ordinal) || action.StartsWith("Rapprochement", StringComparison.Ordinal) ? Import
        : null;
}
