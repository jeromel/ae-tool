using System.Text;
using System.Text.Json;
using AeTool.Connectors;
using AeTool.Core;
using static AeTool.Api.Http;

namespace AeTool.Api.Endpoints;

/// <summary>Objets et relations du référentiel.</summary>
public static class ObjectEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/objects", (Workspace ws, string? type, string? layer, string? q, int? limit) => ws.Read((repo, _) =>
        {
            var query = repo.ActiveObjects;
            if (!string.IsNullOrEmpty(type)) query = query.Where(o => o.TypeKey == type);
            if (!string.IsNullOrEmpty(layer)) query = query.Where(o => repo.Metamodel.LayerOf(o.TypeKey) == layer);
            if (!string.IsNullOrWhiteSpace(q))
            {
                var needle = UrbaImporter.Normalize(q);
                query = query.Where(o => UrbaImporter.Normalize(o.Name).Contains(needle)
                                         || o.Properties.Any(p => p.Value is not null && UrbaImporter.Normalize(p.Value).Contains(needle)));
            }
            var all = query.OrderBy(o => o.Name).ToList();
            return new { Total = all.Count, Items = all.Take(limit ?? 300).Select(o => Dto.Brief(repo, o)) };
        }));

        api.MapGet("/objects/{id}", (Workspace ws, string id) => ws.Read((repo, an) =>
        {
            if (!repo.Objects.TryGetValue(id, out var o)) return Results.NotFound();
            object Rel(Relationship r, string otherId) => new
            {
                r.Id, r.TypeKey, TypeLabel = repo.Metamodel.RelType(r.TypeKey)?.Label ?? r.TypeKey, r.Name,
                Other = repo.Objects.TryGetValue(otherId, out var other) ? Dto.Brief(repo, other) : null,
            };
            return Results.Ok(new
            {
                Object = o,
                Brief = Dto.Brief(repo, o),
                TypeVisual = repo.Metamodel.Type(o.TypeKey)?.Visual,
                EffectiveVisual = repo.Metamodel.EffectiveVisual(o),
                Properties = o.Properties.Select(p =>
                {
                    var def = repo.Metamodel.Property(p.Key);
                    return new { p.Key, Label = def?.Label ?? p.Key, p.Value, Source = def?.Source,
                        DataType = def?.DataType ?? PropertyTypes.Text, AllowedValues = def?.AllowedValues ?? new List<string>(), Required = def?.Required ?? false };
                }),
                MissingRequired = Conformity.MissingRequired(repo.Metamodel, o).Select(p => new { p.Key, Label = p.Label ?? p.Key, p.DataType, p.AllowedValues }),
                Outgoing = an.Outgoing(id).Select(r => Rel(r, r.TargetId)),
                Incoming = an.Incoming(id).Select(r => Rel(r, r.SourceId)),
                Diagrams = repo.ActiveDiagrams.Where(d => d.AllNodes().Any(n => n.ElementId == id)).Select(d => new { d.Id, d.Name }),
                Folders = an.FolderPaths(id),
                Projects = repo.Impacts.Where(i => i.ObjectId == id && repo.Objects.ContainsKey(i.ProjectId))
                    .Select(i => new { i.ProjectId, ProjectName = repo.Objects[i.ProjectId].Name, i.Nature, i.Origin }),
                HasAssessment = repo.Assessments.ContainsKey(id),
            });
        }));

        api.MapPost("/objects", (Workspace ws, ObjectInput input, HttpRequest req) => ws.Write(repo =>
        {
            if (string.IsNullOrWhiteSpace(input.Name)) return Results.BadRequest(new { error = "Le nom est obligatoire." });
            if (repo.Metamodel.Type(input.TypeKey ?? "") is null) return Results.BadRequest(new { error = $"Type inconnu : {input.TypeKey}" });
            if (PropertyProblems(repo.Metamodel, input.Properties, null) is { Count: > 0 } problems) return InvalidValues(problems);
            // Un objet créé dans l'outil a la source « saisie » et aucun concept ArchiMate d'origine.
            var o = new ArchObject { Id = "id-" + Guid.NewGuid().ToString("N"), TypeKey = input.TypeKey!, Name = input.Name.Trim(), Source = "saisie", UpdatedAt = DateTime.UtcNow };
            Dto.Apply(o, input);
            repo.Objects[o.Id] = o;
            repo.Log(User(req), "Objet créé", o.Id, o.Name);
            return Results.Ok(o);
        }));

        api.MapPut("/objects/{id}", (Workspace ws, string id, ObjectInput input, HttpRequest req) => ws.Write(repo =>
        {
            if (!repo.Objects.TryGetValue(id, out var o)) return Results.NotFound();
            if (input.TypeKey is not null && repo.Metamodel.Type(input.TypeKey) is null) return Results.BadRequest(new { error = $"Type inconnu : {input.TypeKey}" });
            if (PropertyProblems(repo.Metamodel, input.Properties, o) is { Count: > 0 } problems) return InvalidValues(problems);
            var before = JsonSerializer.Serialize(o);
            if (!string.IsNullOrWhiteSpace(input.Name)) o.Name = input.Name.Trim();
            if (input.TypeKey is not null) o.TypeKey = input.TypeKey;
            Dto.Apply(o, input);
            if (input.ClearVisualOverride == true) o.VisualOverride = null;
            o.UpdatedAt = DateTime.UtcNow;
            repo.Log(User(req), "Objet modifié", id, $"avant : {before}");
            return Results.Ok(o);
        }));

        api.MapDelete("/objects/{id}", (Workspace ws, string id, HttpRequest req) => ws.Write(repo =>
        {
            if (!repo.Objects.TryGetValue(id, out var o)) return Results.NotFound();
            o.Archived = true; // archivage, jamais de suppression physique
            repo.Log(User(req), "Objet archivé", id, o.Name);
            return Results.NoContent();
        }));

        api.MapPost("/relationships", (Workspace ws, RelationshipInput input, HttpRequest req) => ws.Write(repo =>
        {
            if (!repo.Objects.TryGetValue(input.SourceId, out var s) || !repo.Objects.TryGetValue(input.TargetId, out var t))
                return Results.BadRequest(new { error = "Source ou cible inconnue." });
            if (repo.Metamodel.CheckRelationship(input.TypeKey, s.TypeKey, t.TypeKey) is string problem) return Results.BadRequest(new { error = problem });
            var r = new Relationship { Id = "id-" + Guid.NewGuid().ToString("N"), TypeKey = input.TypeKey, SourceId = s.Id, TargetId = t.Id, Name = input.Name };
            repo.Relationships[r.Id] = r;
            repo.Log(User(req), "Relation créée", r.Id, $"{s.Name} -[{r.TypeKey}]-> {t.Name}");
            return Results.Ok(r);
        }));

        api.MapPut("/relationships/{id}", (Workspace ws, string id, RelationshipUpdate input, HttpRequest req) => ws.Write(repo =>
        {
            if (!repo.Relationships.TryGetValue(id, out var r)) return Results.NotFound();
            if (input.TypeKey is not null)
            {
                if (repo.Objects.TryGetValue(r.SourceId, out var s) && repo.Objects.TryGetValue(r.TargetId, out var t))
                {
                    if (repo.Metamodel.CheckRelationship(input.TypeKey, s.TypeKey, t.TypeKey) is string problem) return Results.BadRequest(new { error = problem });
                }
                else if (repo.Metamodel.RelType(input.TypeKey) is null) return Results.BadRequest(new { error = $"Type de relation inconnu : {input.TypeKey}" });
            }
            if (PropertyProblems(repo.Metamodel, input.Properties, null) is { Count: > 0 } problems) return InvalidValues(problems);
            var before = JsonSerializer.Serialize(r);
            if (input.Name is not null) r.Name = string.IsNullOrWhiteSpace(input.Name) ? null : input.Name.Trim();
            if (input.TypeKey is not null) r.TypeKey = input.TypeKey;
            if (input.Properties is not null) r.Properties = input.Properties;
            repo.Log(User(req), "Relation modifiée", id, $"avant : {before}");
            return Results.Ok(r);
        }));

        api.MapDelete("/relationships/{id}", (Workspace ws, string id, HttpRequest req) => ws.Write(repo =>
        {
            if (!repo.Relationships.TryGetValue(id, out var r)) return Results.NotFound();
            r.Archived = true;
            repo.Log(User(req), "Relation archivée", id);
            return Results.NoContent();
        }));
    }

    /// <summary>
    /// Valeurs de propriétés non conformes à leur définition (liste, nombre, date, booléen). Seules les valeurs modifiées
    /// sont contrôlées : un ancien écart n'empêche pas d'enregistrer autre chose.
    /// </summary>
    private static List<string> PropertyProblems(Metamodel m, List<PropertyValue>? values, ArchObject? current) =>
        (values ?? new List<PropertyValue>())
            .Where(v => current is null || current.Get(v.Key) != v.Value)
            .Select(v => m.Property(v.Key) is { } def ? Conformity.ValueProblem(def, v.Value)?.Message : null)
            .OfType<string>().ToList();

    private static IResult InvalidValues(List<string> problems) =>
        Results.BadRequest(new { error = "Valeurs de propriétés invalides : " + string.Join(" ", problems), problems });
}
