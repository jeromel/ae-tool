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
        Test("export d'une seule vue : uniquement ses objets et relations", () =>
        {
            var repo = Imported(out _);
            var copy = new Repository();
            ArchimateExchange.Import(Fixtures.Stream(ArchimateExchange.Export(repo, new[] { "id-v-bi" }).ToString()), copy);
            Equal(2, copy.Objects.Count); Equal(1, copy.Diagrams.Count);
        });
        Test("type hors ArchiMate : exporté avec le type de repli", () =>
        {
            var repo = Imported(out _);
            repo.Metamodel.ObjectTypes.Add(new ObjectType { Key = "ZoneOT", Label = "Zone OT", ArchimateType = null });
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
