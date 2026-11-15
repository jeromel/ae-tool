using System.Security.Claims;
using AeTool.Core;
using static AeTool.Api.Http;
using CoreRoles = AeTool.Core.Roles;
using UserRoles = AeTool.Api.Auth.Roles;

namespace AeTool.Api.Endpoints;

public sealed record ScenarioInput(string? Name, string? Description, string? Status, string? StartScenarioId, string? StartStepId);
public sealed record StepInput(string? Name, DateOnly? Date, bool? ClearDate, string? Description, int? Order);
public sealed record EnvisionedInput(string? Name, string? TypeKey, string? Documentation, List<PropertyValue>? Properties);
public sealed record ChangeInput(string? Op, string? Target, string? ElementId, string? ReplacementId, PlannedRelation? Relation,
    string? NewName, List<PropertyValue>? Set, string? Note, EnvisionedInput? NewObject);

/// <summary>
/// Scénarios et étapes de transition des projets (EV-03, incrément 1). Les écritures portent toujours sur l'existant ;
/// la lecture du SI à une étape passe par le paramètre « etat » de n'importe quelle route de lecture.
/// </summary>
public static class TrajectoryEndpoints
{
    /// <summary>Statuts qu'on peut choisir à la main dans cet incrément ; les autres viennent de la décision du comité.</summary>
    private static readonly string[] Editable = { ScenarioStatus.Draft, ScenarioStatus.Proposed };

