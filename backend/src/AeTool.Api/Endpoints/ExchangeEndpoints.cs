using System.Text;
using System.Text.Json;
using AeTool.Connectors;
using AeTool.Core;
using static AeTool.Api.Http;

namespace AeTool.Api.Endpoints;

/// <summary>Imports (Archi, classeur d'urbanisation) et exports.</summary>
public static class ExchangeEndpoints
{
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

        api.MapGet("/import/reports", (Workspace ws) => ws.Reports);

        api.MapGet("/export/archimate", (Workspace ws, string? diagram) => ws.Read((repo, _) =>
        {
            var doc = ArchimateExchange.Export(repo, diagram is null ? null : new[] { diagram });
            using var ms = new MemoryStream();
            using (var writer = System.Xml.XmlWriter.Create(ms, new System.Xml.XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) }))
                doc.Save(writer);
            return Results.File(ms.ToArray(), "application/xml", diagram is null ? "referentiel-archimate.xml" : "vue-archimate.xml");
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
