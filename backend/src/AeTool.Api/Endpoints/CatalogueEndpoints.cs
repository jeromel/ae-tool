using AeTool.Api.Sources;
using AeTool.Connectors;
using AeTool.Core;
using static AeTool.Api.Http;

namespace AeTool.Api.Endpoints;

public sealed record CatalogueImportInput(string? Name, string? Content, Dictionary<string, CatalogueDecision>? Decisions);
public sealed record ReclassInput(List<string>? Ids);

/// <summary>
/// Catalogue des données et des flux : import en deux temps, parcours d'une donnée, contrôles de cohérence, registre des
/// données personnelles, reclassement des partages de fichiers, comptage des postes de travail par profil.
/// </summary>
public static class CatalogueEndpoints
{
    public const string BackupReason = "import-catalogue";

    private static string FileName(string? name)
    {
        var n = Path.GetFileName((name ?? "").Replace('\\', '/')).Trim();
        return n.Length == 0 ? "catalogue.xlsx" : n.Length > 120 ? n[..120] : n;
    }

    public static void Map(RouteGroupBuilder api)
    {
        api.MapPost("/import/catalogue/analyse", async (Workspace ws, HttpRequest req, string? name) =>
        {
            var body = await Body(req);
            try { return Results.Ok(ws.Read((repo, _) => CatalogueImporter.Analyse(body, FileName(name), repo))); }
            catch (InvalidDataException e) { return Results.BadRequest(new { error = e.Message }); }
        });

        api.MapPost("/import/catalogue", (Workspace ws, HttpRequest req, CatalogueImportInput input) =>
        {
            byte[] bytes;
            try { bytes = Convert.FromBase64String(input.Content ?? ""); }
            catch (FormatException) { return Results.BadRequest(new { error = "Contenu du fichier illisible." }); }
            if (bytes.Length == 0) return Results.BadRequest(new { error = "Fichier vide." });
            if (input.Decisions?.Values.Any(d => d.Action is not (CatalogueItem.Link or CatalogueItem.Create or CatalogueItem.Ignore)) == true)
                return Results.BadRequest(new { error = "Action inconnue : lier, créer ou ignorer." });
            return Results.Ok(ws.Import(repo => CatalogueImporter.Apply(new MemoryStream(bytes), FileName(input.Name), repo, User(req), input.Decisions), BackupReason));
        });

        // Lectures : elles suivent l'état du SI regardé (existant ou étape d'un scénario).
        api.MapGet("/parcours/{id}", (Workspace ws, string id, bool? derivees) =>
            ws.Read((repo, _) => new LineageReader(repo).Read(id, derivees ?? false)));

        // Enregistrer un parcours comme vue (contributeur, comme toute réorganisation de vue) : elle s'exporte ensuite vers Archi.
        api.MapPost("/diagrams/parcours/{id}", (Workspace ws, string id, bool? derivees, HttpRequest req) => ws.Write(repo =>
        {
            var lineage = new LineageReader(repo).Read(id, derivees ?? false);
            if (!lineage.Focus.Supported) return Results.BadRequest(new { error = lineage.Focus.Message ?? "Parcours indisponible pour cet élément." });
            var d = LineageView.Save(repo, lineage, User(req));
            return Results.Ok(new { d.Id, d.Name, nodes = d.Nodes.Count, connections = d.Connections.Count });
        }));

        api.MapGet("/catalogue/qualite", (Workspace ws) => ws.Read((repo, _) => new CatalogueQuality(repo).Check()));

        api.MapGet("/catalogue/registre", (Workspace ws) => ws.Read((repo, _) => new CatalogueQuality(repo).Register()));

        // Nœuds nommés « Partage… » → partages de fichiers. La propriété de sous-type est posée : l'export vers Archi la reprend.
        api.MapPost("/catalogue/partages/reclasser", (Workspace ws, ReclassInput input, HttpRequest req) => ws.Write(repo =>
        {
            var meta = repo.Metamodel;
            var shareType = meta.FirstTypeWith(Roles.FileShare)?.Key;
            if (shareType is null) return Results.BadRequest(new { error = "Aucun type du métamodèle ne porte le rôle « partage de fichiers »." });
            var nodeTypes = meta.Transcoding.Elements.Where(r => r.ArchimateType == "Node" && !r.Conditional).Select(r => r.TypeKey).ToHashSet();
            var wanted = input.Ids?.ToHashSet();
            var moved = new List<string>();
            foreach (var o in repo.ActiveObjects.Where(o => nodeTypes.Contains(o.TypeKey) && o.Name.TrimStart().StartsWith("Partage", StringComparison.OrdinalIgnoreCase)
                                                             && (wanted is null || wanted.Contains(o.Id))).ToList())
            {
                var before = ChangeSet.Capture(o);
                o.TypeKey = shareType;
                o.Set(CatalogueModel.Subtype, CatalogueModel.FileShareSubtype);
                o.UpdatedAt = DateTime.UtcNow; o.UpdatedBy = User(req);
                repo.Log(User(req), "Objet reclassé en partage de fichiers", JournalKinds.Object, o.Id, ChangeSet.Between(meta, before, ChangeSet.Capture(o)), o.Name);
                moved.Add(o.Name);
            }
            return Results.Ok(new { count = moved.Count, names = moved });
        }));

        // Postes de travail : le dernier comptage et les profils, lisibles par tous.
        api.MapGet("/postes", (Workspace ws) => ws.Read((repo, _) => new
        {
            census = repo.Workstations,
            profiles = repo.ActiveObjects.Where(o => repo.Metamodel.IsA(o.TypeKey, Roles.Workstation))
                .Select(o => new { o.Id, o.Name, Code = o.Get(CatalogueModel.Code) }).OrderBy(o => o.Code ?? "~").ThenBy(o => o.Name),
        }));

        // Comptage : lit la source (fichier reçu ou API) et range les postes par profil d'après le champ pivot.
        api.MapPost("/sources/{key}/postes", async (string key, string? name, SourceStore store, SourceService service, Workspace ws, HttpRequest req, CancellationToken ct) =>
        {
            if (store.Get(key) is not { } profile) return Results.NotFound();
            if (profile.Workstations is not { } settings || string.IsNullOrWhiteSpace(settings.PivotField))
                return Results.BadRequest(new { error = "Réglez d'abord le comptage des postes : le champ qui porte le code du profil de poste." });
            try
            {
                var table = await service.ReadAsync(profile, req.ContentLength is 0 ? null : await Body(req), name, SourceService.MaxRecords, ct);
                if (table.Rows.Count > 0 && !table.Rows.Any(r => r.ContainsKey(settings.PivotField)))
                    return Results.BadRequest(new { error = $"Le champ pivot « {settings.PivotField} » est absent des éléments lus. Colonnes reçues : {string.Join(", ", table.Columns.Take(30))}." });
                var census = ws.Write(repo =>
                {
                    var c = WorkstationCounter.Count(repo, table.Rows, settings, key, profile.Mapping.IdField, profile.Mapping.NameField, User(req));
                    repo.Workstations = c;
                    repo.Log(User(req), "Comptage des postes de travail", null,
                        $"{profile.Label} : {c.Read} poste(s) lu(s), {c.ByProfile.Values.Sum(x => x.Total)} rattaché(s), {c.Unclassified.Total} sans profil, {c.UnknownCodes.Values.Sum(x => x.Total)} avec un code inconnu");
                    return c;
                });
                return Results.Ok(new { census, truncated = table.Truncated, issues = table.Issues });
            }
            catch (SourceAccessException e) { return Results.Json(new { error = e.Message }, statusCode: StatusCodes.Status502BadGateway); }
        });
    }
}