    public static void Map(RouteGroupBuilder api)
    {
        // États proposés au sélecteur : chaque étape des scénarios visibles, par projet.
        api.MapGet("/trajectoires/etats", (Workspace ws, HttpRequest req) => ws.ReadCurrent((repo, _) =>
            Visible(repo, req).Select(s => new
            {
                ScenarioId = s.Id, ScenarioName = s.Name, s.Status, s.ProjectId, ProjectName = repo.Objects.GetValueOrDefault(s.ProjectId)?.Name ?? s.ProjectId,
                Steps = s.Steps.OrderBy(x => x.Order).Select(x => new { x.Id, x.Name, x.Order, x.Date, x.Status }),
            }).OrderBy(s => s.ProjectName).ThenBy(s => s.ScenarioName)));

        // Ce qui distingue l'état projeté de l'existant : de quoi colorer les écrans et dire ce qui est retiré.
        api.MapGet("/trajectoires/etat", (Workspace ws, HttpRequest req, string scenario, string step) =>
        {
            if (!ws.ReadCurrent((repo, _) => Visible(repo, req).Any(s => s.Id == scenario && s.Steps.Any(x => x.Id == step))))
                return Results.NotFound(new { error = "Cet état n'existe plus ou ne vous est pas visible." });
            var p = ws.Project(scenario, step);
            object Item(string id, string mark)
            {
                var repo = p.Repo;
                if (repo.Objects.TryGetValue(id, out var o))
                {
                    var b = Dto.Brief(repo, o);
                    return new { Id = id, Kind = "objet", Name = o.Name, b.TypeLabel, b.LayerLabel, Mark = mark };
                }
                var r = repo.Relationships[id];
                var label = $"{NameOf(repo, r.SourceId)} → {NameOf(repo, r.TargetId)}";
                return new { Id = id, Kind = "relation", Name = r.Name is { Length: > 0 } n ? $"{n} ({label})" : label,
                             TypeLabel = repo.Metamodel.RelType(r.TypeKey)?.Label ?? r.TypeKey, LayerLabel = "Relations", Mark = mark };
            }
            var items = p.Marks.Select(kv => Item(kv.Key, kv.Value)).ToList();
            return Results.Ok(new
            {
                p.Label,
                Scenario = new { p.Scenario.Id, p.Scenario.Name, p.Scenario.Status, p.Scenario.ProjectId },
                Step = new { p.Step.Id, p.Step.Name, p.Step.Order, p.Step.Date },
                p.Marks, Items = items, p.Issues,
            });
        });

        api.MapGet("/projects/{id}/scenarios", (Workspace ws, HttpRequest req, string id) => ws.ReadCurrent((repo, _) =>
            Visible(repo, req).Where(s => s.ProjectId == id).OrderBy(s => s.CreatedAt).Select(s => Describe(repo, s)).ToList()));

        api.MapPost("/projects/{id}/scenarios", (Workspace ws, HttpRequest req, string id, ScenarioInput input) => ws.Write(repo =>
        {
            if (!repo.Objects.TryGetValue(id, out var project) || project.Archived || !repo.Metamodel.IsA(project.TypeKey, CoreRoles.Project))
                return Results.NotFound(new { error = "Projet introuvable." });
            if (string.IsNullOrWhiteSpace(input.Name)) return Results.BadRequest(new { error = "Donnez un nom au scénario." });
            if (input.StartScenarioId is not null)
            {
                if (!repo.Scenarios.TryGetValue(input.StartScenarioId, out var parent) || parent.Steps.All(x => x.Id != input.StartStepId))
                    return Results.BadRequest(new { error = "Point de départ introuvable." });
            }
            var now = DateTime.UtcNow;
            var s = new Scenario
            {
                Id = NewId("scn"), ProjectId = id, Name = input.Name.Trim(), Description = input.Description,
                StartScenarioId = input.StartScenarioId, StartStepId = input.StartScenarioId is null ? null : input.StartStepId,
                CreatedBy = User(req), CreatedAt = now,
                Steps = { new TrajectoryStep { Id = NewId("etp"), Name = "Étape 1", Order = 1 } },
            };
            repo.Scenarios[s.Id] = s;
            repo.Log(User(req), "Scénario créé", "scenario", s.Id, null, $"« {s.Name} » pour le projet « {project.Name} »");
            return Results.Ok(Describe(repo, s));
        }));

        api.MapPut("/scenarios/{id}", (Workspace ws, HttpRequest req, string id, ScenarioInput input) => Edit(ws, req, id, (repo, s) =>
        {
            if (input.Name is { } name) { if (name.Trim().Length == 0) return Bad("Le nom ne peut pas être vide."); s.Name = name.Trim(); }
            if (input.Description is not null) s.Description = input.Description;
            if (input.Status is { } status)
            {
                if (!Editable.Contains(status)) return Bad("Ce statut se décide en comité, pas ici.");
                s.Status = status;
            }
            return null;
        }, "Scénario modifié"));

        api.MapDelete("/scenarios/{id}", (Workspace ws, HttpRequest req, string id) => ws.Write(repo =>
        {
            if (!repo.Scenarios.TryGetValue(id, out var s)) return Results.NotFound();
            if (s.Status != ScenarioStatus.Draft && s.Status != ScenarioStatus.Proposed) return Results.Conflict(new { error = "Un scénario décidé en comité ne se supprime pas." });
            if (repo.Scenarios.Values.Any(x => x.StartScenarioId == id)) return Results.Conflict(new { error = "D'autres scénarios partent de celui-ci : supprimez-les d'abord." });
            repo.Scenarios.Remove(id);
            repo.Log(User(req), "Scénario supprimé", "scenario", id, null, $"« {s.Name} »");
            return Results.NoContent();
        }));

        api.MapPost("/scenarios/{id}/copie", (Workspace ws, HttpRequest req, string id) => ws.Write(repo =>
        {
            if (!repo.Scenarios.TryGetValue(id, out var s) || !CanSee(s, req)) return Results.NotFound();
            var copy = Projection.CloneScenario(s, NewId);
            copy.Name = s.Name + " (copie)"; copy.Status = ScenarioStatus.Draft;
            copy.CreatedBy = User(req); copy.CreatedAt = DateTime.UtcNow; copy.UpdatedAt = null; copy.UpdatedBy = null;
            repo.Scenarios[copy.Id] = copy;
            repo.Log(User(req), "Scénario copié", "scenario", copy.Id, null, $"« {copy.Name} »");
            return Results.Ok(Describe(repo, copy));
        }));

        api.MapPost("/scenarios/{id}/etapes", (Workspace ws, HttpRequest req, string id, StepInput input) => Edit(ws, req, id, (repo, s) =>
        {
            var order = s.Steps.Count == 0 ? 1 : s.Steps.Max(x => x.Order) + 1;
            s.Steps.Add(new TrajectoryStep { Id = NewId("etp"), Name = string.IsNullOrWhiteSpace(input.Name) ? $"Étape {order}" : input.Name.Trim(), Order = order, Date = input.Date, Description = input.Description });
            return null;
        }, "Étape ajoutée"));

        api.MapPut("/scenarios/{id}/etapes/{stepId}", (Workspace ws, HttpRequest req, string id, string stepId, StepInput input) => Edit(ws, req, id, (repo, s) =>
        {
            var step = s.Steps.FirstOrDefault(x => x.Id == stepId);
            if (step is null) return Results.NotFound();
            if (input.Name is { Length: > 0 } n) step.Name = n.Trim();
            if (input.Description is not null) step.Description = input.Description;
            if (input.Date is not null) step.Date = input.Date;
            if (input.ClearDate == true) step.Date = null;
            if (input.Order is { } target)
            {
                // Déplacer une étape : on la réinsère à son nouveau rang et l'on renumérote.
                var ordered = s.Steps.OrderBy(x => x.Order).Where(x => x != step).ToList();
                ordered.Insert(Math.Clamp(target - 1, 0, ordered.Count), step);
                for (var i = 0; i < ordered.Count; i++) ordered[i].Order = i + 1;
            }
            return null;
        }, "Étape modifiée"));

        api.MapDelete("/scenarios/{id}/etapes/{stepId}", (Workspace ws, HttpRequest req, string id, string stepId) => Edit(ws, req, id, (repo, s) =>
        {
            var step = s.Steps.FirstOrDefault(x => x.Id == stepId);
            if (step is null) return Results.NotFound();
            if (repo.Scenarios.Values.Any(x => x.StartStepId == stepId)) return Results.Conflict(new { error = "Un autre scénario part de cette étape." });
            if (s.Steps.Count == 1) return Bad("Un scénario garde au moins une étape.");
            s.Steps.Remove(step);
            var i = 1;
            foreach (var x in s.Steps.OrderBy(x => x.Order)) x.Order = i++;
            return null;
        }, "Étape supprimée"));

        api.MapPost("/scenarios/{id}/envisages", (Workspace ws, HttpRequest req, string id, EnvisionedInput input) => Edit(ws, req, id, (repo, s) =>
        {
            var (o, error) = NewEnvisioned(repo, input, User(req));
            if (o is null) return Bad(error!);
            s.Envisioned[o.Id] = o;
            return null;
        }, "Objet envisagé ajouté"));

        api.MapPut("/scenarios/{id}/envisages/{objectId}", (Workspace ws, HttpRequest req, string id, string objectId, EnvisionedInput input) => Edit(ws, req, id, (repo, s) =>
        {
            if (!s.Envisioned.TryGetValue(objectId, out var o)) return Results.NotFound();
            if (input.Name is { Length: > 0 } n) o.Name = n.Trim();
            if (input.TypeKey is { } t) { if (repo.Metamodel.Type(t) is null) return Bad("Type inconnu."); o.TypeKey = t; }
            if (input.Documentation is not null) o.Documentation = input.Documentation;
            if (input.Properties is not null) o.Properties = input.Properties;
            return null;
        }, "Objet envisagé modifié"));

        api.MapDelete("/scenarios/{id}/envisages/{objectId}", (Workspace ws, HttpRequest req, string id, string objectId) => Edit(ws, req, id, (repo, s) =>
        {
            if (!s.Envisioned.ContainsKey(objectId)) return Results.NotFound();
            var used = repo.Scenarios.Values.SelectMany(x => x.Steps).SelectMany(x => x.Changes).Any(c => References(c, objectId));
            if (used) return Results.Conflict(new { error = "Des changements citent cet objet : retirez-les d'abord." });
            s.Envisioned.Remove(objectId);
            return null;
        }, "Objet envisagé supprimé"));

        api.MapPost("/scenarios/{id}/etapes/{stepId}/changements", (Workspace ws, HttpRequest req, string id, string stepId, ChangeInput input) => Edit(ws, req, id, (repo, s) =>
        {
            var step = s.Steps.FirstOrDefault(x => x.Id == stepId);
            if (step is null) return Results.NotFound();
            var (change, error) = BuildChange(repo, s, input, User(req));
            if (change is null) return Bad(error!);
            step.Changes.Add(change);
            return null;
        }, "Changement ajouté"));

        api.MapDelete("/scenarios/{id}/etapes/{stepId}/changements/{changeId}", (Workspace ws, HttpRequest req, string id, string stepId, string changeId) => Edit(ws, req, id, (repo, s) =>
        {
            var step = s.Steps.FirstOrDefault(x => x.Id == stepId);
            if (step is null || step.Changes.RemoveAll(c => c.Id == changeId) == 0) return Results.NotFound();
            return null;
        }, "Changement retiré"));
    }

