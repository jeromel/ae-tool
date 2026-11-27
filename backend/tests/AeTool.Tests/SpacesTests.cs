using System.Text.Json;
using AeTool.Connectors;
using AeTool.Core;
using static AeTool.Tests.Harness;

namespace AeTool.Tests;

public static class SpacesTests
{
    public static void Run()
    {
        Console.WriteLine("Espaces par couche");
        Test("carte des capacités : l'application liée par relation et par imbrication est rattachée une seule fois", () =>
        {
            var repo = Imported(out _);
            var roots = new SpaceReader(repo, new Analyzer(repo)).CapabilityMap();
            Equal(1, roots.Count, "capacités racines");
            Equal(1, roots[0].Applications.Count(a => a.Id == "id-itsm"), "rattachements de l'ITSM");
            Equal("relation et vue", roots[0].Applications.Single(a => a.Id == "id-itsm").Via, "origine du rattachement de l'ITSM");
            Equal("relation", roots[0].Applications.Single(a => a.Id == "id-legacy").Via, "origine du rattachement du portail");
        });
        Test("espace applications : flux, socle technique, données et capacités comptés par application", () =>
        {
            var repo = Imported(out _);
            var rows = new SpaceReader(repo, new Analyzer(repo)).Applications().ToDictionary(r => r.Id);
            var itsm = rows["id-itsm"];
            Equal(1, itsm.Flows, "flux de l'ITSM");
            Equal(1, itsm.Technical, "socle technique de l'ITSM");
            Equal(1, itsm.Data, "données de l'ITSM");
            Equal(1, itsm.Capabilities, "capacités de l'ITSM");
            Equal(0, rows["id-report"].Capabilities, "capacités du reporting");
        });
        Test("espace flux : le flux porte les données qui lui sont rattachées, et un protocole absent reste vide", () =>
        {
            var repo = Imported(out _);
            var flows = new SpaceReader(repo, new Analyzer(repo)).Flows();
            Equal(1, flows.Count, "flux");
            Equal("Tickets clos", flows[0].Name, "nom");
            True(flows[0].Carried.SequenceEqual(new[] { "Ticket de support" }), "données transportées");
            True(flows[0].Protocol is null, "protocole non renseigné");
        });
        Test("règle « flux de données » : étapes exclues, service et partage inclus, protocole attendu hors fonctions, même lecture partout", () =>
        {
            var repo = Imported(out _);
            CatalogueModel.Ensure(repo.Metamodel);
            void Obj(string id, string type) => repo.Objects[id] = new ArchObject { Id = id, Name = id, TypeKey = type };
            void Flow(string id, string from, string to) => repo.Relationships[id] = new Relationship { Id = id, TypeKey = "flux", SourceId = from, TargetId = to };
            Obj("vs-1", "chaine-valeur"); Obj("vs-2", "chaine-valeur");
            Obj("svc", "service-applicatif"); Obj("bf-1", "bloc-fonctionnel"); Obj("bf-2", "bloc-fonctionnel");
            Obj("share", CatalogueModel.FileShareType);
            Flow("f-etapes", "vs-1", "vs-2");      // entre deux étapes : exclu
            Flow("f-service", "id-itsm", "svc");   // vers un service applicatif : inclus, protocole attendu
            Flow("f-blocs", "bf-1", "bf-2");       // entre deux blocs fonctionnels : inclus, pas de protocole attendu
            Flow("f-partage", "id-itsm", "share"); // d'une application vers un partage : inclus
            var rule = new DataFlowRule(repo);
            var expected = new[] { "f-blocs", "f-partage", "f-service", "id-r3" };
            True(rule.DataFlows.Select(f => f.Id).Order().SequenceEqual(expected), "flux de données : " + string.Join(", ", rule.DataFlows.Select(f => f.Id).Order()));
            True(rule.ExpectsProtocol(repo.Relationships["f-service"]), "protocole attendu vers un service");
            True(!rule.ExpectsProtocol(repo.Relationships["f-blocs"]), "pas de protocole entre blocs fonctionnels");
            True(rule.ExpectsProtocol(repo.Relationships["f-partage"]), "protocole attendu vers un partage");
            True(!rule.ExpectsProtocol(repo.Relationships["f-etapes"]), "rien d'attendu entre étapes");
            // Les trois lecteurs voient les mêmes flux : c'est leur divergence qui faisait le défaut.
            var space = new SpaceReader(repo, new Analyzer(repo)).Flows();
            True(space.Select(f => f.Id).Order().SequenceEqual(expected), "espace Flux : " + string.Join(", ", space.Select(f => f.Id).Order()));
            True(space.Single(f => f.Id == "f-blocs").ProtocolExpected == false && space.Single(f => f.Id == "f-service").ProtocolExpected, "champ protocole attendu");
            True(new LineageHelpers(repo).Flows.Select(f => f.Id).Order().SequenceEqual(expected), "contrôles du catalogue");
            var lineage = new LineageReader(repo).Read("id-itsm", false);
            True(lineage.Flows.Any(f => f.Id == "f-service") && lineage.Flows.Any(f => f.Id == "f-partage"), "parcours : flux vers le service et le partage");
            True(new LineageReader(repo).Read("vs-1", false).Flows.All(f => f.Id != "f-etapes"), "parcours : aucun flux entre étapes");
            // Un nœud doté d'une règle conditionnelle vers un composant reste un nœud : ConceptOf, la même lecture que l'interface.
            repo.Metamodel.Transcoding.Elements.Add(new ElementRule { TypeKey = "noeud", ArchimateType = "ApplicationComponent", WhenProperty = "x", WhenValue = "y" });
            True(!new DataFlowRule(repo).IsApplicative("id-srv"), "nœud : pas une extrémité applicative");
            var svc = lineage.Applications.Single(a => a.Id == "svc");
            True(!svc.External, "un service applicatif n'est pas un destinataire externe");
            True(!lineage.Gaps.Any(g => g.Contains("Hébergement de svc")), "un service n'a pas d'hébergement à renseigner");
        });
        Test("espaces données et infrastructure : applications utilisatrices et applications portées", () =>
        {
            var repo = Imported(out _);
            var reader = new SpaceReader(repo, new Analyzer(repo));
            Equal(1, reader.Data().Single(d => d.Id == "id-ticket").Applications, "applications utilisant le ticket");
            Equal(2, reader.Infrastructure().Single(i => i.Id == "id-srv").Applications, "applications portées par le serveur, flux compris");
        });
        Test("matrice limitée aux flux : elle se lit de l'émetteur vers le récepteur", () =>
        {
            var repo = Imported(out _);
            var m = new Analyzer(repo).CrossMatrix(Roles.Prefix + Roles.Application, "application", true, Roles.Prefix + Roles.Flow);
            Equal(1, m.Rows.Count, "émetteurs");
            Equal("id-itsm", m.Rows[0].Id, "émetteur");
            Equal("id-report", m.Columns.Single().Id, "récepteur");
        });
    }
}
