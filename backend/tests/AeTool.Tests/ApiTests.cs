using System.Net;
using System.Text.Json;
using AeTool.Api;
using Microsoft.AspNetCore.Builder;
using static AeTool.Tests.Harness;

namespace AeTool.Tests;

/// <summary>
/// Tests d'intégration : l'application réelle est démarrée en mémoire sur un port libre, avec un dossier de données temporaire,
/// puis interrogée en HTTP comme le ferait l'interface.
/// </summary>
public static class ApiTests
{
    /// <summary>Démarre l'application sur un port libre et fournit un client HTTP pointé dessus. À libérer après usage.</summary>
    public sealed class Server : IDisposable
    {
        private readonly WebApplication _app;
        public HttpClient Client { get; }
        public string DataDirectory { get; }

        public Server(params string[] extraArgs)
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "ae-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DataDirectory);
            var args = new List<string> { "--urls", "http://127.0.0.1:0", "--DataPath", Path.Combine(DataDirectory, "referentiel.json"),
                "--Logging:LogLevel:Default", "Error" };
            args.AddRange(extraArgs);
            _app = AppHost.Build(args.ToArray());
            _app.StartAsync().GetAwaiter().GetResult();
            Client = new HttpClient { BaseAddress = new Uri(_app.Urls.First()) };
        }

        public JsonElement GetJson(string path)
        {
            var response = Client.GetAsync(path).GetAwaiter().GetResult();
            Equal(HttpStatusCode.OK, response.StatusCode, $"GET {path}");
            return JsonDocument.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult()).RootElement;
        }

        public void Dispose()
        {
            Client.Dispose();
            _app.StopAsync().GetAwaiter().GetResult();
            _app.DisposeAsync().AsTask().GetAwaiter().GetResult();
            try { Directory.Delete(DataDirectory, recursive: true); } catch (IOException) { /* fichier encore tenu : sans conséquence pour le test */ }
        }
    }

    public static void Run()
    {
        Console.WriteLine("API (application démarrée en mémoire)");

        Test("référentiel vide : la synthèse répond et le métamodèle par défaut est servi", () =>
        {
            using var server = new Server();
            Equal(0, server.GetJson("/api/summary").GetProperty("objects").GetInt32(), "objets");
            True(server.GetJson("/api/metamodel").GetProperty("objectTypes").GetArrayLength() > 20, "types du métamodèle par défaut");
        });

        Test("import d'un modèle Archi par l'API, puis lecture d'un objet", () =>
        {
            using var server = new Server();
            var content = new StringContent(Fixtures.Model());
            var response = server.Client.PostAsync("/api/import/archimate", content).GetAwaiter().GetResult();
            Equal(HttpStatusCode.OK, response.StatusCode, "import");
            Equal("ITSM-SUITE", server.GetJson("/api/objects/id-itsm").GetProperty("brief").GetProperty("name").GetString(), "nom de l'objet importé");
        });
    }
}
