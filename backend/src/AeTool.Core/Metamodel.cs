namespace AeTool.Core;

// Le métamodèle est une donnée : couches, types, relations, propriétés, visuels et grille de notation
// se modifient sans toucher au code. Le gabarit par défaut est aligné sur ArchiMate 3.

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
    public string LayerKey { get; set; } = "autre";
    /// <summary>Concept ArchiMate d'export. Vide = type hors ArchiMate, exporté selon FallbackArchimateType.</summary>
    public string? ArchimateType { get; set; }
    public Visual Visual { get; set; } = new();
    public bool Archived { get; set; }
}

public sealed class RelationshipType
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public string? ArchimateType { get; set; }
    /// <summary>Trait du rendu fidèle à Archi : plein | tirets | pointillés.</summary>
    public string LineStyle { get; set; } = "plein";
    /// <summary>Rendu soigné : trait (plein | tirets | pointillés) et tête. Vide = style par défaut du type ArchiMate.</summary>
    public string? SoftLine { get; set; }
    /// <summary>fleche | ouverte | triangle | losange | losange-creux | point | aucune.</summary>
    public string? Head { get; set; }
    /// <summary>Couples « TypeSource&gt;TypeCible » autorisés. Vide = aucun contrôle.</summary>
    public List<string> AllowedPairs { get; set; } = new();
}

public sealed class PropertyDefinition
{
    public string Key { get; set; } = "";
    public string? Label { get; set; }
    public string DataType { get; set; } = "string";
    public List<string> AllowedValues { get; set; } = new();
    public string Source { get; set; } = "saisie";
    /// <summary>Proposée comme calque sur les vues : sa valeur s'affiche en pastille sur les cartes et peut les colorer.</summary>
    public bool Displayable { get; set; }
}

/// <summary>
/// Styles par défaut du rendu soigné des vues, appliqués quand le métamodèle ne précise rien.
/// Les têtes reprennent la notation ArchiMate pour que les lecteurs d'Archi s'y retrouvent.
/// </summary>
public static class DrawingDefaults
{
    public static readonly string[] Heads = { "fleche", "ouverte", "triangle", "losange", "losange-creux", "point", "aucune" };
    public static readonly string[] Templates = { "carte", "rond", "zone" };

    public static (string Line, string Head) Relation(string? archimateType) => archimateType switch
    {
        "Flow" or "Triggering" => ("plein", "fleche"),
        "Serving" => ("plein", "ouverte"),
        "Realization" => ("tirets", "triangle"),
        "Specialization" => ("plein", "triangle"),
        "Access" => ("pointillés", "aucune"),
        "Influence" => ("tirets", "ouverte"),
        "Composition" => ("plein", "losange"),
        "Aggregation" => ("plein", "losange-creux"),
        "Assignment" => ("plein", "point"),
        _ => ("plein", "aucune"),
    };

    public static string Template(string typeKey) => typeKey switch
    {
        "BusinessActor" or "BusinessRole" or "Stakeholder" or "BusinessObject" or "DataObject" or "Artifact"
            or "Contract" or "Representation" or "Deliverable" => "rond",
        "Grouping" or "Location" => "zone",
        _ => "carte",
    };

    public static string Glyph(string typeKey) => typeKey switch
    {
        "ApplicationComponent" or "ApplicationCollaboration" => "application",
        "BusinessActor" or "Stakeholder" => "acteur",
        "BusinessRole" => "role",
        "BusinessObject" or "Contract" or "Representation" or "Product" => "objet",
        "DataObject" => "donnee",
        "Artifact" or "Deliverable" => "artefact",
        "Node" or "Facility" => "noeud",
        "Device" or "Equipment" => "equipement",
        "SystemSoftware" => "logiciel",
        "CommunicationNetwork" or "Path" or "DistributionNetwork" => "reseau",
        "Capability" or "Resource" => "capacite",
        "Location" => "lieu",
        "WorkPackage" or "Plateau" or "Gap" or "CourseOfAction" => "projet",
        "Goal" or "Outcome" or "Driver" or "Principle" or "Value" or "Meaning" or "Assessment" => "objectif",
        "Requirement" or "Constraint" => "exigence",
        "Grouping" => "zone",
        _ when typeKey.EndsWith("Service") => "service",
        _ when typeKey.EndsWith("Function") => "fonction",
        _ when typeKey.EndsWith("Interface") => "interface",
        _ when typeKey.EndsWith("Event") => "evenement",
        _ when typeKey.EndsWith("Process") || typeKey.EndsWith("Interaction") || typeKey == "ValueStream" => "processus",
        _ => "defaut",
    };
}

public sealed class MetamodelSettings
{
    /// <summary>Nom du dossier dont les sous-dossiers portent le nom des projets.</summary>
    public string ProjectFolderLabel { get; set; } = "Projets";
    public string ProjectTypeKey { get; set; } = "WorkPackage";
    public List<string> ApplicationTypeKeys { get; set; } = new() { "ApplicationComponent" };
    public string CapabilityTypeKey { get; set; } = "Capability";
    /// <summary>Types servant de fond de carte : présents dans une vue projet, ils ne valent pas impact.</summary>
    public List<string> ImpactExcludedTypeKeys { get; set; } = new() { "Capability", "Grouping", "Location", "WorkPackage", "AndJunction", "OrJunction" };
    public string FallbackArchimateType { get; set; } = "Grouping";
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

