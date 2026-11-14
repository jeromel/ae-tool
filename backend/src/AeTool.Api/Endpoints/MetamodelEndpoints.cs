using System.Text;
using System.Text.Json;
using AeTool.Connectors;
using AeTool.Core;
using static AeTool.Api.Http;

namespace AeTool.Api.Endpoints;

/// <summary>Lecture et paramétrage du métamodèle.</summary>
public static class MetamodelEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/metamodel", (Workspace ws) => ws.Read((repo, _) => repo.Metamodel));

        api.MapPut("/metamodel/object-types/{key}", (Workspace ws, string key, ObjectType input, HttpRequest req) => ws.Write(repo =>
        {
            var type = repo.Metamodel.Type(key);
            if (type is null)
            {
                type = new ObjectType { Key = key };
                repo.Metamodel.ObjectTypes.Add(type);
            }
            if (repo.Metamodel.Layers.All(l => l.Key != input.LayerKey)) return Results.BadRequest(new { error = $"Couche inconnue : {input.LayerKey}" });
            type.Label = string.IsNullOrWhiteSpace(input.Label) ? key : input.Label;
            type.LayerKey = input.LayerKey;
            type.ArchimateType = string.IsNullOrWhiteSpace(input.ArchimateType) ? null : input.ArchimateType;
            type.Visual = input.Visual;
            type.Archived = input.Archived;
            repo.Log(User(req), "Métamodèle : type modifié", key);
            return Results.Ok(type);
        }));

        api.MapPut("/metamodel/relationship-types/{key}", (Workspace ws, string key, RelationshipType input, HttpRequest req) => ws.Write(repo =>
        {
            var type = repo.Metamodel.RelType(key);
            if (type is null) return Results.NotFound();
            if (input.Head is not null && !DrawingDefaults.Heads.Contains(input.Head)) return Results.BadRequest(new { error = $"Tête inconnue : {input.Head}" });
            type.Label = string.IsNullOrWhiteSpace(input.Label) ? key : input.Label;
            type.SoftLine = string.IsNullOrWhiteSpace(input.SoftLine) ? null : input.SoftLine;
            type.Head = string.IsNullOrWhiteSpace(input.Head) ? null : input.Head;
            repo.Log(User(req), "Métamodèle : type de relation modifié", key);
            return Results.Ok(type);
        }));

        api.MapPut("/metamodel/properties/displayable", (Workspace ws, DisplayableInput input, HttpRequest req) => ws.Write(repo =>
        {
            var def = repo.Metamodel.Property(input.Key);
            if (def is null) return Results.NotFound();
            def.Displayable = input.Displayable;
            repo.Log(User(req), input.Displayable ? "Métamodèle : propriété proposée en calque" : "Métamodèle : propriété retirée des calques", input.Key);
            return Results.Ok(def);
        }));

        api.MapPut("/metamodel/scoring", (Workspace ws, ScoringGrid grid, HttpRequest req) => ws.Write(repo =>
        {
            if (grid.Criteria.Count == 0) return Results.BadRequest(new { error = "La grille doit contenir au moins un critère." });
            repo.Metamodel.Scoring = grid;
            foreach (var a in repo.Assessments.Values) ScoringEngine.Complete(grid, a);
            repo.Log(User(req), "Métamodèle : grille de notation modifiée");
            return Results.Ok(grid);
        }));

        api.MapPut("/metamodel/settings", (Workspace ws, MetamodelSettings settings, HttpRequest req) => ws.Write(repo =>
        {
            repo.Metamodel.Settings = settings;
            new Analyzer(repo).RefreshDeducedImpacts();
            repo.Log(User(req), "Métamodèle : réglages modifiés");
            return settings;
        }));

        api.MapGet("/metamodel/export", (Workspace ws) => ws.Read((repo, _) =>
            Results.File(JsonSerializer.SerializeToUtf8Bytes(repo.Metamodel, new JsonSerializerOptions { WriteIndented = true }),
                "application/json", "metamodele.json")));

        api.MapPost("/metamodel/import", async (Workspace ws, HttpRequest req) =>
        {
            Metamodel? incoming;
            try { incoming = await JsonSerializer.DeserializeAsync<Metamodel>(await Body(req)); }
            catch (JsonException e) { return Results.BadRequest(new { error = $"JSON invalide : {e.Message}" }); }
            if (incoming is null || incoming.ObjectTypes.Count == 0) return Results.BadRequest(new { error = "Métamodèle vide." });
            return ws.Write(repo =>
            {
                // On ne détruit jamais de données : un type utilisé mais absent du métamodèle importé est conservé, archivé.
                foreach (var used in repo.Objects.Values.Select(o => o.TypeKey).Distinct())
                    if (incoming.Type(used) is null && repo.Metamodel.Type(used) is { } kept)
                    {
                        kept.Archived = true;
                        incoming.ObjectTypes.Add(kept);
                    }
                repo.Metamodel = incoming;
                repo.Log(User(req), "Métamodèle importé");
                return Results.Ok(incoming);
            });
        });
    }
}
