using AeTool.Api.Auth;
using AeTool.Core;
using static AeTool.Api.Http;

namespace AeTool.Api.Endpoints;

/// <summary>Opérations d'administration : état du stockage et sauvegardes.</summary>
public static class AdminEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/admin/status", (Workspace ws, DataPaths paths, AuthSettings auth) =>
        {
            var (schema, journal) = ws.Read((repo, _) => (repo.SchemaVersion, repo.Journal.Count));
            var backups = ws.Backups();
            // Tous les fichiers du dossier de données : l'exploitant voit ce que les sauvegardes couvrent (référentiel et journal)
            // et ce qu'il doit sauvegarder lui-même (comptes, profils de sources, clés).
            var files = Directory.GetFiles(paths.Directory, "*", SearchOption.AllDirectories)
                .Select(f => new { Path = Path.GetRelativePath(paths.Directory, f).Replace('\\', '/'), Size = new FileInfo(f).Length })
                .OrderBy(f => f.Path, StringComparer.Ordinal).ToList();
            return new
            {
                SchemaVersion = schema, CurrentSchemaVersion = Repository.CurrentSchemaVersion,
                DataDirectory = paths.Directory, Files = files, TotalSize = files.Sum(f => f.Size),
                BackupCount = backups.Count, LastBackup = backups.FirstOrDefault(), JournalEntries = journal, AuthMode = auth.Mode,
                Covered = new[] { Path.GetFileName(paths.RepositoryFile), Storage.JournalFile.FileName },
            };
        });

        api.MapGet("/admin/backups", (Workspace ws) => ws.Backups());

        api.MapPost("/admin/backups", (Workspace ws, HttpRequest req) => ws.Backup(BackupReasons.Manual, User(req)));

        // Le nom est validé contre un motif strict puis cherché dans le dossier des sauvegardes : aucun chemin fourni n'est jamais ouvert.
        api.MapGet("/admin/backups/{name}", (Workspace ws, string name) =>
            ws.BackupPath(name) is { } path ? Results.File(path, "application/gzip", name) : Results.NotFound(new { error = "Sauvegarde introuvable." }));

        api.MapPost("/admin/backups/{name}/restore", (Workspace ws, string name, HttpRequest req) =>
        {
            try { return ws.Restore(name, User(req)) is { } info ? Results.Ok(info) : Results.NotFound(new { error = "Sauvegarde introuvable." }); }
            catch (Exception e) when (e is System.Text.Json.JsonException or InvalidDataException or IOException)
            {
                return Results.BadRequest(new { error = "La sauvegarde est illisible ; rien n'a été modifié." });
            }
        });
    }

    /// <summary>Routes réservées à l'environnement de développement.</summary>
    public static void MapDevelopment(RouteGroupBuilder api)
    {
        // Remise à zéro du référentiel.
        api.MapPost("/admin/reset", (Workspace ws, HttpRequest req) => { ws.Reset(User(req)); return Results.NoContent(); });
    }
}
