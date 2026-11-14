using System.Net;
using System.Text.Json;
using AeTool.Api;
using AeTool.Api.Auth;
using static AeTool.Tests.Harness;

namespace AeTool.Tests;

/// <summary>Hébergement : sonde de disponibilité, détection d'une écoute en clair, mandataire inverse.</summary>
public static class HostingTests
{
    public static void Run()
    {
        Console.WriteLine("Hébergement (sonde, écoute en clair, mandataire)");

        Test("la sonde de disponibilité répond sans session, même quand les comptes sont exigés", () =>
        {
            Equal<string?>(null, AccessRules.MinimumRole("GET", "/api/health"), "lecture ouverte");
            Equal<string?>(Roles.Administrator, AccessRules.MinimumRole("POST", "/api/health"), "écriture refusée par défaut");

            using var server = new ApiTests.Server("--Auth:Mode=local");
            var response = server.Client.GetAsync("/api/health").GetAwaiter().GetResult();
            Equal(HttpStatusCode.OK, response.StatusCode, "sans session");
            var body = JsonDocument.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult()).RootElement;
            Equal("ok", body.GetProperty("status").GetString(), "état");
            Equal(Hosting.Version, body.GetProperty("version").GetString(), "version livrée");
            True(Version.TryParse(Hosting.Version, out _), "la version est un numéro, sans suffixe de construction");
            Equal(HttpStatusCode.Unauthorized, server.Client.GetAsync("/api/summary").GetAwaiter().GetResult().StatusCode, "le reste exige toujours une session");
        });

        Test("une écoute en HTTP joignable du réseau est repérée, pas la boucle locale ni HTTPS", () =>
        {
            True(Hosting.IsPlainHttpOnNetwork("http://0.0.0.0:5080"), "toutes les interfaces");
            True(Hosting.IsPlainHttpOnNetwork("http://srv-archi.entreprise.example:5080"), "nom de machine");
            True(Hosting.IsPlainHttpOnNetwork("http://[::]:5080"), "toutes les interfaces en IPv6");
            True(Hosting.IsPlainHttpOnNetwork("http://10.20.30.40:80/"), "adresse du réseau");
            True(!Hosting.IsPlainHttpOnNetwork("http://localhost:5080"), "poste local");
            True(!Hosting.IsPlainHttpOnNetwork("http://127.0.0.1:5080"), "boucle locale");
            True(!Hosting.IsPlainHttpOnNetwork("http://[::1]:5080"), "boucle locale en IPv6");
            True(!Hosting.IsPlainHttpOnNetwork("https://0.0.0.0:5443"), "HTTPS");
        });

        Test("derrière un mandataire déclaré, l'adresse et le protocole d'origine sont repris ; sinon ils sont ignorés", () =>
        {
            // Sans mandataire déclaré, un client ne peut pas se dire en HTTPS : le cookie de session n'est pas marqué « Secure ».
            using (var direct = new ApiTests.Server("--Auth:Mode=local"))
            {
                var cookie = SessionCookieAfterSetup(direct, forwardedProto: "https");
                True(!cookie.Contains("secure", StringComparison.OrdinalIgnoreCase), "en-tête ignoré sans mandataire déclaré : " + cookie);
            }
            // Mandataire déclaré sur la machine : le protocole d'origine est cru, le cookie n'est rendu qu'en HTTPS.
            using (var proxied = new ApiTests.Server("--Auth:Mode=local", "--Proxy:Trusted:0=127.0.0.1"))
            {
                var cookie = SessionCookieAfterSetup(proxied, forwardedProto: "https");
                True(cookie.Contains("secure", StringComparison.OrdinalIgnoreCase), "protocole d'origine repris : " + cookie);
            }
        });

        Test("une adresse de mandataire illisible arrête le démarrage avec un message clair", () =>
        {
            try
            {
                using var server = new ApiTests.Server("--Proxy:Trusted:0=pas-une-adresse");
                True(false, "le démarrage aurait dû échouer");
            }
            catch (InvalidOperationException e)
            {
                True(e.Message.Contains("pas-une-adresse"), e.Message);
            }
        });
    }

    /// <summary>Crée le premier compte puis se connecte en annonçant un protocole d'origine ; renvoie l'en-tête du cookie de session reçu.</summary>
    private static string SessionCookieAfterSetup(ApiTests.Server server, string forwardedProto)
    {
        var code = File.ReadAllText(Path.Combine(server.DataDirectory, "code-installation.txt")).Trim();
        using var client = new HttpClient(new HttpClientHandler { UseCookies = false }) { BaseAddress = server.BaseAddress };
        // Toute écriture exige le jeton antifalsification : on lit la session, puis on renvoie le jeton et son cookie.
        var session = Send(client, HttpMethod.Get, "/api/auth/session", forwardedProto);
        var token = JsonDocument.Parse(Body(session)).RootElement.GetProperty("csrfToken").GetString()!;
        var antiforgery = session.Headers.GetValues("Set-Cookie").First(c => c.StartsWith("ae.csrf=", StringComparison.Ordinal)).Split(';')[0];

        const string password = "Mot-de-passe-solide-2026";
        var setup = Send(client, HttpMethod.Post, "/api/auth/setup", forwardedProto, new { code, login = "admin", name = "Administrateur", password }, token, antiforgery);
        Equal(HttpStatusCode.OK, setup.StatusCode, "premier lancement : " + Body(setup));
        var login = Send(client, HttpMethod.Post, "/api/auth/login", forwardedProto, new { login = "admin", password }, token, antiforgery);
        Equal(HttpStatusCode.OK, login.StatusCode, "connexion : " + Body(login));
        return login.Headers.GetValues("Set-Cookie").First(c => c.StartsWith(AuthSetup.CookieName + "=", StringComparison.Ordinal));
    }

    private static HttpResponseMessage Send(HttpClient client, HttpMethod method, string path, string forwardedProto, object? body = null, string? token = null, string? cookie = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Forwarded-Proto", forwardedProto);
        if (token is not null) request.Headers.Add(AuthSetup.CsrfHeader, token);
        if (cookie is not null) request.Headers.Add("Cookie", cookie);
        if (body is not null) request.Content = new StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json");
        return client.SendAsync(request).GetAwaiter().GetResult();
    }

    private static string Body(HttpResponseMessage response) => response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
}
