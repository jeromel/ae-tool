using System.Text.Json;
using AeTool.Connectors;
using AeTool.Core;

namespace AeTool.Api;

/// <summary>
/// Port de stockage. Le prototype fournit un fichier JSON ; une implémentation SQL (SQL Server ou PostgreSQL,
/// selon le standard de l'organisation) se branche ici sans toucher au cœur ni aux connecteurs.
/// </summary>
public interface IRepositoryStore
{
    Repository Load();
    void Save(Repository repository);
}

/// <summary>
/// Emplacement des données de l'application. Tous les fichiers (référentiel, comptes, sources externes, sauvegardes)
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

public sealed class JsonFileStore : IRepositoryStore
{
    private readonly string _path;
    public JsonFileStore(string path) => _path = path;

    public Repository Load()
    {
        if (!File.Exists(_path)) return new Repository();
        using var s = File.OpenRead(_path);
        return JsonSerializer.Deserialize<Repository>(s) ?? new Repository();
    }

    public void Save(Repository repository)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        // Écriture dans un fichier temporaire puis remplacement : une coupure ne laisse jamais un fichier tronqué.
        var tmp = _path + ".tmp";
        using (var s = File.Create(tmp)) JsonSerializer.Serialize(s, repository);
        File.Move(tmp, _path, overwrite: true);
    }
}

/// <summary>État partagé de l'application : un référentiel en mémoire, protégé par un verrou, sauvegardé après chaque écriture.</summary>
public sealed class Workspace
{
    private readonly IRepositoryStore _store;
    private readonly object _gate = new();
    private Repository _repo;
    private Analyzer? _analyzer;

    public List<ImportReport> Reports { get; } = new();

    public Workspace(IRepositoryStore store)
    {
        _store = store;
        _repo = store.Load();
    }

    public T Read<T>(Func<Repository, Analyzer, T> query)
    {
        lock (_gate) return query(_repo, _analyzer ??= new Analyzer(_repo));
    }

    public T Write<T>(Func<Repository, T> command)
    {
        lock (_gate)
        {
            var result = command(_repo);
            _analyzer = null;
            _store.Save(_repo);
            return result;
        }
    }

    public ImportReport Import(Func<Repository, ImportReport> import)
    {
        var report = Write(import);
        lock (_gate)
        {
            Reports.Insert(0, report);
            if (Reports.Count > 10) Reports.RemoveAt(10);
        }
        return report;
    }

    /// <summary>Repart d'un référentiel vide (métamodèle par défaut). Réservé à l'administration.</summary>
    public void Reset() => Write(_ => { _repo = new Repository(); return 0; });
}
