using System.Text.Json;
using System.Text.Json.Nodes;

namespace AeTool.Core;

/// <summary>
/// Migration des fichiers de référentiel. La version se lit dans le JSON avant désérialisation : une propriété absente
/// prendrait la valeur d'initialisation et ferait passer un ancien fichier pour un fichier à jour.
/// Version 1 : les types de l'outil étaient les concepts ArchiMate. Version 2 : métamodèle propre et matrice de transcodification.
/// </summary>
public static class RepositoryUpgrader
{
    public static Repository Read(Stream json, out bool upgraded) => Read(json, out upgraded, out _);

    /// <summary><paramref name="fromVersion"/> : version trouvée dans le fichier, avant migration (sert à nommer la copie de l'original).</summary>
    public static Repository Read(Stream json, out bool upgraded, out int fromVersion)
    {
        var root = JsonNode.Parse(json) as JsonObject ?? throw new JsonException("Le référentiel n'est pas un objet JSON.");
        var version = fromVersion = root["SchemaVersion"]?.GetValue<int>() ?? 1;
        upgraded = version < Repository.CurrentSchemaVersion;
        if (version < 2) MigrateToV2(root);
        if (version < 3) MigrateToV3(root);
        return root.Deserialize<Repository>() ?? new Repository();
    }

    /// <summary>Indique si un métamodèle exporté est au format 1 (sans matrice de transcodification).</summary>
    public static bool IsV1Metamodel(JsonNode metamodel) => metamodel is JsonObject o && o["Transcoding"] is null;

    // ------------------------------------------------------------ 2 -> 3

    /// <summary>
    /// Avant le circuit de validation, tout objet entrait en « Brouillon ». Le modèle Archi étant la référence de l'architecte,
    /// ses objets passent à « Validé » ; ceux que la reprise du classeur d'urbanisation a créés passent à « Proposé ».
    /// Seul l'état « Brouillon » est touché : un état déjà posé à la main n'est jamais écrasé.
    /// </summary>
    private static void MigrateToV3(JsonObject root)
    {
        var now = DateTime.UtcNow;
        if (root["Objects"] is JsonObject all)
            foreach (var (_, item) in all)
            {
                if (item is not JsonObject o || o["ValidationStatus"]?.GetValue<string>() is not (null or Validation.Draft)) continue;
                switch (o["Source"]?.GetValue<string>())
                {
                    case "archi":
                        o["ValidationStatus"] = Validation.Validated;
                        o["ValidatedBy"] = MigrationAuthor;
                        o["ValidatedAt"] = now;
                        break;
                    case "urba":
                        o["ValidationStatus"] = Validation.Proposed;
                        o["ProposedBy"] = MigrationAuthor;
                        o["ProposedAt"] = now;
                        break;
                }
            }
        root["SchemaVersion"] = 3;
    }

    /// <summary>Auteur porté sur les états posés par la migration : ils ne viennent d'aucune personne.</summary>
    public const string MigrationAuthor = "migration";

    // ------------------------------------------------------------ 1 -> 2

    private static void MigrateToV2(JsonObject root)
    {
        var old = root["Metamodel"] is JsonNode m ? m.Deserialize<OldMetamodel>() ?? new OldMetamodel() : new OldMetamodel();
        var map = new KeyMap();
        root["Metamodel"] = JsonSerializer.SerializeToNode(MigrateMetamodel(old, map, root["Metamodel"]?["Scoring"]));
        MigrateItems(root["Objects"], map.ObjectKey, Archimate.IsElement);
        MigrateItems(root["Relationships"], map.RelationKey, Archimate.IsRelationship);
        root["SchemaVersion"] = 2;
    }

    /// <summary>Migre un métamodèle exporté en version 1 (importé dans un référentiel déjà en version 2).</summary>
    public static Metamodel MigrateMetamodel(JsonNode v1)
    {
        var old = v1.Deserialize<OldMetamodel>() ?? new OldMetamodel();
        return MigrateMetamodel(old, new KeyMap(), v1["Scoring"]);
    }

    /// <summary>Ancienne clé (= concept ArchiMate) devenue concept d'origine ; clé du métamodèle par défaut correspondante.</summary>
    private static void MigrateItems(JsonNode? items, Func<string, string> newKey, Func<string?, bool> isConcept)
    {
        if (items is not JsonObject all) return;
        foreach (var (_, item) in all)
        {
            if (item is not JsonObject o) continue;
            var key = o["TypeKey"]?.GetValue<string>() ?? "";
            if (isConcept(key) && string.IsNullOrEmpty(o["ArchimateType"]?.GetValue<string>())) o["ArchimateType"] = key;
            o["TypeKey"] = newKey(key);
        }
    }

