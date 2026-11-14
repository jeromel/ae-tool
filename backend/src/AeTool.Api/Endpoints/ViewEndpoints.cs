using System.Text;
using System.Text.Json;
using AeTool.Connectors;
using AeTool.Core;
using static AeTool.Api.Http;

namespace AeTool.Api.Endpoints;

/// <summary>Dossiers et vues (diagrammes).</summary>
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
    }
}
