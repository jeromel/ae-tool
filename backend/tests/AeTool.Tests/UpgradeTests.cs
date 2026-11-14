using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AeTool.Api.Storage;
using AeTool.Core;
using static AeTool.Tests.Harness;

namespace AeTool.Tests;

public static class UpgradeTests
{
    /// <summary>Petit référentiel au format 1 : types = concepts ArchiMate, réglages par clés, aucune propriété SchemaVersion.</summary>
    private const string V1 = """
        {
          "ModelId": "id-v1", "ModelName": "Ancien", "Language": "fr",
          "Metamodel": {
            "Layers": [{"Key":"applicative","Label":"Applicative","Order":0},{"Key":"technique","Label":"Technique","Order":1},{"Key":"autre","Label":"Autre","Order":2}],
            "ObjectTypes": [
              {"Key":"ApplicationComponent","Label":"Appli","LayerKey":"applicative","ArchimateType":"ApplicationComponent","Visual":{"Shape":"arrondi","Fill":"#123456"},"Archived":false},
              {"Key":"Capability","Label":"Capacité","LayerKey":"autre","ArchimateType":"Capability","Visual":{},"Archived":false},
              {"Key":"Node","Label":"Nœud","LayerKey":"technique","ArchimateType":"Node","Visual":{"Fill":"#c9e7b7"},"Archived":true},
              {"Key":"ZoneOT","Label":"Zone OT","LayerKey":"technique","ArchimateType":null,"Visual":{"Fill":"#eeeeee","Shape":"rectangle"},"Archived":false}
            ],
            "RelationshipTypes": [
              {"Key":"Realization","Label":"Réalise","ArchimateType":"Realization","LineStyle":"pointillés","SoftLine":"tirets","Head":"ouverte","AllowedPairs":["ApplicationComponent>Capability","ZoneOT>Capability"]},
              {"Key":"Flow","Label":"Flux","ArchimateType":"Flow","LineStyle":"tirets","SoftLine":null,"Head":null,"AllowedPairs":[]}
            ],
            "PropertyDefinitions": [
              {"Key":"lifecycle.state","Label":"Cycle de vie","DataType":"string","AllowedValues":["Actif","Retiré"],"Source":"archi","Displayable":true}
            ],
            "Settings": {"ProjectFolderLabel":"Chantiers","ProjectTypeKey":"WorkPackage","ApplicationTypeKeys":["ApplicationComponent","ZoneOT"],"CapabilityTypeKey":"Capability",
              "ImpactExcludedTypeKeys":["Capability","Location"],"FallbackArchimateType":"Grouping","FlowProtocolKey":"proto","FlowEncryptionKey":"chiffre"},
            "Scoring": {"Criteria":[{"Code":"C1","Label":"Santé","SubCriteria":[],"ScoredBy":null,"RedTrigger":null}]}
          },
          "Objects": {
            "o1": {"Id":"o1","TypeKey":"ApplicationComponent","Name":"Appli 1","Properties":[{"Key":"lifecycle.state","Value":"Actif"}],"Source":"archi","ArchimateType":null},
            "o2": {"Id":"o2","TypeKey":"Capability","Name":"Cap","Properties":[],"Source":"archi"},
            "o3": {"Id":"o3","TypeKey":"ZoneOT","Name":"Zone 3","Properties":[],"Source":"saisie"}
          },
          "Relationships": { "r1": {"Id":"r1","TypeKey":"Realization","SourceId":"o1","TargetId":"o2","Properties":[],"Source":"archi"} },
          "Diagrams": {}, "Folders": [], "Impacts": [], "Assessments": {},
          "Journal": [{"At":"2025-01-01T00:00:00Z","User":"moi","Action":"Objet créé"}]
        }
        """;

    private static Repository Read(string json, out bool upgraded) => RepositoryUpgrader.Read(new MemoryStream(Encoding.UTF8.GetBytes(json)), out upgraded);