    private static Metamodel MigrateMetamodel(OldMetamodel old, KeyMap map, JsonNode? scoring)
    {
        var m = MetamodelDefault.Create();
        foreach (var layer in old.Layers.Where(l => m.Layers.All(x => x.Key != l.Key))) m.Layers.Add(new Layer { Key = layer.Key, Label = layer.Label, Order = m.Layers.Count });
        MigrateObjectTypes(old, map, m);
        MigrateRelationshipTypes(old, map, m);
        MigrateRolesFromSettings(old.Settings, map, m);
        foreach (var p in old.PropertyDefinitions)
            m.PropertyDefinitions.Add(new PropertyDefinition
            {
                Key = p.Key, Label = p.Label, DataType = ConvertDataType(p.DataType), AllowedValues = p.AllowedValues, Source = p.Source,
                Displayable = p.Displayable,
            });
        m.Settings = new MetamodelSettings
        {
            ProjectFolderLabel = old.Settings.ProjectFolderLabel, FlowProtocolKey = old.Settings.FlowProtocolKey,
            FlowEncryptionKey = old.Settings.FlowEncryptionKey,
        };
        if (scoring is not null) m.Scoring = scoring.Deserialize<ScoringGrid>() ?? m.Scoring;
        return m;
    }

    private static void MigrateObjectTypes(OldMetamodel old, KeyMap map, Metamodel m)
    {
        foreach (var t in old.ObjectTypes)
        {
            var key = map.ObjectKey(t.Key);
            var target = m.Type(key);
            var layer = m.Layers.Any(l => l.Key == t.LayerKey) ? t.LayerKey : "autre";
            if (target is null)
            {
                // Type hors ArchiMate : repris tel quel, sans règle. Il garde ses rôles d'avant, qui découlaient de sa couche.
                target = new ObjectType { Key = key, Roles = layer == "donnees" ? new() { Roles.Data } : layer == "technique" ? new() { Roles.Infrastructure } : new() };
                m.ObjectTypes.Add(target);
            }
            target.Label = string.IsNullOrWhiteSpace(t.Label) ? target.Label : t.Label;
            target.LayerKey = layer;
            target.Archived = t.Archived;
            var v = t.Visual;
            if (v is null) continue;
            target.Visual = new Visual
            {
                Shape = v.Shape ?? target.Visual.Shape, Fill = v.Fill ?? target.Visual.Fill, Stroke = v.Stroke ?? target.Visual.Stroke,
                TextColor = v.TextColor ?? target.Visual.TextColor, Icon = v.Icon ?? target.Visual.Icon, Template = v.Template ?? target.Visual.Template,
            };
        }
    }

    private static void MigrateRelationshipTypes(OldMetamodel old, KeyMap map, Metamodel m)
    {
        foreach (var t in old.RelationshipTypes)
        {
            var key = map.RelationKey(t.Key);
            var target = m.RelType(key);
            if (target is null) m.RelationshipTypes.Add(target = new RelationshipType { Key = key });
            target.Label = string.IsNullOrWhiteSpace(t.Label) ? target.Label : t.Label;
            target.LineStyle = string.IsNullOrWhiteSpace(t.LineStyle) ? target.LineStyle : t.LineStyle;
            target.SoftLine = t.SoftLine ?? target.SoftLine;
            target.Head = t.Head ?? target.Head;
            target.AllowedPairs = t.AllowedPairs.Select(pair => TranslatePair(pair, map)).Distinct().ToList();
        }
    }

    private static string TranslatePair(string pair, KeyMap map)
    {
        var parts = pair.Split('>');
        return parts.Length == 2 ? $"{map.ObjectKey(parts[0])}>{map.ObjectKey(parts[1])}" : pair;
    }

