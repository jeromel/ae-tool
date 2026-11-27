using System.Xml.Linq;
using AeTool.Api;
using AeTool.Api.Auth;
using AeTool.Connectors;
using AeTool.Core;
using Microsoft.AspNetCore.Http;
using static AeTool.Tests.Harness;

namespace AeTool.Tests;

/// <summary>Export B, « Pas encore dans Archi » (maquette de Camille, décisions 014 et 015).</summary>
public static class ArchiPendingTests
{
    private static readonly XNamespace A = "http://www.opengroup.org/xsd/archimate/3.0/";
    private static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";

    /// <summary>
    /// Modèle d'essai enrichi ici : une application validée reliée à une capacité d'Archi, une donnée validée reliée à un flux
    /// d'Archi (relation vers une relation), un brouillon et un proposé, et une relation qui touche le brouillon.
    /// </summary>
    private static Repository Worked()
    {
        var repo = Imported(out _);
        var app = repo.Objects["id-report"].TypeKey;
        var data = repo.Objects["id-ticket"].TypeKey;
        ArchObject Add(string id, string type, string name, string status)
        {
            var o = new ArchObject { Id = id, TypeKey = type, Name = name, Source = "saisie", ValidationStatus = status,
                Documentation = "Saisi ici.", Properties = { new PropertyValue { Key = "lifecycle.state", Value = "Projet" } } };
            repo.Objects[id] = o;
            return o;
        }
        Add("id-new", app, "NOUVELLE-APP", Validation.Validated).ValidatedBy = "valideur";
        Add("id-new-data", data, "Donnée nouvelle", Validation.Validated);
        Add("id-draft", app, "BROUILLON-APP", Validation.Draft);
        Add("id-proposed", app, "PROPOSEE-APP", Validation.Proposed);
        var realization = repo.Relationships["id-r1"].TypeKey;
        var association = repo.Relationships["id-r5"].TypeKey;
        void Rel(string id, string type, string from, string to) =>
            repo.Relationships[id] = new Relationship { Id = id, TypeKey = type, SourceId = from, TargetId = to, Source = "saisie" };
        Rel("id-n1", realization, "id-new", "id-cap");
        Rel("id-n2", association, "id-new-data", "id-r3");
        Rel("id-n3", realization, "id-draft", "id-cap");
        Rel("id-n4", association, "id-new", "id-n3"); // vise une relation qui attend : elle attend aussi
        return repo;
    }

    /// <summary>Fusion d'Archi simulée : le modèle maître reçoit ce qui manque du fichier B, sans toucher à ce qu'il a déjà.</summary>
    private static string Merge(string master, XDocument b)
    {
        var doc = XDocument.Parse(master);
        var model = doc.Root!;
        foreach (var (section, tag) in new[] { ("elements", "element"), ("relationships", "relationship") })
        {
            var target = model.Element(A + section)!;
            var known = target.Elements(A + tag).Select(e => (string)e.Attribute("identifier")!).ToHashSet();
            foreach (var e in b.Root!.Element(A + section)?.Elements(A + tag) ?? Enumerable.Empty<XElement>())
                if (!known.Contains((string)e.Attribute("identifier")!))
                {
                    var copy = new XElement(e);
                    copy.Element(A + "properties")?.Remove(); // définitions propres au fichier B : non reprises dans la simulation
                    target.Add(copy);
                }
        }
        return doc.ToString();
    }

