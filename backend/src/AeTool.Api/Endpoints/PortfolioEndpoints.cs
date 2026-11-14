using System.Text;
using System.Text.Json;
using AeTool.Connectors;
using AeTool.Core;
using static AeTool.Api.Http;

namespace AeTool.Api.Endpoints;

/// <summary>Portefeuille applicatif et notation d'urbanisation.</summary>
public static class PortfolioEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/portfolio", (Workspace ws) => ws.Read((repo, an) => new
        {
            Grid = repo.Metamodel.Scoring,
            Rows = repo.Assessments.Values.Where(a => repo.Objects.TryGetValue(a.ObjectId, out var o) && !o.Archived).Select(a =>
            {
                var o = repo.Objects[a.ObjectId];
                return new
                {
                    o.Id, o.Name, UrbaId = o.Get(UrbaImporter.IdKey),
                    Domain = o.Get(UrbaImporter.Prefix + "Domaine métier"), State = o.Get(UrbaImporter.Prefix + "État actuel"),
                    KeyPerson = o.Get(UrbaImporter.Prefix + "Dépendance personne clé"),
                    Database = o.Get(UrbaImporter.Prefix + "Base de données / référentiel utilisé"),
                    Horizon = o.Get(UrbaImporter.Prefix + "Horizon connu"),
                    InArchi = o.Source == "archi",
                    a.Cm, a.Need, a.RetainedDecision,
                    Result = ScoringEngine.Evaluate(repo.Metamodel.Scoring, a, an.IsRetired(o)),
                };
            }).OrderBy(r => r.UrbaId),
        }));

        api.MapGet("/portfolio/{id}", (Workspace ws, string id) => ws.Read((repo, an) =>
        {
            if (!repo.Objects.TryGetValue(id, out var o)) return Results.NotFound();
            var a = repo.Assessments.GetValueOrDefault(id) ?? ScoringEngine.Blank(repo.Metamodel.Scoring, id);
            return Results.Ok(new { Assessment = a, Result = ScoringEngine.Evaluate(repo.Metamodel.Scoring, a, an.IsRetired(o)), Grid = repo.Metamodel.Scoring });
        }));

        api.MapPut("/portfolio/{id}", (Workspace ws, string id, Assessment input, HttpRequest req) => ws.Write(repo =>
        {
            if (!repo.Objects.TryGetValue(id, out var o)) return Results.NotFound();
            if (input.Cm is < 1 or > 4) return Results.BadRequest(new { error = "La criticité métier (CM) va de 1 à 4." });
            var allowed = new[] { "0", "0.5", "1", "N/A", "NC" };
            foreach (var e in input.Evidences)
            {
                e.Value = (e.Value ?? "NC").Trim().Replace(',', '.');
                if (!allowed.Contains(e.Value)) return Results.BadRequest(new { error = $"Preuve « {e.SubCriterion} » : valeur {e.Value} hors liste (0, 0.5, 1, N/A, NC)." });
            }
            input.ObjectId = id;
            ScoringEngine.Complete(repo.Metamodel.Scoring, input);
            repo.Assessments[id] = input;
            repo.Log(User(req), "Notation modifiée", id, o.Name);
            return Results.Ok(new { Assessment = input, Result = ScoringEngine.Evaluate(repo.Metamodel.Scoring, input, new Analyzer(repo).IsRetired(o)) });
        }));
    }
}
