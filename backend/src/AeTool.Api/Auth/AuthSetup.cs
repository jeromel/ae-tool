using System.Net.Http.Headers;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.RateLimiting;

namespace AeTool.Api.Auth;

/// <summary>
/// Enregistrement des services et de la chaîne de traitement d'authentification : en-têtes de sécurité, limitation de débit,
/// session par cookie (ou Entra ID), habilitations, protection CSRF. Appelé depuis AppHost.
/// </summary>
public static class AuthSetup
{
    public const string CookieScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    public const string EntraScheme = "entra";
    public const string CookieName = "ae.session";
    public const string CsrfHeader = "X-CSRF-TOKEN";
    public const string DisplayNameClaim = "ae:name";
    public const string MustChangePasswordClaim = "ae:mcp";
    public const string OpenLogin = "anonyme";
    private const long SmallBodyLimit = 16 * 1024;

    public static void AddAeAuthentication(this WebApplicationBuilder builder, DataPaths paths)
    {
        // La configuration est validée d'abord : un mode inacceptable (ouvert hors développement, Entra incomplet) empêche de démarrer.
        var settings = AuthSettings.Load(builder.Configuration, builder.Environment.IsDevelopment());
        var services = builder.Services;
        services.AddSingleton(settings);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IUserStore>(new JsonUserStore(paths.File("utilisateurs.json")));
        services.AddSingleton(sp =>
        {
            var log = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Installation");
            return new SetupCode(paths.File("code-installation.txt"), message => log.LogWarning("{Message}", message));
        });
        services.AddSingleton<IAuditLog, WorkspaceAuditLog>();
        services.AddSingleton<AuthService>();

        AddKeyStorage(services, paths);
        services.AddAntiforgery(o =>
        {
            o.HeaderName = CsrfHeader;
            o.Cookie.Name = "ae.csrf";
            o.Cookie.HttpOnly = true;
            o.Cookie.SameSite = SameSiteMode.Strict;
            o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            o.SuppressXFrameOptionsHeader = true; // X-Frame-Options est posé par nos en-têtes de sécurité (DENY).
        });
        AddAuthentication(services, settings);
        AddRateLimit(services);
    }

    public static void UseAeAuthentication(this WebApplication app)
    {
        var settings = app.Services.GetRequiredService<AuthSettings>();
        if (settings.IsOpen) app.Logger.LogWarning("Mode ouvert : aucune authentification, tout le monde est administrateur.");
        var users = app.Services.GetRequiredService<IUserStore>();
        if (settings.IsLocal && users.Count == 0) app.Services.GetRequiredService<SetupCode>().Issue();
        if (settings.IsEntra && !settings.Entra!.Administrators.Any() && !users.All().Any(u => u.Role == Roles.Administrator))
            app.Logger.LogWarning("Mode Entra : aucun administrateur configuré (Auth:Entra:Administrators) ni existant ; personne ne pourra administrer l'outil.");

        app.Use(SecurityHeaders);
        app.UseRateLimiter();
        app.UseAuthentication();
        app.Use(async (context, next) =>
        {
            if (settings.IsOpen) context.User = OpenPrincipal();
            await next();
        });
        app.Use(Authorize);
        app.Use(AntiforgeryCheck);
    }

