using System.Text;
using System.Text.Json;
using AeTool.Connectors;
using AeTool.Core;
using static AeTool.Api.Http;

namespace AeTool.Api.Endpoints;

/// <summary>Synthèse du référentiel, qualité et journal.</summary>
public static class SummaryEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/summary", (Workspace ws) => ws.Read((repo, an) => new
        {
            repo.ModelName,
            Objects = repo.ActiveObjects.Count(),
            Relationships = repo.ActiveRelationships.Count(),
            Diagrams = repo.ActiveDiagrams.Count(),
            Projects = an.Projects().Count(),
            Applications = repo.Assessments.Count,
            Layers = repo.Metamodel.Layers.OrderBy(l => l.Order).Select(l => new
            {
                l.Key, l.Label,
                Count = repo.ActiveObjects.Count(o => repo.Metamodel.LayerOf(o.TypeKey) == l.Key),
                Types = repo.ActiveObjects.Where(o => repo.Metamodel.LayerOf(o.TypeKey) == l.Key).GroupBy(o => o.TypeKey)
                    .Select(g => new { Key = g.Key, Label = repo.Metamodel.Type(g.Key)?.Label ?? g.Key, Count = g.Count() })
                    .OrderByDescending(t => t.Count),
            }),
            Journal = repo.Journal.AsEnumerable().Reverse().Take(8),
        }));

        api.MapGet("/quality", (Workspace ws) => ws.Read((_, an) => an.Quality()));
        api.MapGet("/journal", (Workspace ws, int? limit) => ws.Read((repo, _) => repo.Journal.AsEnumerable().Reverse().Take(limit ?? 200).ToList()));
    }
}
