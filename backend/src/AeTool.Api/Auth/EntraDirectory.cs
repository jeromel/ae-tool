using System.Text.Json;

namespace AeTool.Api.Auth;

/// <summary>Identité lue dans la réponse Microsoft Graph « /me ».</summary>
public sealed record EntraProfile(string Login, string DisplayName);

/// <summary>
/// Transformations pures du mode Entra ID : réponse Graph vers profil, profil vers compte. Isolées du flux OAuth
/// pour être testables sans annuaire.
/// </summary>
public static class EntraDirectory
{
    /// <summary>
    /// Lit la réponse de GET /v1.0/me?$select=id,displayName,mail,userPrincipalName. L'identifiant de l'outil est le
    /// userPrincipalName en minuscules. Renvoie null si la réponse n'est pas exploitable (pas de JSON, pas d'identifiant).
    /// </summary>
    public static EntraProfile? ParseGraph(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            var login = Text(doc.RootElement, "userPrincipalName")?.ToLowerInvariant();
            if (string.IsNullOrEmpty(login) || login.Length > 200) return null;
            var name = Text(doc.RootElement, "displayName") ?? login;
            return new EntraProfile(login, name.Length > 100 ? name[..100] : name);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Compte créé à la première connexion. Les identifiants listés dans la configuration deviennent administrateurs ;
    /// tous les autres reçoivent le rôle par défaut. Ensuite, le rôle ne se change plus que dans l'outil.
    /// </summary>
    public static UserAccount NewAccount(EntraProfile profile, EntraSettings settings, DateTime now) => new()
    {
        Id = "u-" + Guid.NewGuid().ToString("N"),
        Login = profile.Login,
        DisplayName = profile.DisplayName,
        Role = settings.Administrators.Contains(profile.Login) ? Roles.Administrator : settings.DefaultRole,
        CreatedAt = now,
        Origin = AccountOrigin.Entra,
    };

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString())
            ? v.GetString()!.Trim()
            : null;
}
