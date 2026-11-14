using System.Net;
using System.Text;
using AeTool.Connectors;
using AeTool.Core;
using static AeTool.Tests.Harness;

namespace AeTool.Tests;

/// <summary>
/// Le client est testé contre des réponses simulées, construites d'après la documentation de l'éditeur :
/// aucune plateforme EasyVista réelle n'a pu être jointe.
/// </summary>
public static class EasyVistaClientTests
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

    private static Dictionary<string, string> Settings(string baseUrl = "https://ev.example", string auth = "basic") => new()
    {
        ["baseUrl"] = baseUrl, ["account"] = "demo", ["login"] = "svc-ae", ["authMode"] = auth, ["pageSize"] = "2", ["fields"] = "ASSET_TAG,LOCATION.LOCATION_PATH",
    };

    private static string Record(int id, string tag, string path = "Europe/Spain/Madrid") => $$"""
        { "HREF": "https://ev.example/api/v1/demo/configuration-items/{{id}}", "ASSET_TAG": "{{tag}}", "SERIAL_NUMBER": "S{{id}}",
          "INSTALLATION_DATE": "2007-10-15T00:00:00.000Z", "RETIRED": false, "COUNT": 3,
          "LOCATION": { "HREF": "https://ev.example/api/v1/demo/locations/257", "LOCATION_PATH": "{{path}}" } }
        """;

    private static string PageJson(IEnumerable<string> records, int total, string? next = null) =>
        $$"""{ "HREF": "x", "record_count": "{{records.Count()}}", "total_record_count": "{{total}}", {{(next is null ? "" : $"\"@next\": \"{next}\",")}} "records": [ {{string.Join(",", records)}} ] }""";

    private static EasyVistaClient Client(FakeHandler handler) =>
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
        Console.WriteLine("Adaptateur EasyVista (réponses simulées)");

        Test("pagination par @next : toutes les pages sont lues, décalage repris du lien", () =>
        {
            var handler = new FakeHandler(r => r.RequestUri!.Query.Contains("offset=2")
                ? Json(PageJson(new[] { Record(3, "C") }, 3))
                : Json(PageJson(new[] { Record(1, "A"), Record(2, "B") }, 3, "https://interne.local/api/v1/demo/configuration-items?max_rows=2&offset=2")));
            var table = Wait(Client(handler).FetchAsync(Settings(), "mdp", 1000, default));
            Equal(3, table.Rows.Count, "lignes");
            Equal(3L, table.Total, "total annoncé");
            Equal(false, table.Truncated, "complet");
            Equal(2, handler.Requests.Count, "appels");
            True(handler.Requests.All(r => r.RequestUri!.Host == "ev.example"), "jamais d'appel vers l'hôte du lien @next");
            True(handler.Requests[0].RequestUri!.Query.Contains("max_rows=2") && handler.Requests[0].RequestUri!.AbsolutePath == "/api/v1/demo/configuration-items", "première adresse");
            True(Uri.UnescapeDataString(handler.Requests[0].RequestUri!.Query).Contains("fields=ASSET_TAG,LOCATION.LOCATION_PATH"), "champs demandés");
        });

        Test("version ancienne sans @next : une page suffit si le compteur atteint le total", () =>
        {
            var handler = new FakeHandler(_ => Json(PageJson(new[] { Record(1, "A"), Record(2, "B") }, 2)));
            var table = Wait(Client(handler).FetchAsync(Settings(), "mdp", 1000, default));
            Equal(2, table.Rows.Count, "lignes");
            Equal(1, handler.Requests.Count, "appels");
        });

        Test("version ancienne sans @next : on retente avec offset tant qu'il en reste", () =>
        {
            var handler = new FakeHandler(r => r.RequestUri!.Query.Contains("offset=2")
                ? Json(PageJson(new[] { Record(3, "C") }, 3))
                : Json(PageJson(new[] { Record(1, "A"), Record(2, "B") }, 3)));
            var table = Wait(Client(handler).FetchAsync(Settings(), "mdp", 1000, default));
            Equal(3, table.Rows.Count, "lignes");
            Equal(2, handler.Requests.Count, "appels");
        });

        Test("version qui ignore offset : arrêt sans doublon, lecture déclarée partielle", () =>
        {
            var handler = new FakeHandler(_ => Json(PageJson(new[] { Record(1, "A"), Record(2, "B") }, 5)));
            var table = Wait(Client(handler).FetchAsync(Settings(), "mdp", 1000, default));
            Equal(2, table.Rows.Count, "lignes sans doublon");
            True(table.Truncated, "partielle");
            True(table.Issues.Any(i => i.Code == "pagination-non-prise-en-charge"), "anomalie");
        });

        Test("aplatissement : colonnes pointées, HREF imbriqués ignorés, @id tiré du HREF de l'enregistrement", () =>
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
            Equal("Identifiants refusés par EasyVista.", Message(HttpStatusCode.Unauthorized), "401");
            Equal("Identifiants refusés par EasyVista.", Message(HttpStatusCode.Forbidden), "403");
            Equal("Compte ou ressource introuvable.", Message(HttpStatusCode.NotFound), "404");
            True(Message(HttpStatusCode.TooManyRequests).Contains("Limite d'appels"), "429");
            True(Message(HttpStatusCode.InternalServerError, "trace interne secrète").Contains("erreur"), "500");
            True(!Message(HttpStatusCode.InternalServerError, "trace interne secrète").Contains("secrète"), "le corps de l'erreur n'est pas répété");
            Equal("Réponse d'EasyVista illisible.", Message(HttpStatusCode.OK, "<html>pas du json</html>"), "réponse illisible");
            Equal("Réponse d'EasyVista illisible.", Message(HttpStatusCode.OK, "{\"autre\": 1}"), "réponse sans records");
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

        Test("adresse refusée : http hors localhost, adresse invalide, compte ou ressource douteux", () =>
        {
            string? Check(Action<Dictionary<string, string>> change)
            {
                var s = Settings();
                change(s);
                return EasyVistaClient.Validate(s);
            }
            True(Check(s => s["baseUrl"] = "http://ev.example") is not null, "http distant");
            True(Check(s => s["baseUrl"] = "http://localhost:5000") is null, "http localhost");
            True(Check(s => s["baseUrl"] = "http://127.0.0.1:5000") is null, "http boucle locale");
            True(Check(s => s["baseUrl"] = "ftp://ev.example") is not null, "autre protocole");
            True(Check(s => s["baseUrl"] = "https://user:pw@ev.example") is not null, "identifiants dans l'adresse");
            True(Check(s => s["baseUrl"] = "pas une adresse") is not null, "adresse invalide");
            True(Check(s => s["account"] = "../autre") is not null, "compte avec chemin");
            True(Check(s => s["resource"] = "../../x") is not null, "ressource avec remontée");
            True(Check(s => s.Remove("login")) is not null, "login manquant en mode basic");
            True(Check(s => { s["authMode"] = "bearer"; s.Remove("login"); }) is null, "jeton sans login");
            True(Check(s => { }) is null, "réglages valides");
            var handler = new FakeHandler(_ => Json("{}"));
            var message = Failure(() => Client(handler).FetchAsync(Settings("http://ev.example"), "mdp", 10, default));
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
            Equal(EasyVistaClient.MaxPages, handler.Requests.Count, "appels");
            True(table.Truncated && table.Issues.Any(i => i.Code == "limite-pages"), "anomalie");
        });

        Test("test de connexion : un appel d'une seule ligne, total annoncé renvoyé", () =>
        {
            var handler = new FakeHandler(_ => Json(PageJson(new[] { Record(1, "A") }, 805)));
            Equal(805L, Wait(Client(handler).ProbeAsync(Settings(), "mdp", default)), "total");
            True(handler.Requests.Single().RequestUri!.Query.Contains("max_rows=1"), "max_rows=1");
        });

        Test("profil de départ EasyVista : mappage plausible, aucun type en dur", () =>
        {
            var p = SourceProfile.EasyVistaTemplate();
            Equal("@id", p.Mapping.IdField, "identifiant");
            Equal("NETWORK_IDENTIFIER", p.Mapping.NameField, "nom");
            True(p.Mapping.Fields.Any(f => f.SourceField == "LOCATION.LOCATION_PATH" && f.PropertyKey.StartsWith("cmdb:")), "propriétés préfixées");
            True(p.Mapping.DefaultTypeKey is null && p.Mapping.Types.Count == 0, "aucun type choisi à la place de l'utilisateur");
            Equal(null, EasyVistaClient.Validate(new Dictionary<string, string>(p.Connection) { ["baseUrl"] = "https://ev.example", ["account"] = "demo", ["login"] = "x" }), "réglages cohérents");
        });
    }
}
