namespace AeTool.Api.Auth;

/// <summary>Modes d'authentification, réglés par la configuration <c>Auth:Mode</c>.</summary>
public static class AuthMode
{
    public const string Local = "local";
    public const string Entra = "entra";
    /// <summary>Aucune authentification : tout le monde est administrateur. Réservé au développement et aux tests.</summary>
    public const string Open = "ouvert";
}

/// <summary>Paramètres de la connexion Microsoft Entra ID.</summary>
public sealed record EntraSettings(string TenantId, string ClientId, string ClientSecret, string DefaultRole, IReadOnlyList<string> Administrators);

/// <summary>Configuration d'authentification, validée une fois au démarrage : une configuration dangereuse empêche de démarrer.</summary>
public sealed record AuthSettings(string Mode, EntraSettings? Entra)
{
    public bool IsLocal => Mode == AuthMode.Local;
    public bool IsEntra => Mode == AuthMode.Entra;
    public bool IsOpen => Mode == AuthMode.Open;

    /// <summary>Lit et valide la configuration. Lève une <see cref="InvalidOperationException"/> au message explicite si elle est inacceptable.</summary>
    public static AuthSettings Load(IConfiguration config, bool isDevelopment)
    {
        var mode = (config["Auth:Mode"] ?? AuthMode.Local).Trim().ToLowerInvariant();
        switch (mode)
        {
            case AuthMode.Local:
                return new AuthSettings(mode, null);
            case AuthMode.Open:
                if (!isDevelopment)
                    throw new InvalidOperationException("Le mode d'authentification « ouvert » n'est accepté qu'en environnement Development. "
                        + "Réglez Auth:Mode sur « local » ou « entra ».");
                return new AuthSettings(mode, null);
            case AuthMode.Entra:
                return new AuthSettings(mode, LoadEntra(config.GetSection("Auth:Entra")));
            default:
                throw new InvalidOperationException($"Auth:Mode « {mode} » inconnu : valeurs acceptées « local », « entra » et « ouvert » (développement).");
        }
    }

    private static EntraSettings LoadEntra(IConfigurationSection section)
    {
        string Required(string key) => string.IsNullOrWhiteSpace(section[key])
            ? throw new InvalidOperationException($"Le mode « entra » exige le paramètre Auth:Entra:{key}.")
            : section[key]!.Trim();
        var defaultRole = (section["DefaultRole"] ?? Roles.Reader).Trim().ToLowerInvariant();
        if (!Roles.IsValid(defaultRole))
            throw new InvalidOperationException($"Auth:Entra:DefaultRole « {defaultRole} » inconnu : valeurs acceptées {string.Join(", ", Roles.All)}.");
        var administrators = section.GetSection("Administrators").GetChildren()
            .Select(c => (c.Value ?? "").Trim().ToLowerInvariant()).Where(v => v.Length > 0).Distinct().ToList();
        return new EntraSettings(Required("TenantId"), Required("ClientId"), Required("ClientSecret"), defaultRole, administrators);
    }
}
