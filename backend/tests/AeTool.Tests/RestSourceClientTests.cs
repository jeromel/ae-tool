using System.Net;
using System.Text;
using AeTool.Connectors;
using AeTool.Core;
using static AeTool.Tests.Harness;

namespace AeTool.Tests;

/// <summary>
/// Le client API REST générique est testé contre des réponses simulées : une API paginée par un lien de page suivante,
/// avec un total, des éléments sous « records » et un identifiant porté par un lien. Aucune API réelle n'est jointe.
/// </summary>
public static class RestSourceClientTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _reply;
        public List<HttpRequestMessage> Requests { get; } = new();
        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> reply) => _reply = reply;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            if (request.RequestUri!.Host == "lent.example") await Task.Delay(Timeout.Infinite, ct);
            return _reply(request);
        }
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static Dictionary<string, string> Settings(string host = "https://cmdb.example", string auth = "basic") => new()
    {
        ["url"] = host + "/api/v1/demo/configuration-items", ["query"] = "fields=ASSET_TAG,LOCATION.LOCATION_PATH",
        ["login"] = "svc-ae", ["authMode"] = auth, ["pageSize"] = "2", ["pageSizeParam"] = "max_rows", ["offsetParam"] = "offset",
        ["itemsField"] = "records", ["totalField"] = "total_record_count", ["nextField"] = "@next", ["idField"] = "HREF",
    };

    private static string Record(int id, string tag, string path = "Europe/Spain/Madrid") => $$"""
        { "HREF": "https://cmdb.example/api/v1/demo/configuration-items/{{id}}", "ASSET_TAG": "{{tag}}", "SERIAL_NUMBER": "S{{id}}",
          "INSTALLATION_DATE": "2007-10-15T00:00:00.000Z", "RETIRED": false, "COUNT": 3,
          "LOCATION": { "HREF": "https://cmdb.example/api/v1/demo/locations/257", "LOCATION_PATH": "{{path}}" } }
        """;

    private static string PageJson(IEnumerable<string> records, int total, string? next = null) =>
        $$"""{ "HREF": "x", "record_count": "{{records.Count()}}", "total_record_count": "{{total}}", {{(next is null ? "" : $"\"@next\": \"{next}\",")}} "records": [ {{string.Join(",", records)}} ] }""";

    private static RestSourceClient Client(FakeHandler handler) =>
        new(new HttpClient(handler)) { MinInterval = TimeSpan.Zero, CallTimeout = TimeSpan.FromSeconds(30) };

    private static T Wait<T>(Task<T> task) => task.GetAwaiter().GetResult();

    private static string Failure(Func<Task> call)
    {
        try { call().GetAwaiter().GetResult(); }
        catch (SourceAccessException e) { return e.Message; }
        throw new Exception("une erreur d'accès à la source était attendue");
    }

    public static void Run()
    {
        Console.WriteLine("Adaptateur API REST (réponses simulées)");

        Test("pagination par lien de page suivante : toutes les pages sont lues, décalage repris du lien", () =>
        {
            var handler = new FakeHandler(r => r.RequestUri!.Query.Contains("offset=2")
                ? Json(PageJson(new[] { Record(3, "C") }, 3))
                : Json(PageJson(new[] { Record(1, "A"), Record(2, "B") }, 3, "https://interne.local/api/v1/demo/configuration-items?max_rows=2&offset=2")));
            var table = Wait(Client(handler).FetchAsync(Settings(), "mdp", 1000, default));
            Equal(3, table.Rows.Count, "lignes");
            Equal(3L, table.Total, "total annoncé");
            Equal(false, table.Truncated, "complet");
            Equal(2, handler.Requests.Count, "appels");
            True(handler.Requests.All(r => r.RequestUri!.Host == "cmdb.example"), "jamais d'appel vers l'hôte du lien de page suivante");
            True(handler.Requests[0].RequestUri!.Query.Contains("max_rows=2") && handler.Requests[0].RequestUri!.AbsolutePath == "/api/v1/demo/configuration-items", "première adresse");
            True(Uri.UnescapeDataString(handler.Requests[0].RequestUri!.Query).Contains("fields=ASSET_TAG,LOCATION.LOCATION_PATH"), "champs demandés");
        });

        Test("sans lien de page suivante : une page suffit si le compteur atteint le total", () =>
        {
            var handler = new FakeHandler(_ => Json(PageJson(new[] { Record(1, "A"), Record(2, "B") }, 2)));
            var table = Wait(Client(handler).FetchAsync(Settings(), "mdp", 1000, default));
            Equal(2, table.Rows.Count, "lignes");
            Equal(1, handler.Requests.Count, "appels");
        });

        Test("sans lien de page suivante : on retente avec le décalage tant qu'il en reste", () =>
        {
            var handler = new FakeHandler(r => r.RequestUri!.Query.Contains("offset=2")
                ? Json(PageJson(new[] { Record(3, "C") }, 3))
                : Json(PageJson(new[] { Record(1, "A"), Record(2, "B") }, 3)));
            var table = Wait(Client(handler).FetchAsync(Settings(), "mdp", 1000, default));
            Equal(3, table.Rows.Count, "lignes");
            Equal(2, handler.Requests.Count, "appels");
        });

        Test("API qui ignore le décalage : arrêt sans doublon, lecture déclarée partielle", () =>
        {
            var handler = new FakeHandler(_ => Json(PageJson(new[] { Record(1, "A"), Record(2, "B") }, 5)));
            var table = Wait(Client(handler).FetchAsync(Settings(), "mdp", 1000, default));
            Equal(2, table.Rows.Count, "lignes sans doublon");
            True(table.Truncated, "partielle");
            True(table.Issues.Any(i => i.Code == "pagination-non-prise-en-charge"), "anomalie");
        });

        Test("aplatissement : colonnes pointées, liens imbriqués ignorés, @id tiré du champ identifiant", () =>
        {
            var handler = new FakeHandler(_ => Json(PageJson(new[] { Record(9478, "PHOEBE_4") }, 1)));
            var table = Wait(Client(handler).FetchAsync(Settings(), "mdp", 1000, default));
            var row = table.Rows.Single();
            Equal("9478", row["@id"], "@id");
            Equal("Europe/Spain/Madrid", row["LOCATION.LOCATION_PATH"], "objet imbriqué");
            Equal("PHOEBE_4", row["ASSET_TAG"], "champ simple");
            Equal("false", row["RETIRED"], "booléen");
            Equal("3", row["COUNT"], "nombre");
            True(!row.ContainsKey("LOCATION.HREF") && !row.ContainsKey("HREF") && !row.ContainsKey("LOCATION"), "aucun HREF ni objet brut");
            Equal("@id", table.Columns[0], "@id en première colonne");
        });

        Test("authentification : en-tête Basic (identifiant et mot de passe) ou Bearer (jeton)", () =>
        {
            var handler = new FakeHandler(_ => Json(PageJson(new[] { Record(1, "A") }, 1)));
            Wait(Client(handler).FetchAsync(Settings(), "mdp", 10, default));
            Equal("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("svc-ae:mdp")), handler.Requests[0].Headers.Authorization!.ToString(), "Basic");
            var bearer = new FakeHandler(_ => Json(PageJson(new[] { Record(1, "A") }, 1)));
            Wait(Client(bearer).FetchAsync(Settings(auth: "bearer"), "jeton-123", 10, default));
            Equal("Bearer jeton-123", bearer.Requests[0].Headers.Authorization!.ToString(), "Bearer");
        });

        Test("lecture seule : seules des requêtes GET sont émises", () =>
        {
            var handler = new FakeHandler(r => Json(PageJson(new[] { Record(1, "A"), Record(2, "B") }, 3, "https://x/y?offset=2")));
            Wait(Client(handler).FetchAsync(Settings(), "mdp", 10, default));
            Wait(Client(handler).ProbeAsync(Settings(), "mdp", default));
            True(handler.Requests.All(r => r.Method == HttpMethod.Get), "GET uniquement");
        });

        Test("erreurs traduites : 401 et 403, 404, 429, 500, réponse illisible, injoignable", () =>
        {
            string Message(HttpStatusCode code, string body = "{}") =>
                Failure(() => Client(new FakeHandler(_ => Json(body, code))).FetchAsync(Settings(), "mdp", 10, default));
            Equal("Identifiants refusés par la source.", Message(HttpStatusCode.Unauthorized), "401");
            Equal("Identifiants refusés par la source.", Message(HttpStatusCode.Forbidden), "403");
            True(Message(HttpStatusCode.NotFound).StartsWith("Ressource introuvable"), "404");
            True(Message(HttpStatusCode.TooManyRequests).Contains("Limite d'appels"), "429");
            True(Message(HttpStatusCode.InternalServerError, "trace interne secrète").Contains("erreur"), "500");
            True(!Message(HttpStatusCode.InternalServerError, "trace interne secrète").Contains("secrète"), "le corps de l'erreur n'est pas répété");
            Equal("Réponse de la source illisible.", Message(HttpStatusCode.OK, "<html>pas du json</html>"), "réponse illisible");
            True(Message(HttpStatusCode.OK, "{\"autre\": 1}").Contains("liste d'éléments introuvable"), "réponse sans le champ des éléments");
            var down = Failure(() => Client(new FakeHandler(_ => throw new HttpRequestException("tls: certificat rejeté"))).FetchAsync(Settings(), "mdp", 10, default));
            True(down.Contains("injoignable") && !down.Contains("certificat"), "injoignable sans détail technique");
        });

        Test("délai dépassé : message dédié", () =>
        {
            var client = Client(new FakeHandler(_ => Json("{}")));
            client.CallTimeout = TimeSpan.FromMilliseconds(100);
            var message = Failure(() => client.FetchAsync(Settings("https://lent.example"), "mdp", 10, default));
            True(message.Contains("délai"), message);
        });

        Test("le secret n'apparaît dans aucun message d'erreur", () =>
        {
            const string secret = "S3cr3t-à-ne-pas-fuiter";
            foreach (var code in new[] { HttpStatusCode.Unauthorized, HttpStatusCode.NotFound, HttpStatusCode.BadGateway })
            {
                var message = Failure(() => Client(new FakeHandler(_ => Json("{\"erreur\": \"" + secret + "\"}", code))).FetchAsync(Settings(), secret, 10, default));
                True(!message.Contains(secret), $"secret dans le message ({code})");
            }
        });

        Test("réglages refusés : http hors localhost, adresse invalide, paramètres douteux", () =>
        {
            string? Check(Action<Dictionary<string, string>> change)
            {
                var s = Settings();
                change(s);
                return RestSourceClient.Validate(s);
            }
            True(Check(s => s["url"] = "http://cmdb.example/x") is not null, "http distant");
            True(Check(s => s["url"] = "http://localhost:5000/x") is null, "http localhost");
            True(Check(s => s["url"] = "http://127.0.0.1:5000/x") is null, "http boucle locale");
            True(Check(s => s["url"] = "ftp://cmdb.example/x") is not null, "autre protocole");
            True(Check(s => s["url"] = "https://user:pw@cmdb.example/x") is not null, "identifiants dans l'adresse");
            True(Check(s => s["url"] = "pas une adresse") is not null, "adresse invalide");
            True(Check(s => s["url"] = "https://cmdb.example/x?max_rows=5") is not null, "paramètres dans l'adresse");
            True(Check(s => s["offsetParam"] = "a/b") is not null, "nom de paramètre douteux");
            True(Check(s => s["query"] = "?a=1") is not null, "paramètres commençant par ?");
            True(Check(s => s.Remove("login")) is not null, "login manquant en mode basic");
            True(Check(s => { s["authMode"] = "bearer"; s.Remove("login"); }) is null, "jeton sans login");
            True(Check(s => { }) is null, "réglages valides");
            var handler = new FakeHandler(_ => Json("{}"));
            var message = Failure(() => Client(handler).FetchAsync(Settings("http://cmdb.example"), "mdp", 10, default));
            True(message.Contains("https"), message);
            Equal(0, handler.Requests.Count, "aucun appel émis");
        });

        Test("secret absent : refus avant tout appel", () =>
        {
            var handler = new FakeHandler(_ => Json("{}"));
            Failure(() => Client(handler).FetchAsync(Settings(), "", 10, default));
            Equal(0, handler.Requests.Count, "appels");
        });

        Test("garde-fou : le nombre d'enregistrements est plafonné et la lecture déclarée tronquée", () =>
        {
            var handler = new FakeHandler(r => Json(PageJson(new[] { Record(1, "A"), Record(2, "B") }, 100, "https://x/y?offset=2")));
            var table = Wait(Client(handler).FetchAsync(Settings(), "mdp", 3, default));
            Equal(3, table.Rows.Count, "lignes");
            True(table.Truncated, "tronquée");
        });

        Test("garde-fou : 500 pages au plus", () =>
        {
            var calls = 0;
            var handler = new FakeHandler(_ => { calls++; return Json(PageJson(new[] { Record(calls * 2, "A"), Record(calls * 2 + 1, "B") }, 1_000_000, $"https://x/y?offset={calls * 2}")); });
            var table = Wait(Client(handler).FetchAsync(Settings(), "mdp", 1_000_000, default));
            Equal(RestSourceClient.MaxPages, handler.Requests.Count, "appels");
            True(table.Truncated && table.Issues.Any(i => i.Code == "limite-pages"), "anomalie");
        });

        Test("test de connexion : un appel d'une seule ligne, total annoncé renvoyé", () =>
        {
            var handler = new FakeHandler(_ => Json(PageJson(new[] { Record(1, "A") }, 805)));
            Equal(805L, Wait(Client(handler).ProbeAsync(Settings(), "mdp", default)), "total");
            True(handler.Requests.Single().RequestUri!.Query.Contains("max_rows=1"), "max_rows=1");
        });

        Test("profil de départ API REST : réglages valides une fois l'adresse saisie, aucun type en dur", () =>
        {
            var p = SourceProfile.ApiTemplate();
            Equal("@id", p.Mapping.IdField, "identifiant");
            True(p.Mapping.Fields.All(f => f.PropertyKey.StartsWith("cmdb:")), "propriétés préfixées");
            True(p.Mapping.DefaultTypeKey is null && p.Mapping.Types.Count == 0, "aucun type choisi à la place de l'utilisateur");
            Equal(null, RestSourceClient.Validate(new Dictionary<string, string>(p.Connection) { ["url"] = "https://cmdb.example/api/elements" }), "réglages cohérents");
        });

        Test("API à liste nue ou à champ deviné : la liste est trouvée sans réglage", () =>
        {
            var bare = new FakeHandler(_ => Json("""[ { "id": 1, "name": "A" }, { "id": 2, "name": "B" } ]"""));
            var simple = new Dictionary<string, string> { ["url"] = "https://cmdb.example/elements", ["authMode"] = "bearer", ["idField"] = "id" };
            var table = Wait(Client(bare).FetchAsync(simple, "jeton", 1000, default));
            Equal("1,2", string.Join(",", table.Rows.Select(r => r["@id"])), "identifiants");
            True(bare.Requests[0].RequestUri!.Query.Contains("limit=100"), "paramètres par défaut");
            var wrapped = new FakeHandler(_ => Json("""{ "count": 1, "data": [ { "id": "x", "name": "C" } ] }"""));
            Equal("C", Wait(Client(wrapped).FetchAsync(simple, "jeton", 1000, default)).Rows.Single()["name"], "premier champ qui est une liste");
        });

        Test("reprise d'un profil d'API enregistré avant le connecteur générique : réglages équivalents", () =>
        {
            var old = new SourceProfile
            {
                Key = "cmdb", Kind = "ancien", Connection = new Dictionary<string, string>
                {
                    ["baseUrl"] = "https://cmdb.example/", ["account"] = "demo", ["resource"] = "configuration-items", ["fields"] = "A,B",
                    ["pageSize"] = "50", ["authMode"] = "basic", ["login"] = "svc-ae",
                },
            };
            AeTool.Api.Sources.SourceStore.UpgradeApiProfile(old);
            Equal(SourceProfile.KindApi, old.Kind, "type");
            Equal("https://cmdb.example/api/v1/demo/configuration-items", old.Connection["url"], "adresse");
            Equal("fields=A%2CB", old.Connection["query"], "paramètres");
            Equal(null, RestSourceClient.Validate(old.Connection), "réglages valides");
            var handler = new FakeHandler(_ => Json(PageJson(new[] { Record(1, "A") }, 1)));
            Equal("1", Wait(Client(handler).FetchAsync(old.Connection, "mdp", 10, default)).Rows.Single()["@id"], "lecture identique");
        });
    }
}
