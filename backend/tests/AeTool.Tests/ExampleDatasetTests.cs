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
            // Seul signalement attendu : « Partage Exploitation », nœud nommé « Partage… » sans sous-type, laissé exprès.
            Equal("partage-sans-sous-type", string.Join(",", archi.Anomalies.Select(a => a.Code)), "anomalies");
            Equal(426, repo.ActiveObjects.Count(), "objets");
            Equal(917, repo.ActiveRelationships.Count(), "relations");
            Equal(58, repo.ActiveDiagrams.Count(), "vues");
            Equal(0, repo.Objects.Values.Count(o => o.TypeKey == "autre"), "objets non classés");
        });
        Test("jeu Hydréane : seule la carte des capacités s'ouvre comme une carte (réponses de Camille §5)", () =>
        {
            var maps = repo.ActiveDiagrams.Where(d => AeTool.Api.Dto.IsCapabilityMap(repo, d)).Select(d => d.Name).ToList();
            Equal("Capacités - Carte des capacités", string.Join(" | ", maps), "cartes de capacités");
            var mixed = repo.ActiveDiagrams.First(d => d.Name == "Capacités - Applications : distribuer l'eau");
            mixed.Viewpoint = "Capability Map";
            True(AeTool.Api.Dto.IsCapabilityMap(repo, mixed), "le point de vue d'Archi suffit");
            mixed.Viewpoint = null;
        });
        Test("jeu Hydréane : rejeu du modèle sans écart", () =>
        {
            using var s = File.OpenRead(Path.Combine(dir, "hydreane-referentiel.xml"));
            var again = ArchimateExchange.Import(s, repo);
            Equal(0, again.Counts.Values.Sum(c => c.Added + c.Updated + c.Archived), "écarts au rejeu");
        });
        Test("jeu Hydréane : l'analyse d'impact dit par où l'on atteint chaque objet, tous les chemins d'un même niveau (maquette impact)", () =>
        {
            var an = new Analyzer(repo);
            bool IsApp(string typeKey) => repo.Metamodel.IsA(typeKey, AeTool.Core.Roles.Application);
            var capacities = repo.ActiveObjects.Where(o => repo.Metamodel.IsA(o.TypeKey, AeTool.Core.Roles.Capability)).ToList();
            var shared = capacities.Select(c => (c, reached: an.Impact(c.Id, 2, "amont")))
                .Select(x => (x.c, x.reached, item: x.reached.FirstOrDefault(r => r.Depth == 2 && r.Layer == "technique" && r.From.Count >= 2)))
                .FirstOrDefault(x => x.item is not null);
            True(shared.item is not null, "une infrastructure partagée par deux applications d'une même capacité");
            var apps = shared.reached.Where(r => r.Depth == 1).ToDictionary(r => r.Id);
            True(shared.item!.From.All(f => apps.ContainsKey(f) && IsApp(apps[f].TypeKey)), $"{shared.item.Name} atteint par {string.Join(", ", shared.item.From)}");
            True(shared.reached.Where(r => r.Depth == 1).All(r => r.From.SequenceEqual(new[] { shared.c.Id })), "au premier niveau, on vient de l'objet analysé");
            Equal(shared.reached.Count, shared.reached.Select(r => r.Id).Distinct().Count(), "chaque objet une seule fois");
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
        CatalogueTests.RunOnExample(dir);
    }

    /// <summary>Le dessin draw.io du jeu se rattache au référentiel Hydréane : les chiffres annoncés dans le README d'exemple.</summary>
    public static void RunDrawing(Repository repo, string dir)
    {
        Test("jeu Hydréane : dessins de collègue pour EV-05, lisibles, v2 copie de la v1, dessin 2 distinct", () =>
        {
            // Contenu : note-dessins-collegues-hydreane-haussman. On vérifie la structure, pas le rapprochement (EV-05 à venir).
            DrawioAnalysis Read(string f) { using var s = File.OpenRead(Path.Combine(dir, f)); return DrawioImporter.Analyse(s, f, repo); }
            var v1 = Read("facturation-cible.drawio");
            var v2 = Read("facturation-cible-v2.drawio");
            var infra = Read("infra-facturation-2027.drawio");
            foreach (var (a, name) in new[] { (v1, "v1"), (v2, "v2"), (infra, "infra") }) Equal(0, a.Anomalies.Count, "anomalies " + name);
            Equal("12/9", $"{v1.Pages.Single().Shapes}/{v1.Pages.Single().Links}", "formes et flèches de la v1");
            Equal("13/9", $"{v2.Pages.Single().Shapes}/{v2.Pages.Single().Links}", "v2 : deux formes ajoutées, une retirée ; une flèche ajoutée, une retirée");
            Equal("5/3", $"{infra.Pages.Single().Shapes}/{infra.Pages.Single().Links}", "dessin 2");
            Equal(v1.Pages.Single().Id, v2.Pages.Single().Id, "v2 copiée de la v1 : même identifiant de page");
            True(infra.Pages.Single().Id != v1.Pages.Single().Id, "dessin 2 : autre identifiant de page");
            foreach (var f in new[] { "facturation-cible.drawio", "facturation-cible-v2.drawio", "infra-facturation-2027.drawio" })
                True(!File.ReadAllText(Path.Combine(dir, f)).Contains("ae.id"), "aucun ae.id dans " + f);
        });
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

        Test("jeu Hydréane : une vue de flux porte le code de chaque flux et seules ses données transportées", () =>
        {
            // Référentiel à part : le catalogue modifie le modèle, les contrôles précédents portent sur le modèle seul.
            var dir = Locate()!;
            var local = new Repository();
            using (var s = File.OpenRead(Path.Combine(dir, "hydreane-referentiel.xml"))) ArchimateExchange.Import(s, local);
            using (var s = File.OpenRead(Path.Combine(dir, "hydreane-catalogue-donnees-flux.xlsx"))) CatalogueImporter.Apply(s, "catalogue.xlsx", local, "test", null);
            var view = local.ActiveDiagrams.Single(d => d.Name.StartsWith("Flux - Finances"));
            var json = System.Text.Json.JsonSerializer.SerializeToElement(AeTool.Api.Dto.Diagram(local, view, true));
            var flows = json.GetProperty("Connections").EnumerateArray().Where(c => c.GetProperty("Flow").GetBoolean()).ToList();
            var fa015 = flows.Single(c => c.GetProperty("Code").GetString() == "FA-015");
            var carried = fa015.GetProperty("Carried").EnumerateArray().Select(x => x.GetString()).ToList();
            True(carried.Contains("Dossiers salariés"), "donnée transportée : " + string.Join(", ", carried));
            True(!carried.Any(x => x!.Contains("Bus") || x.Contains("Export RH")), "ni bus ni flux technique : " + string.Join(", ", carried));
        });

        // Décision 015 : une relation revenue d'Archi devient une relation d'Archi, flux du catalogue compris.
        Test("jeu Hydréane : après un aller-retour par Archi, rien n'est signalé, les flux du catalogue passent à Archi en gardant code et caractéristiques", () =>
        {
            var dir = Locate()!;
            var local = new Repository();
            using (var s = File.OpenRead(Path.Combine(dir, "hydreane-referentiel.xml"))) ArchimateExchange.Import(s, local);
            var catalogue = Path.Combine(dir, "hydreane-catalogue-donnees-flux.xlsx");
            using (var s = File.OpenRead(catalogue)) CatalogueImporter.Apply(s, "catalogue.xlsx", local, "test", null);
            var ids = local.ActiveRelationships.Where(r => r.Source == CatalogueImporter.SourceTag).Select(r => r.Id).ToList();
            True(ids.Count > 0, "relations du catalogue avant l'aller-retour");
            string? Code(string i) => local.Relationships[i].Properties.FirstOrDefault(p => p.Key == CatalogueModel.Code)?.Value;
            var codes = ids.ToDictionary(i => i, Code);
            var trip = ArchimateExchange.Import(Fixtures.Stream(ArchimateExchange.Export(local).ToString()), local);
            Equal(0, trip.Counts.Values.Sum(c => c.Added + c.Updated + c.Archived), "écarts signalés au premier aller-retour");
            True(ids.All(i => local.Relationships[i].Source == "archi" && !local.Relationships[i].Archived), "flux du catalogue passés à Archi");
            True(ids.All(i => Code(i) == codes[i]) && codes.Values.Any(c => c is not null), "codes du catalogue gardés");
            True(trip.Notes.Any(n => n.Contains("relation(s) créée(s) dans l'outil revenue(s) d'Archi")), "la bascule est annoncée");
            // Réimport du catalogue : il retrouve ses flux par extrémités et par type, sans doublon.
            var flows = local.ActiveRelationships.Count();
            using (var s = File.OpenRead(catalogue)) CatalogueImporter.Apply(s, "catalogue.xlsx", local, "test", null);
            Equal(flows, local.ActiveRelationships.Count(), "aucun doublon au réimport du catalogue");
            // Risque résiduel accepté (015) : un modèle maître qui ne les porte plus les archive, comme toute relation d'Archi supprimée.
            using (var s = File.OpenRead(Path.Combine(dir, "hydreane-referentiel.xml"))) ArchimateExchange.Import(s, local);
            True(ids.All(i => local.Relationships[i].Archived), "absents du modèle maître : archivés");
        });
    }

    internal static string? Locate()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var candidate = Path.Combine(d.FullName, "exemples", "hydreane");
            if (Directory.Exists(candidate)) return candidate;
        }
        return null;
    }
}
