using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using AeTool.Core;

namespace AeTool.Api.Storage;

/// <summary>Une sauvegarde du dossier <c>sauvegardes/</c>. Le motif et la date se lisent dans le nom du fichier.</summary>
public sealed record BackupInfo(string Name, string Reason, DateTime At, long Size);

/// <summary>
/// Sauvegardes compressées du référentiel et de son journal : <c>referentiel-AAAAMMJJ-HHMMSS-motif.json.gz</c> (heure UTC).
/// Chaque fichier est un référentiel complet, journal compris : il se relit par le même migrateur de schéma que le fichier courant.
/// </summary>
public sealed class BackupStore
{
    public const string FolderName = "sauvegardes";
    public const int DefaultToKeep = 40;
    private static readonly Regex NamePattern = new(@"^referentiel-(\d{8})-(\d{6})-([a-z0-9-]+)\.json\.gz$", RegexOptions.Compiled);

    private readonly string _folder;
    private readonly int _toKeep;

    public BackupStore(string folder, int toKeep)
    {
        _folder = folder;
        _toKeep = toKeep > 0 ? toKeep : DefaultToKeep;
    }

    public BackupInfo Create(Repository repository, string reason, DateTime now)
    {
        Directory.CreateDirectory(_folder);
        var motif = Text.Slug(reason) is { Length: > 0 } slug ? slug : "sauvegarde";
        var stem = $"referentiel-{now:yyyyMMdd-HHmmss}-";
        var name = stem + motif + ".json.gz";
        // Deux sauvegardes dans la même seconde ne s'écrasent jamais : un numéro s'ajoute au motif.
        for (var n = 2; File.Exists(Path.Combine(_folder, name)); n++) name = $"{stem}{motif}-{n}.json.gz";
        AtomicFile.Write(Path.Combine(_folder, name), file =>
        {
            using var zip = new GZipStream(file, CompressionLevel.Optimal, leaveOpen: true);
            JsonSerializer.Serialize(zip, repository);
        });
        Rotate();
        return Describe(Path.Combine(_folder, name))!;
    }

    /// <summary>Sauvegardes présentes, les plus récentes d'abord.</summary>
    public List<BackupInfo> List() =>
        (Directory.Exists(_folder) ? Directory.GetFiles(_folder, "referentiel-*.json.gz") : Array.Empty<string>())
            .Select(f => (File: f, Info: Describe(f))).Where(x => x.Info is not null)
            // À la même seconde, la date d'écriture départage (un motif numéroté vient après l'autre).
            .OrderByDescending(x => x.Info!.At).ThenByDescending(x => File.GetLastWriteTimeUtc(x.File)).Select(x => x.Info!).ToList();

    /// <summary>Chemin d'une sauvegarde par son nom, ou null si le nom n'est pas celui d'une sauvegarde existante (aucun chemin n'est jamais accepté).</summary>
    public string? PathOf(string? name)
    {
        if (name is null || !NamePattern.IsMatch(name)) return null;
        var path = Path.Combine(_folder, name);
        return File.Exists(path) ? path : null;
    }

    public Repository Read(string path)
    {
        using var file = File.OpenRead(path);
        using var zip = new GZipStream(file, CompressionMode.Decompress);
        return RepositoryUpgrader.Read(zip, out _);
    }

    private BackupInfo? Describe(string path)
    {
        var name = Path.GetFileName(path);
        var m = NamePattern.Match(name);
        if (!m.Success || !DateTime.TryParseExact(m.Groups[1].Value + m.Groups[2].Value, "yyyyMMddHHmmss", null,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var at)) return null;
        return new BackupInfo(name, m.Groups[3].Value, at, new FileInfo(path).Length);
    }

    /// <summary>Conserve les plus récentes, supprime les autres. Ce sont des copies : la règle de non-suppression porte sur les données métier.</summary>
    private void Rotate()
    {
        foreach (var old in List().Skip(_toKeep))
            File.Delete(Path.Combine(_folder, old.Name));
    }
}
