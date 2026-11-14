namespace AeTool.Api;

/// <summary>Accès communs à la requête HTTP : auteur de l'action (pour le journal) et corps brut (fichiers envoyés).</summary>
public static class Http
{
    /// <summary>
    /// Identité de l'auteur de la requête, portée au journal. Tant que l'authentification n'est pas branchée,
    /// elle vient de l'en-tête X-User ; c'est le seul endroit à modifier quand elle le sera.
    /// </summary>
    public static string User(HttpRequest request) =>
        request.Headers.TryGetValue("X-User", out var u) && !string.IsNullOrWhiteSpace(u) ? u.ToString() : "anonyme";

    public static async Task<MemoryStream> Body(HttpRequest request)
    {
        var ms = new MemoryStream();
        await request.Body.CopyToAsync(ms);
        ms.Position = 0;
        return ms;
    }
}
