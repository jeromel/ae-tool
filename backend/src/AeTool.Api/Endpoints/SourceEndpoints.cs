using AeTool.Api.Sources;
using AeTool.Connectors;
using AeTool.Core;
using static AeTool.Api.Http;

namespace AeTool.Api.Endpoints;

/// <summary>Sources externes (CMDB, outil de projets, fichiers) : profils, aperçu, rapprochement, application des décisions.</summary>
public static class SourceEndpoints
{
    private const int PreviewRows = 5;
    private const int DefaultTake = 500;
    private const int MaxTake = 2000;

    public static void Map(RouteGroupBuilder api)
    {
        var g = api.MapGroup("/sources");

        g.MapGet("", (SourceStore store, SourceRuns runs) =>
            store.List().Select(p => SourceView.Of(p, store.HasSecret(p.Key), runs.Get(p.Key) is not null, withIgnored: false)));

        g.MapGet("/templates", () => new[]
        {
            new SourceTemplate("Fichier", "Export CSV ou Excel d'EasyVista, d'un outil de gestion de projet ou de tout autre outil.", SourceProfile.FileTemplate()),
            new SourceTemplate("EasyVista", "CMDB EasyVista par son API REST, en lecture seule.", SourceProfile.EasyVistaTemplate()),
        });

        g.MapGet("/{key}", (string key, SourceStore store, SourceRuns runs) =>
            store.Get(key) is { } p ? Results.Ok(SourceView.Of(p, store.HasSecret(key), runs.Get(key) is not null, withIgnored: true)) : Results.NotFound());

        g.MapPut("/{key}", (string key, SourceInput input, SourceStore store, SourceRuns runs, Workspace ws, HttpRequest req) =>
        {
            var existing = store.Get(key);
            var (profile, error) = ws.Read((repo, _) => SourceValidator.Build(key, input, existing, repo.Metamodel));
            if (profile is null) return Results.BadRequest(new { error });
            store.Put(profile, input.Secret);
            ws.Write(repo => { repo.Log(User(req), existing is null ? "Source externe créée" : "Source externe modifiée", null, $"{profile.Label} ({profile.Key})"); return 0; });
            return Results.Ok(SourceView.Of(profile, store.HasSecret(key), runs.Get(key) is not null, withIgnored: true));
        });

        g.MapDelete("/{key}", (string key, SourceStore store, SourceRuns runs, Workspace ws, HttpRequest req) =>
        {
            if (!SourceValidator.IsValidKey(key) || store.Get(key) is not { } profile) return Results.NotFound();
            store.Delete(key);
            runs.Delete(key);
            // Les liens déjà posés sur les objets restent : ils servent si la source est recréée.
            ws.Write(repo => { repo.Log(User(req), "Source externe retirée", null, $"{profile.Label} ({profile.Key})"); return 0; });
            return Results.NoContent();
        });

        g.MapPost("/{key}/test", async (string key, SourceStore store, SourceService service, CancellationToken ct) =>
        {
            if (store.Get(key) is not { } profile) return Results.NotFound();
            if (profile.Kind != SourceProfile.KindEasyVista) return Results.BadRequest(new { error = "Le test de connexion ne concerne que les sources EasyVista." });
            var (ok, total, message) = await service.TestAsync(profile, ct);
            return Results.Ok(new { ok, total, message });
        });

        g.MapPost("/{key}/preview", (string key, string? name, SourceStore store, SourceService service, HttpRequest req, CancellationToken ct) =>
            WithSource(key, store, async profile =>
            {
                var table = await service.ReadAsync(profile, await BodyOrNull(req), name, ReadSize(profile), ct);
                var set = SourceRecords.From(table.Rows, profile.Mapping);
                return Results.Ok(new
                {
                    columns = table.Columns,
                    rows = table.Rows.Take(PreviewRows),
                    rowCount = table.Rows.Count,
                    total = table.Total,
                    truncated = table.Truncated,
                    missingColumns = table.Rows.Count == 0 ? new List<string>() : SourceRecords.MissingColumns(table.Rows, profile.Mapping),
                    issues = table.Issues.Concat(set.Issues).ToList(),
                });
            }));

        g.MapPost("/{key}/run", (string key, string? name, string? status, int? skip, int? take, SourceStore store, SourceService service, HttpRequest req, CancellationToken ct) =>
            WithSource(key, store, async profile =>
            {
                var run = await service.RunAsync(profile, await BodyOrNull(req), name, User(req), ct);
                return Results.Ok(ReportView(run, status, skip, take));
            }));

        g.MapGet("/{key}/report", (string key, string? status, int? skip, int? take, SourceRuns runs) =>
            runs.Get(key) is { } run ? Results.Ok(ReportView(run, status, skip, take)) : Results.NotFound(new { error = "Aucun rapprochement n'a encore été lancé pour cette source." }));

        g.MapPost("/{key}/apply", (string key, ApplyRequest request, SourceService service, HttpRequest req) =>
        {
            if (request.Decisions.Any(d => !DecisionAction.All.Contains(d.Action)))
                return Results.BadRequest(new { error = "Action de décision inconnue : lier, creer, ignorer ou delier." });
            if (request.Decisions.Count > 100_000) return Results.BadRequest(new { error = "Trop de décisions dans une même requête." });
            return service.Apply(key, request, User(req)) is { } applied
                ? Results.Ok(new { result = applied.Result, report = ReportView(applied.Run, null, null, null) })
                : Results.NotFound(new { error = "Source inconnue ou aucun rapprochement lancé : lancez d'abord un rapprochement." });
        });
    }

    /// <summary>Aperçu : une seule page de l'API EasyVista suffit.</summary>
    private static int ReadSize(SourceProfile profile) =>
        profile.Kind == SourceProfile.KindEasyVista && int.TryParse(profile.Connection.GetValueOrDefault("pageSize"), out var n) ? Math.Clamp(n, 1, 1000) : 100;

    private static async Task<Stream?> BodyOrNull(HttpRequest req) => req.ContentLength is 0 ? null : await Body(req);

    /// <summary>Retrouve le profil, exécute l'action et traduit les échecs d'accès à la source en 502 sans détail technique.</summary>
    private static async Task<IResult> WithSource(string key, SourceStore store, Func<SourceProfile, Task<IResult>> action)
    {
        if (store.Get(key) is not { } profile) return Results.NotFound();
        try { return await action(profile); }
        catch (SourceAccessException e) { return Results.Json(new { error = e.Message }, statusCode: StatusCodes.Status502BadGateway); }
    }

    /// <summary>Rapport et lignes filtrées par statut et paginées : une CMDB peut compter des dizaines de milliers de lignes.</summary>
    private static object ReportView(SavedRun run, string? status, int? skip, int? take)
    {
        var r = run.Report;
        var lines = status is null ? r.Lines : r.Lines.Where(l => l.Status == status).ToList();
        return new
        {
            profileKey = r.ProfileKey, at = r.At, sourceName = run.SourceName, rowsRead = run.RowsRead, sourceTotal = run.SourceTotal, truncated = run.Truncated,
            counts = r.Counts, linesTotal = lines.Count, lines = lines.Skip(Math.Max(0, skip ?? 0)).Take(Math.Clamp(take ?? DefaultTake, 1, MaxTake)),
            missing = r.Missing, unlinkedCount = r.UnlinkedCount, unlinked = r.Unlinked, issues = r.Issues, notes = r.Notes,
        };
    }
}