    /// <summary>Identité portée au journal et rôles lus dans la session : l'état à jour du compte, pas une copie figée dans le cookie.</summary>
    public static ClaimsPrincipal Principal(UserAccount account)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, account.Id), new(ClaimTypes.Name, account.Login),
            new(ClaimTypes.Role, account.Role), new(DisplayNameClaim, account.DisplayName),
        };
        if (account.MustChangePassword) claims.Add(new Claim(MustChangePasswordClaim, "1"));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "ae", ClaimTypes.Name, ClaimTypes.Role));
    }

    private static ClaimsPrincipal OpenPrincipal() => Principal(new UserAccount
    {
        Id = "ouvert", Login = OpenLogin, DisplayName = "Anonyme", Role = Roles.Administrator,
    });

    // ---- Services ----------------------------------------------------------------------------------------------------

    private static void AddKeyStorage(IServiceCollection services, DataPaths paths)
    {
        // Les clés qui chiffrent le cookie de session et les jetons CSRF vivent dans le dossier de données :
        // sans cela, chaque redémarrage déconnecterait tout le monde.
        var directory = Path.Combine(paths.Directory, "cles-protection");
        Directory.CreateDirectory(directory);
        var builder = services.AddDataProtection().SetApplicationName("ae-tool").PersistKeysToFileSystem(new DirectoryInfo(directory));
        if (OperatingSystem.IsWindows()) builder.ProtectKeysWithDpapi();
        else File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private static void AddAuthentication(IServiceCollection services, AuthSettings settings)
    {
        var auth = services.AddAuthentication(CookieScheme);
        auth.AddCookie(CookieScheme, o =>
        {
            o.Cookie.Name = CookieName;
            o.Cookie.HttpOnly = true;
            o.Cookie.SameSite = SameSiteMode.Strict;
            o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            o.ExpireTimeSpan = TimeSpan.FromHours(8);
            o.SlidingExpiration = true;
            // L'API ne redirige jamais : 401 (non connecté) ou 403 (rôle insuffisant) en JSON.
            o.Events.OnRedirectToLogin = c => Reply(c.HttpContext, StatusCodes.Status401Unauthorized, "Authentification requise.");
            o.Events.OnRedirectToAccessDenied = c => Reply(c.HttpContext, StatusCodes.Status403Forbidden, DeniedMessage);
            o.Events.OnValidatePrincipal = RefreshFromAccount;
        });
        if (settings.Entra is { } entra) auth.AddOAuth(EntraScheme, o => ConfigureEntra(o, entra));
    }

    /// <summary>
    /// À chaque requête, le compte est relu : un changement de rôle ou une désactivation prend effet tout de suite,
    /// sans attendre l'expiration du cookie.
    /// </summary>
    private static async Task RefreshFromAccount(CookieValidatePrincipalContext context)
    {
        var id = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        var account = id is null ? null : context.HttpContext.RequestServices.GetRequiredService<IUserStore>().Find(u => u.Id == id);
        if (account is null || account.Disabled)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieScheme);
            return;
        }
        context.ReplacePrincipal(Principal(account));
    }

    /// <summary>
    /// Flux « authorization code » avec PKCE par le gestionnaire OAuth générique (les paquets OpenIdConnect et JwtBearer
    /// ne sont pas disponibles). L'identité est lue sur Microsoft Graph avec le jeton d'accès, qui n'est pas conservé.
    /// </summary>
    private static void ConfigureEntra(Microsoft.AspNetCore.Authentication.OAuth.OAuthOptions o, EntraSettings entra)
    {
        var tenant = Uri.EscapeDataString(entra.TenantId);
        o.SignInScheme = CookieScheme;
        o.ClientId = entra.ClientId;
        o.ClientSecret = entra.ClientSecret;
        o.AuthorizationEndpoint = $"https://login.microsoftonline.com/{tenant}/oauth2/v2.0/authorize";
        o.TokenEndpoint = $"https://login.microsoftonline.com/{tenant}/oauth2/v2.0/token";
        o.UserInformationEndpoint = "https://graph.microsoft.com/v1.0/me?$select=id,displayName,mail,userPrincipalName";
        o.CallbackPath = "/signin-entra";
        o.UsePkce = true;
        o.SaveTokens = false;
        foreach (var scope in new[] { "openid", "profile", "User.Read" }) o.Scope.Add(scope);
        o.Events.OnCreatingTicket = async context =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, context.Options.UserInformationEndpoint);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", context.AccessToken);
            using var response = await context.Backchannel.SendAsync(request, context.HttpContext.RequestAborted);
            response.EnsureSuccessStatusCode();
            var profile = EntraDirectory.ParseGraph(await response.Content.ReadAsStringAsync(context.HttpContext.RequestAborted))
                ?? throw new InvalidOperationException("Profil Microsoft Graph inexploitable.");
            var signedIn = context.HttpContext.RequestServices.GetRequiredService<AuthService>().SignInEntra(profile);
            context.Principal = signedIn.Ok ? Principal(signedIn.Value!) : throw new InvalidOperationException(signedIn.Error);
        };
        // La durée de vie de la session est celle du cookie (8 h glissantes), pas celle du jeton Microsoft.
        o.Events.OnTicketReceived = context =>
        {
            context.Properties!.ExpiresUtc = null;
            context.Properties.IsPersistent = false;
            return Task.CompletedTask;
        };
        o.Events.OnRemoteFailure = context =>
        {
            context.Response.Redirect("/connexion?erreur=1");
            context.HandleResponse();
            return Task.CompletedTask;
        };
    }

    /// <summary>10 tentatives par minute et par adresse sur la connexion et le premier lancement ; le reste n'est pas limité.</summary>
    private static void AddRateLimit(IServiceCollection services) => services.AddRateLimiter(o =>
    {
        o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        o.OnRejected = async (context, _) => await Reply(context.HttpContext, StatusCodes.Status429TooManyRequests, "Trop de tentatives : réessayez dans une minute.");
        o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        {
            var path = context.Request.Path.Value?.TrimEnd('/').ToLowerInvariant();
            if (path is not ("/api/auth/login" or "/api/auth/setup")) return RateLimitPartition.GetNoLimiter("libre");
            return RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "inconnue",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });
        });
    });

    // ---- Chaîne de traitement ----------------------------------------------------------------------------------------

    private const string DeniedMessage = "Votre rôle ne permet pas cette action.";

    private static Task SecurityHeaders(HttpContext context, RequestDelegate next)
    {
        var h = context.Response.Headers;
        h["X-Content-Type-Options"] = "nosniff";
        h["Referrer-Policy"] = "same-origin";
        h["X-Frame-Options"] = "DENY";
        h["Content-Security-Policy"] = "default-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; frame-ancestors 'none'";
        return next(context);
    }

    /// <summary>Applique la table des habilitations : 401 sans session, 403 si le rôle est insuffisant.</summary>
    private static async Task Authorize(HttpContext context, RequestDelegate next)
    {
        var path = context.Request.Path.Value ?? "/";
        var minimum = AccessRules.MinimumRole(context.Request.Method, path);
        LimitBody(context, path);
        if (minimum is null) { await next(context); return; }
        if (context.User.Identity?.IsAuthenticated != true) { await Reply(context, StatusCodes.Status401Unauthorized, "Authentification requise."); return; }
        // Tant que le mot de passe temporaire n'est pas changé, seules les routes d'authentification répondent.
        if (context.User.HasClaim(MustChangePasswordClaim, "1") && !path.StartsWith("/api/auth/", StringComparison.OrdinalIgnoreCase))
        {
            await Reply(context, StatusCodes.Status403Forbidden, "Vous devez d'abord changer votre mot de passe temporaire.");
            return;
        }
        if (!Roles.AtLeast(context.User.FindFirstValue(ClaimTypes.Role), minimum)) { await Reply(context, StatusCodes.Status403Forbidden, DeniedMessage); return; }
        await next(context);
    }

    /// <summary>Les routes de comptes ne reçoivent que de petits corps JSON : on refuse le reste avant de le lire.</summary>
    private static void LimitBody(HttpContext context, string path)
    {
        var small = path.StartsWith("/api/auth", StringComparison.OrdinalIgnoreCase) || path.StartsWith("/api/users", StringComparison.OrdinalIgnoreCase);
        var feature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (small && feature is { IsReadOnly: false }) feature.MaxRequestBodySize = SmallBodyLimit;
    }

    /// <summary>
    /// Jeton CSRF exigé sur toute écriture de /api. Non exigé en mode ouvert : il n'y a pas de session à détourner
    /// (développement seulement).
    /// </summary>
    private static async Task AntiforgeryCheck(HttpContext context, RequestDelegate next)
    {
        var settings = context.RequestServices.GetRequiredService<AuthSettings>();
        var method = context.Request.Method;
        var write = method is "POST" or "PUT" or "DELETE" or "PATCH";
        var api = context.Request.Path.StartsWithSegments("/api");
        if (settings.IsOpen || !write || !api) { await next(context); return; }
        try
        {
            await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context);
        }
        catch (AntiforgeryValidationException)
        {
            await Reply(context, StatusCodes.Status400BadRequest, "Jeton de protection absent ou invalide : rechargez la page.");
            return;
        }
        await next(context);
    }

    private static Task Reply(HttpContext context, int status, string message)
    {
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(new { error = message });
    }
}