    public static void Run()
    {
        Console.WriteLine("Export B, pas encore dans Archi");

        Test("pas encore dans Archi : juste après l'import du modèle, rien à reporter, date du dernier import connue", () =>
        {
            var p = ArchiPending.Of(Imported(out _));
            True(p.IsEmpty, "rien à reporter");
            True(!p.NeverImported, "un modèle a été importé");
            True(p.LastImport is not null, "date du dernier import");
        });
        Test("pas encore dans Archi : sans import d'Archi, tout est nouveau, on renvoie à l'export complet", () =>
        {
            var repo = new Repository();
            repo.Objects["id-x"] = new ArchObject { Id = "id-x", TypeKey = "composant", Name = "X", Source = "saisie", ValidationStatus = Validation.Validated };
            True(ArchiPending.Of(repo).NeverImported, "jamais importé");
        });
        Test("pas encore dans Archi : objets validés seulement, brouillons et proposés comptés, relations qui les touchent aussi", () =>
        {
            var p = ArchiPending.Of(Worked());
            Equal("Donnée nouvelle, NOUVELLE-APP", string.Join(", ", p.Objects.Select(o => o.Name)), "objets");
            Equal(1, p.ExcludedDrafts, "brouillons");
            Equal(1, p.ExcludedProposed, "proposés");
            Equal("id-n1, id-n2", string.Join(", ", p.Relations.Select(r => r.Id).OrderBy(x => x)), "relations qui partent");
            Equal(2, p.ExcludedRelations, "relation vers le brouillon, et relation vers cette relation");
        });
        Test("pas encore dans Archi : objets et relations d'Archi cités, identifiant, type et nom seulement", () =>
        {
            var p = ArchiPending.Of(Worked());
            Equal("Gérer les services numériques, ITSM-SUITE, REPORTING-SERVICE", string.Join(", ", p.CitedObjects.Select(o => o.Name)), "objets cités");
            Equal("id-r3", string.Join(", ", p.CitedRelations.Select(r => r.Id)), "relation citée (visée par une relation)");
            var xml = ArchimateExchange.Export(Worked(), pending: p);
            var elements = xml.Root!.Element(A + "elements")!.Elements(A + "element").ToDictionary(e => (string)e.Attribute("identifier")!);
            Equal(5, elements.Count, "2 objets qui partent, 3 cités");
            var cap = elements["id-cap"];
            Equal("Capability", (string?)cap.Attribute(Xsi + "type"), "type de l'objet cité");
            True(cap.Element(A + "properties") is null && cap.Element(A + "documentation") is null, "objet cité sans valeurs");
            True(elements["id-new"].Element(A + "properties") is not null && elements["id-new"].Element(A + "documentation") is not null, "objet qui part, avec ses valeurs");
            var r3 = xml.Root!.Element(A + "relationships")!.Elements(A + "relationship").Single(r => (string?)r.Attribute("identifier") == "id-r3");
            True(r3.Element(A + "name") is null, "relation citée sans nom ni propriétés");
            True(xml.Root!.Element(A + "views") is null, "aucune vue");
            True(xml.Root!.Element(A + "organizations") is null, "aucun dossier");
        });
        Test("pas encore dans Archi : un impact qualifié ici part en association, tant qu'il n'est pas revenu d'Archi", () =>
        {
            var repo = Worked();
            repo.Impacts.Add(new ProjectImpact { ProjectId = "id-prj1", ObjectId = "id-new", Nature = "Création" });
            repo.Impacts.Add(new ProjectImpact { ProjectId = "id-prj2", ObjectId = "id-draft", Nature = "Création" });
            var p = ArchiPending.Of(repo);
            Equal(1, p.Impacts.Count, "l'impact sur le brouillon attend");
            True(p.CitedObjects.Any(o => o.Id == "id-prj1"), "projet d'Archi cité");
            var ids = ArchimateExchange.Export(repo, pending: p).Root!.Element(A + "relationships")!.Elements(A + "relationship")
                .Select(r => (string?)r.Attribute("identifier")).ToList();
            True(ids.Contains(ArchiPending.ImpactId(p.Impacts[0])) && p.Impacts[0].ObjectId == "id-new", "association d'impact dans le fichier");
        });
        Test("pas encore dans Archi : fusion dans Archi puis réimport du modèle maître, la liste se vide", () =>
        {
            var repo = Worked();
            var b = ArchimateExchange.Export(repo, pending: ArchiPending.Of(repo));
            var merged = Merge(Fixtures.Model(), b);
            var report = ArchimateExchange.Import(Fixtures.Stream(merged), repo);
            var after = ArchiPending.Of(repo);
            Equal(0, after.Objects.Count + after.RelationCount, "plus rien à reporter");
            Equal(1, after.ExcludedDrafts, "le brouillon attend toujours");
            Equal(2, after.ExcludedRelations, "ses relations aussi");
            Equal("archi", repo.Objects["id-new"].Source, "objet revenu : relation d'Archi (décision 014)");
            Equal("archi", repo.Relationships["id-n2"].Source, "relation revenue : relation d'Archi (décision 015)");
            Equal("Active", repo.Objects["id-itsm"].Get("lifecycle.state"), "objet cité : valeur d'Archi intacte");
            Equal(0, report.Anomalies.Count(a => a.Code != "partage-sans-sous-type"), "aucune anomalie");
        });
        Test("pas encore dans Archi : ouvert au lecteur comme l'export complet, fermé au périmètre « métier »", () =>
        {
            Equal(AeTool.Api.Auth.Roles.Reader, AccessRules.MinimumRole("GET", "/api/export/a-reporter"), "état");
            Equal(AeTool.Api.Auth.Roles.Reader, AccessRules.MinimumRole("GET", "/api/export/a-reporter/fichier"), "fichier");
            True(ScopeContext.IsClosed("/api/export/a-reporter", new QueryCollection()), "état fermé au lecteur métier");
            True(ScopeContext.IsClosed("/api/export/a-reporter/fichier", new QueryCollection()), "fichier fermé au lecteur métier");
        });
    }
}
