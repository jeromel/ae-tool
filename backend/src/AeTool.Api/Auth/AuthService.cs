using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;

namespace AeTool.Api.Auth;

public enum Failure { None, Invalid, Denied, NotFound, Conflict }

/// <summary>Résultat d'une opération de compte : une valeur, ou un échec avec un message destiné à l'utilisateur.</summary>
public sealed record Result<T>(T? Value, Failure Failure = Failure.None, string? Error = null)
{
    public bool Ok => Failure == Failure.None;
    public static Result<T> Success(T value) => new(value);
    public static Result<T> Fail(Failure failure, string error) => new(default, failure, error);
}

/// <summary>Un compte créé et, en mode local, son mot de passe temporaire (montré une seule fois).</summary>
public sealed record CreatedAccount(UserAccount User, string? TemporaryPassword);

/// <summary>Journal des actions sur les comptes. Implémenté par le journal du référentiel ; remplaçable dans les tests.</summary>
public interface IAuditLog
{
    void Record(string user, string action, string? targetId = null, string? detail = null);
}

public sealed class WorkspaceAuditLog : IAuditLog
{
    private readonly Workspace _ws;
    public WorkspaceAuditLog(Workspace ws) => _ws = ws;
    public void Record(string user, string action, string? targetId = null, string? detail = null) =>
        _ws.Write(repo => { repo.Log(user, action, targetId, detail); return 0; });
}

/// <summary>
/// Règles de gestion des comptes : premier lancement, connexion, verrouillage, mots de passe, rôles.
/// Aucune dépendance HTTP. Aucun mot de passe ni hachage dans les messages, les résultats exposés ou le journal.
/// </summary>
public sealed class AuthService
{
    public const string LoginFailure = "Identifiant ou mot de passe incorrect.";
    public const int MaxFailures = 5;
    public static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(15);
    private const int MinPasswordLength = 12;
    private const int MaxPasswordLength = 128;
    private const string TemporaryAlphabet = "abcdefghijkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private static readonly Regex LoginPattern = new("^[a-z0-9._@+-]{3,100}$", RegexOptions.Compiled);

    private readonly IUserStore _users;
    private readonly SetupCode _setup;
    private readonly IAuditLog _audit;
    private readonly AuthSettings _settings;
    private readonly TimeProvider _clock;
    private readonly PasswordHasher<UserAccount> _hasher = new();
    // Haché une fois : sert à dépenser le même temps de calcul quand le compte n'existe pas.
    private readonly string _dummyHash;

