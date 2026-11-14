using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AeTool.Core;
using static AeTool.Tests.Harness;

namespace AeTool.Tests;

/// <summary>Aide à la saisie : relations permises d'après les couples du métamodèle, recherche d'objets à relier, origine des relations.</summary>
public static class EditingTests
{
    public static void Run()
    {
        Console.WriteLine("Saisie : règles de relation et routes d'aide");
        RulesTests();
        ApiRoutesTests();
    }

    private static Metamodel WithPairs(params string[] pairs)
    {
        var m = Metamodel.Default();
        m.RelType("flux")!.AllowedPairs = pairs.ToList();
        return m;
    }

    private static void RulesTests()
    {
        Test("un type de relation sans couple déclaré est proposé pour tout type d'objet", () =>
        {
            var flow = RelationRules.Outgoing(Metamodel.Default(), "noeud").Single(r => r.TypeKey == "flux");
            True(flow.AnyType && flow.ObjectTypes.Count == 0, "aucun contrôle : tous les types admis");
        });

        Test("les couples déclarés limitent les types de l'autre extrémité, selon le sens", () =>
        {
            var m = WithPairs("application>application", "application>noeud", "equipement>application");
            Equal("application,noeud", string.Join(",", RelationRules.Outgoing(m, "application").Single(r => r.TypeKey == "flux").ObjectTypes), "cibles d'une application");
            Equal("application,equipement", string.Join(",", RelationRules.Incoming(m, "application").Single(r => r.TypeKey == "flux").ObjectTypes), "sources d'une application");
        });

        Test("un type de relation sans couple pour ce type d'objet n'est pas proposé", () =>
        {
            var m = WithPairs("application>application");
            True(RelationRules.Outgoing(m, "processus").All(r => r.TypeKey != "flux"), "flux absent pour un processus");
            True(RelationRules.Incoming(m, "processus").All(r => r.TypeKey != "flux"), "flux absent en entrant");
        });

        Test("les types archivés (relation ou objet) ne sont pas proposés", () =>
        {
            var m = WithPairs("application>application", "application>noeud");
            m.Type("noeud")!.Archived = true;
            m.RelType("composition")!.Archived = true;
            Equal("application", string.Join(",", RelationRules.Outgoing(m, "application").Single(r => r.TypeKey == "flux").ObjectTypes), "cible archivée écartée");
            True(RelationRules.Outgoing(m, "application").All(r => r.TypeKey != "composition"), "relation archivée écartée");
        });

        Test("un couple mal formé est ignoré sans erreur", () =>
        {
            var m = WithPairs("application", ">noeud", "application>");
            True(RelationRules.Outgoing(m, "application").All(r => r.TypeKey != "flux"), "aucune relation déduite de couples invalides");
        });

        Test("les relations proposées sont cohérentes avec le contrôle du serveur (CheckRelationship)", () =>
        {
            var m = WithPairs("application>application", "application>noeud");
            foreach (var target in m.ObjectTypes.Select(t => t.Key))
            {
                var offered = RelationRules.Outgoing(m, "application").Single(r => r.TypeKey == "flux").ObjectTypes.Contains(target);
                Equal(offered, m.CheckRelationship("flux", "application", target) is null, $"application vers {target}");
            }
        });
    }

