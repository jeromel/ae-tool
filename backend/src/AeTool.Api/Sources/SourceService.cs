using AeTool.Connectors;
using AeTool.Core;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace AeTool.Api.Sources;

/// <summary>Enregistrement des services des sources externes dans la composition de l'application.</summary>
public static class SourceServices
{
    public static IServiceCollection AddSources(this IServiceCollection services, DataPaths paths)
    {
        // Les clés de chiffrement des secrets vivent dans le dossier de données, sous « cles » : même sauvegarde, même protection.
        var keyDirectory = Path.GetDirectoryName(paths.File("cles", "x"))!;
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(keyDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keyDirectory)).SetApplicationName("ae-tool");
        services.AddSingleton<SourceStore>();
        services.AddSingleton<SourceRuns>();
        // Pas de redirection suivie : les identifiants ne doivent jamais partir vers un autre serveur que celui configuré.
        services.AddSingleton(new EasyVistaClient(new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
        { Timeout = Timeout.InfiniteTimeSpan }));
        services.AddSingleton<SourceService>();
        return services;
    }
}

/// <summary>Orchestration : lire la source, rapprocher, appliquer. La source n'est jamais modifiée.</summary>
public sealed class SourceService
{
    public const int MaxRecords = 100_000;
    private readonly Workspace _ws;
    private readonly SourceStore _store;
    private readonly SourceRuns _runs;
    private readonly EasyVistaClient _easyVista;
    private readonly object _applyGate = new();

    public SourceService(Workspace ws, SourceStore store, SourceRuns runs, EasyVistaClient easyVista)
    { _ws = ws; _store = store; _runs = runs; _easyVista = easyVista; }

    /// <summary>Lit la source : le fichier reçu, ou l'API (une seule page si maxRecords est petit).</summary>
    public async Task<SourceTable> ReadAsync(SourceProfile profile, Stream? file, string? fileName, int maxRecords, CancellationToken ct)
    {
        if (profile.Kind == SourceProfile.KindEasyVista)
            return await _easyVista.FetchAsync(profile.Connection, _store.Secret(profile.Key), maxRecords, ct);
        if (file is null || file.Length == 0) throw new SourceAccessException("Aucun fichier reçu : envoyez le fichier dans le corps de la requête.");
        return TabularReader.Read(file, fileName);
    }

    public async Task<(bool Ok, long? Total, string Message)> TestAsync(SourceProfile profile, CancellationToken ct)
    {
        try
        {
            var total = await _easyVista.ProbeAsync(profile.Connection, _store.Secret(profile.Key), ct);
            return (true, total, total is null ? "Connexion réussie." : $"Connexion réussie : {total} élément(s) disponibles.");
        }
        catch (SourceAccessException e) { return (false, null, e.Message); }
    }

    public async Task<SavedRun> RunAsync(SourceProfile profile, Stream? file, string? fileName, string user, CancellationToken ct)
    {
        var table = await ReadAsync(profile, file, fileName, MaxRecords, ct);
        var set = SourceRecords.From(table.Rows, profile.Mapping);
        var report = _ws.Read((repo, _) => Reconciler.Compare(repo, profile, set.Records));
        report.Issues = table.Issues.Concat(set.Issues).ToList();
        if (table.Truncated) report.Notes.Add("Lecture partielle de la source : la liste des objets disparus n'est pas fiable.");
        var run = new SavedRun { Report = report, Records = set.Records, SourceName = fileName, RowsRead = table.Rows.Count, SourceTotal = table.Total, Truncated = table.Truncated };
        _runs.Put(profile.Key, run);
        profile.LastRunAt = DateTime.UtcNow;
        profile.LastRunBy = user;
        lock (_applyGate) Persist(profile);
        return run;
    }

    /// <summary>Applique les décisions au référentiel (écriture), garde le profil à jour, puis recalcule le rapport.</summary>
    public (ApplyResult Result, SavedRun Run)? Apply(string key, ApplyRequest request, string user)
    {
        lock (_applyGate)
        {
            var profile = _store.Get(key);
            var run = _runs.Get(key);
            if (profile is null || run is null) return null;
            var result = _ws.Write(repo => Reconciler.Apply(repo, profile, run.Report, run.Records, request, user));
            _store.Save(profile);
            var report = _ws.Read((repo, _) => Reconciler.Compare(repo, profile, run.Records));
            report.Issues = run.Report.Issues;
            report.Notes = run.Report.Notes;
            var updated = new SavedRun
            {
                Report = report, Records = run.Records, SourceName = run.SourceName, RowsRead = run.RowsRead,
                SourceTotal = run.SourceTotal, Truncated = run.Truncated,
            };
            _runs.Put(key, updated);
            return (result, updated);
        }
    }

    // Le profil peut avoir été modifié (PUT) pendant la lecture : on ne réécrit que la date et l'auteur du dernier rapprochement.
    private void Persist(SourceProfile read)
    {
        var current = _store.Get(read.Key);
        if (current is null) return;
        current.LastRunAt = read.LastRunAt;
        current.LastRunBy = read.LastRunBy;
        _store.Save(current);
    }
}
