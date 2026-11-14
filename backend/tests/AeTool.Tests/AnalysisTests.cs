using System.Text.Json;
using AeTool.Connectors;
using AeTool.Core;
using static AeTool.Tests.Harness;

namespace AeTool.Tests;

public static class AnalysisTests
{
    public static void Run()
    {
        Console.WriteLine("Projets, analyses et métamodèle");

        Test("impacts déduits des vues du dossier projet, fond de carte exclu", () =>
        {
            var repo = Imported(out _);
            var an = new Analyzer(repo);
            var impacted = an.ImpactsOf("id-prj1").Select(i => i.ObjectId).OrderBy(x => x).ToList();
            Equal("id-itsm,id-report", string.Join(",", impacted)); // la capacité (fond de carte) est exclue
            True(repo.Impacts.All(i => i.Nature == ImpactNature.ToQualify && i.Origin == "vue"), "impacts déduits à qualifier");
        });
        Test("une nature qualifiée survit au réimport ; un impact non qualifié disparu est retiré", () =>
        {
            var repo = Imported(out _);
            repo.Impacts.First(i => i.ProjectId == "id-prj1" && i.ObjectId == "id-report").Nature = "Évolution";
            var withoutNodes = Fixtures.Model().Replace("""<node identifier="id-n4" elementRef="id-itsm" xsi:type="Element" x="10" y="10" w="120" h="55" />""", "")
                .Replace("""<node identifier="id-n5" elementRef="id-report" xsi:type="Element" x="300" y="10" w="120" h="55" />""", "")
                .Replace("""source="id-n4" target="id-n5">""", """source="id-n6" target="id-n6">""");
            ArchimateExchange.Import(Fixtures.Stream(withoutNodes), repo);
            var left = repo.Impacts.Where(i => i.ProjectId == "id-prj1").ToList();
            Equal(1, left.Count); Equal("Évolution", left[0].Nature);
        });
        Test("adhérence : objet partagé entre deux projets", () =>
        {
            var repo = Imported(out _);
            var adherences = new Analyzer(repo).Adherences();
            Equal(1, adherences.Count); Equal("id-report", adherences[0].SharedObjectIds.Single());
        });
        Test("adhérence : objets directement reliés", () =>
        {
            var repo = Imported(out _);
            repo.Impacts.RemoveAll(i => i.ObjectId == "id-report" && i.ProjectId == "id-prj2");
            var a = new Analyzer(repo).Adherences().Single();
            Equal(0, a.SharedObjectIds.Count);
            True(a.LinkedPairs.Any(p => p.Contains("id-itsm") && p.Contains("id-ticket")), "ITSM et Ticket sont reliés par un accès");
        });
        Test("analyse d'impact amont et aval, profondeur réglable", () =>
        {
            var an = new Analyzer(Imported(out _));
            Equal("id-itsm", an.Impact("id-srv", 1, "aval").Single().Id);
            Equal(4, an.Impact("id-srv", 2, "aval").Count);
            Equal("id-itsm,id-legacy", string.Join(",", an.Impact("id-cap", 1, "amont").Select(r => r.Id).OrderBy(x => x)));
            Equal(3, an.Impact("id-cap", 2, "tous").Count);
        });
        Test("graphe de voisinage", () =>
        {
            var g = new Analyzer(Imported(out _)).Neighborhood("id-itsm", 1);
            Equal(5, g.Nodes.Count); Equal(4, g.Edges.Count);
        });
        Test("matrice capacité × application : relation (R) et imbrication dans une vue (V)", () =>
        {
            var an = new Analyzer(Imported(out _));
            var m = an.CrossMatrix("capacite", "application", includeNesting: true);
            Equal(1, m.Rows.Count); Equal(2, m.Columns.Count);
            Equal("RV", m.Cells[0][m.Columns.FindIndex(c => c.Id == "id-itsm")]);
            Equal("R", m.Cells[0][m.Columns.FindIndex(c => c.Id == "id-legacy")]);
        });
        Test("visuel : celui de l'élément prime sur celui du type, attribut par attribut", () =>
        {
            var repo = Imported(out _);
            var o = repo.Objects["id-itsm"];
            Equal("#b5ffff", repo.Metamodel.EffectiveVisual(o).Fill);
            o.VisualOverride = new Visual { Fill = "#123456" };
            var v = repo.Metamodel.EffectiveVisual(o);
            Equal("#123456", v.Fill); Equal("#5c5c5c", v.Stroke); Equal("rectangle", v.Shape);
        });
        Test("qualité : complétude et orphelins par type", () =>
        {
            var q = new Analyzer(Imported(out _)).Quality().First(t => t.TypeKey == "application");
            Equal(3, q.Count); Equal(1, q.Documented); Equal(1, q.NotInDiagram); Equal(0, q.Orphans);
        });
    }
}
