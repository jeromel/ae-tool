namespace AeTool.Api.Endpoints;

/// <summary>Opérations d'administration.</summary>
public static class AdminEndpoints
{
    /// <summary>Routes réservées à l'environnement de développement.</summary>
    public static void MapDevelopment(RouteGroupBuilder api)
    {
        // Remise à zéro du référentiel.
        api.MapPost("/admin/reset", (Workspace ws) => { ws.Reset(); return Results.NoContent(); });
    }
}
