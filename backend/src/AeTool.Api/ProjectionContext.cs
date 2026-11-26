using System.Security.Claims;
using AeTool.Api.Auth;
using AeTool.Core;

namespace AeTool.Api;

/// <summary>État projeté demandé par la requête en cours : scénario et étape, lus dans le paramètre « etat » (scenario:etape).</summary>
public sealed record ProjectionState(string ScenarioId, string StepId);

public static class ProjectionContext
{
    private static readonly AsyncLocal<ProjectionState?> State = new();
    public static ProjectionState? Current { get => State.Value; set => State.Value = value; }

    /// <summary>Routes qui portent toujours sur l'existant, même avec un état demandé.</summary>
    private static readonly string[] Ignored =
    {
        "/api/scenarios", "/api/trajectoires", "/api/auth", "/api/users", "/api/admin", "/api/journal",
        "/api/metamodel", "/api/import", "/api/export", "/api/sources", "/api/validation", "/api/health", "/api/openapi.json",
    };

    /// <summary>
    /// Pose l'état projeté pour la durée de la requête. Un état projeté est en lecture seule : toute écriture qui en demande
    /// un est refusée. Un lecteur ne voit pas les scénarios en brouillon.
    /// </summary>
    public static async Task Middleware(HttpContext context, RequestDelegate next)
    {
        var path = context.Request.Path.Value ?? "";
        var raw = context.Request.Query["etat"].ToString();
        if (raw.Length == 0 || !path.StartsWith("/api/", StringComparison.Ordinal) || Ignored.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        {
            await next(context);
            return;
        }
        if (!HttpMethods.IsGet(context.Request.Method))
        {
            await Reply(context, StatusCodes.Status409Conflict, "Vous regardez un état projeté du SI, en lecture seule : revenez à l'état actuel pour modifier.");
            return;
        }
        var parts = raw.Split(':', 2);
        if (parts.Length != 2) { await Reply(context, StatusCodes.Status400BadRequest, "État demandé illisible."); return; }
        var ws = context.RequestServices.GetRequiredService<Workspace>();
        var role = context.User.FindFirstValue(ClaimTypes.Role);
        var visible = ws.ReadCurrent((repo, _) =>
            repo.Scenarios.TryGetValue(parts[0], out var s) && s.Steps.Any(x => x.Id == parts[1])
            && (s.Status != ScenarioStatus.Draft || AeTool.Api.Auth.Roles.AtLeast(role, AeTool.Api.Auth.Roles.Contributor)));
        if (!visible) { await Reply(context, StatusCodes.Status404NotFound, "Cet état n'existe plus ou ne vous est pas visible."); return; }
        Current = new ProjectionState(parts[0], parts[1]);
        try { await next(context); }
        finally { Current = null; }
    }

    private static Task Reply(HttpContext context, int status, string message)
    {
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(new { error = message });
    }
}
