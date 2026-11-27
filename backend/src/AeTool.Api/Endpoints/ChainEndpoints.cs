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
        api.MapGet("/chaines", (Workspace ws) => ws.Read((repo, _) =>
        {
            var g = new StepGrid(repo);
            return new
            {
                g.ServersAvailable, g.NatureKnown, Layers = StepGrid.LayerKeys.Where(k => k != StepGrid.Servers || g.ServersAvailable),
                g.Chains, Processes = g.ProcessViews, g.OffChain,
            };
        }));

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
