using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AeTool.Api.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
#if NET9_0_OR_GREATER
using System.Text.Json.Schema;
#endif

namespace AeTool.Api.Endpoints;

/// <summary>
/// Description OpenAPI 3.1 de l'API, produite par l'outil à partir de ses propres routes (décision 013), sans paquet :
/// chemins, méthodes, paramètres, corps attendus, rôle minimal lu dans <see cref="AccessRules"/>, et réponse typée quand la
/// route la déclare par <c>Produces&lt;T&gt;()</c>. Une route ajoutée est décrite d'office.
/// </summary>
public static partial class OpenApiEndpoints
{
    /// <summary>Extension OpenAPI qui porte le rôle minimal ; « anonyme » pour une route ouverte sans session.</summary>
    public const string RoleExtension = "x-ae-role";
    public const string Anonymous = "anonyme";

    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/openapi.json", (EndpointDataSource endpoints, IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions> json) =>
            Results.Text(Build(endpoints, json.Value.SerializerOptions).ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
                "application/json; charset=utf-8"));
    }

    /// <summary>Routes de l'API décrites : celles sous /api qui déclarent leurs méthodes HTTP.</summary>
    public static IEnumerable<(string Path, string Method, RouteEndpoint Endpoint)> Routes(EndpointDataSource endpoints) =>
        endpoints.Endpoints.OfType<RouteEndpoint>()
            .Select(e => (e, path: PathOf(e.RoutePattern.RawText), methods: e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods))
            .Where(x => x.methods is { Count: > 0 } && (x.path == "/api" || x.path.StartsWith("/api/", StringComparison.Ordinal)))
            .SelectMany(x => x.methods!.Select(m => (x.path, m.ToUpperInvariant(), x.e)));

    /// <summary>Rôle minimal d'une route, tel que l'applique le serveur : le chemin modèle est rendu concret pour la table.</summary>
    public static string RoleOf(string method, string path) =>
        AccessRules.MinimumRole(method, Parameter().Replace(path, "x")) ?? Anonymous;

    public static JsonObject Build(EndpointDataSource endpoints, JsonSerializerOptions json)
    {
        var paths = new JsonObject();
        foreach (var (path, method, endpoint) in Routes(endpoints).OrderBy(r => r.Path, StringComparer.Ordinal).ThenBy(r => r.Method))
        {
            if (paths[path] is not JsonObject item) paths[path] = item = new JsonObject();
            item[method.ToLowerInvariant()] = Operation(path, method, endpoint, json);
        }
        var version = typeof(OpenApiEndpoints).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        return new JsonObject
        {
            ["openapi"] = "3.1.0",
            ["info"] = new JsonObject
            {
                ["title"] = "ae-tool",
                ["version"] = version,
                ["description"] = "API de l'interface de l'outil. Elle suit les écrans et peut changer avec eux : ce n'est pas une API "
                                  + "publique stable (décision 013, point 5). Chaque opération porte son rôle minimal dans « " + RoleExtension + " ». "
                                  + "Authentification par session (cookie) et jeton CSRF (en-tête X-CSRF-TOKEN) pour les écritures.",
            },
            ["paths"] = paths,
        };
    }

    private static JsonObject Operation(string path, string method, RouteEndpoint endpoint, JsonSerializerOptions json)
    {
        var role = RoleOf(method, path);
        var op = new JsonObject
        {
            ["tags"] = new JsonArray(TagOf(path)),
            ["summary"] = $"{method} {path}",
            ["description"] = role == Anonymous ? "Ouvert sans session." : $"Rôle minimal : {role}.",
            [RoleExtension] = role,
        };

        var parameters = new JsonArray();
        var routeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in endpoint.RoutePattern.Parameters)
        {
            routeNames.Add(p.Name);
            // OpenAPI ne connaît pas de paramètre de chemin facultatif ni attrape-tout : il est décrit comme obligatoire.
            parameters.Add(new JsonObject { ["name"] = p.Name, ["in"] = "path", ["required"] = true, ["schema"] = new JsonObject { ["type"] = "string" } });
        }
        var handler = endpoint.Metadata.OfType<MethodInfo>().FirstOrDefault();
        foreach (var p in handler?.GetParameters() ?? Array.Empty<ParameterInfo>())
        {
            if (p.Name is null || routeNames.Contains(p.Name) || SimpleType(p.ParameterType) is not { } type) continue;
            var nullable = Nullable.GetUnderlyingType(p.ParameterType) is not null || new NullabilityInfoContext().Create(p).ReadState == NullabilityState.Nullable;
            parameters.Add(new JsonObject { ["name"] = p.Name, ["in"] = "query", ["required"] = !nullable && !p.HasDefaultValue, ["schema"] = new JsonObject { ["type"] = type } });
        }
        if (parameters.Count > 0) op["parameters"] = parameters;

        if (endpoint.Metadata.GetMetadata<IAcceptsMetadata>() is { RequestType: { } body } accepts)
            op["requestBody"] = new JsonObject
            {
                ["required"] = !accepts.IsOptional,
                ["content"] = new JsonObject { ["application/json"] = new JsonObject { ["schema"] = Schema(body, json) } },
            };

        var responses = new JsonObject();
        foreach (var produced in endpoint.Metadata.OfType<IProducesResponseTypeMetadata>().Where(m => m.Type is not null && m.Type != typeof(void)))
            responses[produced.StatusCode.ToString()] = new JsonObject
            {
                ["description"] = "Réponse déclarée par la route.",
                ["content"] = new JsonObject { ["application/json"] = new JsonObject { ["schema"] = Schema(produced.Type!, json) } },
            };
        if (!responses.ContainsKey("200"))
            responses["200"] = new JsonObject
            {
                ["description"] = "Réponse non typée : objet JSON (la route ne déclare pas encore son type, décision 013, point 3).",
                ["content"] = new JsonObject { ["application/json"] = new JsonObject { ["schema"] = new JsonObject() } },
            };
        if (role != Anonymous)
        {
            responses["401"] = new JsonObject { ["description"] = "Pas de session." };
            responses["403"] = new JsonObject { ["description"] = $"Rôle insuffisant : {role} au minimum." };
        }
        op["responses"] = responses;
        return op;
    }

    /// <summary>Schéma JSON d'un type, avec les réglages de sérialisation de l'API. Sous .NET 8, « objet JSON » (décision 013).</summary>
    private static JsonNode Schema(Type type, JsonSerializerOptions json)
    {
#if NET9_0_OR_GREATER
        // Le schéma se calcule sur des options munies d'un résolveur de types ; celles de l'API en ont un, une copie nue non.
        if (json.TypeInfoResolver is null) json = new JsonSerializerOptions(json) { TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver() };
        try { return JsonSchemaExporter.GetJsonSchemaAsNode(json, type); }
        catch (Exception e) when (e is InvalidOperationException or NotSupportedException) { return new JsonObject(); }
#else
        return new JsonObject();
#endif
    }

    /// <summary>Type OpenAPI d'un paramètre de requête lié depuis l'adresse ; null pour un service, un corps ou le contexte HTTP.</summary>
    private static string? SimpleType(Type t)
    {
        t = Nullable.GetUnderlyingType(t) ?? t;
        if (t == typeof(string) || t == typeof(Guid) || t == typeof(DateTime) || t == typeof(DateOnly) || t.IsEnum) return "string";
        if (t == typeof(bool)) return "boolean";
        if (t == typeof(int) || t == typeof(long) || t == typeof(short)) return "integer";
        if (t == typeof(double) || t == typeof(decimal) || t == typeof(float)) return "number";
        return null;
    }

    private static string TagOf(string path)
    {
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 1 ? parts[1] : "api";
    }

    /// <summary>Chemin modèle au format OpenAPI : « / » en tête, contraintes et marques retirées (« {id:int} », « {*path} » → « {id} », « {path} »).</summary>
    private static string PathOf(string? raw)
    {
        var p = "/" + (raw ?? "").TrimStart('/');
        return Constraint().Replace(p, m => "{" + m.Groups[1].Value + "}");
    }

    [GeneratedRegex(@"\{\*{0,2}([A-Za-z0-9_]+)[^}]*\}")] private static partial Regex Constraint();
    [GeneratedRegex(@"\{[^}]+\}")] private static partial Regex Parameter();
}
