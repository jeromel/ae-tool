namespace AeTool.Api.Auth;

/// <summary>Projection d'un compte pour l'administration. Sans hachage ni compteur interne : rien de secret ne sort de l'API.</summary>
public sealed record UserView(string Id, string Login, string Name, string Role, bool Disabled, string Origin,
    DateTime CreatedAt, DateTime? LastLoginAt, DateTime? LockedUntil, bool MustChangePassword)
{
    public static UserView Of(UserAccount u, DateTime now) => new(u.Id, u.Login, u.DisplayName, u.Role, u.Disabled, u.Origin,
        u.CreatedAt, u.LastLoginAt, u.LockedUntil > now ? u.LockedUntil : null, u.MustChangePassword);
}
