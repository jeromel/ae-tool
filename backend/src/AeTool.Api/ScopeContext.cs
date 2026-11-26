using System.Security.Claims;
using AeTool.Api.Auth;
using AeTool.Core;

namespace AeTool.Api;

/// <summary>
/// Périmètre de lecture de la requête (décision 012). Posé après les habilitations à partir du compte connecté :
/// au périmètre « métier », <see cref="Workspace.Read{T}"/> sert le référentiel sans l'infrastructure, et les routes qui
/// ne passent pas par cette lecture filtrée sont fermées ici.
/// </summary>
public static class ScopeContext
{
    /// <summary>État de la requête en cours. Un objet, et non une valeur : la lecture filtrée le marque, et le middleware le voit.</summary>
    private sealed class RequestScope { public string? Scope; public bool Filtered; }
    private static readonly AsyncLocal<RequestScope?> State = new();

    /// <summary>« metier » pendant une requête d'un compte à ce périmètre ; null sinon (tout le référentiel).</summary>
    public static string? Current
    {
        get => State.Value?.Scope;
        set { if (State.Value is { } r) r.Scope = value; else State.Value = new RequestScope { Scope = value }; }
    }

    /// <summary>Appelé par la lecture filtrée : la requête en cours a bien lu le référentiel par le filtre.</summary>
    public static void MarkFiltered() { if (State.Value is { } r) r.Filtered = true; }

    /// <summary>
    /// Lectures servies au périmètre « métier » sans passer par la lecture filtrée (chemin de la requête). Un test vérifie
    /// qu'il ne s'agit que de routes qui ne lisent pas le référentiel (session, santé, métamodèle…) : une nouvelle route qui
    /// lirait les données autrement s'y verrait (décision 012, conséquences).
    /// </summary>
    public static System.Collections.Concurrent.ConcurrentDictionary<string, byte> Unfiltered { get; } = new();

    public static bool IsBusiness => Current == ReadScope.Business;

    public const string Closed = "Cette fonction n'est pas dans votre accès en lecture.";

    /// <summary>
    /// Routes fermées au périmètre « métier » : celles qui lisent l'existant sans le filtre (scénarios et trajectoires, par
    /// <c>ReadCurrent</c>), et les exports complets. L'export d'une vue reste ouvert : il passe par la lecture filtrée.
    /// Le journal, les comptes, l'administration et toute écriture sont déjà fermés au lecteur par la table des habilitations.
    /// </summary>
    public static bool IsClosed(string path, IQueryCollection query)
    {
        var p = path.ToLowerInvariant().TrimEnd('/');
        bool Under(string prefix) => p == prefix || p.StartsWith(prefix + "/", StringComparison.Ordinal);
        if (Under("/api/scenarios") || Under("/api/trajectoires")) return true;
        // Comptes rendus d'import : servis tels quels, ils citent des objets (formes d'un dessin, profils de poste codés).
        if (Under("/api/import")) return true;
        if (p.StartsWith("/api/projects/", StringComparison.Ordinal) && p.EndsWith("/scenarios", StringComparison.Ordinal)) return true;
        if (p == "/api/export/archimate") return string.IsNullOrEmpty(query["diagram"]);
        if (Under("/api/export")) return true;
        return false;
    }

    public static async Task Middleware(HttpContext context, RequestDelegate next)
    {
        var request = new RequestScope
        {
            Scope = context.User.Identity?.IsAuthenticated == true && context.User.FindFirstValue(AuthSetup.ReadScopeClaim) == ReadScope.Business
                ? ReadScope.Business : null,
        };
        State.Value = request;
        try
        {
            if (IsBusiness && IsClosed(context.Request.Path.Value ?? "", context.Request.Query))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new { error = Closed });
                return;
            }
            await next(context);
            var path = context.Request.Path.Value ?? "";
            if (request.Scope == ReadScope.Business && !request.Filtered && HttpMethods.IsGet(context.Request.Method)
                && context.Response.StatusCode == StatusCodes.Status200OK && path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
                Unfiltered.TryAdd(path, 0);
        }
        finally { State.Value = null; }
    }
}