    // ---------------------------------------------------------------- aides

    private static string NewId(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    private static IResult Bad(string message) => Results.BadRequest(new { error = message });

    private static bool CanSee(Scenario s, HttpRequest req) =>
        s.Status != ScenarioStatus.Draft || UserRoles.AtLeast(req.HttpContext.User.FindFirstValue(ClaimTypes.Role), UserRoles.Contributor);

    /// <summary>Un lecteur ne voit pas les brouillons (décision du 15 novembre 2026).</summary>
    private static IEnumerable<Scenario> Visible(Repository repo, HttpRequest req) => repo.Scenarios.Values.Where(s => CanSee(s, req));

    /// <summary>Modification d'un scénario modifiable : tout passe par là pour le contrôle, l'horodatage et le journal.</summary>
    private static IResult Edit(Workspace ws, HttpRequest req, string id, Func<Repository, Scenario, IResult?> change, string action) => ws.Write(repo =>
    {
        if (!repo.Scenarios.TryGetValue(id, out var s)) return Results.NotFound(new { error = "Scénario introuvable." });
        if (!Editable.Contains(s.Status)) return Results.Conflict(new { error = "Ce scénario a été décidé en comité : il ne se modifie plus. Copiez-le pour proposer autre chose." });
        var failure = change(repo, s);
        if (failure is not null) return failure;
        s.UpdatedBy = User(req); s.UpdatedAt = DateTime.UtcNow;
        repo.Log(User(req), action, "scenario", s.Id, null, $"« {s.Name} »");
        return Results.Ok(Describe(repo, s));
    });

    private static (ArchObject?, string?) NewEnvisioned(Repository repo, EnvisionedInput input, string user)
    {
        if (string.IsNullOrWhiteSpace(input.Name)) return (null, "Donnez un nom à l'objet envisagé.");
        if (input.TypeKey is null || repo.Metamodel.Type(input.TypeKey) is null) return (null, "Choisissez le type de l'objet envisagé.");
        return (new ArchObject
        {
            Id = NewId("env"), Name = input.Name.Trim(), TypeKey = input.TypeKey, Documentation = input.Documentation,
            Properties = input.Properties ?? new(), Source = "scénario", ValidationStatus = "Envisagé", UpdatedBy = user, UpdatedAt = DateTime.UtcNow,
        }, null);
    }

    private static bool References(TrajectoryChange c, string id) =>
        c.ElementId == id || c.ReplacementId == id || c.Relation?.SourceId == id || c.Relation?.TargetId == id;

    /// <summary>Objets envisagés connus du scénario et de ceux dont il part.</summary>
    private static Dictionary<string, ArchObject> EnvisionedOf(Repository repo, Scenario s)
    {
        var all = new Dictionary<string, ArchObject>();
        foreach (var x in Projection.ScenariosOf(repo, s)) foreach (var (k, v) in x.Envisioned) all.TryAdd(k, v);
        return all;
    }

    private static (TrajectoryChange?, string?) BuildChange(Repository repo, Scenario s, ChangeInput input, string user)
    {
        var op = input.Op ?? "";
        if (!ChangeOps.All.Contains(op)) return (null, "Opération inconnue.");
        var envisioned = EnvisionedOf(repo, s);
        bool Known(string? id) => id is not null && (repo.Objects.TryGetValue(id, out var o) && !o.Archived || repo.Relationships.TryGetValue(id, out var r) && !r.Archived || envisioned.ContainsKey(id));
        var c = new TrajectoryChange { Id = NewId("chg"), Op = op, Target = input.Target == "relation" ? "relation" : "objet", Note = input.Note };

        // Un nouvel objet envisagé peut être créé avec le changement qui le fait entrer (ajout, ou remplaçant).
        string? Created()
        {
            if (input.NewObject is null) return null;
            var (o, error) = NewEnvisioned(repo, input.NewObject, user);
            if (o is null) return "!" + error;
            s.Envisioned[o.Id] = o;
            envisioned[o.Id] = o;
            return o.Id;
        }

        switch (op)
        {
            case ChangeOps.Add when c.Target == "relation":
                var spec = input.Relation;
                if (spec is null || repo.Metamodel.RelType(spec.TypeKey) is null) return (null, "Choisissez le type de la relation.");
                if (!Known(spec.SourceId) || !Known(spec.TargetId)) return (null, "Choisissez les deux extrémités parmi le référentiel ou les objets envisagés.");
                if (spec.SourceId == spec.TargetId) return (null, "Une relation relie deux éléments différents.");
                c.Relation = spec;
                break;
            case ChangeOps.Add:
                var created = Created();
                if (created?.StartsWith('!') == true) return (null, created[1..]);
                c.ElementId = created ?? input.ElementId;
                if (c.ElementId is null || !envisioned.ContainsKey(c.ElementId)) return (null, "On ajoute un objet envisagé : créez-le ou choisissez-le.");
                break;
            case ChangeOps.Remove:
                if (!Known(input.ElementId)) return (null, "Choisissez ce qui est retiré.");
                c.ElementId = input.ElementId;
                c.Target = repo.Relationships.ContainsKey(input.ElementId!) ? "relation" : "objet";
                break;
            case ChangeOps.Modify:
                if (!Known(input.ElementId)) return (null, "Choisissez ce qui est modifié.");
                if (string.IsNullOrWhiteSpace(input.NewName) && (input.Set is null || input.Set.Count == 0)) return (null, "Indiquez le nouveau nom ou au moins une propriété.");
                c.ElementId = input.ElementId;
                c.Target = repo.Relationships.ContainsKey(input.ElementId!) ? "relation" : "objet";
                c.NewName = string.IsNullOrWhiteSpace(input.NewName) ? null : input.NewName.Trim();
                c.Set = input.Set?.Where(v => !string.IsNullOrWhiteSpace(v.Key)).Select(v => new PropertyValue { Key = v.Key.Trim(), Value = v.Value }).ToList();
                // Valeurs d'avant, prises dans l'existant : la projection signalera un existant qui a bougé depuis.
                var props = repo.Objects.TryGetValue(c.ElementId!, out var mo) ? mo.Properties : repo.Relationships.TryGetValue(c.ElementId!, out var mr) ? mr.Properties : null;
                if (props is not null)
                {
                    c.BeforeName = mo?.Name ?? repo.Relationships.GetValueOrDefault(c.ElementId!)?.Name;
                    c.Before = (c.Set ?? new()).Select(v => new PropertyValue { Key = v.Key, Value = props.FirstOrDefault(x => x.Key == v.Key)?.Value ?? "" }).ToList();
                }
                break;
            case ChangeOps.Replace:
                if (!Known(input.ElementId) || repo.Relationships.ContainsKey(input.ElementId!)) return (null, "Choisissez l'objet remplacé.");
                var replacement = Created();
                if (replacement?.StartsWith('!') == true) return (null, replacement[1..]);
                c.ElementId = input.ElementId;
                c.ReplacementId = replacement ?? input.ReplacementId;
                if (!Known(c.ReplacementId) || c.ReplacementId == c.ElementId) return (null, "Choisissez le remplaçant, existant ou envisagé.");
                break;
        }
        return (c, null);
    }

    private static string NameOf(Repository repo, string id) =>
        repo.Objects.TryGetValue(id, out var o) ? o.Name : repo.Relationships.TryGetValue(id, out var r) ? r.Name ?? id : id;

    /// <summary>Scénario prêt à afficher : étapes, changements en clair, objets envisagés, et ce qui ne s'applique pas.</summary>
    public static object Describe(Repository repo, Scenario s)
    {
        var meta = repo.Metamodel;
        var envisioned = EnvisionedOf(repo, s);
        string Name(string? id) => id is null ? "?" : envisioned.TryGetValue(id, out var e) ? e.Name : NameOf(repo, id);
        string? TypeOf(string? id) => id is null ? null
            : envisioned.TryGetValue(id, out var e) ? meta.Type(e.TypeKey)?.Label
            : repo.Objects.TryGetValue(id, out var o) ? meta.Type(o.TypeKey)?.Label
            : repo.Relationships.TryGetValue(id, out var r) ? meta.RelType(r.TypeKey)?.Label : null;
        string Label(TrajectoryChange c) => c.Op switch
        {
            ChangeOps.Add when c.Target == "relation" => $"Ajout du lien « {Name(c.Relation!.SourceId)} → {Name(c.Relation.TargetId)} » ({meta.RelType(c.Relation.TypeKey)?.Label ?? c.Relation.TypeKey})"
                                                         + (c.Relation.Name is { Length: > 0 } n ? $" : {n}" : ""),
            ChangeOps.Add => $"Ajout de « {Name(c.ElementId)} » ({TypeOf(c.ElementId)})",
            ChangeOps.Remove => $"Retrait de « {Name(c.ElementId)} » ({TypeOf(c.ElementId)})",
            ChangeOps.Modify => $"Modification de « {Name(c.ElementId)} » : "
                                + string.Join(" ; ", new[] { c.NewName is null ? null : $"nom → {c.NewName}" }
                                    .Concat((c.Set ?? new()).Select(v => $"{v.Key} = {(string.IsNullOrEmpty(v.Value) ? "(effacé)" : v.Value)}")).Where(x => x is not null)),
            ChangeOps.Replace => $"Remplacement de « {Name(c.ElementId)} » par « {Name(c.ReplacementId)} »",
            _ => c.Op,
        };
        var start = s.StartScenarioId is not null && repo.Scenarios.TryGetValue(s.StartScenarioId, out var parent)
            ? new { ScenarioId = parent.Id, StepId = s.StartStepId, Label = $"{parent.Name} › {parent.Steps.FirstOrDefault(x => x.Id == s.StartStepId)?.Name}" }
            : null;
        return new
        {
            s.Id, s.ProjectId, s.Name, s.Description, s.Status, Start = start, s.CreatedBy, s.CreatedAt, s.UpdatedBy, s.UpdatedAt,
            Envisioned = s.Envisioned.Values.OrderBy(o => o.Name).Select(o => new { o.Id, o.Name, o.TypeKey, TypeLabel = meta.Type(o.TypeKey)?.Label ?? o.TypeKey, o.Properties }),
            Steps = s.Steps.OrderBy(x => x.Order).Select(x =>
            {
                List<ProjectionIssue> issues;
                try { issues = Projection.Build(repo, s.Id, x.Id).Issues.Where(i => i.StepId == x.Id).ToList(); }
                catch (KeyNotFoundException) { issues = new(); }
                return new
                {
                    x.Id, x.Name, x.Order, x.Date, x.Status, x.Description,
                    Changes = x.Changes.Select(c => new { c.Id, c.Op, c.Target, Label = Label(c), c.ElementId, c.ReplacementId, c.Note,
                                                           Issue = issues.FirstOrDefault(i => i.ChangeId == c.Id)?.Message }),
                    Issues = issues,
                };
            }),
        };
    }
}
