using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using static AeTool.Tests.Harness;

namespace AeTool.Tests;

/// <summary>Intégration des routes /api/sources : l'application réelle est démarrée en mémoire.</summary>
public static class SourcesApiTests
{
    private const string Secret = "S3cr3t-EV-à-ne-jamais-voir";
    private const string Csv = "id;nom;categorie;serie\n1;SRV-APP-01;Serveur;S-001\n2;ITSM-SUITE;Application;S-002\n3;NOUVEAU-SRV;Serveur;S-003\n"
        + "4;INCONNU;Imprimante;S-004\n1;SRV-APP-01 en double;Serveur;S-009\n";

    private static (HttpStatusCode Status, string Body) Send(ApiTests.Server server, HttpMethod method, string path, string? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = new StringContent(body, Encoding.UTF8, path.Contains("/preview") || path.Contains("/run") ? "text/plain" : "application/json");
        var response = server.Client.SendAsync(request).GetAwaiter().GetResult();
        return (response.StatusCode, response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
    }

    private static JsonElement Ok(ApiTests.Server server, HttpMethod method, string path, string? body = null)
    {
        var (status, text) = Send(server, method, path, body);
        Equal(HttpStatusCode.OK, status, $"{method} {path} : {text}");
        return JsonDocument.Parse(text).RootElement;
    }

    private static ApiTests.Server ServerWithModel()
    {
        var server = new ApiTests.Server();
        var response = server.Client.PostAsync("/api/import/archimate", new StringContent(Fixtures.Model())).GetAwaiter().GetResult();
        Equal(HttpStatusCode.OK, response.StatusCode, "import du modèle");
        return server;
    }

    private static string TypeOf(ApiTests.Server server, string objectId) =>
        server.GetJson($"/api/objects/{objectId}").GetProperty("brief").GetProperty("typeKey").GetString()!;

    private static string FileProfile(string nodeType, string appType) => JsonSerializer.Serialize(new
    {
        label = "CMDB d'essai", kind = "fichier", sourceIsMaster = false,
        mapping = new
        {
            idField = "id", nameField = "nom", typeField = "categorie",
            types = new[] { new { sourceValue = "Serveur", typeKey = nodeType }, new { sourceValue = "Application", typeKey = appType } },
            fields = new[] { new { sourceField = "serie", propertyKey = "cmdb:serie", label = "Numéro de série" } },
        },
    });

    private static JsonElement Objects(ApiTests.Server server) => server.GetJson("/api/export/json").GetProperty("Objects");

    public static void Run()
    {
        Console.WriteLine("API des sources externes");

        Test("fichier : profil, aperçu, rapprochement, décisions, objet lié avec propriétés, rejeu sans écart", () =>
        {
            using var server = ServerWithModel();
            var node = TypeOf(server, "id-srv");
            var app = TypeOf(server, "id-itsm");
            Equal("cmdb-test", Ok(server, HttpMethod.Put, "/api/sources/cmdb-test", FileProfile(node, app)).GetProperty("key").GetString(), "profil créé");

            var preview = Ok(server, HttpMethod.Post, "/api/sources/cmdb-test/preview?name=export.csv", Csv);
            Equal("id,nom,categorie,serie", string.Join(",", preview.GetProperty("columns").EnumerateArray().Select(c => c.GetString())), "colonnes");
            Equal(5, preview.GetProperty("rows").GetArrayLength(), "cinq premières lignes");
            Equal(5, preview.GetProperty("rowCount").GetInt32(), "lignes lues");
            Equal(HttpStatusCode.NotFound, Send(server, HttpMethod.Get, "/api/sources/cmdb-test/report").Status, "aucun rapport avant le premier rapprochement");

            var run = Ok(server, HttpMethod.Post, "/api/sources/cmdb-test/run?name=export.csv", Csv);
            var counts = run.GetProperty("counts");
            Equal(2, counts.GetProperty("proposé").GetInt32(), "proposés");
            Equal(1, counts.GetProperty("nouveau").GetInt32(), "nouveaux");
            Equal(1, counts.GetProperty("invalide").GetInt32(), "invalides");
            True(run.GetProperty("issues").EnumerateArray().Any(i => i.GetProperty("code").GetString() == "identifiant-duplique"), "doublon signalé");
            Equal("export.csv", run.GetProperty("sourceName").GetString(), "nom du fichier");

            var apply = Ok(server, HttpMethod.Post, "/api/sources/cmdb-test/apply",
                "{\"acceptProposals\":true,\"decisions\":[{\"externalId\":\"3\",\"action\":\"creer\"}]}");
            var result = apply.GetProperty("result");
            Equal(2, result.GetProperty("linked").GetInt32(), "liés");
            Equal(1, result.GetProperty("created").GetInt32(), "créés");
            Equal(2, result.GetProperty("updated").GetInt32(), "mis à jour");
            Equal(3, apply.GetProperty("report").GetProperty("counts").GetProperty("lié").GetInt32(), "rapport recalculé");

            var objects = Objects(server);
            Equal("1", objects.GetProperty("id-srv").GetProperty("ExternalRefs").GetProperty("cmdb-test").GetString(), "lien externe");
            Equal("S-001", objects.GetProperty("id-srv").GetProperty("Properties").EnumerateArray().Single(p => p.GetProperty("Key").GetString() == "cmdb:serie").GetProperty("Value").GetString(), "propriété reprise");
            Equal("Proposé", objects.GetProperty("id-cmdb-test-3").GetProperty("ValidationStatus").GetString(), "objet créé à valider");

            var again = Ok(server, HttpMethod.Post, "/api/sources/cmdb-test/run", Csv);
            Equal(3, again.GetProperty("counts").GetProperty("lié").GetInt32(), "rejeu : lignes liées");
            True(again.GetProperty("lines").EnumerateArray().All(l => l.GetProperty("differences").GetArrayLength() == 0), "rejeu : aucun écart");
            var replay = Ok(server, HttpMethod.Post, "/api/sources/cmdb-test/apply", "{\"acceptProposals\":true}").GetProperty("result");
            Equal(0, replay.GetProperty("linked").GetInt32() + replay.GetProperty("created").GetInt32() + replay.GetProperty("updated").GetInt32(), "rejeu : rien à faire");
            Equal(Objects(server).EnumerateObject().Count(), objects.EnumerateObject().Count(), "rejeu : aucun doublon");
            True(Ok(server, HttpMethod.Get, "/api/sources/cmdb-test").GetProperty("lastRunAt").ValueKind == JsonValueKind.String, "date du dernier rapprochement renseignée");
        });

        Test("rapport : filtre par statut et pagination des lignes", () =>
        {
            using var server = ServerWithModel();
            Ok(server, HttpMethod.Put, "/api/sources/cmdb", FileProfile(TypeOf(server, "id-srv"), TypeOf(server, "id-itsm")));
            Ok(server, HttpMethod.Post, "/api/sources/cmdb/run", Csv);
            var filtered = Ok(server, HttpMethod.Get, "/api/sources/cmdb/report?status=proposé");
            Equal(2, filtered.GetProperty("linesTotal").GetInt32(), "lignes du statut");
            var page = Ok(server, HttpMethod.Get, "/api/sources/cmdb/report?skip=1&take=2");
            Equal(2, page.GetProperty("lines").GetArrayLength(), "taille de page");
            Equal(4, page.GetProperty("linesTotal").GetInt32(), "total");
        });

        Test("validation du profil : type inconnu, clé invalide, source inconnue, action inconnue", () =>
        {
            using var server = ServerWithModel();
            var node = TypeOf(server, "id-srv");
            var (status, body) = Send(server, HttpMethod.Put, "/api/sources/cmdb", FileProfile("TypeQuiNExistePas", node));
            Equal(HttpStatusCode.BadRequest, status, "type inconnu");
            True(body.Contains("TypeQuiNExistePas"), "le type fautif est nommé");
            var withoutType = FileProfile("TYPE-ABSENT", node).Replace("\"TYPE-ABSENT\"", "null");
            Equal(HttpStatusCode.BadRequest, Send(server, HttpMethod.Put, "/api/sources/cmdb", withoutType).Status, "nature sans type");
            Equal(HttpStatusCode.BadRequest, Send(server, HttpMethod.Put, "/api/sources/Mauvaise%20Cle", FileProfile(node, node)).Status, "clé invalide");
            Equal(HttpStatusCode.NotFound, Send(server, HttpMethod.Get, "/api/sources/absente").Status, "source inconnue");
            Equal(HttpStatusCode.NotFound, Send(server, HttpMethod.Post, "/api/sources/absente/apply", "{}").Status, "application sans rapprochement");
            Ok(server, HttpMethod.Put, "/api/sources/cmdb", FileProfile(node, node));
            Equal(HttpStatusCode.BadRequest, Send(server, HttpMethod.Post, "/api/sources/cmdb/apply", "{\"decisions\":[{\"externalId\":\"1\",\"action\":\"supprimer\"}]}").Status, "action inconnue");
            Equal(HttpStatusCode.BadGateway, Send(server, HttpMethod.Post, "/api/sources/cmdb/run", "").Status, "run sans fichier");
            Equal(HttpStatusCode.BadRequest, Send(server, HttpMethod.Post, "/api/sources/cmdb/test").Status, "test d'une source fichier");
        });

        Test("modèles de profils : fichier et API REST", () =>
        {
            using var server = new ApiTests.Server();
            var templates = Ok(server, HttpMethod.Get, "/api/sources/templates");
            Equal("fichier,api", string.Join(",", templates.EnumerateArray().Select(t => t.GetProperty("profile").GetProperty("kind").GetString())), "types");
        });

        Test("secret : chiffré au repos, jamais dans une réponse, inchangé si absent, effacé si vide", () =>
        {
            using var server = ServerWithModel();
            var node = TypeOf(server, "id-srv");
            var body = ProfileJson(node, "https://cmdb.example", Secret);
            var created = Ok(server, HttpMethod.Put, "/api/sources/cmdb-api", body);
            Equal(true, created.GetProperty("hasSecret").GetBoolean(), "secret enregistré");
            var responses = new[]
            {
                created.GetRawText(), Send(server, HttpMethod.Get, "/api/sources").Body, Send(server, HttpMethod.Get, "/api/sources/cmdb-api").Body,
                Send(server, HttpMethod.Get, "/api/sources/templates").Body, Send(server, HttpMethod.Get, "/api/export/json").Body,
                Send(server, HttpMethod.Post, "/api/sources/cmdb-api/test").Body,
            };
            True(responses.All(r => !r.Contains(Secret)), "secret en clair dans une réponse");

            var files = Directory.GetFiles(server.DataDirectory, "*", SearchOption.AllDirectories);
            True(files.Any(f => Path.GetFileName(f) == "sources.json"), "sources.json présent");
            True(files.Any(f => f.Contains($"{Path.DirectorySeparatorChar}cles{Path.DirectorySeparatorChar}")), "clés de chiffrement dans le dossier de données");
            True(files.All(f => !File.ReadAllText(f).Contains(Secret)), "secret en clair dans un fichier du dossier de données");
            True(File.ReadAllText(Path.Combine(server.DataDirectory, "sources.json")).Contains("protectedSecret"), "secret chiffré stocké");

            Ok(server, HttpMethod.Put, "/api/sources/cmdb-api", ProfileJson(node, "https://cmdb.example", null));
            Equal(true, Ok(server, HttpMethod.Get, "/api/sources/cmdb-api").GetProperty("hasSecret").GetBoolean(), "secret absent de la requête : inchangé");
            Ok(server, HttpMethod.Put, "/api/sources/cmdb-api", ProfileJson(node, "https://cmdb.example", ""));
            Equal(false, Ok(server, HttpMethod.Get, "/api/sources/cmdb-api").GetProperty("hasSecret").GetBoolean(), "chaîne vide : effacé");
        });

        Test("secret : adresse http distante refusée à l'enregistrement, réglage inconnu non conservé", () =>
        {
            using var server = ServerWithModel();
            var node = TypeOf(server, "id-srv");
            Equal(HttpStatusCode.BadRequest, Send(server, HttpMethod.Put, "/api/sources/cmdb-api", ProfileJson(node, "http://cmdb.example", "x")).Status, "http distant");
            var extra = ProfileJson(node, "https://cmdb.example", null).Replace("\"login\"", "\"password\":\"" + Secret + "\",\"login\"");
            var saved = Ok(server, HttpMethod.Put, "/api/sources/cmdb-api", extra);
            True(!saved.GetRawText().Contains(Secret) && !File.ReadAllText(Path.Combine(server.DataDirectory, "sources.json")).Contains(Secret), "réglage inconnu ignoré");
        });

        Test("CMDB par l'API REST : test de connexion, rapprochement, identifiants refusés", () =>
        {
            using var server = ServerWithModel();
            using var fake = new FakeCmdb("svc-ae", "mot-de-passe-cmdb");
            var node = TypeOf(server, "id-srv");
            Ok(server, HttpMethod.Put, "/api/sources/cmdb-api", ProfileJson(node, fake.BaseUrl, "mot-de-passe-cmdb"));

            var test = Ok(server, HttpMethod.Post, "/api/sources/cmdb-api/test");
            Equal(true, test.GetProperty("ok").GetBoolean(), "test : ok");
            Equal(2, test.GetProperty("total").GetInt32(), "test : total annoncé");

            var preview = Ok(server, HttpMethod.Post, "/api/sources/cmdb-api/preview");
            True(preview.GetProperty("columns").EnumerateArray().Any(c => c.GetString() == "LOCATION.LOCATION_PATH"), "colonne aplatie");

            var run = Ok(server, HttpMethod.Post, "/api/sources/cmdb-api/run");
            Equal(1, run.GetProperty("counts").GetProperty("proposé").GetInt32(), "proposé");
            Equal(1, run.GetProperty("counts").GetProperty("nouveau").GetInt32(), "nouveau");
            Ok(server, HttpMethod.Post, "/api/sources/cmdb-api/apply", "{\"acceptProposals\":true,\"decisions\":[{\"externalId\":\"502\",\"action\":\"creer\"}]}");
            Equal("Europe/Spain/Madrid", Objects(server).GetProperty("id-srv").GetProperty("Properties").EnumerateArray()
                .Single(p => p.GetProperty("Key").GetString() == "cmdb:localisation").GetProperty("Value").GetString(), "localisation reprise");
            True(fake.OnlyGet, "l'outil n'a émis que des GET");

            Ok(server, HttpMethod.Put, "/api/sources/cmdb-api", ProfileJson(node, fake.BaseUrl, "mauvais"));
            var refused = Ok(server, HttpMethod.Post, "/api/sources/cmdb-api/test");
            Equal(false, refused.GetProperty("ok").GetBoolean(), "mauvais secret : test en échec");
            Equal("Identifiants refusés par la source.", refused.GetProperty("message").GetString(), "message traduit");
            var (status, text) = Send(server, HttpMethod.Post, "/api/sources/cmdb-api/run");
            Equal(HttpStatusCode.BadGateway, status, "run en échec : 502");
            True(text.Contains("Identifiants refusés") && !text.Contains("mauvais"), "message sans secret");
        });

        Test("reprise après redémarrage : profils, secret et dernier rapport relus depuis le dossier de données", () =>
        {
            using var first = ServerWithModel();
            var node = TypeOf(first, "id-srv");
            using var fake = new FakeCmdb("svc-ae", "mot-de-passe-cmdb");
            Ok(first, HttpMethod.Put, "/api/sources/cmdb-api", ProfileJson(node, fake.BaseUrl, "mot-de-passe-cmdb"));
            Ok(first, HttpMethod.Post, "/api/sources/cmdb-api/run");
            Ok(first, HttpMethod.Put, "/api/sources/cmdb", FileProfile(node, TypeOf(first, "id-itsm")));
            Ok(first, HttpMethod.Post, "/api/sources/cmdb/run", Csv);

            using var second = new ApiTests.Server("--DataPath", Path.Combine(first.DataDirectory, "referentiel.json"));
            Equal(2, Ok(second, HttpMethod.Get, "/api/sources").GetArrayLength(), "profils relus");
            Equal(2, Ok(second, HttpMethod.Get, "/api/sources/cmdb/report").GetProperty("counts").GetProperty("proposé").GetInt32(), "rapport relu");
            Equal(2, Ok(second, HttpMethod.Post, "/api/sources/cmdb/apply", "{\"acceptProposals\":true}").GetProperty("result").GetProperty("linked").GetInt32(), "décisions appliquées après redémarrage");
            Equal(true, Ok(second, HttpMethod.Post, "/api/sources/cmdb-api/test").GetProperty("ok").GetBoolean(), "secret déchiffré par le nouveau processus");
        });

        Test("suppression d'un profil : le profil et son rapport disparaissent, les liens posés restent", () =>
        {
            using var server = ServerWithModel();
            Ok(server, HttpMethod.Put, "/api/sources/cmdb", FileProfile(TypeOf(server, "id-srv"), TypeOf(server, "id-itsm")));
            Ok(server, HttpMethod.Post, "/api/sources/cmdb/run", Csv);
            Ok(server, HttpMethod.Post, "/api/sources/cmdb/apply", "{\"acceptProposals\":true}");
            Equal(HttpStatusCode.NoContent, Send(server, HttpMethod.Delete, "/api/sources/cmdb").Status, "suppression");
            Equal(HttpStatusCode.NotFound, Send(server, HttpMethod.Get, "/api/sources/cmdb").Status, "profil retiré");
            Equal(HttpStatusCode.NotFound, Send(server, HttpMethod.Get, "/api/sources/cmdb/report").Status, "rapport retiré");
            Equal("1", Objects(server).GetProperty("id-srv").GetProperty("ExternalRefs").GetProperty("cmdb").GetString(), "lien conservé");
            Equal(HttpStatusCode.NotFound, Send(server, HttpMethod.Delete, "/api/sources/cmdb").Status, "suppression répétée");
        });
    }

    private static string ProfileJson(string nodeType, string baseUrl, string? secret) => JsonSerializer.Serialize(new Dictionary<string, object?>
    {
        ["label"] = "CMDB par API", ["kind"] = "api", ["sourceIsMaster"] = false,
        ["connection"] = new Dictionary<string, string> { ["url"] = baseUrl + "/api/v1/demo/configuration-items", ["login"] = "svc-ae", ["authMode"] = "basic", ["pageSize"] = "100",
            ["pageSizeParam"] = "max_rows", ["itemsField"] = "records", ["totalField"] = "total_record_count", ["nextField"] = "@next", ["idField"] = "HREF" },
        ["mapping"] = new
        {
            idField = "@id", nameField = "NETWORK_IDENTIFIER", defaultTypeKey = nodeType,
            fields = new[]
            {
                new { sourceField = "SERIAL_NUMBER", propertyKey = "cmdb:numero-serie", label = "Numéro de série" },
                new { sourceField = "LOCATION.LOCATION_PATH", propertyKey = "cmdb:localisation", label = "Localisation" },
            },
        },
        ["secret"] = secret,
    }.Where(p => p.Key != "secret" || p.Value is not null).ToDictionary(p => p.Key, p => p.Value));

    /// <summary>Fausse CMDB locale (HttpListener) : vérifie l'authentification Basic et sert deux éléments de configuration.</summary>
    private sealed class FakeCmdb : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly string _expected;
        private volatile bool _onlyGet = true;
        public string BaseUrl { get; }
        public bool OnlyGet => _onlyGet;

        public FakeCmdb(string login, string password)
        {
            _expected = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{login}:{password}"));
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            BaseUrl = $"http://127.0.0.1:{port}";
            _listener.Prefixes.Add(BaseUrl + "/");
            _listener.Start();
            _ = Task.Run(Serve);
        }

        private async Task Serve()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;
                try { context = await _listener.GetContextAsync(); }
                catch (Exception e) when (e is HttpListenerException or ObjectDisposedException or InvalidOperationException) { return; }
                if (context.Request.HttpMethod != "GET") _onlyGet = false;
                var ok = context.Request.Headers["Authorization"] == _expected;
                var path = context.Request.Url!.AbsolutePath;
                var (status, body) = !ok ? (401, "{}") : path == "/api/v1/demo/configuration-items" ? (200, Page(context.Request.QueryString["max_rows"] == "1")) : (404, "{}");
                var bytes = Encoding.UTF8.GetBytes(body);
                context.Response.StatusCode = status;
                context.Response.ContentType = "application/json";
                await context.Response.OutputStream.WriteAsync(bytes);
                context.Response.Close();
            }
        }

        private string Page(bool single)
        {
            string Record(int id, string name, string path) => $$"""{ "HREF": "{{BaseUrl}}/api/v1/demo/configuration-items/{{id}}", "NETWORK_IDENTIFIER": "{{name}}", "SERIAL_NUMBER": "SN{{id}}", "LOCATION": { "HREF": "{{BaseUrl}}/api/v1/demo/locations/1", "LOCATION_PATH": "{{path}}" } }""";
            var records = single ? Record(501, "SRV-APP-01", "Europe/Spain/Madrid") : Record(501, "SRV-APP-01", "Europe/Spain/Madrid") + "," + Record(502, "SRV-EV-NOUVEAU", "Europe/France/Paris");
            return $$"""{ "record_count": "{{(single ? 1 : 2)}}", "total_record_count": "2", "records": [ {{records}} ] }""";
        }

        public void Dispose() => _listener.Close();
    }
}
