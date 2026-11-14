using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using AeTool.Core;

namespace AeTool.Api.Storage;

/// <summary>
/// Port de stockage. Le prototype fournit un dossier de fichiers ; une implémentation SQL se branche ici sans toucher au cœur.
/// </summary>
public interface IRepositoryStore
{
    Repository Load();
    /// <summary>Écrit les entrées de journal en attente, puis le référentiel.</summary>
    void Save(Repository repository);
    /// <summary>N'écrit que les entrées de journal en attente (événements de comptes, sans toucher au référentiel).</summary>
    void SaveJournal(Repository repository);
    BackupInfo Backup(Repository repository, string reason, DateTime now);
    List<BackupInfo> Backups();
    string? BackupPath(string? name);
    Repository ReadBackup(string path);
}

/// <summary>
/// Dossier de fichiers durci : référentiel écrit de façon sûre, journal séparé en ajout seul, sauvegardes compressées.
/// Une seule instance de l'application peut écrire dans le dossier.
/// </summary>
public sealed class JsonFileStore : IRepositoryStore
{
    // Le journal vit dans journal.jsonl : le fichier du référentiel ne le porte plus. Les sauvegardes et l'export, eux, le contiennent.
    private static readonly JsonSerializerOptions WithoutJournal = new()
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver
        {
            Modifiers = { info => { if (info.Type == typeof(Repository)) info.Properties.Remove(info.Properties.First(p => p.Name == nameof(Repository.Journal))); } },
        },
    };

    private readonly string _path;
    private readonly JournalFile _journal;
    private readonly BackupStore _backups;
    private int _journalWritten;
    // Vrai tant que le fichier du journal ne contient pas tout ce qui est en mémoire (ancien référentiel qui portait son journal).
    private bool _journalNeedsRewrite;

    public JsonFileStore(string path, int backupsToKeep = BackupStore.DefaultToKeep, int dailyBackupsToKeep = BackupStore.DefaultDailyToKeep)
    {
        _path = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(_path)!;
        _journal = new JournalFile(Path.Combine(dir, JournalFile.FileName));
        _backups = new BackupStore(Path.Combine(dir, BackupStore.FolderName), backupsToKeep, dailyBackupsToKeep);
    }

    public Repository Load()
    {
        Repository repository;
        var upgraded = false;
        var fromVersion = Repository.CurrentSchemaVersion;
        if (File.Exists(_path))
            using (var s = File.OpenRead(_path)) repository = RepositoryUpgrader.Read(s, out upgraded, out fromVersion);
        else repository = new Repository();

        // Un référentiel d'avant le journal séparé porte encore ses entrées : on les réunit à celles du fichier, sans doublon.
        var legacy = repository.Journal;
        repository.Journal = JournalFile.Union(legacy, _journal.Read());
        _journalWritten = repository.Journal.Count;
        _journalNeedsRewrite = legacy.Count > 0;

        if (!upgraded) return repository;
        // Migration de format : l'original est conservé à côté avant toute réécriture.
        var stem = Path.Combine(Path.GetDirectoryName(_path)!, Path.GetFileNameWithoutExtension(_path) + $".avant-migration-v{fromVersion}");
        var backup = stem + ".json";
        // Une copie déjà présente n'est jamais écrasée.
        if (File.Exists(backup)) backup = $"{stem}-{DateTime.UtcNow:yyyyMMddHHmmss}.json";
        File.Copy(_path, backup);
        Save(repository);
        return repository;
    }

    public void Save(Repository repository)
    {
        // Journal d'abord : une coupure entre les deux laisse une trace sans modification, jamais une modification sans trace.
        SaveJournal(repository);
        AtomicFile.Write(_path, s => JsonSerializer.Serialize(s, repository, WithoutJournal));
    }

    public void SaveJournal(Repository repository)
    {
        if (_journalNeedsRewrite)
        {
            _journal.Rewrite(repository.Journal);
            _journalNeedsRewrite = false;
        }
        else _journal.Append(repository.Journal.Skip(_journalWritten));
        _journalWritten = repository.Journal.Count;
    }

    public BackupInfo Backup(Repository repository, string reason, DateTime now) => _backups.Create(repository, reason, now);
    public List<BackupInfo> Backups() => _backups.List();
    public string? BackupPath(string? name) => _backups.PathOf(name);
    public Repository ReadBackup(string path) => _backups.Read(path);
}
