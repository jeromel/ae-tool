namespace AeTool.Api.Auth;

/// <summary>
/// Les quatre rôles de l'outil, du moins au plus habilité. La hiérarchie est stricte : chaque rôle
/// peut tout ce que peut le rôle précédent (exigence EF-50).
/// </summary>
public static class Roles
{
    public const string Reader = "lecteur";
    public const string Contributor = "contributeur";
    public const string Validator = "valideur";
    public const string Administrator = "administrateur";

    /// <summary>Ordre croissant d'habilitation.</summary>
    public static readonly IReadOnlyList<string> All = new[] { Reader, Contributor, Validator, Administrator };

    public static bool IsValid(string? role) => role is not null && All.Contains(role);

    /// <summary>Vrai si <paramref name="role"/> vaut au moins <paramref name="minimum"/>. Un rôle inconnu ne vaut rien.</summary>
    public static bool AtLeast(string? role, string minimum)
    {
        var rank = role is null ? -1 : IndexOf(role);
        return rank >= 0 && rank >= IndexOf(minimum);
    }

    private static int IndexOf(string role)
    {
        for (var i = 0; i < All.Count; i++)
            if (All[i] == role) return i;
        return -1;
    }
}
