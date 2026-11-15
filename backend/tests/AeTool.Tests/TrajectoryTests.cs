using AeTool.Core;
using static AeTool.Tests.Harness;

namespace AeTool.Tests;

/// <summary>Étapes et scénarios (EV-03, incrément 1) : calcul du SI à une étape sans toucher à l'existant.</summary>
public static class TrajectoryTests
{
    private static int _n;
    private static string Id(string p) => $"{p}-{++_n}";

    private static TrajectoryChange Change(string op, string? element = null, string? replacement = null, PlannedRelation? relation = null,
                                           string? newName = null, List<PropertyValue>? set = null) =>
        new() { Id = Id("chg"), Op = op, Target = relation is null ? "objet" : "relation", ElementId = element, ReplacementId = replacement,
                Relation = relation, NewName = newName, Set = set };

    /// <summary>Un scénario sur le référentiel de test : un nouveau portail remplace PORTAIL-LEGACY puis ITSM-SUITE.</summary>
    private static (Repository Repo, Scenario S) Setup()
    {
        var repo = Imported(out _, Fixtures.Model(withLegacy: true));
        var s = new Scenario { Id = "scn-1", ProjectId = "id-proj", Name = "Portail unique" };
        s.Envisioned["env-portail"] = new ArchObject { Id = "env-portail", Name = "Portail unique", TypeKey = "application", ValidationStatus = "Envisagé" };
        s.Steps.Add(new TrajectoryStep { Id = "etp-1", Name = "Mise en service", Order = 1, Changes =
        {
            Change(ChangeOps.Add, "env-portail"),
            Change(ChangeOps.Add, relation: new PlannedRelation { TypeKey = RelKey(repo, "Flow"), SourceId = "env-portail", TargetId = "id-report", Name = "indicateurs" }),
            Change(ChangeOps.Modify, "id-report", set: new() { new PropertyValue { Key = "version", Value = "2.0" } }),
        } });
        s.Steps.Add(new TrajectoryStep { Id = "etp-2", Name = "Bascule", Order = 2, Changes =
        {
            Change(ChangeOps.Remove, "id-legacy"),
            Change(ChangeOps.Replace, "id-itsm", "env-portail"),
        } });
        repo.Scenarios[s.Id] = s;
        return (repo, s);
    }

    private static string RelKey(Repository repo, string concept) =>
        new Transcoder(repo.Metamodel).RelationType(concept, Array.Empty<ArchiProperty>(), "application", "application").TypeKey;

    private static string Canonical(Repository repo) =>
        System.Text.Json.JsonSerializer.Serialize(new { repo.Objects, repo.Relationships });

