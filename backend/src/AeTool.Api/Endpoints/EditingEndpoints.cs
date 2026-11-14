using AeTool.Core;

namespace AeTool.Api.Endpoints;

/// <summary>Routes d'aide à la saisie : recherche d'un objet à relier, relations qu'un objet peut porter, origine de ses relations.</summary>
public static class EditingEndpoints
{
    private const int DefaultLimit = 10, MaxLimit = 50;

    public static void Map(RouteGroupBuilder api)
    {
        // Recherche par nom, sans accents ni casse, éventuellement limitée à des types d'objets (clés séparées par des virgules).
        api.MapGet("/objects/search", (Workspace ws, string? q, string? types, int? limit) => ws.Read((repo, _) =>
        {
            var query = repo.ActiveObjects;
            if (!string.IsNullOrWhiteSpace(types))
            {
                var keys = types.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet();
                query = query.Where(o => keys.Contains(o.TypeKey));
            }
            var needle = Text.Plain(q).Trim();
            if (needle.Length > 0) query = query.Where(o => Text.Plain(o.Name).Contains(needle));
            // Les noms qui commencent par la saisie passent avant ceux qui la contiennent seulement.
            var found = query.OrderBy(o => Text.Plain(o.Name).StartsWith(needle, StringComparison.Ordinal) ? 0 : 1).ThenBy(o => o.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            var max = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
            return new { Total = found.Count, Items = found.Take(max).Select(o => Dto.Brief(repo, o)) };
        }));

        // Ce que le métamodèle permet à cet objet de porter, par sens : types de relation et types d'objets admis à l'autre bout.
        api.MapGet("/objects/{id}/allowed-relations", (Workspace ws, string id) => ws.Read((repo, _) =>
        {
            if (!repo.Objects.TryGetValue(id, out var o)) return Results.NotFound();
            return Results.Ok(new { Outgoing = RelationRules.Outgoing(repo.Metamodel, o.TypeKey), Incoming = RelationRules.Incoming(repo.Metamodel, o.TypeKey) });
        }));

        // Origine de chaque relation de l'objet : l'interface s'en sert pour ne proposer la modification que des relations saisies dans l'outil.
        api.MapGet("/objects/{id}/relationship-sources", (Workspace ws, string id) => ws.Read((repo, _) =>
        {
            if (!repo.Objects.ContainsKey(id)) return Results.NotFound();
            return Results.Ok(repo.ActiveRelationships.Where(r => r.SourceId == id || r.TargetId == id).ToDictionary(r => r.Id, r => r.Source));
        }));
    }
}
