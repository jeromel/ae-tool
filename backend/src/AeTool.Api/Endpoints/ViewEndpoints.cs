using System.Text;
using System.Text.Json;
using AeTool.Connectors;
using AeTool.Core;
using static AeTool.Api.Http;

namespace AeTool.Api.Endpoints;

/// <summary>Dossiers et vues (diagrammes).</summary>
public sealed record LayoutInput(Dictionary<string, Box>? Boxes);

public static class ViewEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/folders", (Workspace ws) => ws.Read((repo, _) =>
        {
            object Node(Folder f) => new
            {
                f.Label,
                Children = f.Children.Select(Node),
                Diagrams = f.Entries.Where(e => repo.Diagrams.TryGetValue(e, out var d) && !d.Archived)
                    .Select(e => new { Id = e, repo.Diagrams[e].Name }).OrderBy(d => d.Name),
                ObjectCount = f.Entries.Count(repo.Objects.ContainsKey),
            };
            var filed = new HashSet<string>();
            void Collect(Folder f) { foreach (var e in f.Entries) filed.Add(e); foreach (var c in f.Children) Collect(c); }
            foreach (var f in repo.Folders) Collect(f);
            return new
            {
                Folders = repo.Folders.Select(Node),
                Unfiled = repo.ActiveDiagrams.Where(d => !filed.Contains(d.Id)).Select(d => new { d.Id, d.Name }).OrderBy(d => d.Name),
            };
        }));

        api.MapGet("/diagrams/{id}", (Workspace ws, string id, string? visuel) => ws.Read((repo, _) =>
            repo.Diagrams.TryGetValue(id, out var d) ? Results.Ok(Dto.Diagram(repo, d, visuel == "metamodele")) : Results.NotFound()));

        // Disposition retouchée : elle remplace la précédente en entier ; DELETE rend la disposition d'origine.
        api.MapPut("/diagrams/{id}/layout", (Workspace ws, HttpRequest req, string id, LayoutInput input) => ws.Write(repo =>
        {
            if (!repo.Diagrams.TryGetValue(id, out var d) || d.Archived) return Results.NotFound();
            var nodes = d.AllNodes().ToDictionary(n => n.Id);
            var boxes = input.Boxes ?? new Dictionary<string, Box>();
            var unknown = boxes.Keys.Where(k => !nodes.ContainsKey(k)).ToList();
            if (unknown.Count > 0) return Results.BadRequest(new { error = $"Nœuds absents de la vue : {string.Join(", ", unknown.Take(5))}." });
            if (boxes.Values.Any(b => b.W < 8 || b.H < 8 || b.W > 20000 || b.H > 20000 || Math.Abs(b.X) > 100000 || Math.Abs(b.Y) > 100000))
                return Results.BadRequest(new { error = "Taille ou position hors limites." });
            // Seuls les nœuds réellement déplacés ou redimensionnés sont gardés.
            var changed = boxes.Where(kv => kv.Value != new Box(nodes[kv.Key].X, nodes[kv.Key].Y, nodes[kv.Key].W, nodes[kv.Key].H))
                               .ToDictionary(kv => kv.Key, kv => kv.Value);
            d.Layout = changed.Count == 0 ? null : changed;
            d.LayoutBy = changed.Count == 0 ? null : Http.User(req);
            d.LayoutAt = changed.Count == 0 ? null : DateTime.UtcNow;
            repo.Log(Http.User(req), "Vue réorganisée", "vue", d.Id, null, $"« {d.Name} » : {changed.Count} nœud(s) déplacé(s) ou redimensionné(s)");
            return Results.Ok(Dto.Diagram(repo, d, false));
        }));

        api.MapDelete("/diagrams/{id}/layout", (Workspace ws, HttpRequest req, string id) => ws.Write(repo =>
        {
            if (!repo.Diagrams.TryGetValue(id, out var d) || d.Archived) return Results.NotFound();
            d.Layout = null; d.LayoutBy = null; d.LayoutAt = null;
            repo.Log(Http.User(req), "Disposition d'origine rendue", "vue", d.Id, null, $"« {d.Name} »");
            return Results.Ok(Dto.Diagram(repo, d, false));
        }));
    }
}
