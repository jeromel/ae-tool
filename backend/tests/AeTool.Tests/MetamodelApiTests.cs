using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using static AeTool.Tests.Harness;

namespace AeTool.Tests;

/// <summary>Routes du métamodèle, interrogées sur l'application réelle démarrée en mémoire.</summary>
public static class MetamodelApiTests
{
    private static (HttpStatusCode Status, JsonElement Body) Send(ApiTests.Server server, HttpMethod method, string path, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body);
        var response = server.Client.SendAsync(request).GetAwaiter().GetResult();
        var text = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement);
    }

    private static ApiTests.Server Loaded()
    {
        var server = new ApiTests.Server();
        server.Client.PostAsync("/api/import/archimate", new StringContent(Fixtures.Model())).GetAwaiter().GetResult().EnsureSuccessStatusCode();
        return server;
    }

    public static void Run()
    {
        Console.WriteLine("API du métamodèle");

        Test("création d'un type : clé dérivée du libellé, unicité par suffixe, rôle et couche validés", () =>
        {
            using var server = new ApiTests.Server();
            var first = Send(server, HttpMethod.Post, "/api/metamodel/object-types", new { label = "Équipement spécial", layerKey = "technique", roles = new[] { "infrastructure" } });
            Equal(HttpStatusCode.OK, first.Status, "création");
            Equal("equipement-special", first.Body.GetProperty("key").GetString(), "clé");
            Equal("equipement-special-2", Send(server, HttpMethod.Post, "/api/metamodel/object-types", new { label = "Équipement spécial", layerKey = "technique" }).Body.GetProperty("key").GetString(), "clé en double");
            Equal(HttpStatusCode.BadRequest, Send(server, HttpMethod.Post, "/api/metamodel/object-types", new { label = "X", layerKey = "inconnue" }).Status, "couche inconnue");
            Equal(HttpStatusCode.BadRequest, Send(server, HttpMethod.Post, "/api/metamodel/object-types", new { label = "X", layerKey = "technique", roles = new[] { "magique" } }).Status, "rôle inconnu");
            var usage = server.GetJson("/api/metamodel/usage");
            True(usage.GetProperty("objectTypes").ValueKind == JsonValueKind.Object, "usage");
        });
        Test("réglages : l'état des objets importés d'Archi est contrôlé, puis appliqué aux nouveaux objets seulement", () =>
        {
            using var server = new ApiTests.Server();
            var settings = JsonNode.Parse(server.GetJson("/api/metamodel").GetProperty("settings").GetRawText())!.AsObject();
            Equal("Validé", settings["archiImportStatus"]!.GetValue<string>(), "état par défaut");
            settings["archiImportStatus"] = "Approuvé";
            Equal(HttpStatusCode.BadRequest, Send(server, HttpMethod.Put, "/api/metamodel/settings", settings).Status, "état inconnu refusé");
            settings["archiImportStatus"] = "Proposé";
            Equal(HttpStatusCode.OK, Send(server, HttpMethod.Put, "/api/metamodel/settings", settings).Status, "état accepté");
            server.Client.PostAsync("/api/import/archimate", new StringContent(Fixtures.Model())).GetAwaiter().GetResult().EnsureSuccessStatusCode();
            Equal("Proposé", server.GetJson("/api/objects/id-srv").GetProperty("object").GetProperty("validationStatus").GetString(), "objet importé à relire");
        });
        Test("suppression d'un type utilisé : refusée avec l'effectif, puis fusion qui redirige objets, règles, couples et propriétés", () =>
        {
            using var server = Loaded();
            Send(server, HttpMethod.Put, "/api/metamodel/relationship-types/affectation", new { label = "Affectation", allowedPairs = new[] { "noeud>application" }, lineStyle = "plein" });
            Send(server, HttpMethod.Post, "/api/metamodel/properties", new { key = "ip", dataType = "texte", appliesTo = new[] { "noeud" } });
            var refused = Send(server, HttpMethod.Delete, "/api/metamodel/object-types/noeud");
            Equal(HttpStatusCode.Conflict, refused.Status, "suppression refusée");
            True(refused.Body.GetProperty("error").GetString()!.Contains('1'), "le message donne l'effectif");
            Equal(HttpStatusCode.BadRequest, Send(server, HttpMethod.Delete, "/api/metamodel/object-types/noeud?mergeInto=inexistant").Status, "destination inconnue");
            Equal(HttpStatusCode.NoContent, Send(server, HttpMethod.Delete, "/api/metamodel/object-types/noeud?mergeInto=equipement").Status, "fusion");
            Equal("equipement", server.GetJson("/api/objects/id-srv").GetProperty("brief").GetProperty("typeKey").GetString(), "objet fusionné");
            var metamodel = server.GetJson("/api/metamodel");
            True(!metamodel.GetProperty("objectTypes").EnumerateArray().Any(t => t.GetProperty("key").GetString() == "noeud"), "type retiré");
            True(!metamodel.GetProperty("transcoding").GetProperty("elements").EnumerateArray().Any(r => r.GetProperty("typeKey").GetString() == "noeud"), "règle retirée");
            var pairs = metamodel.GetProperty("relationshipTypes").EnumerateArray().Single(t => t.GetProperty("key").GetString() == "affectation").GetProperty("allowedPairs");
            Equal("equipement>application", pairs[0].GetString(), "couple redirigé");
            var property = metamodel.GetProperty("propertyDefinitions").EnumerateArray().Single(p => p.GetProperty("key").GetString() == "ip");
            Equal("equipement", property.GetProperty("appliesTo")[0].GetString(), "« s'applique à » redirigé");
        });
        Test("suppression d'un type inutilisé sans fusion ; le type des concepts sans règle ne peut pas être supprimé", () =>
        {
            using var server = new ApiTests.Server();
            Equal(HttpStatusCode.NoContent, Send(server, HttpMethod.Delete, "/api/metamodel/object-types/matiere").Status, "type inutilisé");
            Equal(HttpStatusCode.Conflict, Send(server, HttpMethod.Delete, "/api/metamodel/object-types/autre").Status, "type non classé");
            Equal(HttpStatusCode.NotFound, Send(server, HttpMethod.Delete, "/api/metamodel/object-types/matiere").Status, "déjà supprimé");
        });
        Test("types de relations : couples validés, suppression refusée puis fusion", () =>
        {
            using var server = Loaded();
            Equal(HttpStatusCode.BadRequest, Send(server, HttpMethod.Post, "/api/metamodel/relationship-types", new { label = "Pilote", allowedPairs = new[] { "fantome>application" } }).Status, "couple inconnu");
            Equal(HttpStatusCode.BadRequest, Send(server, HttpMethod.Post, "/api/metamodel/relationship-types", new { label = "Pilote", allowedPairs = new[] { "application" } }).Status, "couple mal formé");
            var created = Send(server, HttpMethod.Post, "/api/metamodel/relationship-types", new { label = "Pilote", reverseLabel = "Est piloté par", allowedPairs = new[] { "acteur>application" } });
            Equal(HttpStatusCode.OK, created.Status, "création");
            Equal("pilote", created.Body.GetProperty("key").GetString(), "clé");
            Equal(HttpStatusCode.Conflict, Send(server, HttpMethod.Delete, "/api/metamodel/relationship-types/acces").Status, "suppression refusée");
            Equal(HttpStatusCode.NoContent, Send(server, HttpMethod.Delete, "/api/metamodel/relationship-types/acces?mergeInto=association").Status, "fusion");
            Equal("association", server.GetJson("/api/objects/id-itsm").GetProperty("outgoing").EnumerateArray().Single(r => r.GetProperty("other").GetProperty("id").GetString() == "id-ticket").GetProperty("typeKey").GetString(), "relation fusionnée");
        });
        Test("propriétés : création validée, valeurs contrôlées à l'enregistrement d'un objet, suppression refusée si des valeurs existent", () =>
        {
            using var server = Loaded();
            Equal(HttpStatusCode.BadRequest, Send(server, HttpMethod.Post, "/api/metamodel/properties", new { key = "niveau", dataType = "liste" }).Status, "liste sans valeurs");
            Equal(HttpStatusCode.OK, Send(server, HttpMethod.Post, "/api/metamodel/properties", new { key = "niveau", dataType = "liste", allowedValues = new[] { "A", "B" }, required = true, appliesTo = new[] { "application" } }).Status, "création");
            Send(server, HttpMethod.Post, "/api/metamodel/properties", new { key = "cout", dataType = "nombre" });
            Equal(HttpStatusCode.Conflict, Send(server, HttpMethod.Post, "/api/metamodel/properties", new { key = "cout", dataType = "nombre" }).Status, "doublon");
            var bad = Send(server, HttpMethod.Put, "/api/objects/id-itsm", new { properties = new[] { new { key = "niveau", value = "Z" }, new { key = "cout", value = "cher" } } });
            Equal(HttpStatusCode.BadRequest, bad.Status, "valeurs invalides");
            Equal(2, bad.Body.GetProperty("problems").GetArrayLength(), "liste des problèmes");
            Equal(HttpStatusCode.OK, Send(server, HttpMethod.Put, "/api/objects/id-itsm", new { properties = new[] { new { key = "niveau", value = "A" }, new { key = "cout", value = "12.5" } } }).Status, "valeurs valides");
            var missing = server.GetJson("/api/objects/id-report").GetProperty("missingRequired");
            Equal("niveau", missing[0].GetProperty("key").GetString(), "propriété obligatoire absente signalée");
            Equal(HttpStatusCode.OK, Send(server, HttpMethod.Put, "/api/objects/id-report", new { owner = "sans valeur de niveau : non bloquant" }).Status, "l'obligatoire ne bloque pas");
            var detail = server.GetJson("/api/objects/id-itsm").GetProperty("properties").EnumerateArray().Single(p => p.GetProperty("key").GetString() == "niveau");
            Equal(("liste", true, 2), (detail.GetProperty("dataType").GetString()!, detail.GetProperty("required").GetBoolean(), detail.GetProperty("allowedValues").GetArrayLength()), "type, obligatoire et valeurs permises");
            Equal(HttpStatusCode.Conflict, Send(server, HttpMethod.Delete, "/api/metamodel/properties?key=niveau").Status, "suppression refusée");
            Send(server, HttpMethod.Put, "/api/objects/id-itsm", new { properties = Array.Empty<object>() });
            Equal(HttpStatusCode.NoContent, Send(server, HttpMethod.Delete, "/api/metamodel/properties?key=niveau").Status, "suppression sans valeur");
        });
        Test("matrice de transcodification : refusée si une règle vise un type inconnu, acceptée sinon ; contrôles et réapplication", () =>
        {
            using var server = Loaded();
            JsonNode Current() => JsonNode.Parse(server.GetJson("/api/metamodel").GetProperty("transcoding").GetRawText())!;
            var broken = Current();
            broken["elements"]!.AsArray().Add(new JsonObject { ["typeKey"] = "fantome", ["archimateType"] = "Node" });
            var refused = Send(server, HttpMethod.Put, "/api/metamodel/transcoding", broken);
            Equal(HttpStatusCode.BadRequest, refused.Status, "règle vers un type inconnu");
            Equal(1, refused.Body.GetProperty("errors").GetArrayLength(), "liste des erreurs");
            var valid = Current();
            foreach (var rule in valid["elements"]!.AsArray().Where(r => r!["archimateType"]!.GetValue<string>() == "ApplicationComponent")) rule!["typeKey"] = "logiciel-systeme";
            Equal(HttpStatusCode.OK, Send(server, HttpMethod.Put, "/api/metamodel/transcoding", valid).Status, "matrice valide");
            True(server.GetJson("/api/metamodel/transcoding/checks").GetProperty("rules").GetArrayLength() > 0, "contrôles");
            var dry = Send(server, HttpMethod.Post, "/api/metamodel/transcoding/reclassify");
            Equal((3, true), (dry.Body.GetProperty("changes").GetInt32(), dry.Body.GetProperty("dryRun").GetBoolean()), "à blanc par défaut");
            Equal("application", server.GetJson("/api/objects/id-itsm").GetProperty("brief").GetProperty("typeKey").GetString(), "inchangé à blanc");
            Equal(3, Send(server, HttpMethod.Post, "/api/metamodel/transcoding/reclassify?dryRun=false").Body.GetProperty("changes").GetInt32(), "appliquée");
            Equal("logiciel-systeme", server.GetJson("/api/objects/id-itsm").GetProperty("brief").GetProperty("typeKey").GetString(), "objet retypé");
        });
        Test("couches : liste ordonnée, retrait refusé si un type l'utilise ; catalogue servi", () =>
        {
            using var server = new ApiTests.Server();
            var layers = server.GetJson("/api/metamodel").GetProperty("layers").EnumerateArray().Select(l => new { key = l.GetProperty("key").GetString(), label = l.GetProperty("label").GetString() }).ToList();
            Equal(HttpStatusCode.Conflict, Send(server, HttpMethod.Put, "/api/metamodel/layers", layers.Where(l => l.key != "technique")).Status, "couche utilisée");
            Equal(HttpStatusCode.OK, Send(server, HttpMethod.Put, "/api/metamodel/layers", Enumerable.Reverse(layers)).Status, "réordonnancement");
            Equal("autre", server.GetJson("/api/metamodel").GetProperty("layers")[0].GetProperty("key").GetString(), "nouvel ordre");
            var catalog = server.GetJson("/api/metamodel/catalog");
            Equal((13, 3), (catalog.GetProperty("objectRoles").GetArrayLength(), catalog.GetProperty("relationshipRoles").GetArrayLength()), "rôles");
            True(catalog.GetProperty("archimate").GetProperty("elements").GetArrayLength() > 50, "concepts d'éléments");
            Equal(5, catalog.GetProperty("dataTypes").GetArrayLength(), "types de données");
            var template = catalog.GetProperty("templateObjectTypeKeys").EnumerateArray().Select(k => k.GetString()).ToList();
            True(template.Contains("application") && template.Contains("capacite"), "types du gabarit livré");
        });
        Test("relations : modification d'une relation avec contrôle des couples ; objet créé dans l'outil sans concept d'origine", () =>
        {
            using var server = Loaded();
            Send(server, HttpMethod.Put, "/api/metamodel/relationship-types/service", new { label = "Sert", allowedPairs = new[] { "application>application" }, lineStyle = "plein" });
            Equal(HttpStatusCode.OK, Send(server, HttpMethod.Put, "/api/relationships/id-r3", new { name = "Tickets fermés" }).Status, "renommage");
            Equal(HttpStatusCode.BadRequest, Send(server, HttpMethod.Put, "/api/relationships/id-r4", new { typeKey = "service" }).Status, "couple non autorisé");
            Equal(HttpStatusCode.OK, Send(server, HttpMethod.Put, "/api/relationships/id-r3", new { typeKey = "service" }).Status, "couple autorisé");
            Equal(HttpStatusCode.NotFound, Send(server, HttpMethod.Put, "/api/relationships/absente", new { name = "x" }).Status, "relation inconnue");
            var created = Send(server, HttpMethod.Post, "/api/objects", new { name = "Nouveau", typeKey = "application" });
            Equal("saisie", created.Body.GetProperty("source").GetString(), "source");
            True(created.Body.GetProperty("archimateType").ValueKind == JsonValueKind.Null, "pas de concept d'origine");
        });
        Test("conformité et import d'un métamodèle de version 1", () =>
        {
            using var server = Loaded();
            Send(server, HttpMethod.Post, "/api/metamodel/properties", new { key = "owner.team", dataType = "texte", required = true });
            Send(server, HttpMethod.Post, "/api/metamodel/object-types", new { label = "Zone spéciale", layerKey = "technique" });
            var special = Send(server, HttpMethod.Post, "/api/objects", new { name = "Z1", typeKey = "zone-speciale" });
            var specialId = special.Body.GetProperty("id").GetString();
            var conformity = server.GetJson("/api/quality/conformity");
            Equal(9, conformity.GetProperty("total").GetInt32(), "une propriété obligatoire absente sur chacun des 9 objets");
            Equal("propriete-obligatoire-absente", conformity.GetProperty("summary")[0].GetProperty("code").GetString(), "résumé par code");
            const string v1 = """{"Layers":[{"Key":"autre","Label":"Autre","Order":0}],"ObjectTypes":[{"Key":"ApplicationComponent","Label":"Applis","LayerKey":"autre","Visual":{},"Archived":false}],"RelationshipTypes":[],"PropertyDefinitions":[],"Settings":{}}""";
            var response = server.Client.PostAsync("/api/metamodel/import", new StringContent(v1, Encoding.UTF8, "application/octet-stream")).GetAwaiter().GetResult();
            Equal(HttpStatusCode.OK, response.StatusCode, "import");
            var types = server.GetJson("/api/metamodel").GetProperty("objectTypes").EnumerateArray().ToDictionary(t => t.GetProperty("key").GetString()!);
            Equal("Applis", types["application"].GetProperty("label").GetString(), "type migré");
            True(types["zone-speciale"].GetProperty("archived").GetBoolean(), "type utilisé mais absent du fichier : conservé, archivé");
            Equal("zone-speciale", server.GetJson("/api/objects/" + specialId).GetProperty("brief").GetProperty("typeKey").GetString(), "objet conservé");
            Equal(HttpStatusCode.BadRequest, server.Client.PostAsync("/api/metamodel/import", new StringContent("pas du json")).GetAwaiter().GetResult().StatusCode, "JSON invalide");
        });
    }
}
