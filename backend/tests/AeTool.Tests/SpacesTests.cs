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
