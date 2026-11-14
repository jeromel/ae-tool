using AeTool.Core;

namespace AeTool.Api.Endpoints;

/// <summary>Éclairages en lecture seule pour paramétrer le métamodèle sans deviner : ce que contient déjà le référentiel.</summary>
public static class MetamodelInsightEndpoints
{
    public static void Map(RouteGroupBuilder api) =>
        api.MapGet("/metamodel/relationship-types/{key}/observed-pairs", (Workspace ws, string key) => ws.Read((repo, _) =>
            repo.Metamodel.RelType(key) is null ? Results.NotFound() : Results.Ok(ObservedPairs(repo, key))));

    /// <summary>
    /// Couples « type source → type cible » présents dans les relations actives du type, avec leur effectif.
    /// Une relation dont une extrémité est une autre relation (ArchiMate l'autorise) n'a pas de type d'objet à cette extrémité : elle est ignorée.
    /// </summary>
    private static List<ObservedPair> ObservedPairs(Repository repo, string key) =>
        repo.ActiveRelationships.Where(r => r.TypeKey == key)
            .Select(r => (Source: repo.Objects.GetValueOrDefault(r.SourceId), Target: repo.Objects.GetValueOrDefault(r.TargetId)))
            .Where(ends => ends.Source is not null && ends.Target is not null)
            .GroupBy(ends => (Source: ends.Source!.TypeKey, Target: ends.Target!.TypeKey))
            .Select(g => new ObservedPair(g.Key.Source, g.Key.Target, g.Count()))
            .OrderByDescending(p => p.Count).ThenBy(p => p.SourceTypeKey, StringComparer.Ordinal).ThenBy(p => p.TargetTypeKey, StringComparer.Ordinal)
            .ToList();

    private sealed record ObservedPair(string SourceTypeKey, string TargetTypeKey, int Count);
}
