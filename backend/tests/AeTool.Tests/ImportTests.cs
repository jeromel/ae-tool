using System.Text.Json;
using AeTool.Connectors;
using AeTool.Core;
using static AeTool.Tests.Harness;

namespace AeTool.Tests;

public static class ImportTests
{
    public static void Run()
    {
        Console.WriteLine("Import Archi (format d'échange ArchiMate)");

        Test("importe objets, relations, propriétés, dossiers et vues", () =>
        {
            var repo = Imported(out var report);
            Equal(8, repo.Objects.Count, "objets"); Equal(6, repo.Relationships.Count, "relations"); Equal(3, repo.Diagrams.Count, "vues");
            Equal(0, report.Anomalies.Count, "anomalies");
            Equal("2", repo.Objects["id-cap"].Get("pos.order"));
            Equal("application", repo.Objects["id-itsm"].TypeKey, "type du métamodèle :");
            Equal("ApplicationComponent", repo.Objects["id-itsm"].ArchimateType, "concept d'origine :");
            Equal("Suite ITSM cible.", repo.Objects["id-itsm"].Documentation);
            True(repo.Metamodel.Property("lifecycle.state") is not null, "la définition de propriété doit être créée");
        });
        Test("un objet présent dans plusieurs vues n'est créé qu'une fois", () =>
        {
            var repo = Imported(out _);
            Equal(2, repo.Diagrams.Values.Count(d => d.AllNodes().Any(n => n.ElementId == "id-itsm")));
            Equal(1, repo.Objects.Values.Count(o => o.Name == "ITSM-SUITE"));
        });
        Test("positions, imbrication, styles, points de passage et libellés sont conservés", () =>
        {
            var repo = Imported(out _);
            var pos = repo.Diagrams["id-v-pos"];
            Equal(1, pos.Nodes[0].Children.Count); Equal(400, pos.Nodes[0].W); Equal("#f5deaa", pos.Nodes[0].Style!.Fill!.Hex);
            Equal("Label", pos.Nodes[1].Kind); Equal("Légende", pos.Nodes[1].Label);
            var c = repo.Diagrams["id-v-cible"].Connections[0];
            Equal("id-r3", c.RelationshipId); Equal(200, c.Bendpoints[0].X); Equal("2", c.Style!.LineWidth);
            Equal("Application Cooperation", repo.Diagrams["id-v-cible"].Viewpoint);
        });
        Test("une relation vers une autre relation est valide et ne casse pas l'analyse", () =>
        {
            var repo = Imported(out var report);
            Equal("id-r3", repo.Relationships["id-r5"].TargetId);
            True(report.Anomalies.All(a => a.Code != "reference-cassee"), "aucune référence cassée attendue");
            Equal(0, new Analyzer(repo).Outgoing("id-ticket").Count());
        });
        Test("rejouer le même import ne crée aucun doublon et ne modifie rien", () =>
        {
            var repo = Imported(out _);
            var before = Canonical(repo);
            var second = ArchimateExchange.Import(Fixtures.Stream(Fixtures.Model()), repo);
            Equal(before, Canonical(repo));
            Equal(8, second.Counts["Objets"].Unchanged); Equal(0, second.Counts["Objets"].Added + second.Counts["Objets"].Updated);
            Equal(6, second.Counts["Relations"].Unchanged); Equal(3, second.Counts["Vues"].Unchanged);
        });
        Test("réimport : modification détectée, objet disparu archivé (suppression logique)", () =>
        {
            var repo = Imported(out _);
            var report = ArchimateExchange.Import(Fixtures.Stream(Fixtures.Model(appName: "ITSM-SUITE v2", withLegacy: false)), repo);
            Equal("ITSM-SUITE v2", repo.Objects["id-itsm"].Name);
            Equal(1, report.Counts["Objets"].Updated); Equal(1, report.Counts["Objets"].Archived);
            True(repo.Objects["id-legacy"].Archived, "l'objet absent du fichier doit être archivé, pas supprimé");
            True(repo.Relationships["id-r6"].Archived, "sa relation doit être archivée");
        });
        Test("réimport : les données saisies dans l'outil sont conservées", () =>
        {
            var repo = Imported(out _);
            repo.Metamodel.EnsureProperty("urba:ID", source: "urba");
            repo.Objects["id-itsm"].Set("urba:ID", "APP-001");
            repo.Objects["id-itsm"].VisualOverride = new Visual { Fill = "#ff0000" };
            ArchimateExchange.Import(Fixtures.Stream(Fixtures.Model(appName: "ITSM bis")), repo);
            Equal("APP-001", repo.Objects["id-itsm"].Get("urba:ID")); Equal("#ff0000", repo.Objects["id-itsm"].VisualOverride!.Fill);
        });
        Test("références cassées, types inconnus et doublons sont signalés, jamais corrigés en silence", () =>
        {
            var extra = """
                <relationship identifier="id-bad" source="id-itsm" target="id-fantome" xsi:type="Serving" />
                <relationship identifier="id-r1" source="id-itsm" target="id-cap" xsi:type="Serving" />
                """;
            var xml = Fixtures.Model(extra: extra).Replace("xsi:type=\"Node\"", "xsi:type=\"TypeMaison\"")
                .Replace("elementRef=\"id-ticket\"", "elementRef=\"id-absent\"");
            var repo = Imported(out var report, xml);
            True(report.Anomalies.Any(a => a.Code == "reference-cassee" && a.Ref == "id-bad"), "référence cassée non signalée");
            True(report.Anomalies.Any(a => a.Code == "identifiant-duplique" && a.Ref == "id-r1"), "doublon non signalé");
            True(report.Anomalies.Any(a => a.Code == "concept-non-transcode"), "concept sans règle non signalé");
            Equal("autre", repo.Objects["id-srv"].TypeKey, "le concept sans règle tombe dans le type non classé :");
            Equal("TypeMaison", repo.Objects["id-srv"].ArchimateType, "le concept d'origine est conservé :");
            True(report.Anomalies.Any(a => a.Code == "occurrence-orpheline"), "occurrence orpheline non signalée");
            Equal("id-fantome", repo.Relationships["id-bad"].TargetId, "la relation cassée est conservée telle quelle :");
            Equal("realisation", repo.Relationships["id-r1"].TypeKey, "la première occurrence fait foi :");
        });
        Test("relation non conforme au métamodèle : signalée", () =>
        {
            var repo = new Repository();
            repo.Metamodel.RelType("realisation")!.AllowedPairs.Add("application>service-applicatif");
            var report = ArchimateExchange.Import(Fixtures.Stream(Fixtures.Model()), repo);
            Equal(2, report.Anomalies.Count(a => a.Code == "relation-non-conforme"));
        });
        Test("fichier illisible : erreur explicite, référentiel intact", () =>
        {
            var repo = new Repository();
            var report = ArchimateExchange.Import(Fixtures.Stream("<model><pas-ferme>"), repo);
            Equal("xml-invalide", report.Anomalies[0].Code); Equal(0, repo.Objects.Count);
        });

        Test("un fichier portant une déclaration DOCTYPE est refusé : aucune entité n'est développée", () =>
        {
            var repo = new Repository();
            var model = Fixtures.Model();
            var afterDeclaration = model.IndexOf("?>", StringComparison.Ordinal) + 2;
            var hostile = model[..afterDeclaration] + "<!DOCTYPE model [<!ENTITY e \"injecté\">]>" + model[afterDeclaration..];
            var report = ArchimateExchange.Import(Fixtures.Stream(hostile), repo);
            Equal("xml-invalide", report.Anomalies[0].Code, "refus signalé");
            True(report.Anomalies[0].Message.Contains("DTD"), "c'est bien la déclaration qui est refusée : " + report.Anomalies[0].Message);
            Equal(0, repo.Objects.Count, "rien n'est importé");
        });
    }
}
