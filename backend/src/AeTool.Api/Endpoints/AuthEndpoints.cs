using System.Security.Claims;
using AeTool.Api.Auth;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;

namespace AeTool.Api.Endpoints;

public sealed record LoginInput(string? Login, string? Password);
public sealed record SetupInput(string? Code, string? Login, string? Name, string? Password);
public sealed record PasswordInput(string? Current, string? Next);

/// <summary>Session, connexion, déconnexion, premier lancement, mot de passe. Les droits sont ceux de AccessRules.</summary>
public static class AuthEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/auth/session", (HttpContext ctx, AuthSettings settings, AuthService auth, IAntiforgery antiforgery) =>
            Describe(ctx, settings, auth, antiforgery));

        api.MapPost("/auth/setup", (SetupInput input, AuthService auth) =>
        {
            var result = auth.Setup(input.Code, input.Login, input.Name, input.Password);
            return result.Ok ? Results.Ok(new { login = result.Value!.Login }) : Fail(result.Failure, result.Error!);
        });

        api.MapPost("/auth/login", async (LoginInput input, HttpContext ctx, AuthSettings settings, AuthService auth, IAntiforgery antiforgery) =>
        {
            if (!settings.IsLocal) return Fail(Failure.Invalid, "La connexion par identifiant et mot de passe n'est pas activée.");
            var result = auth.Login(input.Login, input.Password);
            if (!result.Ok) return Fail(result.Failure, result.Error!);
            var principal = AuthSetup.Principal(result.Value!);
            await ctx.SignInAsync(AuthSetup.CookieScheme, principal, new AuthenticationProperties { IsPersistent = false });
            ctx.User = principal; // pour que le jeton CSRF renvoyé soit celui du compte connecté
            return Results.Ok(Describe(ctx, settings, auth, antiforgery));
        });

        api.MapPost("/auth/logout", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync(AuthSetup.CookieScheme);
            return Results.NoContent();
        });

        api.MapPost("/auth/password", (PasswordInput input, HttpContext ctx, AuthSettings settings, AuthService auth) =>
        {
            if (settings.IsOpen) return Fail(Failure.Invalid, "Aucun compte en mode ouvert.");
            var result = auth.ChangePassword(ctx.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "", input.Current, input.Next);
            return result.Ok ? Results.NoContent() : Fail(result.Failure, result.Error!);
        });

        // Déclenche le défi OAuth vers Microsoft ; le retour est traité par le gestionnaire sur /signin-entra.
        api.MapGet("/auth/entra/login", (AuthSettings settings) => settings.IsEntra
            ? Results.Challenge(new AuthenticationProperties { RedirectUri = "/" }, new[] { AuthSetup.EntraScheme })
            : Results.NotFound(new { error = "La connexion Microsoft n'est pas activée." }));
    }

    /// <summary>État de la session, avec le jeton CSRF à renvoyer dans X-CSRF-TOKEN sur les écritures.</summary>
    private static object Describe(HttpContext ctx, AuthSettings settings, AuthService auth, IAntiforgery antiforgery)
    {
        var user = ctx.User;
        var authenticated = user.Identity?.IsAuthenticated == true;
        return new
        {
            mode = settings.Mode,
            setupRequired = auth.SetupRequired,
            authenticated,
            user = authenticated ? new
            {
                login = user.Identity!.Name,
                name = user.FindFirstValue(AuthSetup.DisplayNameClaim),
                role = user.FindFirstValue(ClaimTypes.Role),
                mustChangePassword = user.HasClaim(AuthSetup.MustChangePasswordClaim, "1"),
                // Affichage seulement : c'est le serveur qui filtre (décision 012, point 3).
                readScope = user.FindFirstValue(AuthSetup.ReadScopeClaim) ?? AeTool.Core.ReadScope.All,
            } : null,
            csrfToken = settings.IsOpen ? null : antiforgery.GetAndStoreTokens(ctx).RequestToken,
        };
    }

    internal static IResult Fail(Failure failure, string message) => Results.Json(new { error = message }, statusCode: failure switch
    {
        Failure.Denied => StatusCodes.Status401Unauthorized,
        Failure.NotFound => StatusCodes.Status404NotFound,
        Failure.Conflict => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status400BadRequest,
    });
}
