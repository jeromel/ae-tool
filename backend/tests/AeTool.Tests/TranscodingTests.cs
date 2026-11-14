using System.Text.Json;
using System.Xml.Linq;
using AeTool.Connectors;
using AeTool.Core;
using static AeTool.Tests.Harness;

namespace AeTool.Tests;

public static class TranscodingTests
{
    private static string Xml(string elements, string relationships = "", string definitions = "") => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <model xmlns="http://www.opengroup.org/xsd/archimate/3.0/" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" identifier="id-t">
          <name xml:lang="fr">Essai</name>
          <elements>{elements}</elements>
          <relationships>{relationships}</relationships>
          <propertyDefinitions>{definitions}</propertyDefinitions>
        </model>
        """;

    private static Repository WithMetamodel(Metamodel m) => new() { Metamodel = JsonSerializer.Deserialize<Metamodel>(JsonSerializer.Serialize(m))! };

    private static Repository Import(Repository repo, string xml, out ImportReport report)
    {
        report = ArchimateExchange.Import(Fixtures.Stream(xml), repo);
        return repo;
    }

    private static string ConceptOf(XDocument doc, string id) =>
        doc.Descendants().First(e => e.Name.LocalName == "element" && (string?)e.Attribute("identifier") == id)
            .Attributes().First(a => a.Name.LocalName == "type").Value;

    /// <summary>Métamodèle d'essai : un type « quartier » pour les regroupements dont « Niveau POS » vaut « Quartier ».</summary>
    private static Metamodel WithQuartier()
    {
        var m = Metamodel.Default();
        m.ObjectTypes.Add(new ObjectType { Key = "quartier", Label = "Quartier", LayerKey = "fonctionnelle" });
        m.Transcoding.Elements.Add(new ElementRule { TypeKey = "quartier", ArchimateType = "Grouping", WhenProperty = "Niveau POS", WhenValue = "Quartier", Principal = true });
        return m;
    }

    private const string Groupings = """
        <element identifier="g1" xsi:type="Grouping"><name xml:lang="fr">Pilotage</name>
          <properties><property propertyDefinitionRef="p1"><value xml:lang="fr"> quartier </value></property></properties></element>
        <element identifier="g2" xsi:type="Grouping"><name xml:lang="fr">Divers</name></element>
        """;
    private const string NiveauPos = """<propertyDefinition identifier="p1" type="string"><name>Niveau POS</name></propertyDefinition>""";

    public static void Run()
    {
        Console.WriteLine("Transcodification métamodèle <-> ArchiMate");

        Test("import : un concept devient le type du métamodèle et garde son concept d'origine", () =>
        {
            var repo = Imported(out _);
            Equal("application", repo.Objects["id-itsm"].TypeKey, "type");
            Equal("ApplicationComponent", repo.Objects["id-itsm"].ArchimateType, "concept d'origine");
            Equal("realisation", repo.Relationships["id-r1"].TypeKey, "type de relation");
            Equal("Realization", repo.Relationships["id-r1"].ArchimateType, "concept de relation d'origine");
            Equal("flux", repo.Relationships["id-r3"].TypeKey, "flux");
        });
        Test("règle conditionnelle : discriminant vérifié à l'import, concept d'origine écrit à l'export, aller-retour identique", () =>
        {
            var repo = Import(WithMetamodel(WithQuartier()), Xml(Groupings, definitions: NiveauPos), out var report);
            Equal(0, report.Anomalies.Count, "anomalies");
            Equal("quartier", repo.Objects["g1"].TypeKey, "regroupement discriminé");
            Equal("zone", repo.Objects["g2"].TypeKey, "autre regroupement");
            Equal("quartier", repo.Objects["g1"].Get("Niveau POS")?.Trim(), "le discriminant reste une propriété normale");
            var exported = ArchimateExchange.Export(repo);
            Equal("Grouping", ConceptOf(exported, "g1"), "concept du quartier");
            Equal("Grouping", ConceptOf(exported, "g2"), "concept de la zone");
            var copy = Import(WithMetamodel(WithQuartier()), exported.ToString(), out var again);
            Equal(0, again.Anomalies.Count, "anomalies au réimport");
            Equal(Canonical(repo), Canonical(copy));
        });
        Test("règle conditionnelle : un objet créé dans l'outil écrit son discriminant à l'export", () =>
        {
            var repo = WithMetamodel(WithQuartier());
            repo.Objects["q"] = new ArchObject { Id = "q", TypeKey = "quartier", Name = "Exploitation" };
            var xml = ArchimateExchange.Export(repo).ToString();
            var copy = Import(WithMetamodel(WithQuartier()), xml, out _);
            Equal("quartier", copy.Objects["q"].TypeKey, "type retrouvé par le discriminant");
            Equal("Quartier", copy.Objects["q"].Get("Niveau POS"), "valeur du discriminant");
        });
        Test("type sans règle : exporté en repli avec le marqueur, retrouvé au réimport, marqueur jamais stocké", () =>
        {
            var m = Metamodel.Default();
            m.ObjectTypes.Add(new ObjectType { Key = "zone-ot", Label = "Zone OT", LayerKey = "technique" });
            m.RelationshipTypes.Add(new RelationshipType { Key = "supervise", Label = "Supervise" });
            var repo = WithMetamodel(m);
            repo.Objects["z1"] = new ArchObject { Id = "z1", TypeKey = "zone-ot", Name = "Zone 3" };
            repo.Objects["z2"] = new ArchObject { Id = "z2", TypeKey = "zone-ot", Name = "Zone 4" };
            repo.Relationships["rz"] = new Relationship { Id = "rz", TypeKey = "supervise", SourceId = "z1", TargetId = "z2" };
            var xml = ArchimateExchange.Export(repo).ToString();
            True(xml.Contains("<name>ae:type</name>"), "le marqueur doit être écrit");
            Equal("Grouping", ConceptOf(XDocument.Parse(xml), "z1"), "concept de repli");
            var copy = Import(WithMetamodel(m), xml, out var report);
            Equal(0, report.Anomalies.Count, "anomalies");
            Equal("zone-ot", copy.Objects["z1"].TypeKey, "type d'objet retrouvé");
            Equal("supervise", copy.Relationships["rz"].TypeKey, "type de relation retrouvé");
            True(copy.Objects["z1"].Properties.Count == 0, "le marqueur ne doit pas être une propriété de l'objet");
            True(copy.Metamodel.Property("ae:type") is null, "le marqueur ne doit pas devenir une définition");
        });
        Test("plusieurs concepts pour un type : concept d'origine conservé, règle principale pour un objet créé dans l'outil", () =>
        {
            var m = Metamodel.Default();
            var rule = m.Transcoding.Elements.Single(r => r.ArchimateType == "ApplicationCollaboration");
            rule.TypeKey = "application";
            rule.Principal = false;
            var repo = Import(WithMetamodel(m), Xml("""
                <element identifier="a1" xsi:type="ApplicationComponent"><name>A</name></element>
                <element identifier="a2" xsi:type="ApplicationCollaboration"><name>B</name></element>
                """), out _);
            Equal("application", repo.Objects["a2"].TypeKey, "collaboration classée comme application");
            repo.Objects["a3"] = new ArchObject { Id = "a3", TypeKey = "application", Name = "C" };
            var exported = ArchimateExchange.Export(repo);
            Equal("ApplicationComponent", ConceptOf(exported, "a1"), "concept d'origine a1");
            Equal("ApplicationCollaboration", ConceptOf(exported, "a2"), "concept d'origine a2");
            Equal("ApplicationComponent", ConceptOf(exported, "a3"), "règle principale pour un objet de l'outil");
        });
        Test("règle de relation contrainte : une réalisation d'application vers une capacité devient « soutient »", () =>
        {
            var m = Metamodel.Default();
            m.RelationshipTypes.Add(new RelationshipType { Key = "soutient", Label = "Soutient" });
            m.Transcoding.Relations.Add(new RelationRule { TypeKey = "soutient", ArchimateType = "Realization", SourceTypeKey = "application", TargetTypeKey = "capacite", Principal = false });
            var repo = Import(WithMetamodel(m), Fixtures.Model(extra: """<relationship identifier="id-r7" source="id-srv" target="id-itsm" xsi:type="Realization" />"""), out _);
            Equal("soutient", repo.Relationships["id-r1"].TypeKey, "application vers capacité");
            Equal("realisation", repo.Relationships["id-r7"].TypeKey, "autres extrémités : règle simple");
            var exported = ArchimateExchange.Export(repo).ToString();
            var copy = Import(WithMetamodel(m), exported, out _);
            Equal(Canonical(repo), Canonical(copy));
            True(exported.Contains("identifier=\"id-r1\" source=\"id-itsm\" target=\"id-cap\" xsi:type=\"Realization\""), "concept de la relation contrainte");
        });
        Test("concept sans règle : type « autre », anomalie signalée, concept d'origine conservé à l'export", () =>
        {
            var m = Metamodel.Default();
            m.Transcoding.Elements.RemoveAll(r => r.ArchimateType == "Node");
            var repo = Import(WithMetamodel(m), Fixtures.Model(), out var report);
            var anomaly = report.Anomalies.Single(a => a.Code == "concept-non-transcode");
            Equal("avertissement", anomaly.Severity, "gravité");
            Equal("autre", repo.Objects["id-srv"].TypeKey, "type non classé");
            Equal("Node", ConceptOf(ArchimateExchange.Export(repo), "id-srv"), "concept d'origine réexporté");
        });
        Test("réapplication : à blanc elle compte sans rien changer, appliquée elle retype et journalise", () =>
        {
            var repo = Imported(out _);
            repo.Metamodel.Transcoding.Elements.Single(r => r.ArchimateType == "ApplicationComponent").TypeKey = "logiciel-systeme";
            var dry = new Transcoder(repo.Metamodel).Reclassify(repo, dryRun: true);
            Equal(3, dry.Changes, "changements à blanc");
            var move = dry.Moves.Single(x => x.Kind == "objet");
            Equal(("application", "logiciel-systeme", 3), (move.From, move.To, move.Count), "ventilation");
            Equal("application", repo.Objects["id-itsm"].TypeKey, "rien ne doit changer à blanc");
            var journal = repo.Journal.Count;
            Equal(3, new Transcoder(repo.Metamodel).Reclassify(repo, dryRun: false, "essai").Changes, "changements appliqués");
            Equal("logiciel-systeme", repo.Objects["id-itsm"].TypeKey, "objet retypé");
            Equal(journal + 1, repo.Journal.Count, "entrée de journal");
            Equal(0, new Transcoder(repo.Metamodel).Reclassify(repo, dryRun: true).Changes, "second passage");
        });
        Test("réapplication : un type sans règle venu de l'outil n'est pas touché ; un concept devenu transcodable sort de « autre »", () =>
        {
            var m = Metamodel.Default();
            m.Transcoding.Elements.RemoveAll(r => r.ArchimateType == "Node");
            var repo = Import(WithMetamodel(m), Fixtures.Model(), out _);
            repo.Metamodel.ObjectTypes.Add(new ObjectType { Key = "libre", Label = "Libre" });
            repo.Objects["l"] = new ArchObject { Id = "l", TypeKey = "libre", Name = "L", ArchimateType = "Grouping" };
            repo.Metamodel.Transcoding.Elements.Add(new ElementRule { TypeKey = "noeud", ArchimateType = "Node", Principal = true });
            var result = new Transcoder(repo.Metamodel).Reclassify(repo, dryRun: false);
            Equal(1, result.Changes, "changements");
            Equal("noeud", repo.Objects["id-srv"].TypeKey, "concept devenu transcodable");
            Equal("libre", repo.Objects["l"].TypeKey, "type de l'outil préservé");
        });
        Test("propriété : le nom Archi est respecté à l'import et à l'export", () =>
        {
            var m = Metamodel.Default();
            m.PropertyDefinitions.Add(new PropertyDefinition { Key = "etat", Label = "État", ArchiName = "lifecycle.state" });
            var repo = Import(WithMetamodel(m), Fixtures.Model(), out _);
            Equal("Active", repo.Objects["id-itsm"].Get("etat"), "valeur sous la clé de l'outil");
            True(repo.Objects["id-itsm"].Get("lifecycle.state") is null, "pas de doublon sous le nom Archi");
            True(repo.Metamodel.Property("lifecycle.state") is null, "pas de définition créée pour le nom Archi");
            var xml = ArchimateExchange.Export(repo).ToString();
            True(xml.Contains("<name>lifecycle.state</name>") && !xml.Contains("<name>etat</name>"), "nom Archi à l'export");
            Equal(Canonical(repo), Canonical(Import(WithMetamodel(m), xml, out _)));
        });
        Test("propriété : le type de donnée devient le type de la définition Archi, et inversement", () =>
        {
            var m = Metamodel.Default();
            m.PropertyDefinitions.Add(new PropertyDefinition { Key = "cout", DataType = PropertyTypes.Number });
            m.PropertyDefinitions.Add(new PropertyDefinition { Key = "niveau", DataType = PropertyTypes.List, AllowedValues = { "A" } });
            var repo = WithMetamodel(m);
            repo.Objects["o"] = new ArchObject { Id = "o", TypeKey = "application", Name = "O", Properties = { new() { Key = "cout", Value = "3" }, new() { Key = "niveau", Value = "A" } } };
            var definitions = ArchimateExchange.Export(repo).Descendants().Where(e => e.Name.LocalName == "propertyDefinition")
                .ToDictionary(e => e.Elements().Single().Value, e => (string?)e.Attribute("type"));
            Equal(("number", "string"), (definitions["cout"]!, definitions["niveau"]!), "types écrits");
            var fresh = Import(new Repository(), Xml("""<element identifier="x" xsi:type="Node"><name>X</name></element>""",
                definitions: """<propertyDefinition identifier="p" type="date"><name>echeance</name></propertyDefinition>"""), out _);
            Equal(PropertyTypes.Date, fresh.Metamodel.Property("echeance")!.DataType, "date");
        });
        Test("réimport : un objet retypé dans l'outil garde son type ; un concept modifié dans Archi retype l'objet", () =>
        {
            var repo = Imported(out _);
            repo.Objects["id-itsm"].TypeKey = "logiciel-systeme";
            var report = ArchimateExchange.Import(Fixtures.Stream(Fixtures.Model()), repo);
            Equal("logiciel-systeme", repo.Objects["id-itsm"].TypeKey, "retypage survivant");
            Equal(0, report.Counts["Objets"].Updated + report.Counts["Objets"].Added, "rien à mettre à jour");
            var changed = Fixtures.Model().Replace("identifier=\"id-itsm\" xsi:type=\"ApplicationComponent\"", "identifier=\"id-itsm\" xsi:type=\"Node\"");
            ArchimateExchange.Import(Fixtures.Stream(changed), repo);
            Equal("noeud", repo.Objects["id-itsm"].TypeKey, "concept changé dans Archi");
            Equal("Node", repo.Objects["id-itsm"].ArchimateType, "nouveau concept d'origine");
        });
        Test("réimport : un objet sans concept d'origine reçoit son type de la matrice", () =>
        {
            var repo = Imported(out _);
            repo.Objects["id-itsm"].ArchimateType = null;
            repo.Objects["id-itsm"].TypeKey = "logiciel-systeme";
            ArchimateExchange.Import(Fixtures.Stream(Fixtures.Model()), repo);
            Equal("application", repo.Objects["id-itsm"].TypeKey, "type recalculé");
            Equal("ApplicationComponent", repo.Objects["id-itsm"].ArchimateType, "concept renseigné");
        });
        Test("contrôles : règle vers un type inconnu, concept hors liste, doublon, type sans règle, concept sans règle", () =>
        {
            var m = Metamodel.Default();
            m.ObjectTypes.Add(new ObjectType { Key = "libre", Label = "Libre" });
            m.Transcoding.Elements.Add(new ElementRule { TypeKey = "fantome", ArchimateType = "Node" });
            m.Transcoding.Elements.Add(new ElementRule { TypeKey = "noeud", ArchimateType = "Inventé" });
            m.Transcoding.Elements.Add(new ElementRule { TypeKey = "equipement", ArchimateType = "Capability" });
            m.Transcoding.Elements.RemoveAll(r => r.ArchimateType == "Resource");
            var repo = WithMetamodel(m);
            repo.Objects["r"] = new ArchObject { Id = "r", TypeKey = "autre", Name = "R", ArchimateType = "Resource" };
            repo.Objects["c"] = new ArchObject { Id = "c", TypeKey = "capacite", Name = "C", ArchimateType = "Capability" };
            var report = new Transcoder(repo.Metamodel).Check(repo);
            bool Has(string severity, string code, string? key = null) => report.Findings.Any(f => f.Severity == severity && f.Code == code && (key is null || f.Key == key));
            True(Has("erreur", "regle-type-inconnu", "fantome"), "type inconnu");
            True(Has("erreur", "concept-hors-standard", "Inventé"), "concept hors liste");
            True(Has("avertissement", "regle-doublon", "Capability"), "doublon");
            True(Has("information", "type-sans-regle", "libre"), "type sans règle");
            True(Has("avertissement", "concept-sans-regle", "Resource"), "concept sans règle");
            Equal(1, report.Rules.Single(r => r.Kind == "element" && r.TypeKey == "capacite").Count, "objets concernés par la règle des capacités");
        });
    }
}
