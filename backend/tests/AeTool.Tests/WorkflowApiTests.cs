using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AeTool.Api.Auth;
using static AeTool.Tests.Harness;

namespace AeTool.Tests;

/// <summary>
/// Circuit de validation, historique, journal et administration du stockage, par l'API démarrée en mémoire
/// en mode local, avec de vrais comptes de chaque rôle.
/// </summary>
public static class WorkflowApiTests
{
    private const string Password = "Utilisateur-2024!";

    private sealed class Client
    {
        private readonly HttpClient _http;
        public Client(ApiTests.Server server) => _http = server.NewClient();

        public HttpResponseMessage Get(string path) => _http.GetAsync(path).GetAwaiter().GetResult();

        public HttpResponseMessage Send(HttpMethod method, string path, object? body = null)
        {
            var token = Json(Get("/api/auth/session")).GetProperty("csrfToken").GetString();
            var request = new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body) };
            if (token is not null) request.Headers.Add("X-CSRF-TOKEN", token);
            return _http.SendAsync(request).GetAwaiter().GetResult();
        }

        public HttpResponseMessage SendRaw(HttpRequestMessage request) => _http.SendAsync(request).GetAwaiter().GetResult();

        public HttpResponseMessage Post(string path, object? body = null) => Send(HttpMethod.Post, path, body);
        public HttpResponseMessage Put(string path, object? body) => Send(HttpMethod.Put, path, body);
        public JsonElement GetJson(string path) { var r = Get(path); Equal(HttpStatusCode.OK, r.StatusCode, "GET " + path); return Json(r); }

        public static JsonElement Json(HttpResponseMessage r) => JsonDocument.Parse(r.Content.ReadAsStringAsync().GetAwaiter().GetResult()).RootElement.Clone();
    }

    /// <summary>Serveur en mode local avec un administrateur et un compte par rôle.</summary>
    private sealed class Rig : IDisposable
    {
        public ApiTests.Server Server { get; } = new("--Auth:Mode=local");
        public Client Admin { get; }
        public Client Reader { get; }
        public Client Contributor { get; }
        public Client Validator { get; }

        public Rig()
        {
            var code = File.ReadAllText(Path.Combine(Server.DataDirectory, "code-installation.txt")).Trim();
            Admin = new Client(Server);
            Equal(HttpStatusCode.OK, Admin.Post("/api/auth/setup", new { code, login = "admin", name = "Administrateur", password = "Administrateur-2024" }).StatusCode, "premier lancement");
            Equal(HttpStatusCode.OK, Admin.Post("/api/auth/login", new { login = "admin", password = "Administrateur-2024" }).StatusCode, "connexion");
            Reader = Account("lise", Roles.Reader);
            Contributor = Account("paul", Roles.Contributor);
            Validator = Account("vera", Roles.Validator);
        }

        private Client Account(string login, string role)
        {
            var created = Admin.Post("/api/users", new { login, name = login, role });
            Equal(HttpStatusCode.Created, created.StatusCode, "compte " + login);
            var temporary = Client.Json(created).GetProperty("temporaryPassword").GetString()!;
            var client = new Client(Server);
            Equal(HttpStatusCode.OK, client.Post("/api/auth/login", new { login, password = temporary }).StatusCode, "connexion " + login);
            Equal(HttpStatusCode.NoContent, client.Post("/api/auth/password", new { current = temporary, next = Password }).StatusCode, "mot de passe " + login);
            return client;
        }

        public string TypeKey() => Admin.GetJson("/api/metamodel").GetProperty("objectTypes")[0].GetProperty("key").GetString()!;

        /// <summary>Objet créé par le contributeur (donc en brouillon).</summary>
        public string NewObject(string name = "Objet de test")
        {
            var response = Contributor.Post("/api/objects", new { name, typeKey = TypeKey() });
            Equal(HttpStatusCode.OK, response.StatusCode, "création");
            return Client.Json(response).GetProperty("id").GetString()!;
        }

        public string Status(string id) => Admin.GetJson("/api/objects/" + id).GetProperty("object").GetProperty("validationStatus").GetString()!;

        public void Dispose() => Server.Dispose();
    }

    private static HttpStatusCode Code(HttpResponseMessage r) => r.StatusCode;

    public static void Run()
    {
        Console.WriteLine("Validation, historique, journal et administration (API, comptes de chaque rôle)");

        Test("un objet créé dans l'outil est un brouillon ; un objet importé d'Archi est validé", () =>
        {
            using var rig = new Rig();
            Equal("Brouillon", rig.Status(rig.NewObject()), "objet saisi");
            Equal(HttpStatusCode.OK, ImportAsAdmin(rig).StatusCode, "import Archi");
            Equal("Validé", rig.Status("id-itsm"), "objet importé");
        });

        Test("circuit : proposer (contributeur), refuser sans commentaire, refuser avec commentaire, reproposer, valider (valideur)", () =>
        {
            using var rig = new Rig();
            var id = rig.NewObject();
            Equal(HttpStatusCode.Forbidden, Code(rig.Reader.Post($"/api/objects/{id}/propose")), "un lecteur ne propose pas");
            Equal(HttpStatusCode.OK, Code(rig.Contributor.Post($"/api/objects/{id}/propose")), "proposer");
            Equal("Proposé", rig.Status(id), "état proposé");
            Equal(HttpStatusCode.Conflict, Code(rig.Contributor.Post($"/api/objects/{id}/propose")), "proposer deux fois");
            Equal(HttpStatusCode.Forbidden, Code(rig.Contributor.Post($"/api/validation/objects/{id}/validate")), "un contributeur ne valide pas");
            Equal(HttpStatusCode.Forbidden, Code(rig.Contributor.Post($"/api/validation/objects/{id}/reject", new { comment = "non" })), "un contributeur ne refuse pas");
            var noComment = rig.Validator.Post($"/api/validation/objects/{id}/reject", new { comment = "  " });
            Equal(HttpStatusCode.Conflict, noComment.StatusCode, "refus sans commentaire");
            True(Client.Json(noComment).GetProperty("error").GetString()!.Contains("obligatoire"), "message");
            Equal("Proposé", rig.Status(id), "état inchangé");
            Equal(HttpStatusCode.OK, Code(rig.Validator.Post($"/api/validation/objects/{id}/reject", new { comment = "Ajoutez un responsable." })), "refus");
            var o = rig.Admin.GetJson("/api/objects/" + id).GetProperty("object");
            Equal(("Brouillon", "Ajoutez un responsable."), (o.GetProperty("validationStatus").GetString()!, o.GetProperty("validationComment").GetString()!), "brouillon avec le commentaire");
            Equal(HttpStatusCode.Conflict, Code(rig.Validator.Post($"/api/validation/objects/{id}/reject", new { comment = "encore" })), "refuser un brouillon");
            rig.Contributor.Post($"/api/objects/{id}/propose");
            Equal(HttpStatusCode.OK, Code(rig.Validator.Post($"/api/validation/objects/{id}/validate")), "valider");
            o = rig.Admin.GetJson("/api/objects/" + id).GetProperty("object");
            Equal(("Validé", "vera"), (o.GetProperty("validationStatus").GetString()!, o.GetProperty("validatedBy").GetString()!), "validé par vera");
            Equal(JsonValueKind.Null, o.GetProperty("validationComment").ValueKind, "commentaire effacé");
            Equal(HttpStatusCode.Conflict, Code(rig.Validator.Post($"/api/validation/objects/{id}/validate")), "valider deux fois");
            Equal(HttpStatusCode.NotFound, Code(rig.Validator.Post("/api/validation/objects/id-inconnu/validate")), "objet inconnu");
        });

        Test("un valideur peut valider directement un brouillon ; validation par lot : les objets déjà validés sont signalés sans bloquer les autres", () =>
        {
            using var rig = new Rig();
            var a = rig.NewObject("A");
            var b = rig.NewObject("B");
            var c = rig.NewObject("C");
            Equal(HttpStatusCode.OK, Code(rig.Validator.Post($"/api/validation/objects/{c}/validate")), "brouillon → validé");
            Equal(HttpStatusCode.Forbidden, Code(rig.Contributor.Post("/api/validation/validate", new { ids = new[] { a } })), "lot : contributeur refusé");
            Equal(HttpStatusCode.BadRequest, Code(rig.Validator.Post("/api/validation/validate", new { ids = Array.Empty<string>() })), "lot vide");
            var result = Client.Json(rig.Validator.Post("/api/validation/validate", new { ids = new[] { a, b, c, "id-inconnu" } }));
            Equal(2, result.GetProperty("validated").GetInt32(), "validés");
            Equal(2, result.GetProperty("refused").GetArrayLength(), "refusés (déjà validé, inconnu)");
            Equal("Validé", rig.Status(a), "a");
            Equal("Validé", rig.Status(b), "b");
        });

        Test("modification d'un objet validé : un contributeur le renvoie à « proposé », le visuel non, un valideur non", () =>
        {
            using var rig = new Rig();
            var id = rig.NewObject();
            rig.Validator.Post($"/api/validation/objects/{id}/validate");
            Equal(HttpStatusCode.OK, Code(rig.Contributor.Put($"/api/objects/{id}", new { visualOverride = new { fill = "#ff0000" } })), "visuel");
            Equal("Validé", rig.Status(id), "le visuel ne change pas l'état");
            Equal(HttpStatusCode.OK, Code(rig.Validator.Put($"/api/objects/{id}", new { name = "Renommé par le valideur" })), "valideur");
            Equal("Validé", rig.Status(id), "un valideur ne change pas l'état");
            Equal(HttpStatusCode.OK, Code(rig.Contributor.Put($"/api/objects/{id}", new { name = "Renommé par Paul" })), "contributeur");
            var o = rig.Admin.GetJson("/api/objects/" + id).GetProperty("object");
            Equal(("Proposé", "paul"), (o.GetProperty("validationStatus").GetString()!, o.GetProperty("proposedBy").GetString()!), "renvoyé à la validation");
            Equal(1, rig.Admin.GetJson("/api/validation/pending").GetProperty("total").GetInt32(), "dans la file");
        });

        Test("le statut ne peut plus être forcé par PUT /api/objects/{id}", () =>
        {
            using var rig = new Rig();
            var id = rig.NewObject();
            Equal(HttpStatusCode.OK, Code(rig.Contributor.Put($"/api/objects/{id}", new { validationStatus = "Validé", documentation = "texte" })), "PUT");
            Equal("Brouillon", rig.Status(id), "statut inchangé");
            Equal(HttpStatusCode.OK, Code(rig.Validator.Put($"/api/objects/{id}", new { validationStatus = "Validé" })), "PUT par un valideur");
            Equal("Brouillon", rig.Status(id), "même pour un valideur");
        });

        Test("historique : création puis modification donnent avant/après avec libellés ; rien n'est journalisé quand rien ne change", () =>
        {
            using var rig = new Rig();
            var id = rig.NewObject("Portail");
            rig.Contributor.Put($"/api/objects/{id}", new { name = "Portail agents", owner = "Dupont" });
            var count = rig.Admin.GetJson($"/api/objects/{id}/history").GetArrayLength();
            Equal(HttpStatusCode.OK, Code(rig.Contributor.Put($"/api/objects/{id}", new { name = "Portail agents", owner = "Dupont" })), "PUT identique");
            Equal(count, rig.Admin.GetJson($"/api/objects/{id}/history").GetArrayLength(), "aucune entrée de plus");
            var history = rig.Admin.GetJson($"/api/objects/{id}/history");
            var modified = history[0];
            Equal(("Objet modifié", "paul"), (modified.GetProperty("action").GetString()!, modified.GetProperty("user").GetString()!), "la plus récente d'abord");
            var name = modified.GetProperty("changes").EnumerateArray().Single(c => c.GetProperty("field").GetString() == "Nom");
            Equal(("Portail", "Portail agents"), (name.GetProperty("before").GetString()!, name.GetProperty("after").GetString()!), "avant/après du nom");
            var created = history[1];
            Equal("Objet créé", created.GetProperty("action").GetString(), "création");
            True(created.GetProperty("changes").EnumerateArray().Any(c => c.GetProperty("field").GetString() == "Type"), "type lisible à la création");
            Equal(HttpStatusCode.NotFound, Code(rig.Admin.Get("/api/objects/id-inconnu/history")), "objet inconnu");
            True(!rig.Admin.Get($"/api/objects/{id}/history").Content.ReadAsStringAsync().GetAwaiter().GetResult().Contains("avant :"), "plus de JSON brut « avant : »");
        });

        Test("historique : transitions de validation, relation, archivage et notation sont journalisés avec leur avant/après", () =>
        {
            using var rig = new Rig();
            var a = rig.NewObject("A");
            var b = rig.NewObject("B");
            rig.Contributor.Post($"/api/objects/{a}/propose");
            rig.Validator.Post($"/api/validation/objects/{a}/reject", new { comment = "Incomplet" });
            var history = rig.Admin.GetJson($"/api/objects/{a}/history").EnumerateArray().ToList();
            var refusal = history.First(e => e.GetProperty("action").GetString() == "Objet refusé");
            True(refusal.GetProperty("changes").EnumerateArray().Any(c => c.GetProperty("field").GetString() == "Commentaire du refus" && c.GetProperty("after").GetString() == "Incomplet"), "commentaire du refus");
            True(refusal.GetProperty("changes").EnumerateArray().Any(c => c.GetProperty("field").GetString() == "État de validation" && c.GetProperty("before").GetString() == "Proposé"), "transition");
            var relType = rig.Admin.GetJson("/api/metamodel").GetProperty("relationshipTypes").EnumerateArray().First(t => t.GetProperty("allowedPairs").GetArrayLength() == 0).GetProperty("key").GetString();
            var rel = rig.Contributor.Post("/api/relationships", new { typeKey = relType, sourceId = a, targetId = b, name = "lien" });
            Equal(HttpStatusCode.OK, rel.StatusCode, "relation");
            var relId = Client.Json(rel).GetProperty("id").GetString()!;
            Equal(HttpStatusCode.NoContent, Code(rig.Contributor.Send(HttpMethod.Delete, $"/api/relationships/{relId}")), "archivage de la relation");
            Equal(HttpStatusCode.NoContent, Code(rig.Contributor.Send(HttpMethod.Delete, $"/api/objects/{b}")), "archivage de l'objet");
            var journal = rig.Admin.GetJson("/api/journal?limit=100").GetProperty("items").EnumerateArray().ToList();
            Equal("Relation archivée", journal.First(e => e.GetProperty("targetId").GetString() == relId).GetProperty("action").GetString(), "relation archivée");
            var archived = journal.First(e => e.GetProperty("targetId").GetString() == b && e.GetProperty("action").GetString() == "Objet archivé");
            Equal(("Actif", "Archivé"), (archived.GetProperty("changes")[0].GetProperty("before").GetString()!, archived.GetProperty("changes")[0].GetProperty("after").GetString()!), "archivage");
        });

        Test("journal : réservé aux valideurs, filtres par auteur, nature, texte et période, pagination", () =>
        {
            using var rig = new Rig();
            var id = rig.NewObject("Portail Hélène");
            rig.Contributor.Put($"/api/objects/{id}", new { owner = "Dupont" });
            rig.Validator.Put($"/api/objects/{id}", new { documentation = "Validée" });
            Equal(HttpStatusCode.Forbidden, Code(rig.Reader.Get("/api/journal")), "lecteur");
            Equal(HttpStatusCode.Forbidden, Code(rig.Contributor.Get("/api/journal")), "contributeur");
            Equal(HttpStatusCode.OK, Code(rig.Validator.Get("/api/journal")), "valideur");
            var all = rig.Validator.GetJson("/api/journal?limit=500");
            True(all.GetProperty("total").GetInt32() >= 3, "total");
            True(all.GetProperty("users").EnumerateArray().Any(u => u.GetString() == "paul"), "liste des auteurs");
            Equal(2, rig.Validator.GetJson("/api/journal?user=paul&kind=objet").GetProperty("total").GetInt32(), "auteur et nature (création et modification)");
            Equal(1, rig.Validator.GetJson("/api/journal?user=vera&kind=objet").GetProperty("total").GetInt32(), "auteur vera");
            Equal(1, rig.Validator.GetJson("/api/journal?q=dupont&kind=objet").GetProperty("total").GetInt32(), "texte dans une valeur modifiée");
            Equal(1, rig.Validator.GetJson("/api/journal?q=helene&kind=objet&user=paul&limit=1&offset=1").GetProperty("items").GetArrayLength(), "texte sans accents, page");
            var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
            True(rig.Validator.GetJson($"/api/journal?from={today}&to={today}").GetProperty("total").GetInt32() >= 3, "période du jour");
            Equal(0, rig.Validator.GetJson("/api/journal?to=2000-01-01").GetProperty("total").GetInt32(), "période ancienne");
            var page = rig.Validator.GetJson("/api/journal?limit=2&offset=0");
            Equal(2, page.GetProperty("items").GetArrayLength(), "taille de page");
            Equal(HttpStatusCode.OK, Code(rig.Validator.Get("/api/journal?limit=100000&offset=-5")), "bornes absurdes tolérées");
        });

        Test("file des objets à valider, filtre par état et compteur de la synthèse", () =>
        {
            using var rig = new Rig();
            var a = rig.NewObject("A");
            var b = rig.NewObject("B");
            rig.Contributor.Post($"/api/objects/{a}/propose");
            Equal(1, rig.Reader.GetJson("/api/summary").GetProperty("toValidate").GetInt32(), "compteur");
            var pending = rig.Reader.GetJson("/api/validation/pending");
            Equal(1, pending.GetProperty("total").GetInt32(), "file");
            var item = pending.GetProperty("items")[0];
            Equal(("A", "paul"), (item.GetProperty("brief").GetProperty("name").GetString()!, item.GetProperty("proposedBy").GetString()!), "objet et auteur de la proposition");
            Equal(1, rig.Reader.GetJson("/api/objects?status=Proposé").GetProperty("total").GetInt32(), "filtre Proposé");
            Equal(1, rig.Reader.GetJson("/api/objects?status=Brouillon").GetProperty("total").GetInt32(), "filtre Brouillon");
            Equal("Brouillon", rig.Status(b), "autre objet");
        });

        Test("administration : réservée aux administrateurs ; état du stockage complet", () =>
        {
            using var rig = new Rig();
            foreach (var client in new[] { rig.Reader, rig.Contributor, rig.Validator })
            {
                Equal(HttpStatusCode.Forbidden, Code(client.Get("/api/admin/status")), "état");
                Equal(HttpStatusCode.Forbidden, Code(client.Get("/api/admin/backups")), "liste");
                Equal(HttpStatusCode.Forbidden, Code(client.Post("/api/admin/backups")), "création");
            }
            rig.NewObject();
            var status = rig.Admin.GetJson("/api/admin/status");
            Equal(Repository_CurrentVersion, status.GetProperty("schemaVersion").GetInt32(), "version de schéma");
            Equal("local", status.GetProperty("authMode").GetString(), "mode d'authentification");
            Equal(1, status.GetProperty("backupCount").GetInt32(), "sauvegarde automatique du jour");
            True(status.GetProperty("lastBackup").GetProperty("name").GetString()!.Contains("-auto"), "dernière sauvegarde");
            True(status.GetProperty("journalEntries").GetInt32() > 0, "entrées du journal");
            var files = status.GetProperty("files").EnumerateArray().Select(f => f.GetProperty("path").GetString()!).ToList();
            True(files.Contains("referentiel.json") && files.Contains("journal.jsonl") && files.Any(f => f.StartsWith("sauvegardes/")), "fichiers du dossier : " + string.Join(", ", files));
            True(files.Any(f => f.StartsWith("cles/")) || files.Contains("utilisateurs.json"), "comptes et clés listés pour que l'exploitant les sauvegarde");
            True(status.GetProperty("files").EnumerateArray().All(f => f.GetProperty("size").GetInt64() >= 0), "tailles");
        });

        Test("sauvegardes : création, liste, téléchargement gzip, restauration qui rend l'état d'origine, noms malveillants refusés", () =>
        {
            using var rig = new Rig();
            var id = rig.NewObject("Version 1");
            var created = rig.Admin.Post("/api/admin/backups");
            Equal(HttpStatusCode.OK, created.StatusCode, "création");
            var name = Client.Json(created).GetProperty("name").GetString()!;
            True(name.EndsWith("-manuelle.json.gz"), "motif manuel : " + name);
            rig.Contributor.Put($"/api/objects/{id}", new { name = "Version 2" });
            rig.NewObject("Autre");
            Equal(true, rig.Admin.GetJson("/api/admin/backups").EnumerateArray().Any(b => b.GetProperty("name").GetString() == name), "dans la liste");
            var download = rig.Admin.Get("/api/admin/backups/" + name);
            Equal(HttpStatusCode.OK, download.StatusCode, "téléchargement");
            var bytes = download.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            Equal((byte)0x1f, bytes[0], "signature gzip, octet 1");
            Equal((byte)0x8b, bytes[1], "signature gzip, octet 2");
            foreach (var bad in new[] { "..%2F..%2Freferentiel.json", "..%5Creferentiel.json", "referentiel.json", "inconnue.json.gz", "referentiel-20250101-000000-auto.json.gz" })
            {
                Equal(HttpStatusCode.NotFound, Code(rig.Admin.Get("/api/admin/backups/" + bad)), "téléchargement de " + bad);
                Equal(HttpStatusCode.NotFound, Code(rig.Admin.Post($"/api/admin/backups/{bad}/restore")), "restauration de " + bad);
            }
            Equal(HttpStatusCode.OK, Code(rig.Admin.Post($"/api/admin/backups/{name}/restore")), "restauration");
            Equal("Version 1", rig.Admin.GetJson("/api/objects/" + id).GetProperty("brief").GetProperty("name").GetString(), "état d'origine rendu");
            Equal(1, rig.Admin.GetJson("/api/objects").GetProperty("total").GetInt32(), "l'objet créé après la sauvegarde a disparu");
            var names = rig.Admin.GetJson("/api/admin/backups").EnumerateArray().Select(b => b.GetProperty("name").GetString()!).ToList();
            True(names.Any(n => n.Contains("-avant-restauration")), "sauvegarde de précaution : " + string.Join(", ", names));
            var journal = rig.Validator.GetJson("/api/journal?limit=500").GetProperty("items").EnumerateArray().Select(e => e.GetProperty("action").GetString()).ToList();
            True(journal.Contains("Sauvegarde restaurée") && journal.Contains("Objet modifié"), "journal complet après restauration");
        });

        Test("opération risquée par l'API : l'import Archi est précédé d'une sauvegarde", () =>
        {
            using var rig = new Rig();
            Equal(HttpStatusCode.OK, ImportAsAdmin(rig).StatusCode, "import Archi");
            var names = rig.Admin.GetJson("/api/admin/backups").EnumerateArray().Select(b => b.GetProperty("name").GetString()!).ToList();
            True(names.Any(n => n.EndsWith("-import-archi.json.gz")), "sauvegarde avant import Archi : " + string.Join(", ", names));
        });

        Test("événements de comptes : une ligne ajoutée au journal, le fichier du référentiel n'est pas réécrit", () =>
        {
            using var rig = new Rig();
            rig.NewObject();
            var referentiel = Path.Combine(rig.Server.DataDirectory, "referentiel.json");
            var journal = Path.Combine(rig.Server.DataDirectory, "journal.jsonl");
            var (bytesBefore, linesBefore) = (File.ReadAllBytes(referentiel), File.ReadAllLines(journal).Length);
            var again = new Client(rig.Server);
            Equal(HttpStatusCode.OK, again.Post("/api/auth/login", new { login = "admin", password = "Administrateur-2024" }).StatusCode, "connexion");
            True(File.ReadAllLines(journal).Length == linesBefore + 1, "une ligne de plus");
            True(File.ReadAllBytes(referentiel).SequenceEqual(bytesBefore), "référentiel intact");
            True(File.ReadAllText(journal).Contains("\"TargetKind\":\"compte\""), "nature « compte »");
        });
    }

    private static int Repository_CurrentVersion => AeTool.Core.Repository.CurrentSchemaVersion;

    private static HttpResponseMessage ImportAsAdmin(Rig rig)
    {
        var token = rig.Admin.GetJson("/api/auth/session").GetProperty("csrfToken").GetString();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/import/archimate") { Content = new StringContent(Fixtures.Model()) };
        request.Headers.Add("X-CSRF-TOKEN", token);
        return rig.Admin.SendRaw(request);
    }
}
