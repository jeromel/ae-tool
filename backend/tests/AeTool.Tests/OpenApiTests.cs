using System.Net;
using System.Text.Json;
using AeTool.Api.Endpoints;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using static AeTool.Tests.Harness;

namespace AeTool.Tests;

/// <summary>Description OpenAPI (décision 013, point 4) : elle ne ment pas sur les routes ni sur les droits.</summary>
public static class OpenApiTests
{
    public static void Run()
    {
        Console.WriteLine("Description OpenAPI");
        using var server = new ApiTests.Server("--Auth:Mode=local");
        var routes = OpenApiEndpoints.Routes(server.Services.GetRequiredService<EndpointDataSource>()).ToList();
        JsonElement doc = default;

        Test("openapi.json : lisible par un lecteur seulement (rôle lecteur), refusé sans session", () =>
        {
            var anonymous = server.NewClient().GetAsync("/api/openapi.json").GetAwaiter().GetResult();
            Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode, "sans session");
            Equal("lecteur", OpenApiEndpoints.RoleOf("GET", "/api/openapi.json"), "rôle de la route");
        });

        Test("openapi.json : OpenAPI 3.1, chaque route de l'application y figure avec sa méthode", () =>
        {
            var json = server.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>().Value.SerializerOptions;
            doc = JsonDocument.Parse(OpenApiEndpoints.Build(server.Services.GetRequiredService<EndpointDataSource>(), json).ToJsonString()).RootElement;
            Equal("3.1.0", doc.GetProperty("openapi").GetString(), "version du format");
            True(routes.Count > 100, $"routes lues : {routes.Count}");
            var paths = doc.GetProperty("paths");
            var missing = routes.Where(r => !paths.TryGetProperty(r.Path, out var item) || !item.TryGetProperty(r.Method.ToLowerInvariant(), out _))
                .Select(r => $"{r.Method} {r.Path}").ToList();
            Equal("", string.Join(", ", missing), "routes absentes");
            var described = paths.EnumerateObject().Sum(p => p.Value.EnumerateObject().Count());
            Equal(routes.Count, described, "autant d'opérations que de routes");
        });

        Test("openapi.json : le rôle de chaque opération est celui qu'applique la table des habilitations", () =>
        {
            var wrong = new List<string>();
            foreach (var path in doc.GetProperty("paths").EnumerateObject())
                foreach (var op in path.Value.EnumerateObject())
                {
                    var expected = OpenApiEndpoints.RoleOf(op.Name.ToUpperInvariant(), path.Name);
                    var actual = op.Value.GetProperty(OpenApiEndpoints.RoleExtension).GetString();
                    if (actual != expected) wrong.Add($"{op.Name} {path.Name} : {actual} au lieu de {expected}");
                }
            Equal("", string.Join(", ", wrong), "rôles");
            var ops = doc.GetProperty("paths");
            Equal("administrateur", ops.GetProperty("/api/users").GetProperty("get").GetProperty(OpenApiEndpoints.RoleExtension).GetString(), "comptes");
            Equal(OpenApiEndpoints.Anonymous, ops.GetProperty("/api/auth/login").GetProperty("post").GetProperty(OpenApiEndpoints.RoleExtension).GetString(), "connexion");
            Equal("contributeur", ops.GetProperty("/api/import/drawio").GetProperty("post").GetProperty(OpenApiEndpoints.RoleExtension).GetString(), "dessin");
        });

        Test("openapi.json : paramètres de chemin et de requête, corps typé d'une écriture", () =>
        {
            var paths = doc.GetProperty("paths");
            var withId = paths.EnumerateObject().First(p => p.Name.StartsWith("/api/parcours/{"));
            var get = withId.Value.GetProperty("get");
            var names = get.GetProperty("parameters").EnumerateArray().Select(p => $"{p.GetProperty("in").GetString()}:{p.GetProperty("name").GetString()}").ToList();
            True(names.Contains("path:id") && names.Contains("query:derivees"), "paramètres du parcours : " + string.Join(", ", names));
            True(!names.Any(n => n.Contains("ws") || n.Contains("req")), "ni service ni requête HTTP en paramètre");
            var body = paths.GetProperty("/api/import/catalogue").GetProperty("post").GetProperty("requestBody");
            var schema = body.GetProperty("content").GetProperty("application/json").GetProperty("schema");
#if NET9_0_OR_GREATER
            True(schema.TryGetProperty("properties", out var props) && props.TryGetProperty("decisions", out _), "schéma du corps : " + schema);
#else
            True(schema.ValueKind == JsonValueKind.Object, "corps décrit comme objet JSON sous .NET 8");
#endif
        });
    }
}
