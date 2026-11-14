namespace AeTool.Core;

// Le métamodèle est une donnée propre à l'outil : couches, types, rôles, relations, propriétés typées, visuels,
// grille de notation et matrice de transcodification vers ArchiMate. ArchiMate n'est qu'un format d'échange.

public sealed class Layer
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public int Order { get; set; }
}

public sealed class ObjectType
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public string? Description { get; set; }
    public string LayerKey { get; set; } = "autre";
    /// <summary>Rôles fonctionnels portés par le type (voir <see cref="Core.Roles"/>). Vide = aucun effet fonctionnel.</summary>
    public List<string> Roles { get; set; } = new();
    public Visual Visual { get; set; } = new();
    public bool Archived { get; set; }
}

public sealed class RelationshipType
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    /// <summary>Sens de lecture inverse (« Est servi par »). Il n'existe pas de type de relation inversé.</summary>
    public string? ReverseLabel { get; set; }
    public string? Description { get; set; }
    public List<string> Roles { get; set; } = new();
    /// <summary>Trait du rendu fidèle à Archi : plein | tirets | pointillés.</summary>
    public string LineStyle { get; set; } = "plein";
    /// <summary>Rendu soigné : trait (plein | tirets | pointillés) et tête. Vide = valeur de repli.</summary>
    public string? SoftLine { get; set; }
    /// <summary>fleche | ouverte | triangle | losange | losange-creux | point | aucune.</summary>
    public string? Head { get; set; }
    /// <summary>Couples « TypeSource&gt;TypeCible » autorisés. Vide = aucun contrôle.</summary>
    public List<string> AllowedPairs { get; set; } = new();
    public bool Archived { get; set; }
}

/// <summary>Types de données d'une propriété.</summary>
public static class PropertyTypes
{
    public const string Text = "texte";
    public const string Number = "nombre";
    public const string Date = "date";
    public const string Boolean = "booleen";
    public const string List = "liste";

    public static readonly IReadOnlyList<(string Key, string Label)> All = new[]
    {
        (Text, "Texte"), (Number, "Nombre"), (Date, "Date (AAAA-MM-JJ)"), (Boolean, "Oui / non"), (List, "Liste de valeurs"),
    };

    public static bool IsKnown(string key) => All.Any(t => t.Key == key);
}

public sealed class PropertyDefinition
{
    public string Key { get; set; } = "";
    public string? Label { get; set; }
    public string? Description { get; set; }
    /// <summary>texte | nombre | date | booleen | liste. L'ancienne valeur « string » se lit comme texte.</summary>
    public string DataType { get; set; } = PropertyTypes.Text;
    /// <summary>Valeurs permises quand le type de donnée est « liste ».</summary>
    public List<string> AllowedValues { get; set; } = new();
    /// <summary>Clés des types d'objets concernés ; vide = tous.</summary>
    public List<string> AppliesTo { get; set; } = new();
    public bool Required { get; set; }
    public string Source { get; set; } = "saisie";
    /// <summary>Proposée comme calque sur les vues : sa valeur s'affiche en pastille sur les cartes et peut les colorer.</summary>
    public bool Displayable { get; set; }
    /// <summary>Nom de la propriété dans Archi ; vide = identique à la clé.</summary>
    public string? ArchiName { get; set; }

    public string ArchiPropertyName => string.IsNullOrEmpty(ArchiName) ? Key : ArchiName;
    public bool AppliesToType(string typeKey) => AppliesTo.Count == 0 || AppliesTo.Contains(typeKey);
}

/// <summary>
/// Styles de repli du rendu soigné des vues, appliqués quand le métamodèle ne précise rien (type créé à la main).
/// Les têtes reprennent la notation ArchiMate pour que les lecteurs d'Archi s'y retrouvent.
/// </summary>
public static class DrawingDefaults
{
    public static readonly string[] Heads = { "fleche", "ouverte", "triangle", "losange", "losange-creux", "point", "aucune" };
    public static readonly string[] Templates = { "carte", "rond", "zone" };
    public static readonly string[] Lines = { "plein", "tirets", "pointillés" };
    public static readonly string[] Shapes = { "rectangle", "arrondi", "ellipse", "hexagone" };

    public const string FallbackTemplate = "carte";
    public const string FallbackGlyph = "defaut";
    public const string FallbackLine = "plein";
    public const string FallbackHead = "aucune";
}

