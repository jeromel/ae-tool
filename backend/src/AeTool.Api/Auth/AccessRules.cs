namespace AeTool.Api.Auth;

/// <summary>
/// Table des habilitations : le rôle minimal exigé pour chaque requête. Une seule fonction pure, appliquée par un
/// middleware, plutôt que des attributs semés sur chaque route : la politique d'accès se relit et se teste en un endroit,
/// et une route ajoutée plus tard est refusée par défaut tant qu'elle n'est pas prévue ici.
/// </summary>
public static class AccessRules
{
    /// <summary>Rôle minimal exigé, ou null si la requête est ouverte à un visiteur non connecté.</summary>
    public static string? MinimumRole(string method, string path)
    {
        var p = Normalize(path);
        // L'interface (fichiers statiques, index.html) reste servie sans session : c'est elle qui affiche la connexion.
        if (p != "/api" && !p.StartsWith("/api/", StringComparison.Ordinal)) return null;

        var read = IsRead(method);
        // Sonde de disponibilité : la supervision n'a pas de session, et la réponse ne dit rien du contenu.
        if (read && p == "/api/health") return null;
        if (IsUnder(p, "/api/auth")) return IsAnonymousAuth(method, p) ? null : Roles.Reader;
        if (IsUnder(p, "/api/users") || IsUnder(p, "/api/admin") || IsUnder(p, "/api/sources")) return Roles.Administrator;
        // L'export JSON contient tout l'état du référentiel, journal compris.
        if (read && p == "/api/export/json") return Roles.Administrator;
        // Le journal dit qui a fait quoi : il se consulte à partir du rôle valideur.
        if (read && IsUnder(p, "/api/journal")) return Roles.Validator;
        if (read) return Roles.Reader;

        if (IsUnder(p, "/api/objects") || IsUnder(p, "/api/relationships") || IsUnder(p, "/api/projects") || IsUnder(p, "/api/portfolio"))
            return Roles.Contributor;
        if (IsUnder(p, "/api/validation")) return Roles.Validator;
        // Un dessin draw.io s'importe dès le rôle contributeur : les objets qu'il crée partent en validation.
        if (IsUnder(p, "/api/import/drawio")) return Roles.Contributor;
        // Réorganiser une vue (déplacer, redimensionner) : contributeur ; cela ne change aucun objet.
        if (IsUnder(p, "/api/diagrams")) return Roles.Contributor;
        // Scénarios et étapes : saisis par les chefs de projet et les architectes, dès le rôle contributeur.
        if (IsUnder(p, "/api/scenarios")) return Roles.Contributor;
        // Métamodèle, imports et tout chemin d'écriture inconnu : refus par défaut, administrateur seulement.
        return Roles.Administrator;
    }

    /// <summary>Requêtes d'authentification accessibles sans session : lire la session, se connecter, premier lancement, défi Entra.</summary>
    private static bool IsAnonymousAuth(string method, string p) =>
        (method.Equals("GET", StringComparison.OrdinalIgnoreCase) && (p == "/api/auth/session" || IsUnder(p, "/api/auth/entra")))
        || (method.Equals("POST", StringComparison.OrdinalIgnoreCase) && (p == "/api/auth/login" || p == "/api/auth/setup"));

    private static bool IsRead(string method) =>
        method.Equals("GET", StringComparison.OrdinalIgnoreCase) || method.Equals("HEAD", StringComparison.OrdinalIgnoreCase);

    /// <summary>Le chemin est le préfixe lui-même ou un de ses sous-chemins (« /api/objectsX » n'est pas sous « /api/objects »).</summary>
    private static bool IsUnder(string path, string prefix) =>
        path == prefix || path.StartsWith(prefix + "/", StringComparison.Ordinal);

    /// <summary>Le routage ignore la casse et le « / » final : la table doit faire de même, sinon une variante contournerait la règle.</summary>
    private static string Normalize(string path)
    {
        var p = path.ToLowerInvariant();
        return p.Length > 1 ? p.TrimEnd('/') : p;
    }
}
