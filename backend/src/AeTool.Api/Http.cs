namespace AeTool.Api;

/// <summary>Accès communs à la requête HTTP : auteur de l'action (pour le journal) et corps brut (fichiers envoyés).</summary>
public static class Http
{
    /// <summary>
    /// Identifiant de l'utilisateur authentifié, porté au journal. En mode ouvert (développement), c'est « anonyme ».
    /// </summary>
    public static string User(HttpRequest request) =>
        request.HttpContext.User.Identity is { IsAuthenticated: true, Name: { Length: > 0 } name } ? name : "anonyme";

    public static async Task<MemoryStream> Body(HttpRequest request)
    {
        var ms = new MemoryStream();
        await request.Body.CopyToAsync(ms);
        ms.Position = 0;
        return ms;
    }
}
