using System.Text;
using System.Text.Json;
using AeTool.Connectors;
using AeTool.Core;
using static AeTool.Api.Http;

namespace AeTool.Api.Endpoints;

/// <summary>Imports (Archi, classeur d'urbanisation) et exports.</summary>
public sealed record DrawioImportInput(string? Name, string? Content, Dictionary<string, DrawioDecision>? Decisions);

public static class ExchangeEndpoints
{
    /// <summary>Nom de fichier affiché et gardé sur les vues : sans chemin, et jamais vide.</summary>
    private static string FileName(string? name)
    {
        var n = Path.GetFileName((name ?? "").Replace('\\', '/')).Trim();
        return n.Length == 0 ? "dessin.drawio" : n.Length > 120 ? n[..120] : n;
    }

    /// <summary>État de « Pas encore dans Archi » : nombres toujours, listes pour la partie repliée, exclusions comptées.</summary>
    private static object PendingSummary(Repository repo, ArchiPending p)
    {
        var meta = repo.Metamodel;
        string NameOf(string id) => repo.Objects.TryGetValue(id, out var o) ? o.Name : repo.Relationships.TryGetValue(id, out var r) ? r.Name ?? meta.RelType(r.TypeKey)?.Label ?? r.TypeKey : id;
        return new
        {
            p.LastImport, p.NeverImported,
            Objects = p.Objects.Select(o => new { Brief = Dto.Brief(repo, o), o.ValidatedBy, o.ValidatedAt }),
            Relations = p.Relations.Select(r => new
            {
                r.Id, r.Name, TypeLabel = meta.RelType(r.TypeKey)?.Label ?? r.TypeKey, r.Source,
                From = NameOf(r.SourceId), To = NameOf(r.TargetId),
            }).Concat(p.Impacts.Select(i => new
            {
                Id = ArchiPending.ImpactId(i), Name = (string?)$"Impact : {i.Nature}", TypeLabel = "Impact de projet", Source = "impact",
                From = NameOf(i.ProjectId), To = NameOf(i.ObjectId),
            })),
            p.ExcludedProposed, p.ExcludedDrafts, p.ExcludedRelations,
            CitedObjects = p.CitedObjects.Count,
            // Vues créées ici (dessins draw.io, parcours enregistrés) : elles ne partent pas, l'écran le dit.
            ViewsHere = repo.ActiveDiagrams.Count(d => d.Source != ArchimateExchange.SourceTag),
        };
    }

    public static void Map(RouteGroupBuilder api)
    {
        api.MapPost("/import/archimate", async (Workspace ws, HttpRequest req) =>
        {
            var body = await Body(req);
            return ws.Import(repo => ArchimateExchange.Import(body, repo, User(req)), BackupReasons.ArchiImport);
        });

        api.MapPost("/import/urba", async (Workspace ws, HttpRequest req) =>
        {
            var body = await Body(req);
            return ws.Import(repo => UrbaImporter.Import(body, repo, User(req)), BackupReasons.UrbaImport);
        });

        // draw.io en deux temps : l'analyse propose sans rien écrire, l'import applique les choix.
        api.MapPost("/import/drawio/analyse", async (Workspace ws, HttpRequest req, string? name) =>
        {
            var body = await Body(req);
            try { return Results.Ok(ws.Read((repo, _) => DrawioImporter.Analyse(body, FileName(name), repo))); }
            catch (InvalidDataException e) { return Results.BadRequest(new { error = e.Message }); }
        });

        api.MapPost("/import/drawio", async (Workspace ws, HttpRequest req, DrawioImportInput input) =>
        {
            byte[] bytes;
            try { bytes = Convert.FromBase64String(input.Content ?? ""); }
            catch (FormatException) { return Results.BadRequest(new { error = "Contenu du fichier illisible." }); }
            if (bytes.Length == 0) return Results.BadRequest(new { error = "Fichier vide." });
            var name = FileName(input.Name);
            return Results.Ok(ws.Import(repo => DrawioImporter.Apply(new MemoryStream(bytes), name, repo, User(req), input.Decisions), BackupReasons.DrawioImport));
        });

        api.MapGet("/import/reports", (Workspace ws) => ws.Reports);

        api.MapGet("/export/archimate", (Workspace ws, string? diagram) => ws.Read((repo, _) =>
        {
            var doc = ArchimateExchange.Export(repo, diagram is null ? null : new[] { diagram });
            using var ms = new MemoryStream();
            using (var writer = System.Xml.XmlWriter.Create(ms, new System.Xml.XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) }))
                doc.Save(writer);
            return Results.File(ms.ToArray(), "application/xml", diagram is null ? "referentiel-archimate.xml" : "vue-archimate.xml");
        }));

        // Export B, « Pas encore dans Archi » (maquette de Camille, décisions 014 et 015) : l'état, puis le fichier. Sous
        // /api/export, donc fermé au périmètre « métier » : la sélection lit tout le référentiel, pas la lecture filtrée.
        api.MapGet("/export/a-reporter", (Workspace ws) => ws.Read((repo, _) => PendingSummary(repo, ArchiPending.Of(repo))));

        api.MapGet("/export/a-reporter/fichier", (Workspace ws) => ws.Read((repo, _) =>
        {
            var pending = ArchiPending.Of(repo);
            if (pending.NeverImported)
                return Results.Json(new { error = "Aucun modèle Archi n'a encore été importé : utilisez l'export complet." }, statusCode: StatusCodes.Status409Conflict);
            if (pending.IsEmpty)
                return Results.Json(new { error = "Rien à reporter : tout ce qui a été créé et validé ici est déjà dans Archi." }, statusCode: StatusCodes.Status409Conflict);
            var doc = ArchimateExchange.Export(repo, pending: pending);
            using var ms = new MemoryStream();
            using (var writer = System.Xml.XmlWriter.Create(ms, new System.Xml.XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) }))
                doc.Save(writer);
            return Results.File(ms.ToArray(), "application/xml", "pas-encore-dans-archi.xml");
        }));

        api.MapGet("/export/json", (Workspace ws) => ws.Read((repo, _) =>
            Results.File(JsonSerializer.SerializeToUtf8Bytes(repo), "application/json", "referentiel-complet.json")));

        api.MapGet("/export/objects.csv", (Workspace ws) => ws.Read((repo, _) =>
        {
            static string Csv(string? v) => "\"" + (v ?? "").Replace("\"", "\"\"") + "\"";
            var keys = repo.ActiveObjects.SelectMany(o => o.Properties.Select(p => p.Key)).Distinct().OrderBy(k => k).ToList();
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(";", new[] { "id", "type", "couche", "nom", "source", "statut" }.Concat(keys).Select(Csv)));
            foreach (var o in repo.ActiveObjects.OrderBy(o => o.TypeKey).ThenBy(o => o.Name))
                sb.AppendLine(string.Join(";", new[] { o.Id, o.TypeKey, repo.Metamodel.LayerOf(o.TypeKey), o.Name, o.Source, o.ValidationStatus }
                    .Concat(keys.Select(k => o.Get(k))).Select(Csv)));
            // BOM UTF-8 pour une ouverture directe dans Excel.
            return Results.File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray(), "text/csv", "objets.csv");
        }));
    }
}
