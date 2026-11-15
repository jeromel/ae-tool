using System.IO.Compression;
using System.Text;
using AeTool.Api.Auth;
using AeTool.Connectors;
using AeTool.Core;
using static AeTool.Tests.Harness;

namespace AeTool.Tests;

/// <summary>Import des dessins draw.io : lecture des formats, reconnaissance ArchiMate, rapprochement, vues et rejeu.</summary>
public static class DrawioTests
{
    private const string App = "html=1;whiteSpace=wrap;fillColor=#99ffff;shape=mxgraph.archimate3.application;appType=comp;archiType=square;";
    private const string FlowLine = "html=1;endArrow=block;dashed=1;elbow=vertical;endFill=1;";

    /// <summary>Une page : ITSM-SUITE (existe), PORTAIL CLIENT (nouveau), un rectangle ordinaire, un conteneur et ses enfants, des liens.</summary>
    public static string Page(string extraCells = "") => $"""
        <mxGraphModel><root>
          <mxCell id="0"/><mxCell id="1" parent="0"/>
          <mxCell id="a" value="ITSM-SUITE" style="{App}" vertex="1" parent="1"><mxGeometry x="40" y="40" width="120" height="55" as="geometry"/></mxCell>
          <mxCell id="b" value="&lt;b&gt;Portail&lt;/b&gt;&lt;br&gt;client" style="{App}" vertex="1" parent="1"><mxGeometry x="300" y="40" width="120" height="55" as="geometry"/></mxCell>
          <mxCell id="c" value="Note libre" style="rounded=0;whiteSpace=wrap;html=1;" vertex="1" parent="1"><mxGeometry x="40" y="200" width="100" height="40" as="geometry"/></mxCell>
          <mxCell id="z" value="Zone usine" style="swimlane;" vertex="1" parent="1"><mxGeometry x="500" y="200" width="200" height="120" as="geometry"/></mxCell>
          <mxCell id="z1" value="REPORTING-SERVICE" style="rounded=1;" vertex="1" parent="z"><mxGeometry x="20" y="40" width="120" height="50" as="geometry"/></mxCell>
          <mxCell id="e1" value="tickets" style="{FlowLine}" edge="1" parent="1" source="a" target="b"><mxGeometry relative="1" as="geometry"><Array as="points"><mxPoint x="230" y="20"/></Array></mxGeometry></mxCell>
          <mxCell id="e2" style="endArrow=classic;html=1;" edge="1" parent="1" source="a" target="c"><mxGeometry relative="1" as="geometry"/></mxCell>
          <mxCell id="e3" style="endArrow=block;dashed=1;endFill=1;" edge="1" parent="1" source="a" target="z1"><mxGeometry relative="1" as="geometry"/></mxCell>
          <mxCell id="e4" style="endArrow=classic;" edge="1" parent="1" source="a"><mxGeometry relative="1" as="geometry"><mxPoint x="10" y="10" as="targetPoint"/></mxGeometry></mxCell>
          {extraCells}
        </root></mxGraphModel>
        """;

    private static string File(params (string Id, string Name, string Content)[] pages) =>
        "<mxfile host=\"drawio\">" + string.Concat(pages.Select(p => $"<diagram id=\"{p.Id}\" name=\"{p.Name}\">{p.Content}</diagram>")) + "</mxfile>";

