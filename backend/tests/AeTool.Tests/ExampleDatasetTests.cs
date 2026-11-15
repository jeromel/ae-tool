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
        RunDrawing(repo, dir);
        RunTrajectory(repo);
    }

    /// <summary>Le dessin draw.io du jeu se rattache au référentiel Hydréane : les chiffres annoncés dans le README d'exemple.</summary>
    public static void RunDrawing(Repository repo, string dir)
    {
        Test("jeu Hydréane : le dessin draw.io se rattache au référentiel comme annoncé", () =>
        {
            var path = Path.Combine(dir, "hydreane-facturation-saas.drawio");
            DrawioAnalysis a;
            using (var s = File.OpenRead(path)) a = DrawioImporter.Analyse(s, "hydreane-facturation-saas.drawio", repo);
            Equal(12, a.Items.Count(i => i.Proposal == DrawioItem.Link), "rattachés");
            Equal(5, a.Items.Count(i => i.Proposal == DrawioItem.Create), "à créer");
            Equal(3, a.Items.Count(i => i.Proposal == DrawioItem.Drawing), "dessin");
            ImportReport r;
            using (var s = File.OpenRead(path)) r = DrawioImporter.Apply(s, "hydreane-facturation-saas.drawio", repo, "test", null);
            Equal(0, r.Anomalies.Count, "anomalies");
            Equal(2, r.Views?.Count ?? 0, "vues");
        });
    }

    /// <summary>Critère d'acceptation de l'incrément 1 d'EV-03 : la facturation en SaaS, en deux étapes, sur Hydréane.</summary>
    public static void RunTrajectory(Repository repo)
    {
        Test("jeu Hydréane : à l'étape 2 du scénario SaaS, ONDINE est retirée, le SaaS l'a remplacée et ses flux suivent", () =>
        {
            var app = repo.ActiveObjects.Where(o => repo.Metamodel.IsA(o.TypeKey, AeTool.Core.Roles.Application)).ToDictionary(o => o.Name, o => o.Id);
            var flow = repo.Metamodel.RelationshipTypes.First(t => t.Roles.Contains(AeTool.Core.Roles.Flow)).Key;
            bool IsFlow(Relationship r) => repo.Metamodel.RelIsA(r.TypeKey, AeTool.Core.Roles.Flow);
            var ondineFlows = repo.ActiveRelationships.Count(r => IsFlow(r) && (r.SourceId == app["ONDINE"] || r.TargetId == app["ONDINE"]));
            var before = System.Text.Json.JsonSerializer.Serialize(repo.Objects);
            var s = new Scenario { Id = "scn-saas", ProjectId = "p", Name = "Facturation en SaaS" };
            s.Envisioned["env-saas"] = new ArchObject { Id = "env-saas", Name = "Offre de facturation en ligne", TypeKey = repo.Objects[app["ONDINE"]].TypeKey };
            s.Steps.Add(new TrajectoryStep { Id = "e1", Name = "Mise en service", Order = 1, Changes =
            {
                new TrajectoryChange { Id = "c1", Op = ChangeOps.Add, ElementId = "env-saas" },
                new TrajectoryChange { Id = "c2", Op = ChangeOps.Add, Target = "relation", Relation = new PlannedRelation { TypeKey = flow, SourceId = app["CAPTA"], TargetId = "env-saas", Name = "envoie les index relevés" } },
            } });
            s.Steps.Add(new TrajectoryStep { Id = "e2", Name = "Décommissionnement", Order = 2, Changes =
            {
                new TrajectoryChange { Id = "c3", Op = ChangeOps.Replace, ElementId = app["ONDINE"], ReplacementId = "env-saas" },
            } });
            repo.Scenarios[s.Id] = s;
            try
            {
                var p = Projection.Build(repo, "scn-saas", "e2");
                var space = new SpaceReader(p.Repo, new Analyzer(p.Repo));
                var names = space.Applications().Select(a => a.Name).ToHashSet();
                True(!names.Contains("ONDINE") && names.Contains("Offre de facturation en ligne"), "applications à l'étape 2");
                var saasFlows = p.Repo.ActiveRelationships.Count(r => IsFlow(r) && (r.SourceId == "env-saas" || r.TargetId == "env-saas"));
                Equal(ondineFlows, saasFlows, "les flux d'ONDINE passent au SaaS (CAPTA déjà relié à l'étape 1)");
                Equal(0, p.Issues.Count, "anomalies");
                Equal(before, System.Text.Json.JsonSerializer.Serialize(repo.Objects), "existant intact");
                var step1 = new SpaceReader(Projection.Build(repo, "scn-saas", "e1").Repo, new Analyzer(repo)).Applications().Select(a => a.Name).ToHashSet();
                True(step1.Contains("ONDINE") && step1.Contains("Offre de facturation en ligne"), "à l'étape 1, les deux coexistent");
            }
            finally { repo.Scenarios.Remove(s.Id); }
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