    public AuthService(IUserStore users, SetupCode setup, IAuditLog audit, AuthSettings settings, TimeProvider clock)
    {
        (_users, _setup, _audit, _settings, _clock) = (users, setup, audit, settings, clock);
        _dummyHash = _hasher.HashPassword(new UserAccount(), RandomPassword());
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    public bool SetupRequired => _settings.IsLocal && _users.Count == 0;

    public static string NormalizeLogin(string? login) => (login ?? "").Trim().ToLowerInvariant();

    // ---- Premier lancement -------------------------------------------------------------------------------------------

    public Result<UserAccount> Setup(string? code, string? login, string? name, string? password)
    {
        if (!_settings.IsLocal) return Result<UserAccount>.Fail(Failure.Invalid, "Le premier lancement n'existe qu'en mode local.");
        var result = _users.Write(list =>
        {
            if (list.Count > 0) return Result<UserAccount>.Fail(Failure.Conflict, "Le premier lancement a déjà été effectué.");
            if (!_setup.Matches(code)) return Result<UserAccount>.Fail(Failure.Invalid, "Code d'installation incorrect.");
            var invalid = ValidateNewAccount(list, login, name) ?? PasswordProblem(password, NormalizeLogin(login));
            if (invalid is not null) return Result<UserAccount>.Fail(Failure.Invalid, invalid);
            var account = NewLocal(NormalizeLogin(login), name!, Roles.Administrator, password!, mustChange: false);
            list.Add(account);
            return Result<UserAccount>.Success(account);
        });
        if (!result.Ok) return result;
        _setup.Revoke();
        _audit.Record(result.Value!.Login, "Premier administrateur créé", result.Value.Id);
        return result;
    }

    // ---- Connexion ---------------------------------------------------------------------------------------------------

    /// <summary>Un seul message d'échec, que le compte existe, soit verrouillé, désactivé ou n'existe pas : on ne révèle pas les comptes.</summary>
    public Result<UserAccount> Login(string? login, string? password)
    {
        var id = NormalizeLogin(login);
        var found = _users.Find(u => u.Login == id);
        if (found is null || found.Origin != AccountOrigin.Local || found.PasswordHash is null)
        {
            _hasher.VerifyHashedPassword(new UserAccount(), _dummyHash, password ?? "");
            return Result<UserAccount>.Fail(Failure.Denied, LoginFailure);
        }
        var outcome = _users.Write(list =>
        {
            var account = list.First(u => u.Id == found.Id);
            var now = Now;
            if (account.LockedUntil > now) return Refused("compte verrouillé");
            var check = _hasher.VerifyHashedPassword(account, account.PasswordHash!, password ?? "");
            if (check == PasswordVerificationResult.Failed)
            {
                account.FailedAttempts++;
                if (account.FailedAttempts >= MaxFailures)
                {
                    account.LockedUntil = now + LockDuration;
                    account.FailedAttempts = 0;
                }
                return Refused("mot de passe incorrect");
            }
            if (account.Disabled) return Refused("compte désactivé");
            if (check == PasswordVerificationResult.SuccessRehashNeeded) account.PasswordHash = _hasher.HashPassword(account, password!);
            account.FailedAttempts = 0;
            account.LockedUntil = null;
            account.LastLoginAt = now;
            return (Reason: (string?)null, Account: (UserAccount?)account);
        });
        if (outcome.Account is null)
        {
            // Seuls les comptes existants sont journalisés : un inconnu pourrait saturer le journal.
            _audit.Record(found.Login, "Connexion refusée", found.Id, outcome.Reason);
            return Result<UserAccount>.Fail(Failure.Denied, LoginFailure);
        }
        _audit.Record(found.Login, "Connexion", found.Id);
        return Result<UserAccount>.Success(outcome.Account);
    }

    private static (string? Reason, UserAccount? Account) Refused(string reason) => (reason, null);

    /// <summary>Connexion Entra ID réussie : crée le compte à la première fois, refuse un compte désactivé.</summary>
    public Result<UserAccount> SignInEntra(EntraProfile profile)
    {
        if (_settings.Entra is null) return Result<UserAccount>.Fail(Failure.Invalid, "Le mode Entra ID n'est pas activé.");
        var created = false;
        var result = _users.Write(list =>
        {
            var account = list.FirstOrDefault(u => u.Login == profile.Login);
            if (account is null)
            {
                account = EntraDirectory.NewAccount(profile, _settings.Entra, Now);
                list.Add(account);
                created = true;
            }
            if (account.Disabled) return Result<UserAccount>.Fail(Failure.Denied, "Compte désactivé.");
            account.DisplayName = profile.DisplayName;
            account.LastLoginAt = Now;
            return Result<UserAccount>.Success(account);
        });
        if (created) _audit.Record(profile.Login, "Compte créé", result.Value?.Id, $"origine entra, rôle {result.Value?.Role}");
        _audit.Record(profile.Login, result.Ok ? "Connexion" : "Connexion refusée", result.Value?.Id, result.Ok ? null : "compte désactivé");
        return result;
    }

    // ---- Mots de passe -----------------------------------------------------------------------------------------------

    public Result<bool> ChangePassword(string userId, string? current, string? next)
    {
        var outcome = _users.Write(list =>
        {
            var account = list.FirstOrDefault(u => u.Id == userId);
            if (account is null || account.Disabled) return Result<UserAccount>.Fail(Failure.NotFound, "Compte introuvable.");
            if (account.PasswordHash is null) return Result<UserAccount>.Fail(Failure.Invalid, "Le mot de passe de ce compte est géré par l'annuaire.");
            var now = Now;
            if (account.LockedUntil > now) return Result<UserAccount>.Fail(Failure.Invalid, "Trop de tentatives : réessayez plus tard.");
            if (_hasher.VerifyHashedPassword(account, account.PasswordHash, current ?? "") == PasswordVerificationResult.Failed)
            {
                if (++account.FailedAttempts >= MaxFailures) { account.LockedUntil = now + LockDuration; account.FailedAttempts = 0; }
                return Result<UserAccount>.Fail(Failure.Invalid, "Le mot de passe actuel est incorrect.");
            }
            var problem = PasswordProblem(next, account.Login);
            if (problem is not null) return Result<UserAccount>.Fail(Failure.Invalid, problem);
            if (next == current) return Result<UserAccount>.Fail(Failure.Invalid, "Le nouveau mot de passe doit différer de l'actuel.");
            account.PasswordHash = _hasher.HashPassword(account, next!);
            account.MustChangePassword = false;
            account.FailedAttempts = 0;
            return Result<UserAccount>.Success(account);
        });
        if (!outcome.Ok) return Result<bool>.Fail(outcome.Failure, outcome.Error!);
        _audit.Record(outcome.Value!.Login, "Mot de passe modifié", userId);
        return Result<bool>.Success(true);
    }

    /// <summary>Règles : 12 caractères au moins, 128 au plus, différent de l'identifiant.</summary>
    public static string? PasswordProblem(string? password, string login)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinPasswordLength)
            return $"Le mot de passe doit comporter au moins {MinPasswordLength} caractères.";
        if (password.Length > MaxPasswordLength) return $"Le mot de passe ne doit pas dépasser {MaxPasswordLength} caractères.";
        if (string.Equals(password.Trim(), login, StringComparison.OrdinalIgnoreCase)) return "Le mot de passe doit différer de l'identifiant.";
        return null;
    }

    // ---- Administration des comptes ----------------------------------------------------------------------------------

    public IReadOnlyList<UserAccount> List() => _users.All().OrderBy(u => u.Login).ToList();

    public Result<CreatedAccount> Create(string actor, string? login, string? name, string? role)
    {
        if (_settings.IsOpen) return Result<CreatedAccount>.Fail(Failure.Invalid, "La gestion des comptes est indisponible en mode ouvert.");
        if (!Roles.IsValid(role)) return Result<CreatedAccount>.Fail(Failure.Invalid, "Rôle inconnu.");
        var result = _users.Write(list =>
        {
            var id = NormalizeLogin(login);
            var problem = _settings.IsLocal ? ValidateNewAccount(list, login, name) : ValidateProfile(list, id, name);
            if (problem is not null) return Result<CreatedAccount>.Fail(problem.Contains("existe") ? Failure.Conflict : Failure.Invalid, problem);
            // En mode Entra, le compte est préparé : il n'a pas de mot de passe et sera rattaché à la première connexion.
            var temporary = _settings.IsLocal ? RandomPassword() : null;
            var account = temporary is null
                ? new UserAccount { Id = "u-" + Guid.NewGuid().ToString("N"), Login = id, DisplayName = name!.Trim(), Role = role!, CreatedAt = Now, Origin = AccountOrigin.Entra }
                : NewLocal(id, name!, role!, temporary, mustChange: true);
            list.Add(account);
            return Result<CreatedAccount>.Success(new CreatedAccount(account, temporary));
        });
        if (result.Ok) _audit.Record(actor, "Compte créé", result.Value!.User.Id, $"{result.Value.User.Login}, rôle {result.Value.User.Role}");
        return result;
    }

    public Result<UserAccount> Update(string actor, string id, string? name, string? role, bool disabled)
    {
        if (!Roles.IsValid(role)) return Result<UserAccount>.Fail(Failure.Invalid, "Rôle inconnu.");
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 100) return Result<UserAccount>.Fail(Failure.Invalid, "Le nom est obligatoire (100 caractères au plus).");
        string? roleChange = null;
        bool? disabledChange = null;
        var result = _users.Write(list =>
        {
            var account = list.FirstOrDefault(u => u.Id == id);
            if (account is null) return Result<UserAccount>.Fail(Failure.NotFound, "Compte introuvable.");
            var staysAdmin = role == Roles.Administrator && !disabled;
            if (IsActiveAdmin(account) && !staysAdmin && list.Count(IsActiveAdmin) == 1)
                return Result<UserAccount>.Fail(Failure.Conflict, "Impossible : ce compte est le dernier administrateur actif.");
            if (account.Role != role) roleChange = $"{account.Role} -> {role}";
            if (account.Disabled != disabled) disabledChange = disabled;
            account.DisplayName = name.Trim();
            account.Role = role!;
            account.Disabled = disabled;
            return Result<UserAccount>.Success(account);
        });
        if (!result.Ok) return result;
        if (roleChange is not null) _audit.Record(actor, "Rôle modifié", id, $"{result.Value!.Login} : {roleChange}");
        if (disabledChange is bool d) _audit.Record(actor, d ? "Compte désactivé" : "Compte réactivé", id, result.Value!.Login);
        return result;
    }

    /// <summary>Nouveau mot de passe temporaire, renvoyé une seule fois ; le compte devra le changer à sa prochaine connexion.</summary>
    public Result<string> ResetPassword(string actor, string id)
    {
        var temporary = RandomPassword();
        var result = _users.Write(list =>
        {
            var account = list.FirstOrDefault(u => u.Id == id);
            if (account is null) return Result<UserAccount>.Fail(Failure.NotFound, "Compte introuvable.");
            if (account.Origin != AccountOrigin.Local) return Result<UserAccount>.Fail(Failure.Invalid, "Le mot de passe de ce compte est géré par l'annuaire.");
            account.PasswordHash = _hasher.HashPassword(account, temporary);
            account.MustChangePassword = true;
            account.FailedAttempts = 0;
            account.LockedUntil = null;
            return Result<UserAccount>.Success(account);
        });
        if (!result.Ok) return Result<string>.Fail(result.Failure, result.Error!);
        _audit.Record(actor, "Mot de passe réinitialisé", id, result.Value!.Login);
        return Result<string>.Success(temporary);
    }

    // ---- Outils internes ---------------------------------------------------------------------------------------------

    private static bool IsActiveAdmin(UserAccount u) => u.Role == Roles.Administrator && !u.Disabled;

    private UserAccount NewLocal(string login, string name, string role, string password, bool mustChange)
    {
        var account = new UserAccount
        {
            Id = "u-" + Guid.NewGuid().ToString("N"), Login = login, DisplayName = name.Trim(), Role = role,
            CreatedAt = Now, MustChangePassword = mustChange, Origin = AccountOrigin.Local,
        };
        account.PasswordHash = _hasher.HashPassword(account, password);
        return account;
    }

    private static string? ValidateNewAccount(List<UserAccount> list, string? login, string? name) =>
        ValidateProfile(list, NormalizeLogin(login), name);

    private static string? ValidateProfile(List<UserAccount> list, string id, string? name)
    {
        if (!LoginPattern.IsMatch(id)) return "L'identifiant doit comporter 3 à 100 caractères : lettres, chiffres et . _ @ + -";
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 100) return "Le nom est obligatoire (100 caractères au plus).";
        return list.Any(u => u.Login == id) ? "Cet identifiant existe déjà." : null;
    }

    /// <summary>Mot de passe aléatoire de 16 caractères, tiré d'un alphabet sans caractères ambigus.</summary>
    private static string RandomPassword() =>
        string.Create(16, 0, (span, _) => { for (var i = 0; i < span.Length; i++) span[i] = TemporaryAlphabet[RandomNumberGenerator.GetInt32(TemporaryAlphabet.Length)]; });
}