public sealed class MetamodelSettings
{
    /// <summary>Nom du dossier dont les sous-dossiers portent le nom des projets.</summary>
    public string ProjectFolderLabel { get; set; } = "Projets";
    /// <summary>Propriétés d'une relation de flux affichées sur sa pastille : protocole et chiffrement.</summary>
    public string FlowProtocolKey { get; set; } = "connect.protocol";
    public string FlowEncryptionKey { get; set; } = "security.encryption";
}

public sealed class Metamodel
{
    public List<Layer> Layers { get; set; } = new();
    public List<ObjectType> ObjectTypes { get; set; } = new();
    public List<RelationshipType> RelationshipTypes { get; set; } = new();
    public List<PropertyDefinition> PropertyDefinitions { get; set; } = new();
    public MetamodelSettings Settings { get; set; } = new();
    public ScoringGrid Scoring { get; set; } = ScoringGrid.Default();
    public Transcoding Transcoding { get; set; } = new();

    public ObjectType? Type(string key) => ObjectTypes.FirstOrDefault(t => t.Key == key);
    public RelationshipType? RelType(string key) => RelationshipTypes.FirstOrDefault(t => t.Key == key);
    public PropertyDefinition? Property(string key) => PropertyDefinitions.FirstOrDefault(p => p.Key == key);
    public string LayerOf(string typeKey) => Type(typeKey)?.LayerKey ?? "autre";

    // ---- Rôles ----

    public bool IsA(string typeKey, string role) => Type(typeKey)?.Roles.Contains(role) == true;
    public IEnumerable<ObjectType> TypesWith(string role) => ObjectTypes.Where(t => t.Roles.Contains(role));
    /// <summary>Clés des types portant un rôle : à calculer une fois avant de parcourir beaucoup d'objets.</summary>
    public HashSet<string> TypeKeysWith(string role) => TypesWith(role).Select(t => t.Key).ToHashSet();
    /// <summary>Premier type actif portant le rôle (à défaut, un type archivé).</summary>
    public ObjectType? FirstTypeWith(string role) => TypesWith(role).FirstOrDefault(t => !t.Archived) ?? TypesWith(role).FirstOrDefault();
    public bool RelIsA(string relTypeKey, string role) => RelType(relTypeKey)?.Roles.Contains(role) == true;
    public HashSet<string> RelTypeKeysWith(string role) => RelationshipTypes.Where(t => t.Roles.Contains(role)).Select(t => t.Key).ToHashSet();

    public PropertyDefinition EnsureProperty(string key, string? label = null, string source = "saisie")
    {
        var def = Property(key);
        if (def is null)
        {
            def = new PropertyDefinition { Key = key, Label = label ?? key, Source = source };
            PropertyDefinitions.Add(def);
        }
        return def;
    }

    /// <summary>Visuel effectif hors diagramme : celui de l'objet s'il est défini, sinon celui du type.</summary>
    public Visual EffectiveVisual(ArchObject o) => Merge(Type(o.TypeKey)?.Visual ?? new Visual(), o.VisualOverride);

    public static Visual Merge(Visual baseVisual, Visual? over) => over is null ? baseVisual : new Visual
    {
        Shape = over.Shape ?? baseVisual.Shape,
        Fill = over.Fill ?? baseVisual.Fill,
        Stroke = over.Stroke ?? baseVisual.Stroke,
        TextColor = over.TextColor ?? baseVisual.TextColor,
        Icon = over.Icon ?? baseVisual.Icon,
        Template = over.Template ?? baseVisual.Template,
    };

    /// <summary>Vérifie qu'une relation respecte les couples autorisés ; null si elle est conforme.</summary>
    public string? CheckRelationship(string relTypeKey, string sourceTypeKey, string targetTypeKey)
    {
        var rt = RelType(relTypeKey);
        if (rt is null) return $"type de relation inconnu : {relTypeKey}";
        if (rt.AllowedPairs.Count == 0) return null;
        return rt.AllowedPairs.Contains($"{sourceTypeKey}>{targetTypeKey}") ? null
            : $"relation {rt.Label} non autorisée entre {sourceTypeKey} et {targetTypeKey}";
    }

    public static Metamodel Default() => MetamodelDefault.Create();

    /// <summary>
    /// Clé dérivée d'un libellé : minuscules, sans accents, tout ce qui n'est pas alphanumérique devient « - ».
    /// Un suffixe « -2 », « -3 »… est ajouté si la clé existe déjà.
    /// </summary>
    public static string DeriveKey(string label, IEnumerable<string> existing)
    {
        var taken = existing.ToHashSet();
        var key = Text.Slug(label);
        if (key == "") key = "type";
        var candidate = key;
        for (var n = 2; taken.Contains(candidate); n++) candidate = $"{key}-{n}";
        return candidate;
    }
}
