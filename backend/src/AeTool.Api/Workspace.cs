using AeTool.Api.Storage;
using AeTool.Connectors;
using AeTool.Core;

namespace AeTool.Api;

/// <summary>
/// Emplacement des données de l'application. Tous les fichiers (référentiel, journal, comptes, sources externes, sauvegardes)
/// vivent dans le dossier du fichier du référentiel : un seul dossier à sauvegarder et à protéger.
/// </summary>
public sealed class DataPaths
{
    public string RepositoryFile { get; }
    public string Directory { get; }

    public DataPaths(string repositoryFile)
    {
        RepositoryFile = Path.GetFullPath(repositoryFile);
        Directory = Path.GetDirectoryName(RepositoryFile)!;
    }

    /// <summary>Chemin d'un fichier ou d'un sous-dossier du dossier de données ; le dossier parent est créé au besoin.</summary>
    public string File(params string[] parts)
    {
        var path = Path.Combine(new[] { Directory }.Concat(parts).ToArray());
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
    }
}

/// <summary>Motifs des sauvegardes d'avant opération risquée (ils font le nom du fichier).</summary>
public static class BackupReasons
{
    public const string Daily = BackupStore.DailyReason;
    public const string Manual = "manuelle";
    public const string ArchiImport = "import-archi";
    public const string UrbaImport = "import-urbanisation";
    public const string DrawioImport = "import-drawio";
    public const string MetamodelImport = "import-metamodele";
    public const string MatrixReapplied = "matrice-reappliquee";
    public const string BeforeRestore = "avant-restauration";
    public const string Reset = "remise-a-zero";
}

/// <summary>État partagé de l'application : un référentiel en mémoire, protégé par un verrou, sauvegardé après chaque écriture.</summary>
public sealed class Workspace
{
    private readonly IRepositoryStore _store;
    private readonly TimeProvider _clock;
    private readonly object _gate = new();
    private Repository _repo;
    private Analyzer? _analyzer;
    private DateOnly? _lastDaily;

    public List<ImportReport> Reports { get; } = new();

    public Workspace(IRepositoryStore store, TimeProvider clock)
    {
        _store = store;
        _clock = clock;
        _repo = store.Load();
        _lastDaily = _store.Backups().Where(b => b.Reason == BackupReasons.Daily).Select(b => (DateOnly?)DateOnly.FromDateTime(b.At)).FirstOrDefault();
    }

    public T Read<T>(Func<Repository, Analyzer, T> query)
    {
        lock (_gate) return query(_repo, _analyzer ??= new Analyzer(_repo));
    }

    /// <summary>
    /// Exécute une écriture puis sauvegarde. Avant la première écriture de chaque jour, une sauvegarde automatique est prise ;
    /// avant une opération risquée (<paramref name="backupReason"/>), une sauvegarde de l'état d'avant l'est aussi.
    /// </summary>
    public T Write<T>(Func<Repository, T> command, string? backupReason = null)
    {
        lock (_gate)
        {
            DailyBackup();
            if (backupReason is not null) _store.Backup(_repo, backupReason, Now());
            var result = command(_repo);
            _analyzer = null;
            _store.Save(_repo);
            return result;
        }
    }

    /// <summary>
    /// Ajoute une entrée au journal sans réécrire le référentiel : une seule ligne est écrite. Pour les événements qui ne modifient
    /// pas le référentiel (connexion, gestion des comptes).
    /// </summary>
    public void Append(JournalEntry entry)
    {
        lock (_gate)
        {
            _repo.Journal.Add(entry);
            _store.SaveJournal(_repo);
        }
    }

    public ImportReport Import(Func<Repository, ImportReport> import, string backupReason)
    {
        var report = Write(import, backupReason);
        lock (_gate)
        {
            Reports.Insert(0, report);
            if (Reports.Count > 10) Reports.RemoveAt(10);
        }
        return report;
    }

    /// <summary>Repart d'un référentiel vide (métamodèle par défaut), après sauvegarde. Le journal est conservé : il retrace aussi cette remise à zéro.</summary>
    public void Reset(string user)
    {
        lock (_gate)
        {
            _store.Backup(_repo, BackupReasons.Reset, Now());
            var journal = _repo.Journal;
            _repo = new Repository { Journal = journal };
            _repo.Log(user, "Remise à zéro du référentiel");
            _analyzer = null;
            _store.Save(_repo);
        }
    }

    // ------------------------------------------------------------ Sauvegardes

    public List<BackupInfo> Backups() { lock (_gate) return _store.Backups(); }

    /// <summary>Sauvegarde à la demande. Elle ne dépend d'aucune écriture : elle photographie l'état courant.</summary>
    public BackupInfo Backup(string reason, string user)
    {
        lock (_gate)
        {
            var info = _store.Backup(_repo, reason, Now());
            Append(new JournalEntry { At = DateTime.UtcNow, User = user, Action = "Sauvegarde créée", TargetKind = JournalKinds.Import, Detail = info.Name });
            return info;
        }
    }

    public string? BackupPath(string? name) { lock (_gate) return _store.BackupPath(name); }

    /// <summary>
    /// Rend l'état d'une sauvegarde. L'état courant est d'abord sauvegardé, puis la sauvegarde est lue par le migrateur de schéma.
    /// Le journal n'est jamais rembobiné : il garde tout ce qui s'est passé, restauration comprise.
    /// Renvoie null si le nom ne désigne aucune sauvegarde.
    /// </summary>
    public BackupInfo? Restore(string name, string user)
    {
        lock (_gate)
        {
            if (_store.BackupPath(name) is not { } path) return null;
            // Lu avant toute écriture : une sauvegarde illisible ne détruit rien.
            var restored = _store.ReadBackup(path);
            _store.Backup(_repo, BackupReasons.BeforeRestore, Now());
            restored.Journal = AeTool.Api.Storage.JournalFile.Union(_repo.Journal, restored.Journal);
            _repo = restored;
            _repo.Log(user, "Sauvegarde restaurée", JournalKinds.Import, null, null, name);
            _analyzer = null;
            _store.Save(_repo);
            return _store.Backups().FirstOrDefault(b => b.Name == name) ?? new BackupInfo(name, "", Now(), 0);
        }
    }

    private DateTime Now() => _clock.GetUtcNow().UtcDateTime;

    private void DailyBackup()
    {
        var today = DateOnly.FromDateTime(Now());
        if (_lastDaily == today) return;
        _store.Backup(_repo, BackupReasons.Daily, Now());
        _lastDaily = today;
    }
}
