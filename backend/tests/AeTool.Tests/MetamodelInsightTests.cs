using System.Net;
using System.Text.Json;
using static AeTool.Tests.Harness;

namespace AeTool.Tests;

/// <summary>Couples observés d'un type de relation : ce que l'écran du métamodèle propose de reprendre comme couples autorisés.</summary>
public static class MetamodelInsightTests
{
    private static List<(string Source, string Target, int Count)> Pairs(JsonElement body) =>
        body.EnumerateArray().Select(p => (p.GetProperty("sourceTypeKey").GetString()!, p.GetProperty("targetTypeKey").GetString()!, p.GetProperty("count").GetInt32())).ToList();

    public static void Run()
    {
        Console.WriteLine("Couples observés");

        Test("les couples observés sont comptés par type de relation, les extrémités « relation » sont exclues", () =>
        {
            using var server = new ApiTests.Server();
            server.Client.PostAsync("/api/import/archimate", new StringContent(Fixtures.Model())).GetAwaiter().GetResult().EnsureSuccessStatusCode();
            var realisation = Pairs(server.GetJson("/api/metamodel/relationship-types/realisation/observed-pairs"));
            Equal(1, realisation.Count, "un seul couple");
            Equal(("application", "capacite", 2), realisation[0], "deux réalisations application vers capacité");
            Equal(0, Pairs(server.GetJson("/api/metamodel/relationship-types/association/observed-pairs")).Count, "l'association vers une relation est ignorée");
            Equal(0, Pairs(server.GetJson("/api/metamodel/relationship-types/composition/observed-pairs")).Count, "type sans relation");
        });
        Test("un type de relation inconnu répond 404 ; une relation archivée ne compte pas", () =>
        {
            using var server = new ApiTests.Server();
            server.Client.PostAsync("/api/import/archimate", new StringContent(Fixtures.Model())).GetAwaiter().GetResult().EnsureSuccessStatusCode();
            Equal(HttpStatusCode.NotFound, server.Client.GetAsync("/api/metamodel/relationship-types/inconnu/observed-pairs").GetAwaiter().GetResult().StatusCode, "type inconnu");
            Equal(HttpStatusCode.NoContent, server.Client.DeleteAsync("/api/relationships/id-r6").GetAwaiter().GetResult().StatusCode, "archivage");
            Equal(1, Pairs(server.GetJson("/api/metamodel/relationship-types/realisation/observed-pairs"))[0].Count, "relation archivée écartée");
        });
    }
}
