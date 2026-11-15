using AeTool.Connectors;
using AeTool.Core;
using static AeTool.Tests.Harness;

namespace AeTool.Tests;

/// <summary>Jeu de démonstration versionné (exemples/hydreane) : il doit rester importable sans anomalie et cohérent avec le classeur.</summary>
public static class ExampleDatasetTests
{
    public static void Run()
    {
        var dir = Locate();
        if (dir is null) { Console.WriteLine("  (jeu exemples/hydreane introuvable, contrôles ignorés)"); return; }
        var repo = new Repository();
        ImportReport archi = new(), urba = new();

        Test("jeu Hydréane : import du modèle Archi sans anomalie ni concept non transcodé", () =>
        {
            using var s = File.OpenRead(Path.Combine(dir, "hydreane-referentiel.xml"));
            archi = ArchimateExchange.Import(s, repo);
            Equal(0, archi.Anomalies.Count, "anomalies");
            Equal(417, repo.ActiveObjects.Count(), "objets");
            Equal(903, repo.ActiveRelationships.Count(), "relations");
            Equal(58, repo.ActiveDiagrams.Count(), "vues");
            Equal(0, repo.Objects.Values.Count(o => o.TypeKey == "autre"), "objets non classés");
        });
        Test("jeu Hydréane : rejeu du modèle sans écart", () =>
        {
            using var s = File.OpenRead(Path.Combine(dir, "hydreane-referentiel.xml"));
            var again = ArchimateExchange.Import(s, repo);
            Equal(0, again.Counts.Values.Sum(c => c.Added + c.Updated + c.Archived), "écarts au rejeu");
        });
        Test("jeu Hydréane : le classeur enrichit les 38 applications du modèle", () =>
        {
            using var s = File.OpenRead(Path.Combine(dir, "hydreane-referentiel-urbanisation.xlsx"));
            urba = UrbaImporter.Import(s, repo);
            Equal(0, urba.Anomalies.Count, "anomalies du classeur");
            Equal(0, urba.For("Applications").Added, "applications créées par le classeur");
            Equal(38, repo.Assessments.Count, "évaluations");
            Equal(1080, urba.For("Preuves").Added, "preuves");
        });
        Test("jeu Hydréane : la notation de l'outil retrouve celle du classeur", () =>
        {
            var an = new Analyzer(repo);
            var results = repo.Assessments.Values.Select(a => (o: repo.Objects[a.ObjectId], r: ScoringEngine.Evaluate(repo.Metamodel.Scoring, a, an.IsRetired(repo.Objects[a.ObjectId])))).ToList();
            var ondine = results.Single(x => x.o.Get(UrbaImporter.IdKey) == "APP-001").r;
            Equal("1/2/2/2", string.Join("/", new[] { "C1", "C2", "C3", "C4" }.Select(c => ondine.Notes[c])), "notes d'ONDINE");
            Equal("À instruire", ondine.ComputedDecision, "décision d'ONDINE");
            Equal("Haute", ondine.Priority, "priorité d'ONDINE");
            Equal(9, results.Count(x => x.r.ComputedDecision == "À instruire"), "décisions À instruire");
            Equal(5, results.Count(x => x.r.Priority == "Haute"), "priorités hautes");
        });
    }

    static string? Locate()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var candidate = Path.Combine(d.FullName, "exemples", "hydreane");
            if (Directory.Exists(candidate)) return candidate;
        }
        return null;
    }
}
