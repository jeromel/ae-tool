using AeTool.Core;

namespace AeTool.Api.Endpoints;

/// <summary>
/// EV-06 « Chaînes et processus » : la grille étape × couche, et les étapes touchées par un objet (index inverse de la grille).
/// Lue par `Workspace.Read` : le lecteur « métier » reçoit la grille sans couche serveurs (décision 012, C9).
/// </summary>
public static class ChainEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/chaines", (Workspace ws) => ws.Read((repo, an) =>
        {
            var g = new StepGrid(repo);
            // Domaines (charte §9) : les capacités de premier niveau, dans l'ordre de la carte des capacités ; chaque capacité
            // d'une cellule dit son domaine, l'écran en tire la couleur.
            var domains = new SpaceReader(repo, an).CapabilityMap().Select(n => new { n.Id, n.Name }).ToList();
            var caps = g.Chains.SelectMany(c => c.Columns.Concat(c.Whole is null ? Array.Empty<StepColumn>() : new[] { c.Whole }))
                .SelectMany(col => col.Layers.Values.SelectMany(l => l)).Select(i => i.Capability).OfType<string>().Distinct();
            return new
            {
                g.ServersAvailable, g.NatureKnown, Layers = StepGrid.LayerKeys.Where(k => k != StepGrid.Servers || g.ServersAvailable),
                Domains = domains, DomainOf = caps.ToDictionary(c => c, g.Top),
                g.Chains, Processes = g.ProcessViews, g.OffChain,
            };
        }));

        // Un processus sur son propre écran (revue de Camille §4.5) : ses étapes, ou une colonne, et où il se place.
        api.MapGet("/processus/{id}", (Workspace ws, string id) => ws.Read((repo, _) =>
            new StepGrid(repo).Process(id) is { } p ? Results.Ok(p) : Results.NotFound()));

        api.MapGet("/chaines/impact/{id}", (Workspace ws, string id) => ws.Read((repo, _) =>
        {
            if (!repo.Objects.TryGetValue(id, out var o) || o.Archived) return Results.NotFound();
            var g = new StepGrid(repo);
            return Results.Ok(new
            {
                Object = Dto.Brief(repo, o), Layer = g.ImpactLayer(id),
                Steps = g.Touched(id), OffChain = g.TouchedOffChain(id),
            });
        }));
    }
}
