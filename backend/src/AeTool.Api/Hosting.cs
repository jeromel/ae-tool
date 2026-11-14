using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.HttpOverrides;

namespace AeTool.Api;

/// <summary>
/// Réglages d'hébergement, séparés de la composition : mandataire inverse, HTTPS, sonde de disponibilité,
/// et les avertissements de démarrage qui évitent les deux accidents classiques d'une mise en service
/// (mots de passe en clair sur le réseau, données rangées dans le dossier du programme).
/// </summary>
public static class Hosting
{
    private const string TrustedProxies = "Proxy:Trusted";

    /// <summary>Version livrée, lue dans l'assemblage (réglée dans Directory.Build.props).</summary>
    public static string Version { get; } =
        (typeof(Hosting).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0").Split('+')[0];

    /// <summary>
    /// Derrière un mandataire inverse (nginx, IIS hors processus…), l'adresse du client et le protocole d'origine
    /// arrivent dans les en-têtes X-Forwarded-*. Ils ne sont lus que si Proxy:Trusted est renseigné, et crus seulement
    /// quand ils viennent d'une adresse de cette liste ou de la machine elle-même : sans cela, n'importe quel client
    /// pourrait se faire passer pour un autre.
    /// </summary>
    public static void AddProxySupport(this WebApplicationBuilder builder)
    {
        var trusted = ProxyAddresses(builder.Configuration);
        if (trusted.Count == 0) return;
        builder.Services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            foreach (var address in trusted.Where(a => !o.KnownProxies.Contains(a))) o.KnownProxies.Add(address);
        });
    }

    /// <summary>À placer en tête de la chaîne de traitement : le reste doit voir la vraie adresse et le vrai protocole.</summary>
    public static void UseHosting(this WebApplication app, DataPaths paths)
    {
        var behindProxy = ProxyAddresses(app.Configuration).Count > 0;
        if (behindProxy) app.UseForwardedHeaders();
        // L'en-tête HSTS n'est émis que sur une réponse HTTPS : sans effet sur un poste local en HTTP.
        if (!app.Environment.IsDevelopment()) app.UseHsts();
        app.Lifetime.ApplicationStarted.Register(() => Announce(app, paths, behindProxy));
    }

    /// <summary>Sonde de disponibilité pour la supervision : ne dit rien du contenu, ne demande pas de session.</summary>
    public static void MapHealth(RouteGroupBuilder api) =>
        api.MapGet("/health", () => Results.Ok(new { status = "ok", version = Version }));

    private static List<IPAddress> ProxyAddresses(IConfiguration configuration)
    {
        var values = configuration.GetSection(TrustedProxies).Get<string[]>() ?? Array.Empty<string>();
        return values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => IPAddress.TryParse(v.Trim(), out var address)
            ? address
            : throw new InvalidOperationException($"{TrustedProxies} : « {v} » n'est pas une adresse IP.")).ToList();
    }

    private static void Announce(WebApplication app, DataPaths paths, bool behindProxy)
    {
        var log = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Installation");
        log.LogInformation("Version {Version}. Dossier de données : {Directory}", Version, paths.Directory);
        if (app.Environment.IsDevelopment()) return;

        if (IsUnder(paths.Directory, AppContext.BaseDirectory))
            log.LogWarning("Le dossier de données est dans le dossier du programme : une mise à jour ou un nettoyage peut l'effacer. "
                           + "Réglez DataPath vers un dossier à part (voir docs/installation.md).");
        var exposed = app.Urls.Where(IsPlainHttpOnNetwork).ToList();
        if (exposed.Count > 0 && !behindProxy)
            log.LogWarning("Écoute en HTTP sans chiffrement sur {Urls} : mots de passe et sessions circulent en clair. "
                           + "Placez l'outil derrière HTTPS (voir docs/installation.md).", string.Join(", ", exposed));
    }

    private static bool IsUnder(string directory, string parent) =>
        Path.GetFullPath(directory + Path.DirectorySeparatorChar).StartsWith(Path.GetFullPath(parent), StringComparison.OrdinalIgnoreCase);

    /// <summary>Vrai pour une adresse d'écoute en HTTP joignable depuis le réseau (tout sauf la boucle locale).</summary>
    public static bool IsPlainHttpOnNetwork(string url)
    {
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) return false;
        var host = url["http://".Length..].Split('/')[0];
        if (host.StartsWith('[')) host = host[1..host.IndexOf(']')];
        else if (host.Contains(':')) host = host[..host.LastIndexOf(':')];
        return !(host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || (IPAddress.TryParse(host, out var ip) && IPAddress.IsLoopback(ip)));
    }
}