    private static JsonElement Send(ApiTests.Server server, HttpMethod method, string path, object? body, out HttpStatusCode status)
    {
        var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body);
        var response = server.Client.SendAsync(request).GetAwaiter().GetResult();
        status = response.StatusCode;
        var text = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        return text.Length == 0 ? default : JsonDocument.Parse(text).RootElement;
    }

    private static ApiTests.Server Loaded()
    {
        var server = new ApiTests.Server();
        server.Client.PostAsync("/api/import/archimate", new StringContent(Fixtures.Model())).GetAwaiter().GetResult().EnsureSuccessStatusCode();
        return server;
    }

    private static List<string> Names(JsonElement result) => result.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("name").GetString()!).ToList();

    private static void ApiRoutesTests()
    {
        Test("recherche d'objets : sans accents ni casse, le début du nom passe avant, la limite s'applique", () =>
        {
            using var server = Loaded();
            Send(server, HttpMethod.Post, "/api/objects", new { name = "Équipe de la Logistique", typeKey = "acteur" }, out _);
            var found = server.GetJson("/api/objects/search?q=EQUIPE");
            Equal("Équipe de la Logistique", Names(found).Single(), "accents et casse ignorés");
            var apps = server.GetJson("/api/objects/search?q=e&types=application");
            True(Names(apps).Count >= 2 && Names(apps).All(n => n.Contains('e', StringComparison.OrdinalIgnoreCase)), "plusieurs applications contiennent « e »");
            Equal(1, server.GetJson("/api/objects/search?q=e&types=application&limit=1").GetProperty("items").GetArrayLength(), "limite");
            Equal(apps.GetProperty("total").GetInt32(), server.GetJson("/api/objects/search?q=e&types=application&limit=1").GetProperty("total").GetInt32(), "le total ignore la limite");
        });

        Test("recherche d'objets : le filtre de types écarte les autres types, les objets archivés sont absents", () =>
        {
            using var server = Loaded();
            Equal("SRV-APP-01", Names(server.GetJson("/api/objects/search?q=srv&types=application,noeud")).Single(), "nœud trouvé");
            Equal(0, server.GetJson("/api/objects/search?q=srv&types=application").GetProperty("items").GetArrayLength(), "type non demandé");
            Send(server, HttpMethod.Delete, "/api/objects/id-srv", null, out _);
            Equal(0, server.GetJson("/api/objects/search?q=srv").GetProperty("items").GetArrayLength(), "objet archivé");
        });

        Test("recherche sans texte : liste les premiers objets du type, la route ne masque pas /objects/{id}", () =>
        {
            using var server = Loaded();
            True(Names(server.GetJson("/api/objects/search?types=donnee,objet-donnees")).Contains("Ticket de support"), "objet de données listé");
            Equal("ITSM-SUITE", server.GetJson("/api/objects/id-itsm").GetProperty("brief").GetProperty("name").GetString(), "fiche d'objet toujours servie");
        });

        Test("relations permises d'un objet : couples du métamodèle, 404 si l'objet est inconnu", () =>
        {
            using var server = Loaded();
            Send(server, HttpMethod.Put, "/api/metamodel/relationship-types/flux", new { label = "Flux", reverseLabel = "Reçoit un flux de", allowedPairs = new[] { "application>application" } }, out var put);
            Equal(HttpStatusCode.OK, put, "déclaration du couple");
            var allowed = server.GetJson("/api/objects/id-itsm/allowed-relations");
            var out1 = allowed.GetProperty("outgoing").EnumerateArray().Single(r => r.GetProperty("typeKey").GetString() == "flux");
            Equal(false, out1.GetProperty("anyType").GetBoolean(), "couples déclarés");
            Equal("application", out1.GetProperty("objectTypes").EnumerateArray().Single().GetString(), "cible admise");
            Equal("Reçoit un flux de", out1.GetProperty("reverseLabel").GetString(), "lecture inverse");
            True(allowed.GetProperty("outgoing").EnumerateArray().Any(r => r.GetProperty("typeKey").GetString() == "service" && r.GetProperty("anyType").GetBoolean()), "relation sans couple : tout admis");
            Send(server, HttpMethod.Get, "/api/objects/id-inconnu/allowed-relations", null, out var missing);
            Equal(HttpStatusCode.NotFound, missing, "objet inconnu");
        });

        Test("origine des relations d'un objet : importée d'Archi ou saisie dans l'outil, archivées exclues", () =>
        {
            using var server = Loaded();
            var created = Send(server, HttpMethod.Post, "/api/relationships", new { typeKey = "flux", sourceId = "id-itsm", targetId = "id-legacy", name = "Test" }, out var status);
            Equal(HttpStatusCode.OK, status, "création");
            var id = created.GetProperty("id").GetString()!;
            var sources = server.GetJson("/api/objects/id-itsm/relationship-sources");
            Equal("saisie", sources.GetProperty(id).GetString(), "relation saisie");
            Equal("archi", sources.GetProperty("id-r1").GetString(), "relation importée");
            Send(server, HttpMethod.Delete, $"/api/relationships/{id}", null, out _);
            True(!server.GetJson("/api/objects/id-itsm/relationship-sources").TryGetProperty(id, out _), "relation archivée absente");
        });

        Test("une relation hors couples est refusée par le serveur avec un message lisible", () =>
        {
            using var server = Loaded();
            Send(server, HttpMethod.Put, "/api/metamodel/relationship-types/flux", new { label = "Flux", allowedPairs = new[] { "application>application" } }, out _);
            var refusal = Send(server, HttpMethod.Post, "/api/relationships", new { typeKey = "flux", sourceId = "id-itsm", targetId = "id-srv" }, out var status);
            Equal(HttpStatusCode.BadRequest, status, "refus");
            True(refusal.GetProperty("error").GetString()!.Contains("non autorisée"), "message du serveur");
        });

        Test("un seul maître : nom, description et propriétés venus d'Archi refusés par le serveur, le reste accepté, rien d'effacé par omission", () =>
        {
            using var server = Loaded();
            var before = server.GetJson("/api/objects/id-itsm").GetProperty("object");
            var archiProperty = before.GetProperty("properties")[0];
            var (key, value) = (archiProperty.GetProperty("key").GetString()!, archiProperty.GetProperty("value").GetString()!);

            var renamed = Send(server, HttpMethod.Put, "/api/objects/id-itsm", new { name = "Renommé dans l'outil" }, out var status);
            Equal(HttpStatusCode.BadRequest, status, "nom");
            True(renamed.GetProperty("error").GetString()!.Contains("Archi"), "le message dit où modifier");
            Send(server, HttpMethod.Put, "/api/objects/id-itsm", new { documentation = "Réécrite dans l'outil" }, out status);
            Equal(HttpStatusCode.BadRequest, status, "description");
            Send(server, HttpMethod.Put, "/api/objects/id-itsm", new { properties = new[] { new { key, value = value + " modifiée" } } }, out status);
            Equal(HttpStatusCode.BadRequest, status, "propriété venue d'Archi");
            Equal(before.GetProperty("name").GetString(), server.GetJson("/api/objects/id-itsm").GetProperty("object").GetProperty("name").GetString(), "rien n'a changé");

            Send(server, HttpMethod.Put, "/api/objects/id-itsm", new { owner = "DSI", typeKey = "logiciel-systeme" }, out status);
            Equal(HttpStatusCode.OK, status, "responsable et type se règlent dans l'outil");
            Send(server, HttpMethod.Post, "/api/metamodel/properties", new { key = "cout", label = "Coût", dataType = "nombre" }, out _);
            Send(server, HttpMethod.Put, "/api/objects/id-itsm", new { properties = new[] { new { key = "cout", value = "12" } } }, out status);
            Equal(HttpStatusCode.OK, status, "propriété saisie ajoutée sans renvoyer celles d'Archi");
            var after = server.GetJson("/api/objects/id-itsm").GetProperty("object").GetProperty("properties").EnumerateArray()
                .ToDictionary(p => p.GetProperty("key").GetString()!, p => p.GetProperty("value").GetString());
            Equal(value, after[key], "la propriété venue d'Archi est conservée");
            Equal("12", after["cout"], "la propriété saisie est enregistrée");
        });

        Test("objet créé dans l'outil : propriétés enregistrées, valeur invalide refusée avec la liste des problèmes", () =>
        {
            using var server = new ApiTests.Server();
            Send(server, HttpMethod.Post, "/api/metamodel/properties", new { key = "criticite", label = "Criticité", dataType = "liste", allowedValues = new[] { "Haute", "Basse" } }, out var def);
            Equal(HttpStatusCode.OK, def, "définition");
            var bad = Send(server, HttpMethod.Post, "/api/objects", new { name = "Portail", typeKey = "application", properties = new[] { new { key = "criticite", value = "Moyenne" } } }, out var status);
            Equal(HttpStatusCode.BadRequest, status, "valeur hors liste");
            Equal(1, bad.GetProperty("problems").GetArrayLength(), "un problème listé");
            var ok = Send(server, HttpMethod.Post, "/api/objects", new { name = "Portail", typeKey = "application", properties = new[] { new { key = "criticite", value = "Haute" } } }, out status);
            Equal(HttpStatusCode.OK, status, "valeur permise");
            Equal("saisie", ok.GetProperty("source").GetString(), "source");
        });
    }
}
