using System.Text.Json;
using AeTool.Core;

namespace AeTool.Api.Sources;

/// <summary>Dernier rapprochement d'une source : le rapport et les enregistrements lus, pour pouvoir appliquer des décisions plus tard.</summary>
public sealed class SavedRun
{
    public ReconciliationReport Report { get; set; } = new();
    public List<SourceRecord> Records { get; set; } = new();
    /// <summary>Nom du fichier lu (mode fichier).</summary>
    public string? SourceName { get; set; }
    /// <summary>Nombre de lignes lues dans la source, avant écartement des lignes en anomalie.</summary>
    public int RowsRead { get; set; }
    public long? SourceTotal { get; set; }
    public bool Truncated { get; set; }
}

/// <summary>Dernier rapprochement de chaque source, en mémoire et dans rapprochement-&lt;clé&gt;.json (reprise après redémarrage).</summary>
public sealed class SourceRuns
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly object _gate = new();
    private readonly DataPaths _paths;
    private readonly Dictionary<string, SavedRun> _cache = new();

    public SourceRuns(DataPaths paths) => _paths = paths;

    // La clé de source est contrainte par la validation du profil (minuscules, chiffres, tirets) : elle ne peut pas sortir du dossier.
    private string PathOf(string key) => _paths.File($"rapprochement-{key}.json");

    public SavedRun? Get(string key)
    {
        lock (_gate)
        {
            if (_cache.TryGetValue(key, out var run)) return run;
            var path = PathOf(key);
            if (!File.Exists(path)) return null;
            var loaded = JsonSerializer.Deserialize<SavedRun>(File.ReadAllBytes(path), Json);
            if (loaded is not null) _cache[key] = loaded;
            return loaded;
        }
    }

    public void Put(string key, SavedRun run)
    {
        lock (_gate)
        {
            _cache[key] = run;
            AtomicFile.Write(PathOf(key), JsonSerializer.SerializeToUtf8Bytes(run, Json));
        }
    }

    public void Delete(string key)
    {
        lock (_gate)
        {
            _cache.Remove(key);
            File.Delete(PathOf(key));
        }
    }
}
