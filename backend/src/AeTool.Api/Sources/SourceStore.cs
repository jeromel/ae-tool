using System.Text.Json;
using AeTool.Connectors;
using AeTool.Core;
using Microsoft.AspNetCore.DataProtection;

namespace AeTool.Api.Sources;

/// <summary>
/// Profils des sources externes, dans sources.json du dossier de données (écriture atomique).
/// Le secret de chaque source (mot de passe ou jeton) n'y est jamais en clair : il est chiffré par ASP.NET Core Data Protection.
/// Les profils rendus sont des copies : on les modifie puis on les enregistre avec Save.
/// </summary>
public sealed class SourceStore
{
    private sealed class StoredSource
    {
        public SourceProfile Profile { get; set; } = new();
        public string? ProtectedSecret { get; set; }
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly object _gate = new();
    private readonly string _path;
    private readonly IDataProtector _protector;
    private readonly Dictionary<string, StoredSource> _sources;

    public SourceStore(DataPaths paths, IDataProtectionProvider protection)
    {
        _path = paths.File("sources.json");
        _protector = protection.CreateProtector("ae-tool.sources.secret.v1");
        _sources = File.Exists(_path)
            ? (JsonSerializer.Deserialize<List<StoredSource>>(File.ReadAllText(_path), Json) ?? new()).ToDictionary(s => s.Profile.Key)
            : new();
        foreach (var s in _sources.Values) UpgradeApiProfile(s.Profile);
    }

    /// <summary>
    /// Reprise des profils d'API enregistrés avant le connecteur générique (version 0.13 et avant) : adresse décomposée
    /// en serveur, compte et ressource, et pagination implicite. On les traduit en réglages génériques équivalents.
    /// </summary>
    public static void UpgradeApiProfile(SourceProfile p)
    {
        var c = p.Connection;
        if (p.Kind is SourceProfile.KindFile or SourceProfile.KindApi || !c.ContainsKey("baseUrl")) return;
        string Get(string key) => c.TryGetValue(key, out var v) ? v.Trim() : "";
        var resource = Get("resource") is { Length: > 0 } r ? r : "configuration-items";
        var query = string.Join("&", new[] { ("fields", Get("fields")), ("search", Get("search")) }
            .Where(x => x.Item2.Length > 0).Select(x => $"{x.Item1}={Uri.EscapeDataString(x.Item2)}"));
        p.Kind = SourceProfile.KindApi;
        p.Connection = new Dictionary<string, string>
        {
            ["url"] = $"{Get("baseUrl").TrimEnd('/')}/api/v1/{Get("account")}/{resource}", ["query"] = query,
            ["pageSize"] = Get("pageSize") is { Length: > 0 } size ? size : "100", ["pageSizeParam"] = "max_rows", ["offsetParam"] = "offset",
            ["itemsField"] = "records", ["totalField"] = "total_record_count", ["nextField"] = "@next", ["idField"] = "HREF",
            ["authMode"] = Get("authMode") is { Length: > 0 } mode ? mode : "basic", ["login"] = Get("login"),
        };
    }

    public List<SourceProfile> List()
    {
        lock (_gate) return _sources.Values.OrderBy(s => s.Profile.Key, StringComparer.Ordinal).Select(s => Copy(s.Profile)).ToList();
    }

    public SourceProfile? Get(string key)
    {
        lock (_gate) return _sources.TryGetValue(key, out var s) ? Copy(s.Profile) : null;
    }

    public bool HasSecret(string key)
    {
        lock (_gate) return _sources.TryGetValue(key, out var s) && s.ProtectedSecret is not null;
    }

    /// <summary>Secret en clair, pour un appel à la source uniquement. Null si aucun n'est enregistré.</summary>
    public string? Secret(string key)
    {
        lock (_gate)
        {
            if (!_sources.TryGetValue(key, out var s) || s.ProtectedSecret is null) return null;
            try { return _protector.Unprotect(s.ProtectedSecret); }
            catch (System.Security.Cryptography.CryptographicException)
            {
                // Clés de chiffrement perdues ou remplacées : message sans détail, le secret est à ressaisir.
                throw new SourceAccessException("Le secret enregistré est illisible : saisissez-le à nouveau.");
            }
        }
    }

    /// <summary>Crée ou remplace un profil. Secret : null = inchangé, chaîne vide = effacé, sinon remplacé (chiffré).</summary>
    public void Put(SourceProfile profile, string? secret)
    {
        lock (_gate)
        {
            var stored = _sources.GetValueOrDefault(profile.Key) ?? new StoredSource();
            stored.Profile = Copy(profile);
            if (secret is not null) stored.ProtectedSecret = secret.Length == 0 ? null : _protector.Protect(secret);
            _sources[profile.Key] = stored;
            Persist();
        }
    }

    /// <summary>Enregistre un profil modifié (lignes ignorées, dernier rapprochement) sans toucher au secret.</summary>
    public void Save(SourceProfile profile) => Put(profile, null);

    public bool Delete(string key)
    {
        lock (_gate)
        {
            if (!_sources.Remove(key)) return false;
            Persist();
            return true;
        }
    }

    private static SourceProfile Copy(SourceProfile p) => JsonSerializer.Deserialize<SourceProfile>(JsonSerializer.Serialize(p, Json), Json)!;

    private void Persist() => AtomicFile.Write(_path, JsonSerializer.SerializeToUtf8Bytes(_sources.Values.ToList(), Json));
}

/// <summary>Écriture atomique d'un fichier du dossier de données : fichier temporaire puis remplacement, lisible par son seul propriétaire.</summary>
public static class AtomicFile
{
    public static void Write(string path, byte[] content)
    {
        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, content);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(tmp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.Move(tmp, path, overwrite: true);
    }
}
