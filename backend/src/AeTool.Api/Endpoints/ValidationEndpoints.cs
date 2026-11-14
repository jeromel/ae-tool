using System.Security.Claims;
using AeTool.Core;
using static AeTool.Api.Http;

namespace AeTool.Api.Endpoints;

/// <summary>Circuit de validation des objets (EF-51) : proposer, valider, refuser, et la file des objets à valider.</summary>
public static class ValidationEndpoints
{
    public sealed record RejectInput(string? Comment);
    public sealed record BatchInput(List<string>? Ids);

    /// <summary>Vrai si l'auteur de la requête est valideur ou administrateur (le mode ouvert donne le rôle d'administrateur).</summary>
    public static bool IsValidator(HttpRequest req) => Auth.Roles.AtLeast(req.HttpContext.User.FindFirstValue(ClaimTypes.Role), Auth.Roles.Validator);

    public static void Map(RouteGroupBuilder api)
    {
        api.MapPost("/objects/{id}/propose", (Workspace ws, string id, HttpRequest req) => ws.Write(repo =>
            Move(repo, id, "Objet proposé", (o, now) => Validation.Propose(o, User(req), now), User(req))));

        api.MapPost("/validation/objects/{id}/validate", (Workspace ws, string id, HttpRequest req) => ws.Write(repo =>
            Move(repo, id, "Objet validé", (o, now) => Validation.Validate(o, User(req), now), User(req))));

        api.MapPost("/validation/objects/{id}/reject", (Workspace ws, string id, RejectInput input, HttpRequest req) => ws.Write(repo =>
            Move(repo, id, "Objet refusé", (o, now) => Validation.Reject(o, input.Comment, now), User(req), extra: input.Comment?.Trim())));

        // Chaque objet est traité pour lui-même : un objet déjà validé n'empêche pas de valider les autres.
        api.MapPost("/validation/validate", (Workspace ws, BatchInput input, HttpRequest req) => ws.Write(repo =>
        {
            if (input.Ids is not { Count: > 0 }) return Results.BadRequest(new { error = "Aucun objet à valider." });
            var refused = new List<object>();
            var validated = 0;
            foreach (var id in input.Ids.Distinct())
            {
                var result = Move(repo, id, "Objet validé", (o, now) => Validation.Validate(o, User(req), now), User(req));
                if (result is IStatusCodeHttpResult { StatusCode: 200 }) validated++;
                else refused.Add(new { id, error = repo.Objects.ContainsKey(id) ? Reason(repo.Objects[id]) : "Objet introuvable." });
            }
            return Results.Ok(new { validated, refused });
        }));

        api.MapGet("/validation/pending", (Workspace ws) => ws.Read((repo, _) =>
        {
            var pending = repo.ActiveObjects.Where(o => o.ValidationStatus == Validation.Proposed)
                .OrderBy(o => o.ProposedAt ?? DateTime.MaxValue).ToList();
            var recent = repo.Journal.Where(e => e.TargetKind == JournalKinds.Object && e.Changes is not null)
                .GroupBy(e => e.TargetId ?? "").Where(g => pending.Any(o => o.Id == g.Key)).ToDictionary(g => g.Key, g => g.TakeLast(3).Reverse().ToList());
            return new
            {
                Total = pending.Count,
                Items = pending.Select(o => new
                {
                    Brief = Dto.Brief(repo, o), o.ProposedBy, o.ProposedAt, o.UpdatedAt, o.UpdatedBy,
                    Recent = recent.GetValueOrDefault(o.Id)?.Select(e => new { e.At, e.User, e.Action, e.Changes }),
                }),
            };
        }));
    }

    /// <summary>Applique une transition à un objet, journalise l'avant/après, et traduit un refus en réponse 409.</summary>
    private static IResult Move(Repository repo, string id, string action, Func<ArchObject, DateTime, Transition> run, string user, string? extra = null)
    {
        if (!repo.Objects.TryGetValue(id, out var o)) return Results.NotFound();
        var before = ChangeSet.Capture(o);
        var outcome = run(o, DateTime.UtcNow);
        if (!outcome.Ok) return Results.Json(new { error = outcome.Error }, statusCode: StatusCodes.Status409Conflict);
        var changes = ChangeSet.Between(repo.Metamodel, before, ChangeSet.Capture(o));
        if (extra is not null) changes.Add(new FieldChange("Commentaire du refus", null, extra));
        repo.Log(user, action, JournalKinds.Object, id, changes, o.Name);
        return Results.Ok(o);
    }

    // Le message d'un refus de lot : on rejoue le contrôle sans rien modifier.
    private static string Reason(ArchObject o) => o.Archived ? "L'objet est archivé." : o.ValidationStatus == Validation.Validated ? "L'objet est déjà validé." : "Transition impossible.";
}
