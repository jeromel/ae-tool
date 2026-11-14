using System.Security.Cryptography;
using System.Text;

namespace AeTool.Api.Auth;

/// <summary>
/// Code d'installation à usage unique du premier lancement. Sans lui, le premier venu sur le réseau pourrait créer
/// l'administrateur : le code n'est lisible que dans la console et dans le dossier de données, donc par qui contrôle le serveur.
/// </summary>
public sealed class SetupCode
{
    // Alphabet sans caractères ambigus (0/O, 1/I) : le code se recopie à la main depuis la console.
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private readonly string _file;
    private readonly Action<string> _announce;
    private readonly object _gate = new();
    private string? _code;

    public SetupCode(string file, Action<string> announce)
    {
        _file = file;
        _announce = announce;
    }

    /// <summary>Génère un code et l'annonce (console et fichier). À appeler au démarrage tant qu'aucun compte n'existe.</summary>
    public void Issue()
    {
        lock (_gate)
        {
            _code = Generate();
            File.WriteAllText(_file, _code + Environment.NewLine);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(_file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            _announce($"Premier lancement : code d'installation {_code} (également dans {_file}). Il ne sert qu'une fois.");
        }
    }

    /// <summary>Vrai si le texte saisi est le code actif. Comparaison en temps constant.</summary>
    public bool Matches(string? attempt)
    {
        lock (_gate)
        {
            if (_code is null || attempt is null) return false;
            var expected = Encoding.UTF8.GetBytes(_code);
            var given = Encoding.UTF8.GetBytes(attempt.Trim().ToUpperInvariant());
            return CryptographicOperations.FixedTimeEquals(expected, given);
        }
    }

    /// <summary>Invalide le code (usage unique) et supprime le fichier.</summary>
    public void Revoke()
    {
        lock (_gate)
        {
            _code = null;
            File.Delete(_file);
        }
    }

    private static string Generate()
    {
        var chars = new char[14];
        for (var i = 0; i < chars.Length; i++) chars[i] = i % 5 == 4 ? '-' : Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        return new string(chars);
    }
}