    /// <summary>Compression telle que draw.io l'écrit : encodage d'URL, deflate brut, base64.</summary>
    private static string Packed(string xml)
    {
        using var ms = new MemoryStream();
        using (var deflate = new DeflateStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            deflate.Write(Encoding.UTF8.GetBytes(Uri.EscapeDataString(xml)));
        return Convert.ToBase64String(ms.ToArray());
    }

    private static Stream S(string text) => new MemoryStream(Encoding.UTF8.GetBytes(text));
    private static DrawioItem Item(DrawioAnalysis a, string key) => a.Items.Single(i => i.Key == key);

    public static void Run()
    {
        Console.WriteLine("Import draw.io");

        Test("lecture : page en clair, page compressée et SVG modifiable donnent le même dessin", () =>
        {
            var plain = DrawioReader.Read(S(File(("p1", "Vue SI", Page()))));
            var packed = DrawioReader.Read(S(File(("p1", "Vue SI", Packed(Page())))));
            var svgContent = System.Security.SecurityElement.Escape(File(("p1", "Vue SI", Packed(Page()))));
            var svg = DrawioReader.Read(S($"<svg xmlns=\"http://www.w3.org/2000/svg\" content=\"{svgContent}\"><g/></svg>"));
            foreach (var pages in new[] { packed, svg })
            {
                Equal(1, pages.Count, "pages");
                Equal(plain[0].Cells.Count, pages[0].Cells.Count, "cellules");
                Equal("Vue SI", pages[0].Name, "nom de page");
            }
            var bare = DrawioReader.Read(S(Page()));
            Equal(plain[0].Cells.Count, bare[0].Cells.Count, "mxGraphModel seul");
        });

        Test("lecture : libellé HTML en texte brut, enfant en coordonnées de page", () =>
        {
            var page = DrawioReader.Read(S(File(("p1", "Vue", Page()))))[0];
            Equal("Portail\nclient", page.Cells.Single(c => c.Id == "b").Label, "libellé");
            var child = page.Cells.Single(c => c.Id == "z1");
            Equal(520.0, child.X, "x absolu"); Equal(240.0, child.Y, "y absolu");
        });

        Test("lecture : un fichier qui n'est pas un dessin est refusé avec un message clair", () =>
        {
            try { DrawioReader.Read(S("<html><body/></html>")); throw new Exception("aurait dû échouer"); }
            catch (InvalidDataException e) { True(e.Message.Contains("draw.io"), e.Message); }
            try { DrawioReader.Read(S("<svg xmlns=\"http://www.w3.org/2000/svg\"/>")); throw new Exception("aurait dû échouer"); }
            catch (InvalidDataException e) { True(e.Message.Contains("SVG modifiable"), e.Message); }
        });

        Test("reconnaissance ArchiMate : formes par couche, couleur changée signalée, liens par leur tracé", () =>
        {
            DrawioCell Shape(string style) => new() { Vertex = true, Style = DrawioReader.ParseStyle(style) };
            DrawioCell Line(string style) => new() { Edge = true, Style = DrawioReader.ParseStyle(style) };
            Equal("ApplicationComponent", DrawioConcepts.Element(Shape(App))!.Concept);
            Equal("BusinessProcess", DrawioConcepts.Element(Shape("shape=mxgraph.archimate3.application;appType=proc;fillColor=#ffff99;"))!.Concept);
            Equal("TechnologyService", DrawioConcepts.Element(Shape("shape=mxgraph.archimate3.application;appType=serv;fillColor=#AFFFAF;"))!.Concept);
            var guessed = DrawioConcepts.Element(Shape("shape=mxgraph.archimate3.application;appType=proc;fillColor=#123456;"))!;
            Equal("ApplicationProcess", guessed.Concept); Equal(DrawioConcepts.Guessed, guessed.How);
            Equal("DataObject", DrawioConcepts.Element(Shape("shape=mxgraph.archimate3.businessObject;fillColor=#99ffff;"))!.Concept);
            Equal("Equipment", DrawioConcepts.Element(Shape("shape=mxgraph.archimate3.tech;techType=equipment;"))!.Concept);
            Equal("Stakeholder", DrawioConcepts.Element(Shape("shape=mxgraph.archimate3.application;appType=role;fillColor=#CCCCFF;"))!.Concept);
            Equal<DrawioConcept?>(null, DrawioConcepts.Element(Shape("rounded=1;whiteSpace=wrap;")));
            Equal("Flow", DrawioConcepts.Relation(Line(FlowLine))!.Concept);
            Equal("Triggering", DrawioConcepts.Relation(Line("endArrow=block;endFill=1;"))!.Concept);
            Equal("Realization", DrawioConcepts.Relation(Line("endArrow=block;endFill=0;dashed=1;"))!.Concept);
            Equal("Serving", DrawioConcepts.Relation(Line("endArrow=open;endFill=1;"))!.Concept);
            Equal("Composition", DrawioConcepts.Relation(Line("startArrow=diamondThin;startFill=1;endArrow=none;"))!.Concept);
            Equal("Aggregation", DrawioConcepts.Relation(Line("startArrow=diamondThin;startFill=0;endArrow=none;"))!.Concept);
            Equal<DrawioConcept?>(null, DrawioConcepts.Relation(Line("html=1;")), "flèche ordinaire");
        });

        Test("analyse : rapproche par nom, propose de créer, garde le reste en dessin, sans rien écrire", () =>
        {
            var repo = Imported(out _);
            var before = Canonical(repo);
            var a = DrawioImporter.Analyse(S(File(("p1", "Vue SI", Page()))), "si.drawio", repo);
            Equal(before, Canonical(repo), "l'analyse n'écrit rien");
            var itsm = Item(a, "p1/a");
            Equal(DrawioItem.Link, itsm.Proposal, "ITSM-SUITE"); Equal("id-itsm", itsm.TargetId);
            var portal = Item(a, "p1/b");
            Equal(DrawioItem.Create, portal.Proposal, "Portail client"); Equal("Portail client", portal.Label); Equal("application", portal.TypeKey);
            Equal(DrawioItem.Drawing, Item(a, "p1/c").Proposal, "rectangle sans objet");
            Equal(DrawioItem.Link, Item(a, "p1/z1").Proposal, "forme ordinaire au nom d'un objet");
            Equal(DrawioItem.Create, Item(a, "p1/e1").Proposal, "flux ArchiMate vers un objet à créer");
            Equal(DrawioItem.Drawing, Item(a, "p1/e2").Proposal, "flèche ordinaire");
            Equal(DrawioItem.Link, Item(a, "p1/e3").Proposal, "flux existant ITSM-SUITE -> REPORTING-SERVICE");
            Equal("id-r3", Item(a, "p1/e3").TargetId);
            True(a.Anomalies.Any(x => x.Code == "lien-flottant"), "lien sans cible signalé");
        });

        Test("analyse : deux objets du même nom ne sont jamais choisis au hasard", () =>
        {
            var repo = Imported(out _);
            repo.Objects["id-twin"] = new ArchObject { Id = "id-twin", TypeKey = "application", Name = "ITSM SUITE" };
            var a = DrawioImporter.Analyse(S(File(("p1", "Vue", Page()))), "si.drawio", repo);
            var itsm = Item(a, "p1/a");
            Equal(DrawioItem.Drawing, itsm.Proposal); Equal(2, itsm.Candidates.Count, "candidats");
        });

        Test("analyse : un identifiant ou un type porté par la forme l'emporte sur le nom", () =>
        {
            var repo = Imported(out _);
            var extra = """
                <object id="d" label="Nom différent" ae.id="id-report"><mxCell style="rounded=1;" vertex="1" parent="1"><mxGeometry x="0" y="400" width="80" height="40" as="geometry"/></mxCell></object>
                <object id="f" label="Serveur batch" archimate.type="Node" vlan.id="12"><mxCell style="rounded=1;" vertex="1" parent="1"><mxGeometry x="200" y="400" width="80" height="40" as="geometry"/></mxCell></object>
                """;
            var a = DrawioImporter.Analyse(S(File(("p1", "Vue", Page(extra)))), "si.drawio", repo);
            Equal("id-report", Item(a, "p1/d").TargetId);
            Equal(DrawioConcepts.ByData, Item(a, "p1/f").ConceptHow);
            Equal(DrawioItem.Create, Item(a, "p1/f").Proposal);
        });

        Test("import : objets proposés à la validation, relation créée, une vue par page rangée dans son dossier", () =>
        {
            var repo = Imported(out _);
            var report = DrawioImporter.Apply(S(File(("p1", "Vue SI", Page()), ("p2", "Détail", Page()))), "si.drawio", repo, "claire", null);
            Equal(0, report.Anomalies.Count(x => x.Severity == "erreur"), "erreurs");
            var portal = repo.Objects.Values.Single(o => o.Name == "Portail client");
            Equal("Proposé", portal.ValidationStatus); Equal("drawio", portal.Source); Equal("claire", portal.ProposedBy);
            Equal("ApplicationComponent", portal.ArchimateType);
            Equal(1, repo.Objects.Values.Count(o => o.Name == "Portail client"), "un seul objet malgré deux pages");
            var flow = repo.Relationships.Values.Single(r => r.SourceId == "id-itsm" && r.TargetId == portal.Id);
            Equal("tickets", flow.Name);
            var views = repo.Diagrams.Values.Where(d => d.Source == "drawio").ToList();
            Equal(2, views.Count, "vues");
            var view = views.Single(v => v.Name == "Vue SI");
            var nodes = view.AllNodes().ToList();
            Equal("id-itsm", nodes.Single(n => n.Id.EndsWith("-a")).ElementId);
            Equal("Label", nodes.Single(n => n.Id.EndsWith("-c")).Kind);
            Equal("Note libre", nodes.Single(n => n.Id.EndsWith("-c")).Label);
            var zone = nodes.Single(n => n.Id.EndsWith("-z"));
            Equal("Container", zone.Kind); Equal(1, zone.Children.Count, "enfant dans sa zone");
            var e2 = view.Connections.Single(c => c.Id.EndsWith("-e2"));
            Equal("Line", e2.Kind); Equal<string?>(null, e2.RelationshipId);
            Equal("id-r3", view.Connections.Single(c => c.Id.EndsWith("-e3")).RelationshipId);
            Equal(1, view.Connections.Single(c => c.Id.EndsWith("-e1")).Bendpoints.Count, "point de passage");
            var folder = repo.Folders.Single(f => f.Source == "drawio");
            Equal(DrawioImporter.FolderLabel, folder.Label);
            Equal(2, folder.Children.Single(f => f.Label == "si.drawio").Entries.Count, "vues rangées");
            True(repo.Journal.Any(j => j.Action == "Import draw.io"), "journal");
        });

        Test("import rejoué : aucun doublon, aucune modification, et le lien survit à un renommage", () =>
        {
            var repo = Imported(out _);
            DrawioImporter.Apply(S(File(("p1", "Vue SI", Page()))), "si.drawio", repo, "claire", null);
            var before = Canonical(repo);
            var again = DrawioImporter.Apply(S(File(("p1", "Vue SI", Page()))), "si.drawio", repo, "claire", null);
            Equal(0, again.For("Objets").Added, "objets ajoutés"); Equal(0, again.For("Relations").Added, "relations ajoutées");
            Equal(1, again.For("Vues").Unchanged, "vue inchangée");
            Equal(before, Canonical(repo), "rien ne change");
            // Renommé dans l'outil, l'objet reste lié à sa forme grâce au lien gardé.
            repo.Objects.Values.Single(o => o.Name == "Portail client").Name = "Portail des abonnés";
            var a = DrawioImporter.Analyse(S(File(("p1", "Vue SI", Page()))), "si.drawio", repo);
            Equal(DrawioItem.Link, Item(a, "p1/b").Proposal);
        });

        Test("import : les choix de l'utilisateur l'emportent sur les propositions", () =>
        {
            var repo = Imported(out _);
            var decisions = new Dictionary<string, DrawioDecision>
            {
                ["p1/b"] = new() { Action = DrawioItem.Link, TargetId = "id-legacy" },
                ["p1/c"] = new() { Action = DrawioItem.Create, TypeKey = "noeud" },
                ["p1/z1"] = new() { Action = DrawioItem.Drawing },
            };
            var report = DrawioImporter.Apply(S(File(("p1", "Vue", Page()))), "si.drawio", repo, "claire", decisions);
            Equal(0, report.Anomalies.Count(x => x.Severity == "erreur"), "erreurs");
            True(!repo.Objects.Values.Any(o => o.Name == "Portail client"), "rien créé pour b, lié à l'existant");
            Equal("noeud", repo.Objects.Values.Single(o => o.Name == "Note libre").TypeKey);
            var view = repo.Diagrams.Values.Single(d => d.Source == "drawio");
            Equal("id-legacy", view.AllNodes().Single(n => n.Id.EndsWith("-b")).ElementId);
            Equal<string?>(null, view.AllNodes().Single(n => n.Id.EndsWith("-z1")).ElementId);
        });

        Test("une page retirée du fichier archive sa vue ; un import Archi garde les schémas draw.io", () =>
        {
            var repo = Imported(out _);
            DrawioImporter.Apply(S(File(("p1", "A", Page()), ("p2", "B", Page()))), "si.drawio", repo, "claire", null);
            var report = DrawioImporter.Apply(S(File(("p1", "A", Page()))), "si.drawio", repo, "claire", null);
            Equal(1, report.For("Vues").Archived, "vue archivée");
            ArchimateExchange.Import(Fixtures.Stream(Fixtures.Model()), repo);
            True(repo.Folders.Any(f => f.Source == "drawio"), "dossier draw.io gardé");
            True(repo.Diagrams.Values.Any(d => d.Source == "drawio" && !d.Archived), "vue draw.io gardée");
        });

        Test("export Archi : l'export complet laisse les schémas draw.io, l'export d'une vue choisie les emporte", () =>
        {
            var repo = Imported(out _);
            DrawioImporter.Apply(S(File(("p1", "Vue SI", Page()))), "si.drawio", repo, "claire", null);
            var full = ArchimateExchange.Export(repo).ToString();
            True(!full.Contains("drawio-"), "pas de vue draw.io dans l'export complet");
            True(!full.Contains(DrawioImporter.FolderLabel), "pas de dossier draw.io");
            var id = repo.Diagrams.Values.Single(d => d.Source == "drawio").Id;
            var one = ArchimateExchange.Export(repo, new[] { id }).ToString();
            True(one.Contains(id), "vue choisie exportée");
            var back = new Repository();
            var r = ArchimateExchange.Import(Fixtures.Stream(one), back);
            Equal(0, r.Anomalies.Count(x => x.Severity == "erreur"), "la vue exportée se réimporte");
        });

        Test("habilitations : un contributeur importe un dessin, le reste des imports reste à l'administrateur", () =>
        {
            Equal(AeTool.Api.Auth.Roles.Contributor, AccessRules.MinimumRole("POST", "/api/import/drawio"));
            Equal(AeTool.Api.Auth.Roles.Contributor, AccessRules.MinimumRole("POST", "/api/import/drawio/analyse"));
            Equal(AeTool.Api.Auth.Roles.Administrator, AccessRules.MinimumRole("POST", "/api/import/archimate"));
        });
    }
}