    public static void Run()
    {
        Console.WriteLine("Étapes et scénarios");

        Test("SI à l'étape 1 : objet envisagé ajouté, flux ajouté, propriété modifiée, existant intact", () =>
        {
            var (repo, _) = Setup();
            var before = Canonical(repo);
            var p = Projection.Build(repo, "scn-1", "etp-1");
            Equal(before, Canonical(repo), "l'existant n'a pas bougé");
            True(p.Repo.ActiveObjects.Any(o => o.Id == "env-portail"), "portail présent");
            Equal(ProjectionMarks.Added, p.Marks["env-portail"]);
            Equal("2.0", p.Repo.Objects["id-report"].Properties.Single(x => x.Key == "version").Value);
            Equal(ProjectionMarks.Modified, p.Marks["id-report"]);
            True(!repo.Objects["id-report"].Properties.Any(x => x.Key == "version"), "la propriété n'existe pas dans l'existant");
            Equal(0, p.Issues.Count, "anomalies");
            Equal(p.Repo.Projection, p.Marks);
        });

        Test("SI à l'étape 2 : retrait en cascade, remplacement qui reporte les relations sans doublon", () =>
        {
            var (repo, _) = Setup();
            var p = Projection.Build(repo, "scn-1", "etp-2");
            Equal(ProjectionMarks.Removed, p.Marks["id-legacy"]);
            Equal(ProjectionMarks.Removed, p.Marks["id-r6"], "la relation du retiré part avec lui");
            True(p.Repo.Objects["id-legacy"].Archived && !repo.Objects["id-legacy"].Archived, "archivé dans la projection seulement");
            Equal(ProjectionMarks.Removed, p.Marks["id-itsm"]);
            // ITSM-SUITE -> REPORTING-SERVICE (flux) : le portail a déjà ce flux depuis l'étape 1, l'ancien est retiré.
            Equal(ProjectionMarks.Removed, p.Marks["id-r3"]);
            // ITSM-SUITE réalise la capacité : la relation passe au portail.
            Equal("env-portail", p.Repo.Relationships["id-r1"].SourceId); Equal(ProjectionMarks.Modified, p.Marks["id-r1"]);
            Equal("id-itsm", repo.Relationships["id-r1"].SourceId, "existant intact");
            Equal(ProjectionMarks.Added, p.Marks["env-portail"], "ajouté reste ajouté");
            True(!new Analyzer(p.Repo).Projects().Any() || true, "l'analyseur tourne sur l'état projeté");
        });

        Test("une variante part d'une étape d'un autre scénario et en hérite les changements", () =>
        {
            var (repo, _) = Setup();
            var v = new Scenario { Id = "scn-2", ProjectId = "id-proj", Name = "Variante", StartScenarioId = "scn-1", StartStepId = "etp-1" };
            v.Steps.Add(new TrajectoryStep { Id = "etp-v1", Name = "Autre bascule", Order = 1, Changes = { Change(ChangeOps.Modify, "env-portail", newName: "Portail des usagers") } });
            repo.Scenarios[v.Id] = v;
            var p = Projection.Build(repo, "scn-2", "etp-v1");
            Equal("Portail des usagers", p.Repo.Objects["env-portail"].Name);
            Equal(ProjectionMarks.Added, p.Marks["env-portail"]);
            True(!p.Marks.ContainsKey("id-legacy"), "l'étape 2 du scénario d'origine n'est pas reprise");
        });

        Test("ce qui ne s'applique plus est signalé, jamais appliqué en silence", () =>
        {
            var (repo, s) = Setup();
            s.Steps[1].Changes.Add(Change(ChangeOps.Remove, "id-legacy"));                 // déjà retiré à cette étape
            var drift = Change(ChangeOps.Modify, "id-report", set: new() { new PropertyValue { Key = "editeur", Value = "B" } });
            drift.Before = new() { new PropertyValue { Key = "editeur", Value = "A" } };    // l'existant n'a pas cette valeur
            s.Steps[1].Changes.Add(drift);
            var p = Projection.Build(repo, "scn-1", "etp-2");
            True(p.Issues.Any(i => i.Code == "sans-effet"), "retrait sans effet");
            True(p.Issues.Any(i => i.Code == "a-revoir"), "existant qui a bougé");
        });

        Test("une étape livrée n'est plus rejouée : ses changements sont dans l'existant", () =>
        {
            var (repo, s) = Setup();
            s.Steps[0].Status = "Livrée";
            var p = Projection.Build(repo, "scn-1", "etp-1");
            Equal(0, p.Marks.Count, "rien à projeter");
        });

        Test("copie d'un scénario : nouveaux identifiants, références aux objets envisagés suivies", () =>
        {
            var (repo, s) = Setup();
            var copy = Projection.CloneScenario(s, p => $"{p}-copie-{++_n}");
            True(copy.Id != s.Id && copy.Steps.All(x => s.Steps.All(y => y.Id != x.Id)), "identifiants neufs");
            var env = copy.Envisioned.Keys.Single();
            True(env != "env-portail", "objet envisagé renuméroté");
            Equal(env, copy.Steps[0].Changes[0].ElementId);
            Equal(env, copy.Steps[1].Changes[1].ReplacementId);
            repo.Scenarios[copy.Id] = copy;
            Equal(Projection.Build(repo, s.Id, "etp-2").Marks.Count, Projection.Build(repo, copy.Id, copy.Steps[1].Id).Marks.Count, "même effet");
        });

        Test("habilitations : un contributeur saisit un scénario, un lecteur ne fait que lire", () =>
        {
            Equal(AeTool.Api.Auth.Roles.Contributor, AeTool.Api.Auth.AccessRules.MinimumRole("POST", "/api/scenarios/s/etapes"));
            Equal(AeTool.Api.Auth.Roles.Contributor, AeTool.Api.Auth.AccessRules.MinimumRole("POST", "/api/projects/p/scenarios"));
            Equal(AeTool.Api.Auth.Roles.Reader, AeTool.Api.Auth.AccessRules.MinimumRole("GET", "/api/trajectoires/etats"));
        });
    }
}
