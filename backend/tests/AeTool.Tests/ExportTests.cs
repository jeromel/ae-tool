using System.Text.Json;
using AeTool.Connectors;
using AeTool.Core;
using static AeTool.Tests.Harness;

namespace AeTool.Tests;

public static class ExportTests
{
    public static void Run()
    {
        Console.WriteLine("Export et réversibilité");

        Test("export puis réimport dans un référentiel vierge : état identique", () =>
        {
            var repo = Imported(out _);
            var xml = ArchimateExchange.Export(repo).ToString();
            var copy = new Repository();
            var report = ArchimateExchange.Import(Fixtures.Stream(xml), copy);
            Equal(0, report.Anomalies.Count, "anomalies au réimport");
            Equal(Canonical(repo), Canonical(copy));
        });
        Test("aller-retour dans le même référentiel : rien n'est signalé modifié (valeur vide, concept pas encore fixé)", () =>
        {
            // Essai du PO sur Archi réel (26/11) : au premier aller-retour, des objets non modifiés étaient comptés « modifiés ».
            var repo = Imported(out _);
            var itsm = repo.Objects["id-itsm"];
            itsm.Properties.Add(new PropertyValue { Key = "commentaire", Value = null });
            // Objet créé dans l'outil : pas encore de concept ArchiMate, une propriété sans valeur.
            repo.Objects["id-saisi"] = new ArchObject { Id = "id-saisi", TypeKey = repo.Objects["id-report"].TypeKey, Name = "Créé dans l'outil",
                Source = "saisie", Properties = { new PropertyValue { Key = "commentaire", Value = null } } };
            var before = Canonical(repo);
            var report = ArchimateExchange.Import(Fixtures.Stream(ArchimateExchange.Export(repo).ToString()), repo);
            Equal(0, report.Counts["Objets"].Updated, "objets signalés modifiés");
            Equal(0, report.Counts["Objets"].Added, "objets ajoutés");
            Equal(before, Canonical(repo), "référentiel inchangé");
        });
        Test("aller-retour : une vraie modification reste signalée, et le concept reçu est alors gardé", () =>
        {
            var repo = Imported(out _);
            repo.Objects["id-saisi"] = new ArchObject { Id = "id-saisi", TypeKey = repo.Objects["id-report"].TypeKey, Name = "Créé dans l'outil", Source = "saisie" };
            var xml = ArchimateExchange.Export(repo).ToString().Replace(">Créé dans l'outil<", ">Renommé dans Archi<");
            var report = ArchimateExchange.Import(Fixtures.Stream(xml), repo);
            Equal(1, report.Counts["Objets"].Updated, "objets signalés modifiés");
            Equal("Renommé dans Archi", repo.Objects["id-saisi"].Name);
            Equal("ApplicationComponent", repo.Objects["id-saisi"].ArchimateType);
        });
        Test("aller-retour : une relation du catalogue garde le catalogue pour source, sans être signalée modifiée (note trajet Colbert §5)", () =>
        {
            var repo = Imported(out _);
            repo.Relationships["id-flux-cat"] = new Relationship { Id = "id-flux-cat", TypeKey = repo.Relationships["id-r3"].TypeKey,
                SourceId = "id-report", TargetId = "id-itsm", Name = "Flux du catalogue", Source = CatalogueImporter.SourceTag };
            var report = ArchimateExchange.Import(Fixtures.Stream(ArchimateExchange.Export(repo).ToString()), repo);
            Equal(CatalogueImporter.SourceTag, repo.Relationships["id-flux-cat"].Source, "source après l'aller-retour");
            Equal(0, report.Counts["Relations"].Updated, "relations signalées modifiées");
            Equal(null, repo.Relationships["id-flux-cat"].ArchimateType, "concept reçu à l'export, sans modification");
        });
        Test("aller-retour puis réimport sans lui : un flux du catalogue n'est pas archivé", () =>
        {
            var repo = Imported(out _);
            var without = ArchimateExchange.Export(repo).ToString();
            repo.Relationships["id-flux-cat"] = new Relationship { Id = "id-flux-cat", TypeKey = repo.Relationships["id-r3"].TypeKey,
                SourceId = "id-report", TargetId = "id-itsm", Name = "Flux du catalogue", Source = CatalogueImporter.SourceTag };
            ArchimateExchange.Import(Fixtures.Stream(ArchimateExchange.Export(repo).ToString()), repo);
            var report = ArchimateExchange.Import(Fixtures.Stream(without), repo);
            Equal(false, repo.Relationships["id-flux-cat"].Archived, "flux du catalogue archivé");
            Equal(0, report.Counts["Relations"].Archived, "relations archivées");
        });
        Test("export d'une seule vue : uniquement ses objets et relations", () =>
        {
            var repo = Imported(out _);
            var copy = new Repository();
            ArchimateExchange.Import(Fixtures.Stream(ArchimateExchange.Export(repo, new[] { "id-v-bi" }).ToString()), copy);
            Equal(2, copy.Objects.Count); Equal(1, copy.Diagrams.Count);
        });
        Test("type sans règle : exporté avec le concept de repli", () =>
        {
            var repo = Imported(out _);
            repo.Metamodel.ObjectTypes.Add(new ObjectType { Key = "ZoneOT", Label = "Zone OT" });
            repo.Objects["z"] = new ArchObject { Id = "z", TypeKey = "ZoneOT", Name = "Zone 3" };
            True(ArchimateExchange.Export(repo).ToString().Contains("identifier=\"z\" xsi:type=\"Grouping\""), "type de repli attendu");
        });
        Test("impact qualifié : exporté en association nommée", () =>
        {
            var repo = Imported(out _);
            repo.Impacts.First(i => i.ProjectId == "id-prj1" && i.ObjectId == "id-itsm").Nature = "Création";
            True(ArchimateExchange.Export(repo).ToString().Contains("Impact : Création"), "association d'impact attendue");
        });
        Test("le référentiel complet se sérialise et se relit en JSON", () =>
        {
            var repo = Imported(out _);
            var copy = JsonSerializer.Deserialize<Repository>(JsonSerializer.Serialize(repo))!;
            Equal(Canonical(repo), Canonical(copy)); Equal(repo.Metamodel.ObjectTypes.Count, copy.Metamodel.ObjectTypes.Count);
            Equal(4, copy.Metamodel.Scoring.Criteria.Count); Equal(2, copy.Metamodel.Scoring.MasteryCriteria.Count);
        });
    }
}