    /// <summary>Les réglages d'avant (types d'application, de projet, de capacité, de fond de carte) deviennent des rôles.</summary>
    private static void MigrateRolesFromSettings(OldSettings s, KeyMap map, Metamodel m)
    {
        void Assign(string role, IEnumerable<string> oldKeys)
        {
            var keys = oldKeys.Select(map.ObjectKey).ToHashSet();
            foreach (var t in m.ObjectTypes)
            {
                t.Roles.Remove(role);
                if (keys.Contains(t.Key)) t.Roles.Add(role);
            }
        }
        Assign(Roles.Application, s.ApplicationTypeKeys);
        Assign(Roles.Capability, new[] { s.CapabilityTypeKey });
        Assign(Roles.Project, new[] { s.ProjectTypeKey });
        // Un projet est déjà exclu des impacts par son rôle : le fond de carte ne le répète pas.
        Assign(Roles.Backdrop, s.ImpactExcludedTypeKeys.Where(k => k != s.ProjectTypeKey));
    }

    private static string ConvertDataType(string? old) => old switch
    {
        "number" or PropertyTypes.Number => PropertyTypes.Number,
        "date" => PropertyTypes.Date,
        "boolean" or PropertyTypes.Boolean => PropertyTypes.Boolean,
        PropertyTypes.List => PropertyTypes.List,
        _ => PropertyTypes.Text,
    };

    /// <summary>Correspondance ancienne clé → nouvelle clé, pour les objets et pour les relations.</summary>
    private sealed class KeyMap
    {
        private readonly Dictionary<string, string> _objects;
        private readonly Dictionary<string, string> _relations;
        private readonly HashSet<string> _defaultObjectKeys;
        private readonly HashSet<string> _defaultRelationKeys;

        public KeyMap()
        {
            var d = MetamodelDefault.Create();
            _objects = d.Transcoding.Elements.Where(r => r.Principal).ToDictionary(r => r.ArchimateType, r => r.TypeKey);
            _relations = d.Transcoding.Relations.Where(r => r.Principal).ToDictionary(r => r.ArchimateType, r => r.TypeKey);
            _defaultObjectKeys = d.ObjectTypes.Select(t => t.Key).ToHashSet();
            _defaultRelationKeys = d.RelationshipTypes.Select(t => t.Key).ToHashSet();
        }

        // Un type maison dont la clé coïncide avec une clé du nouveau métamodèle est écarté de celle-ci : ses objets ne se mêlent pas aux autres.
        public string ObjectKey(string old) => _objects.TryGetValue(old, out var k) ? k : _defaultObjectKeys.Contains(old) ? old + "-v1" : old;
        public string RelationKey(string old) => _relations.TryGetValue(old, out var k) ? k : _defaultRelationKeys.Contains(old) ? old + "-v1" : old;
    }

    // Forme des données en version 1, lue telle quelle.
    private sealed class OldMetamodel
    {
        public List<Layer> Layers { get; set; } = new();
        public List<OldObjectType> ObjectTypes { get; set; } = new();
        public List<OldRelationshipType> RelationshipTypes { get; set; } = new();
        public List<OldProperty> PropertyDefinitions { get; set; } = new();
        public OldSettings Settings { get; set; } = new();
    }

    private sealed class OldObjectType
    {
        public string Key { get; set; } = "";
        public string Label { get; set; } = "";
        public string LayerKey { get; set; } = "autre";
        public Visual? Visual { get; set; }
        public bool Archived { get; set; }
    }

    private sealed class OldRelationshipType
    {
        public string Key { get; set; } = "";
        public string Label { get; set; } = "";
        public string LineStyle { get; set; } = "plein";
        public string? SoftLine { get; set; }
        public string? Head { get; set; }
        public List<string> AllowedPairs { get; set; } = new();
    }

    private sealed class OldProperty
    {
        public string Key { get; set; } = "";
        public string? Label { get; set; }
        public string? DataType { get; set; }
        public List<string> AllowedValues { get; set; } = new();
        public string Source { get; set; } = "saisie";
        public bool Displayable { get; set; }
    }

    private sealed class OldSettings
    {
        public string ProjectFolderLabel { get; set; } = "Projets";
        public string ProjectTypeKey { get; set; } = "WorkPackage";
        public List<string> ApplicationTypeKeys { get; set; } = new() { "ApplicationComponent" };
        public string CapabilityTypeKey { get; set; } = "Capability";
        public List<string> ImpactExcludedTypeKeys { get; set; } = new() { "Capability", "Grouping", "Location", "WorkPackage", "AndJunction", "OrJunction" };
        public string FlowProtocolKey { get; set; } = "connect.protocol";
        public string FlowEncryptionKey { get; set; } = "security.encryption";
    }
}
