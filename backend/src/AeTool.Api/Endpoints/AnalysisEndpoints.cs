using System.Text;
using System.Text.Json;
using AeTool.Connectors;
using AeTool.Core;
using static AeTool.Api.Http;

namespace AeTool.Api.Endpoints;

/// <summary>Analyse d'impact, voisinage et matrices croisées.</summary>
public static class AnalysisEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/analysis/impact/{id}", (Workspace ws, string id, int? depth, string? direction) => ws.Read((repo, an) =>
            repo.Objects.ContainsKey(id) ? Results.Ok(an.Impact(id, Math.Clamp(depth ?? 2, 1, 6), direction ?? "tous")) : Results.NotFound()));

        api.MapGet("/analysis/graph/{id}", (Workspace ws, string id, int? depth) => ws.Read((repo, an) =>
            repo.Objects.ContainsKey(id) ? Results.Ok(an.Neighborhood(id, Math.Clamp(depth ?? 1, 1, 4))) : Results.NotFound()));

        api.MapGet("/analysis/matrix", (Workspace ws, string rows, string cols, bool? nesting, string? relation) => ws.Read((_, an) =>
            an.CrossMatrix(rows, cols, nesting ?? true, string.IsNullOrWhiteSpace(relation) ? null : relation)));
    }
}
