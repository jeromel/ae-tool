using AeTool.Api.Auth;
using static AeTool.Api.Http;

namespace AeTool.Api.Endpoints;

public sealed record NewUserInput(string? Login, string? Name, string? Role, string? ReadScope = null);
public sealed record UserUpdateInput(string? Name, string? Role, bool Disabled, string? ReadScope = null);

/// <summary>Administration des comptes (rôle administrateur, imposé par AccessRules). Aucune réponse ne contient de hachage.</summary>
public static class UserEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/users", (AuthService auth, TimeProvider clock) =>
            auth.List().Select(u => UserView.Of(u, clock.GetUtcNow().UtcDateTime)));

        api.MapPost("/users", (NewUserInput input, HttpRequest req, AuthService auth, TimeProvider clock) =>
        {
            var result = auth.Create(User(req), input.Login, input.Name, input.Role, input.ReadScope);
            if (!result.Ok) return AuthEndpoints.Fail(result.Failure, result.Error!);
            var view = UserView.Of(result.Value!.User, clock.GetUtcNow().UtcDateTime);
            // Le mot de passe temporaire n'est renvoyé que cette fois : il n'est stocké que haché.
            return Results.Json(new { user = view, temporaryPassword = result.Value.TemporaryPassword }, statusCode: StatusCodes.Status201Created);
        });

        api.MapPut("/users/{id}", (string id, UserUpdateInput input, HttpRequest req, AuthService auth, TimeProvider clock) =>
        {
            var result = auth.Update(User(req), id, input.Name, input.Role, input.Disabled, input.ReadScope);
            return result.Ok ? Results.Ok(UserView.Of(result.Value!, clock.GetUtcNow().UtcDateTime)) : AuthEndpoints.Fail(result.Failure, result.Error!);
        });

        api.MapPost("/users/{id}/reset-password", (string id, HttpRequest req, AuthService auth) =>
        {
            var result = auth.ResetPassword(User(req), id);
            return result.Ok ? Results.Ok(new { temporaryPassword = result.Value }) : AuthEndpoints.Fail(result.Failure, result.Error!);
        });
    }
}