    public ObjectType? Type(string key) => ObjectTypes.FirstOrDefault(t => t.Key == key);
    public RelationshipType? RelType(string key) => RelationshipTypes.FirstOrDefault(t => t.Key == key);
    public PropertyDefinition? Property(string key) => PropertyDefinitions.FirstOrDefault(p => p.Key == key);
    public string LayerOf(string typeKey) => Type(typeKey)?.LayerKey ?? "autre";

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

    public static Metamodel Default()
    {
        var m = new Metamodel();
        string[][] layers =
        {
            new[] { "strategie", "Stratégie et capacités" }, new[] { "metier", "Métier" },
            new[] { "fonctionnelle", "Fonctionnelle" }, new[] { "applicative", "Applicative" },
            new[] { "donnees", "Données" }, new[] { "technique", "Technique et infrastructure" },
            new[] { "transformation", "Transformation" }, new[] { "motivation", "Motivation" },
            new[] { "autre", "Autre" },
        };
        for (var i = 0; i < layers.Length; i++) m.Layers.Add(new Layer { Key = layers[i][0], Label = layers[i][1], Order = i });

        void Add(string layer, string fill, string shape, params string[] types)
        {
            foreach (var t in types)
            {
                var parts = t.Split('|');
                m.ObjectTypes.Add(new ObjectType
                {
                    Key = parts[0], Label = parts[1], LayerKey = layer, ArchimateType = parts[0],
                    Visual = new Visual { Shape = shape, Fill = fill, Stroke = "#5c5c5c", TextColor = "#000000" },
                });
            }
        }
        Add("strategie", "#f5deaa", "arrondi", "Capability|Capacité", "ValueStream|Chaîne de valeur", "Resource|Ressource", "CourseOfAction|Plan d'action");
        Add("motivation", "#ccccff", "rectangle", "Stakeholder|Partie prenante", "Driver|Moteur", "Assessment|Constat", "Goal|Objectif",
            "Outcome|Résultat", "Principle|Principe", "Requirement|Exigence", "Constraint|Contrainte", "Meaning|Signification", "Value|Valeur");
        Add("metier", "#ffffb5", "rectangle", "BusinessActor|Acteur", "BusinessRole|Rôle", "BusinessCollaboration|Collaboration métier",
            "BusinessInterface|Interface métier", "BusinessProcess|Processus", "BusinessFunction|Fonction métier",
            "BusinessInteraction|Interaction métier", "BusinessEvent|Événement métier", "BusinessService|Service métier",
            "Contract|Contrat", "Representation|Représentation", "Product|Produit");
        Add("fonctionnelle", "#d9e7f5", "rectangle", "Grouping|Zone / quartier", "ApplicationFunction|Bloc fonctionnel");
        Add("applicative", "#b5ffff", "rectangle", "ApplicationComponent|Application", "ApplicationCollaboration|Collaboration applicative",
            "ApplicationInterface|Interface applicative", "ApplicationInteraction|Interaction applicative",
            "ApplicationProcess|Traitement applicatif", "ApplicationEvent|Événement applicatif", "ApplicationService|Service applicatif");
        Add("donnees", "#ffe9b5", "rectangle", "BusinessObject|Objet métier", "DataObject|Objet de données");
        Add("technique", "#c9e7b7", "rectangle", "Node|Nœud", "Device|Équipement informatique", "SystemSoftware|Logiciel système",
            "TechnologyCollaboration|Collaboration technique", "TechnologyInterface|Interface technique", "Path|Chemin",
            "CommunicationNetwork|Réseau", "TechnologyFunction|Fonction technique", "TechnologyProcess|Traitement technique",
            "TechnologyInteraction|Interaction technique", "TechnologyEvent|Événement technique", "TechnologyService|Service technique",
            "Artifact|Artefact", "Equipment|Équipement industriel (OT)", "Facility|Installation", "DistributionNetwork|Réseau de distribution",
            "Material|Matière");
        Add("transformation", "#ffe0e0", "rectangle", "WorkPackage|Projet", "Deliverable|Livrable", "ImplementationEvent|Jalon",
            "Plateau|Plateau", "Gap|Écart");
        Add("autre", "#f0f0f0", "rectangle", "Location|Lieu", "AndJunction|Jonction ET", "OrJunction|Jonction OU");

        string[][] rels =
        {
            new[] { "Composition", "Composition", "plein" }, new[] { "Aggregation", "Agrégation", "plein" },
            new[] { "Assignment", "Affectation", "plein" }, new[] { "Realization", "Réalisation", "pointillés" },
            new[] { "Serving", "Sert", "plein" }, new[] { "Access", "Accès", "pointillés" },
            new[] { "Influence", "Influence", "tirets" }, new[] { "Triggering", "Déclenche", "plein" },
            new[] { "Flow", "Flux", "tirets" }, new[] { "Specialization", "Spécialisation", "plein" },
            new[] { "Association", "Association", "plein" },
        };
        foreach (var r in rels)
            m.RelationshipTypes.Add(new RelationshipType { Key = r[0], Label = r[1], ArchimateType = r[0], LineStyle = r[2] });
        return m;
    }
}
