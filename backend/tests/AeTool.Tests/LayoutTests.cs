using AeTool.Connectors;
using AeTool.Core;
using static AeTool.Tests.Harness;

namespace AeTool.Tests;

/// <summary>Disposition retouchée d'une vue : elle s'affiche, part vers Archi et survit aux réimports.</summary>
public static class LayoutTests
{
    public static void Run()
    {
        Console.WriteLine("Disposition des vues");

        Test("une disposition retouchée survit au réimport Archi, pour les nœuds qui existent encore", () =>
        {
            var repo = Imported(out _);
            var d = repo.ActiveDiagrams.First();
            var node = d.AllNodes().First();
            d.Layout = new Dictionary<string, Box> { [node.Id] = new Box(900, 900, 200, 80), ["disparu"] = new Box(0, 0, 10, 10) };
            d.LayoutBy = "claire";
            var report = ArchimateExchange.Import(Fixtures.Stream(Fixtures.Model()), repo);
            var after = repo.Diagrams[d.Id];
            Equal(new Box(900, 900, 200, 80), after.Placed(node));
            True(!after.Layout!.ContainsKey("disparu"), "nœud disparu oublié");
            Equal("claire", after.LayoutBy);
            var again = ArchimateExchange.Import(Fixtures.Stream(Fixtures.Model()), repo);
            Equal(0, again.For("Vues").Updated, "un réimport ne compte pas la disposition gardée comme une modification");
        });

        Test("l'export Archi emporte la disposition retouchée et laisse les points de passage devenus faux", () =>
        {
            var repo = Imported(out _);
            var d = repo.ActiveDiagrams.First(x => x.Connections.Count > 0);
            var c = d.Connections.First();
            c.Bendpoints.Add(new Point { X = 1, Y = 2 });
            d.Layout = new Dictionary<string, Box> { [c.SourceNodeId] = new Box(1234, 567, 210, 70) };
            var xml = ArchimateExchange.Export(repo, new[] { d.Id }).ToString();
            True(xml.Contains("x=\"1234\"") && xml.Contains("y=\"567\""), "position retouchée exportée");
            var back = new Repository();
            ArchimateExchange.Import(Fixtures.Stream(xml), back);
            Equal(0, back.Diagrams[d.Id].Connections.Single(x => x.Id == c.Id).Bendpoints.Count, "points de passage retirés");
        });

        Test("habilitations : un contributeur réorganise une vue", () =>
        {
            Equal(AeTool.Api.Auth.Roles.Contributor, AeTool.Api.Auth.AccessRules.MinimumRole("PUT", "/api/diagrams/v1/layout"));
            Equal(AeTool.Api.Auth.Roles.Contributor, AeTool.Api.Auth.AccessRules.MinimumRole("DELETE", "/api/diagrams/v1/layout"));
            Equal(AeTool.Api.Auth.Roles.Reader, AeTool.Api.Auth.AccessRules.MinimumRole("GET", "/api/diagrams/v1"));
        });
    }
}
