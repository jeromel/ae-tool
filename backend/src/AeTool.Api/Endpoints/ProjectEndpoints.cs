using System.Text;
using System.Text.Json;
using AeTool.Connectors;
using AeTool.Core;
using static AeTool.Api.Http;

namespace AeTool.Api.Endpoints;

/// <summary>Projets, impacts et adhérences.</summary>
public static class ProjectEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/projects", (Workspace ws) => ws.Read((repo, an) => an.Projects().Select(p =>
        {
            var impacts = an.ImpactsOf(p.Id).ToList();
            return new
            {
                p.Id, p.Name, Status = p.Get("project.status"), ExternalId = p.Get("project.id"),
                Impacts = impacts.Count,
                ToQualify = impacts.Count(i => i.Nature == ImpactNature.ToQualify),
                Diagrams = an.ProjectDiagramIds(p).Count,
            };
        })));

        api.MapGet("/projects/adherences", (Workspace ws) => ws.Read((repo, an) => new
        {
            Projects = an.Projects().Select(p => new { p.Id, p.Name }),
            Pairs = an.Adherences().Select(a => new
            {
                a.ProjectA, a.ProjectB,
                Shared = a.SharedObjectIds.Select(i => Dto.Brief(repo, repo.Objects[i])),
                Linked = a.LinkedPairs.Select(p => new[] { repo.Objects[p[0]].Name, repo.Objects[p[1]].Name }),
            }),
        }));

        api.MapGet("/projects/{id}", (Workspace ws, string id) => ws.Read((repo, an) =>
        {
            if (!repo.Objects.TryGetValue(id, out var p)) return Results.NotFound();
            return Results.Ok(new
            {
                Project = Dto.Brief(repo, p),
                p.Documentation,
                Properties = p.Properties.Select(x => new { Label = repo.Metamodel.Property(x.Key)?.Label ?? x.Key, x.Value }),
                Natures = ImpactNature.All,
                Diagrams = an.ProjectDiagramIds(p).Select(d => new { Id = d, repo.Diagrams[d].Name }),
                Impacts = repo.Impacts.Where(i => i.ProjectId == id && repo.Objects.TryGetValue(i.ObjectId, out var o) && !o.Archived)
                    .Select(i => new { Object = Dto.Brief(repo, repo.Objects[i.ObjectId]), i.Nature, i.Origin, i.Comment, Views = i.DiagramIds.Count })
                    .OrderBy(i => i.Object.LayerLabel).ThenBy(i => i.Object.Name),
            });
        }));

        api.MapPut("/projects/{id}/impacts/{objectId}", (Workspace ws, string id, string objectId, ImpactInput input, HttpRequest req) => ws.Write(repo =>
        {
            if (!repo.Objects.ContainsKey(id) || !repo.Objects.ContainsKey(objectId)) return Results.NotFound();
            if (!ImpactNature.All.Contains(input.Nature)) return Results.BadRequest(new { error = $"Nature inconnue : {input.Nature}" });
            var impact = repo.Impacts.FirstOrDefault(i => i.ProjectId == id && i.ObjectId == objectId);
            var (oldNature, oldComment) = (impact?.Nature, impact?.Comment);
            if (impact is null) repo.Impacts.Add(impact = new ProjectImpact { ProjectId = id, ObjectId = objectId });
            impact.Nature = input.Nature;
            impact.Comment = input.Comment;
            var changes = new List<FieldChange>();
            if (oldNature != input.Nature) changes.Add(new FieldChange("Nature de l'impact", oldNature, input.Nature));
            if (!string.IsNullOrWhiteSpace(oldComment) || !string.IsNullOrWhiteSpace(input.Comment))
                if ((oldComment ?? "").Trim() != (input.Comment ?? "").Trim()) changes.Add(new FieldChange("Commentaire de l'impact", oldComment, input.Comment));
            if (changes.Count > 0)
                repo.Log(User(req), "Impact projet qualifié", JournalKinds.Impact, id, changes, $"{repo.Objects[objectId].Name} : {input.Nature}");
            return Results.Ok(impact);
        }));
    }
}