    public static void Run(string[] args)
    {
        Console.WriteLine("Migration du format 1 vers le format 2");

        Test("fichier sans SchemaVersion : lu en version 1, objets et relations migrés avec leur concept d'origine", () =>
        {
            var repo = Read(V1, out var upgraded);
            True(upgraded, "la migration doit être signalée");
            Equal(Repository.CurrentSchemaVersion, repo.SchemaVersion, "version");
            Equal("application", repo.Objects["o1"].TypeKey, "type de l'application");
            Equal("ApplicationComponent", repo.Objects["o1"].ArchimateType, "concept de l'application");
            Equal("capacite", repo.Objects["o2"].TypeKey, "type de la capacité");
            Equal("ZoneOT", repo.Objects["o3"].TypeKey, "type hors ArchiMate conservé");
            Equal(null, repo.Objects["o3"].ArchimateType, "pas de concept pour un type maison");
            Equal("realisation", repo.Relationships["r1"].TypeKey, "type de relation");
            Equal("Realization", repo.Relationships["r1"].ArchimateType, "concept de relation");
            Equal("Actif", repo.Objects["o1"].Get("lifecycle.state"), "propriétés conservées");
            Equal(1, repo.Journal.Count, "journal inchangé");
        });
        Test("métamodèle reporté : libellés, visuels non vides, archivage, relations, couples, propriétés, réglages, notation", () =>
        {
            var m = Read(V1, out _).Metamodel;
            var app = m.Type("application")!;
            Equal("Appli", app.Label, "libellé");
            Equal(("arrondi", "#123456"), (app.Visual.Shape!, app.Visual.Fill!), "visuel repris");
            Equal("carte", app.Visual.Template, "gabarit du métamodèle par défaut conservé");
            True(m.Type("noeud")!.Archived, "archivage");
            Equal("autre", m.Type("capacite")!.LayerKey, "couche reportée");
            var zone = m.Type("ZoneOT")!;
            True(zone.Roles.Contains(Roles.Infrastructure) && zone.Roles.Contains(Roles.Application), "rôles déduits de la couche et des réglages");
            True(!m.Transcoding.Elements.Any(r => r.TypeKey == "ZoneOT"), "pas de règle pour un type hors ArchiMate");
            var rel = m.RelType("realisation")!;
            Equal(("Réalise", "tirets", "ouverte"), (rel.Label, rel.SoftLine!, rel.Head!), "relation reportée");
            Equal("application>capacite,ZoneOT>capacite", string.Join(",", rel.AllowedPairs), "couples traduits");
            Equal("Est réalisé par", rel.ReverseLabel, "lecture inverse du nouveau métamodèle");
            var def = m.Property("lifecycle.state")!;
            Equal((PropertyTypes.Text, true, "Actif,Retiré"), (def.DataType, def.Displayable, string.Join(",", def.AllowedValues)), "propriété");
            Equal(("Chantiers", "proto", "chiffre"), (m.Settings.ProjectFolderLabel, m.Settings.FlowProtocolKey, m.Settings.FlowEncryptionKey), "réglages");
            Equal(1, m.Scoring.Criteria.Count, "grille de notation");
            Equal("capacite", m.FirstTypeWith(Roles.Capability)!.Key, "rôle de capacité issu des réglages");
            True(m.IsA("capacite", Roles.Backdrop) && !m.IsA("zone", Roles.Backdrop), "fond de carte issu des réglages");
            Equal(0, Transcoder.Errors(m, m.Transcoding).Count, "matrice cohérente");
        });
        Test("un fichier déjà en version 2 n'est pas migré, et la migration est stable", () =>
        {
            var migrated = Read(V1, out _);
            var json = JsonSerializer.Serialize(migrated);
            var again = Read(json, out var upgraded);
            True(!upgraded, "pas de seconde migration");
            Equal(Canonical(migrated), Canonical(again));
            Equal(migrated.Metamodel.ObjectTypes.Count, again.Metamodel.ObjectTypes.Count, "types");
        });
        Test("chargement : le fichier d'origine est copié à côté avant réécriture, puis le fichier est en version 2", () =>
        {
            var dir = Path.Combine(Path.GetTempPath(), "ae-upgrade-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var path = Path.Combine(dir, "referentiel.json");
                File.WriteAllText(path, V1);
                var repo = new JsonFileStore(path).Load();
                Equal("application", repo.Objects["o1"].TypeKey, "type migré");
                Equal(V1, File.ReadAllText(Path.Combine(dir, "referentiel.avant-migration-v1.json")), "copie de l'original");
                Equal(Repository.CurrentSchemaVersion, JsonNode.Parse(File.ReadAllText(path))!["SchemaVersion"]!.GetValue<int>(), "version écrite");
                new JsonFileStore(path).Load();
                Equal(1, Directory.GetFiles(dir, "*avant-migration*").Length, "aucune seconde copie");
            }
            finally { Directory.Delete(dir, recursive: true); }
        });
        Test("métamodèle exporté en version 1 : reconnu, migré", () =>
        {
            var v1 = JsonNode.Parse(V1)!["Metamodel"]!;
            True(RepositoryUpgrader.IsV1Metamodel(v1), "version 1 reconnue");
            True(!RepositoryUpgrader.IsV1Metamodel(JsonSerializer.SerializeToNode(Metamodel.Default())!), "version 2 reconnue");
            Equal("application", RepositoryUpgrader.MigrateMetamodel(v1).Type("application")!.Key, "type migré");
        });

        var index = Array.IndexOf(args, "--v1");
        if (index >= 0 && index + 1 < args.Length) RealFile(args[index + 1]);
    }

