using System.Text.Json;

namespace AeTool.Api.Auth;

/// <summary>Origine d'un compte : créé dans l'outil (mot de passe local) ou issu de l'annuaire Entra ID.</summary>
public static class AccountOrigin
{
    public const string Local = "local";
    public const string Entra = "entra";
}

/// <summary>Compte utilisateur de l'outil. Le rôle est géré ici dans tous les modes : l'annuaire authentifie, l'outil habilite.</summary>
public sealed class UserAccount
{
    public string Id { get; set; } = "";
    /// <summary>Identifiant de connexion, en minuscules. Pour Entra : le userPrincipalName.</summary>
    public string Login { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Role { get; set; } = Roles.Reader;
    /// <summary>Hachage du mot de passe (PasswordHasher). Nul pour un compte Entra. Ne sort jamais de l'application.</summary>
    public string? PasswordHash { get; set; }
    public bool Disabled { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public int FailedAttempts { get; set; }
    public DateTime? LockedUntil { get; set; }
    public bool MustChangePassword { get; set; }
    public string Origin { get; set; } = AccountOrigin.Local;
    /// <summary>
    /// Périmètre de lecture choisi par l'administrateur (décision 012) : « tout » ou « metier ». Il ne vaut que pour un lecteur ;
    /// absent, un lecteur est au périmètre « métier ». Voir <see cref="EffectiveScope"/>.
    /// </summary>
    public string? ReadScope { get; set; }

    /// <summary>Périmètre appliqué : « tout » pour un contributeur, un valideur ou un administrateur ; pour un lecteur, « métier » sauf ouverture explicite.</summary>
    public string EffectiveScope => Role == Roles.Reader && ReadScope != AeTool.Core.ReadScope.All ? AeTool.Core.ReadScope.Business : AeTool.Core.ReadScope.All;

    public UserAccount Copy() => (UserAccount)MemberwiseClone();
}

/// <summary>Stockage des comptes. Les lectures renvoient des copies ; les modifications passent toutes par <see cref="Write{T}"/>.</summary>
public interface IUserStore
{
    int Count { get; }
    IReadOnlyList<UserAccount> All();
    UserAccount? Find(Func<UserAccount, bool> predicate);
    /// <summary>
    /// Applique une modification de façon atomique : elle porte sur une copie de la liste, qui n'est conservée
    /// que si la modification puis l'enregistrement ont réussi.
    /// </summary>
    T Write<T>(Func<List<UserAccount>, T> change);
}

/// <summary>
/// Comptes dans le fichier utilisateurs.json du dossier de données. Fichier séparé du référentiel pour qu'aucun hachage
/// ne parte dans un export ni dans une sauvegarde du référentiel.
/// </summary>
public sealed class JsonUserStore : IUserStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly string _path;
    private readonly object _gate = new();
    private List<UserAccount> _users;

    public JsonUserStore(string path)
    {
        _path = path;
        _users = File.Exists(path) ? JsonSerializer.Deserialize<List<UserAccount>>(File.ReadAllText(path), Options) ?? new() : new();
    }

    public int Count { get { lock (_gate) return _users.Count; } }

    public IReadOnlyList<UserAccount> All()
    {
        lock (_gate) return _users.Select(u => u.Copy()).ToList();
    }

    public UserAccount? Find(Func<UserAccount, bool> predicate)
    {
        lock (_gate) return _users.FirstOrDefault(predicate)?.Copy();
    }

    public T Write<T>(Func<List<UserAccount>, T> change)
    {
        lock (_gate)
        {
            var working = _users.Select(u => u.Copy()).ToList();
            var result = change(working);
            Save(working);
            _users = working;
            return result;
        }
    }

    private void Save(List<UserAccount> users)
    {
        // Écriture dans un fichier temporaire puis remplacement : une coupure ne laisse jamais un fichier tronqué.
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(users, Options));
        // Le fichier contient des hachages de mots de passe : lisible par le seul compte du service.
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(tmp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.Move(tmp, _path, overwrite: true);
    }
}