    private static int Count(JsonNode? items, Func<JsonNode, bool> where) => items is JsonObject o ? o.Count(kv => kv.Value is not null && where(kv.Value)) : 0;

    /// <summary>Migre le vrai fichier d'un utilisateur et compare les effectifs à ce que contenait le fichier d'origine.</summary>
    private static void RealFile(string path)
    {
        Console.WriteLine($"Migration du fichier réel : {Path.GetFileName(path)}");
        var original = JsonNode.Parse(File.ReadAllText(path))!;
        Repository repo = null!;
        Test("migration du fichier réel : effectifs identiques", () =>
        {
            using var s = File.OpenRead(path);
            repo = RepositoryUpgrader.Read(s, out var upgraded);
            True(upgraded, "le fichier doit être en version 1");
            Equal(original["Objects"]!.AsObject().Count, repo.Objects.Count, "objets");
            Equal(original["Relationships"]!.AsObject().Count, repo.Relationships.Count, "relations");
            Equal(original["Diagrams"]!.AsObject().Count, repo.Diagrams.Count, "vues");
            Equal(original["Impacts"]!.AsArray().Count, repo.Impacts.Count, "impacts");
            Equal(original["Assessments"]!.AsObject().Count, repo.Assessments.Count, "notations");
            Equal(original["Journal"]!.AsArray().Count, repo.Journal.Count, "journal");
            Equal(original["Metamodel"]!["PropertyDefinitions"]!.AsArray().Count, repo.Metamodel.PropertyDefinitions.Count, "propriétés");
        });
        Test("migration du fichier réel : types, concepts d'origine et métamodèle cohérents", () =>
        {
            True(repo.Objects.Values.All(o => repo.Metamodel.Type(o.TypeKey) is not null), "objet d'un type inconnu");
            True(repo.Relationships.Values.All(r => repo.Metamodel.RelType(r.TypeKey) is not null), "relation d'un type inconnu");
            True(repo.Objects.Values.All(o => Archimate.IsElement(o.ArchimateType)), "concept d'origine manquant");
            True(repo.Relationships.Values.All(r => Archimate.IsRelationship(r.ArchimateType)), "concept de relation d'origine manquant");
            Equal(0, new Transcoder(repo.Metamodel).Check(repo).Findings.Count(f => f.Severity == "erreur" || f.Code == "concept-sans-regle"), "constats de transcodification");
            Equal(0, Conformity.Check(repo).Count(i => i.Code is "type-inconnu" or "type-archive"), "conformité des types");
            var again = new ExportRoundTrip(repo).Differences();
            Equal(0, again, "écarts à l'aller-retour Archi du fichier migré");
        });
        Test("migration du fichier réel : applications, capacités, projets, flux, données et infrastructure comme avant", () =>
        {
            var objects = original["Objects"]!;
            var layerOf = original["Metamodel"]!["ObjectTypes"]!.AsArray().ToDictionary(t => t!["Key"]!.GetValue<string>(), t => t!["LayerKey"]!.GetValue<string>());
            bool Active(JsonNode n) => !(n["Archived"]?.GetValue<bool>() ?? false);
            int Of(params string[] keys) => Count(objects, o => Active(o) && keys.Contains(o["TypeKey"]!.GetValue<string>()));
            var reader = new SpaceReader(repo, new Analyzer(repo));
            Equal(Of("ApplicationComponent"), reader.Applications().Count, "applications");
            Equal(Of("WorkPackage"), new Analyzer(repo).Projects().Count(), "projets");
            Equal(Of("BusinessObject", "DataObject"), reader.Data().Count, "objets de données");
            Equal(Count(objects, o => Active(o) && layerOf.GetValueOrDefault(o["TypeKey"]!.GetValue<string>()) == "technique" && o["TypeKey"]!.GetValue<string>() != "Artifact"),
                reader.Infrastructure().Count, "objets d'infrastructure");
            var activeIds = objects.AsObject().Where(kv => Active(kv.Value!)).Select(kv => kv.Key).ToHashSet();
            Equal(Count(original["Relationships"], r => Active(r) && r["TypeKey"]!.GetValue<string>() == "Flow"
                      && activeIds.Contains(r["SourceId"]!.GetValue<string>()) && activeIds.Contains(r["TargetId"]!.GetValue<string>())),
                reader.Flows().Count, "flux");
            var capabilities = Of("Capability");
            int Nodes(IEnumerable<CapabilityNode> list) => list.Sum(n => 1 + Nodes(n.Children));
            Equal(capabilities, Nodes(reader.CapabilityMap()), "capacités de l'arbre");
        });
        Test("migration du fichier réel : espaces et synthèse identiques à ceux d'avant la refonte (dossier before/ voisin s'il existe)", () =>
        {
            var before = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, "before");
            if (!Directory.Exists(before)) { Console.WriteLine("        (dossier before/ absent : comparaison ignorée)"); return; }
            var reader = new SpaceReader(repo, new Analyzer(repo));
            int Length(string file) => JsonDocument.Parse(File.ReadAllText(Path.Combine(before, file))).RootElement.GetArrayLength();
            Equal(Length("space-applications.json"), reader.Applications().Count, "applications avant/après");
            Equal(Length("space-flows.json"), reader.Flows().Count, "flux avant/après");
            Equal(Length("space-data.json"), reader.Data().Count, "données avant/après");
            Equal(Length("space-infrastructure.json"), reader.Infrastructure().Count, "infrastructure avant/après");
            Equal(Length("projects.json"), new Analyzer(repo).Projects().Count(), "projets avant/après");
        });
    }

    /// <summary>Aller-retour Archi d'un référentiel : nombre d'écarts de types après export puis réimport.</summary>
    private sealed class ExportRoundTrip
    {
        private readonly Repository _repo;
        public ExportRoundTrip(Repository repo) => _repo = repo;

        public int Differences()
        {
            using var ms = new MemoryStream();
            AeTool.Connectors.ArchimateExchange.Export(_repo).Save(ms);
            ms.Position = 0;
            var copy = new Repository();
            AeTool.Connectors.ArchimateExchange.Import(ms, copy);
            return _repo.ActiveObjects.Count(o => !copy.Objects.TryGetValue(o.Id, out var c) || c.TypeKey != o.TypeKey);
        }
    }
}
