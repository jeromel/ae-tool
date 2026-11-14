using System.Text.Json;
using System.Text.Json.Nodes;
using AeTool.Core;
using static AeTool.Api.Http;

namespace AeTool.Api.Endpoints;

/// <summary>Lecture et paramétrage du métamodèle : types, rôles, propriétés, matrice de transcodification.</summary>
public static class MetamodelEndpoints
{
    private static IResult Bad(string message) => Results.BadRequest(new { error = message });
    private static IResult Refused(string message) => Results.Json(new { error = message }, statusCode: StatusCodes.Status409Conflict);

    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/metamodel", (Workspace ws) => ws.Read((repo, _) => repo.Metamodel));
        api.MapGet("/metamodel/catalog", () => Catalog());
        api.MapGet("/metamodel/usage", (Workspace ws) => ws.Read((repo, _) => Usage(repo)));
        MapLayers(api);
        MapObjectTypes(api);
        MapRelationshipTypes(api);
        MapProperties(api);
        MapTranscoding(api);
        MapSettingsAndExchange(api);
    }

    private static object Catalog() => new
    {
        ObjectRoles = Roles.ForObjects,
        RelationshipRoles = Roles.ForRelationships,
        Archimate = new { Elements = Archimate.Elements, Relationships = Archimate.Relationships },
        DrawingDefaults.Templates, DrawingDefaults.Heads, DrawingDefaults.Lines, DrawingDefaults.Shapes,
        DataTypes = PropertyTypes.All.Select(t => new { t.Key, t.Label }),
    };

    private static object Usage(Repository repo) => new
    {
        ObjectTypes = repo.ActiveObjects.GroupBy(o => o.TypeKey).ToDictionary(g => g.Key, g => g.Count()),
        RelationshipTypes = repo.ActiveRelationships.GroupBy(r => r.TypeKey).ToDictionary(g => g.Key, g => g.Count()),
        Properties = PropertyValueCounts(repo),
    };

    private static Dictionary<string, int> PropertyValueCounts(Repository repo) =>
        repo.Objects.Values.SelectMany(o => o.Properties)
            .Concat(repo.Relationships.Values.SelectMany(r => r.Properties))
            .Concat(repo.Diagrams.Values.SelectMany(d => d.Properties))
            .GroupBy(p => p.Key).ToDictionary(g => g.Key, g => g.Count());

    // ------------------------------------------------------------ Couches

    private static void MapLayers(RouteGroupBuilder api) =>
        api.MapPut("/metamodel/layers", (Workspace ws, List<Layer> input, HttpRequest req) => ws.Write(repo =>
        {
            if (input.Count == 0 || input.Any(l => string.IsNullOrWhiteSpace(l.Key) || string.IsNullOrWhiteSpace(l.Label)))
                return Bad("Chaque couche doit avoir une clé et un libellé.");
            if (input.Select(l => l.Key).Distinct().Count() != input.Count) return Bad("Les clés de couches doivent être uniques.");
            var kept = input.Select(l => l.Key).ToHashSet();
            var inUse = repo.Metamodel.ObjectTypes.Where(t => !kept.Contains(t.LayerKey)).Select(t => t.LayerKey).Distinct().ToList();
            if (inUse.Count > 0) return Refused($"Couche encore utilisée par des types : {string.Join(", ", inUse)}.");
            repo.Metamodel.Layers = input.Select((l, i) => new Layer { Key = l.Key.Trim(), Label = l.Label.Trim(), Order = i }).ToList();
            repo.Log(User(req), "Métamodèle : couches modifiées");
            return Results.Ok(repo.Metamodel.Layers);
        }));

    // ------------------------------------------------------------ Types d'objets

    private static string? ObjectTypeProblem(Metamodel m, string? layerKey, List<string>? roles, Visual? visual)
    {
        if (m.Layers.All(l => l.Key != layerKey)) return $"Couche inconnue : {layerKey}";
        if (roles?.FirstOrDefault(r => !Roles.IsObjectRole(r)) is { } role) return $"Rôle d'objet inconnu : {role}";
        if (visual?.Shape is { } shape && !DrawingDefaults.Shapes.Contains(shape)) return $"Forme inconnue : {shape}";
        if (visual?.Template is { } template && !DrawingDefaults.Templates.Contains(template)) return $"Gabarit inconnu : {template}";
        return null;
    }

    private static void MapObjectTypes(RouteGroupBuilder api)
    {
        api.MapPost("/metamodel/object-types", (Workspace ws, ObjectTypeInput input, HttpRequest req) => ws.Write(repo =>
        {
            if (string.IsNullOrWhiteSpace(input.Label)) return Bad("Le libellé est obligatoire.");
            if (ObjectTypeProblem(repo.Metamodel, input.LayerKey, input.Roles, input.Visual) is { } problem) return Bad(problem);
            var fallback = new Visual { Shape = "rectangle", Fill = "#f0f0f0", Stroke = "#5c5c5c", TextColor = "#000000",
                Template = DrawingDefaults.FallbackTemplate, Icon = DrawingDefaults.FallbackGlyph };
            var type = new ObjectType
            {
                Key = Metamodel.DeriveKey(input.Label, repo.Metamodel.ObjectTypes.Select(t => t.Key)), Label = input.Label.Trim(),
                LayerKey = input.LayerKey!, Description = input.Description, Roles = input.Roles?.Distinct().ToList() ?? new(),
                Visual = Metamodel.Merge(fallback, input.Visual),
            };
            repo.Metamodel.ObjectTypes.Add(type);
            repo.Log(User(req), "Métamodèle : type créé", type.Key, type.Label);
            return Results.Ok(type);
        }));

        api.MapPut("/metamodel/object-types/{key}", (Workspace ws, string key, ObjectType input, HttpRequest req) => ws.Write(repo =>
        {
            var type = repo.Metamodel.Type(key);
            if (type is null) return Results.NotFound();
            if (ObjectTypeProblem(repo.Metamodel, input.LayerKey, input.Roles, input.Visual) is { } problem) return Bad(problem);
            type.Label = string.IsNullOrWhiteSpace(input.Label) ? key : input.Label.Trim();
            type.Description = input.Description;
            type.LayerKey = input.LayerKey;
            type.Roles = input.Roles.Distinct().ToList();
            type.Visual = input.Visual;
            type.Archived = input.Archived;
            repo.Log(User(req), "Métamodèle : type modifié", key);
            return Results.Ok(type);
        }));

        api.MapDelete("/metamodel/object-types/{key}", (Workspace ws, string key, string? mergeInto, HttpRequest req) => ws.Write(repo =>
        {
            var m = repo.Metamodel;
            if (m.Type(key) is null) return Results.NotFound();
            if (key == m.Transcoding.UnmappedTypeKey) return Refused("Ce type reçoit les concepts ArchiMate sans règle : il ne peut pas être supprimé.");
            var objects = repo.Objects.Values.Where(o => o.TypeKey == key).ToList();
            var target = string.IsNullOrWhiteSpace(mergeInto) ? null : mergeInto;
            if (target is not null && (target == key || m.Type(target) is null)) return Bad($"Type de destination invalide : {target}");
            if (objects.Count > 0 && target is null)
                return Refused($"{objects.Count} objet(s) ont ce type : indiquez le type de destination (mergeInto) pour les y fusionner.");

            foreach (var o in objects) o.TypeKey = target!;
            m.ObjectTypes.RemoveAll(t => t.Key == key);
            m.Transcoding.Elements.RemoveAll(r => r.TypeKey == key);
            RedirectRelationRules(m.Transcoding, key, target);
            foreach (var rt in m.RelationshipTypes) rt.AllowedPairs = rt.AllowedPairs.Select(p => RedirectPair(p, key, target)).OfType<string>().Distinct().ToList();
            foreach (var p in m.PropertyDefinitions.Where(p => p.AppliesTo.Contains(key)))
            {
                p.AppliesTo = p.AppliesTo.Select(k => k == key ? target : k).OfType<string>().Distinct().ToList();
                // Une liste vidée vaudrait « tous les types » : la propriété cesse alors d'être obligatoire.
                if (p.AppliesTo.Count == 0) p.Required = false;
            }
            repo.Log(User(req), "Métamodèle : type supprimé", key, target is null ? null : $"{objects.Count} objet(s) fusionné(s) dans {target}");
            return Results.NoContent();
        }));
    }

    private static void RedirectRelationRules(Transcoding t, string key, string? target)
    {
        if (target is null) t.Relations.RemoveAll(r => r.SourceTypeKey == key || r.TargetTypeKey == key);
        foreach (var r in t.Relations)
        {
            if (r.SourceTypeKey == key) r.SourceTypeKey = target;
            if (r.TargetTypeKey == key) r.TargetTypeKey = target;
        }
    }

    private static string? RedirectPair(string pair, string key, string? target)
    {
        var parts = pair.Split('>');
        if (parts.Length != 2 || !parts.Contains(key)) return pair;
        return target is null ? null : $"{(parts[0] == key ? target : parts[0])}>{(parts[1] == key ? target : parts[1])}";
    }

    // ------------------------------------------------------------ Types de relations

    private static string? RelationshipTypeProblem(Metamodel m, List<string>? roles, string? line, string? softLine, string? head, List<string>? pairs)
    {
        if (roles?.FirstOrDefault(r => !Roles.IsRelationshipRole(r)) is { } role) return $"Rôle de relation inconnu : {role}";
        if (line is not null && !DrawingDefaults.Lines.Contains(line)) return $"Trait inconnu : {line}";
        if (softLine is not null && !DrawingDefaults.Lines.Contains(softLine)) return $"Trait inconnu : {softLine}";
        if (head is not null && !DrawingDefaults.Heads.Contains(head)) return $"Tête inconnue : {head}";
        foreach (var pair in pairs ?? new List<string>())
        {
            var parts = pair.Split('>');
            if (parts.Length != 2 || m.Type(parts[0]) is null || m.Type(parts[1]) is null) return $"Couple autorisé invalide : « {pair} » (attendu : typeSource>typeCible, types existants).";
        }
        return null;
    }

    private static string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v;

    private static void MapRelationshipTypes(RouteGroupBuilder api)
    {
        api.MapPost("/metamodel/relationship-types", (Workspace ws, RelationshipTypeInput input, HttpRequest req) => ws.Write(repo =>
        {
            if (string.IsNullOrWhiteSpace(input.Label)) return Bad("Le libellé est obligatoire.");
            if (RelationshipTypeProblem(repo.Metamodel, input.Roles, Clean(input.LineStyle), Clean(input.SoftLine), Clean(input.Head), input.AllowedPairs) is { } problem) return Bad(problem);
            var type = new RelationshipType
            {
                Key = Metamodel.DeriveKey(input.Label, repo.Metamodel.RelationshipTypes.Select(t => t.Key)), Label = input.Label.Trim(),
                ReverseLabel = Clean(input.ReverseLabel), Description = input.Description, Roles = input.Roles?.Distinct().ToList() ?? new(),
                LineStyle = Clean(input.LineStyle) ?? DrawingDefaults.FallbackLine, SoftLine = Clean(input.SoftLine), Head = Clean(input.Head),
                AllowedPairs = input.AllowedPairs?.Distinct().ToList() ?? new(),
            };
            repo.Metamodel.RelationshipTypes.Add(type);
            repo.Log(User(req), "Métamodèle : type de relation créé", type.Key, type.Label);
            return Results.Ok(type);
        }));

        api.MapPut("/metamodel/relationship-types/{key}", (Workspace ws, string key, RelationshipType input, HttpRequest req) => ws.Write(repo =>
        {
            var type = repo.Metamodel.RelType(key);
            if (type is null) return Results.NotFound();
            if (RelationshipTypeProblem(repo.Metamodel, input.Roles, Clean(input.LineStyle), Clean(input.SoftLine), Clean(input.Head), input.AllowedPairs) is { } problem) return Bad(problem);
            type.Label = string.IsNullOrWhiteSpace(input.Label) ? key : input.Label.Trim();
            type.ReverseLabel = Clean(input.ReverseLabel);
            type.Description = input.Description;
            type.Roles = input.Roles.Distinct().ToList();
            type.LineStyle = Clean(input.LineStyle) ?? type.LineStyle;
            type.SoftLine = Clean(input.SoftLine);
            type.Head = Clean(input.Head);
            type.AllowedPairs = input.AllowedPairs.Distinct().ToList();
            type.Archived = input.Archived;
            repo.Log(User(req), "Métamodèle : type de relation modifié", key);
            return Results.Ok(type);
        }));

        api.MapDelete("/metamodel/relationship-types/{key}", (Workspace ws, string key, string? mergeInto, HttpRequest req) => ws.Write(repo =>
        {
            var m = repo.Metamodel;
            if (m.RelType(key) is null) return Results.NotFound();
            if (key == m.Transcoding.UnmappedRelationTypeKey) return Refused("Ce type reçoit les relations ArchiMate sans règle : il ne peut pas être supprimé.");
            var relations = repo.Relationships.Values.Where(r => r.TypeKey == key).ToList();
            var target = string.IsNullOrWhiteSpace(mergeInto) ? null : mergeInto;
            if (target is not null && (target == key || m.RelType(target) is null)) return Bad($"Type de destination invalide : {target}");
            if (relations.Count > 0 && target is null)
                return Refused($"{relations.Count} relation(s) ont ce type : indiquez le type de destination (mergeInto) pour les y fusionner.");
            foreach (var r in relations) r.TypeKey = target!;
            m.RelationshipTypes.RemoveAll(t => t.Key == key);
            m.Transcoding.Relations.RemoveAll(r => r.TypeKey == key);
            repo.Log(User(req), "Métamodèle : type de relation supprimé", key, target is null ? null : $"{relations.Count} relation(s) fusionnée(s) dans {target}");
            return Results.NoContent();
        }));
    }

    // ------------------------------------------------------------ Propriétés

    private static string? PropertyProblem(Metamodel m, PropertyDefinition p)
    {
        if (!PropertyTypes.IsKnown(p.DataType)) return $"Type de donnée inconnu : {p.DataType}";
        if (p.DataType == PropertyTypes.List && p.AllowedValues.Count == 0) return "Une propriété de type liste doit avoir des valeurs permises.";
        if (p.AppliesTo.FirstOrDefault(k => m.Type(k) is null) is { } unknown) return $"Type d'objet inconnu dans « s'applique à » : {unknown}";
        return null;
    }

    private static void MapProperties(RouteGroupBuilder api)
    {
        api.MapPost("/metamodel/properties", (Workspace ws, PropertyDefinition input, HttpRequest req) => ws.Write(repo =>
        {
            if (string.IsNullOrWhiteSpace(input.Key)) return Bad("La clé est obligatoire.");
            if (repo.Metamodel.Property(input.Key) is not null) return Refused($"La propriété « {input.Key} » existe déjà.");
            if (PropertyProblem(repo.Metamodel, input) is { } problem) return Bad(problem);
            input.Key = input.Key.Trim();
            input.Label = string.IsNullOrWhiteSpace(input.Label) ? input.Key : input.Label;
            input.ArchiName = Clean(input.ArchiName);
            input.Source = "saisie";
            repo.Metamodel.PropertyDefinitions.Add(input);
            repo.Log(User(req), "Métamodèle : propriété créée", input.Key);
            return Results.Ok(input);
        }));

        api.MapPut("/metamodel/properties", (Workspace ws, string key, PropertyDefinition input, HttpRequest req) => ws.Write(repo =>
        {
            var def = repo.Metamodel.Property(key);
            if (def is null) return Results.NotFound();
            if (PropertyProblem(repo.Metamodel, input) is { } problem) return Bad(problem);
            def.Label = string.IsNullOrWhiteSpace(input.Label) ? key : input.Label;
            def.Description = input.Description;
            def.DataType = input.DataType;
            def.AllowedValues = input.AllowedValues;
            def.AppliesTo = input.AppliesTo;
            def.Required = input.Required;
            def.Displayable = input.Displayable;
            def.ArchiName = Clean(input.ArchiName);
            repo.Log(User(req), "Métamodèle : propriété modifiée", key);
            return Results.Ok(def);
        }));

        api.MapDelete("/metamodel/properties", (Workspace ws, string key, HttpRequest req) => ws.Write(repo =>
        {
            if (repo.Metamodel.Property(key) is null) return Results.NotFound();
            var values = PropertyValueCounts(repo).GetValueOrDefault(key);
            if (values > 0) return Refused($"{values} valeur(s) de cette propriété existent : elle ne peut pas être supprimée.");
            repo.Metamodel.PropertyDefinitions.RemoveAll(p => p.Key == key);
            repo.Log(User(req), "Métamodèle : propriété supprimée", key);
            return Results.NoContent();
        }));

        api.MapPut("/metamodel/properties/displayable", (Workspace ws, DisplayableInput input, HttpRequest req) => ws.Write(repo =>
        {
            var def = repo.Metamodel.Property(input.Key);
            if (def is null) return Results.NotFound();
            def.Displayable = input.Displayable;
            repo.Log(User(req), input.Displayable ? "Métamodèle : propriété proposée en calque" : "Métamodèle : propriété retirée des calques", input.Key);
            return Results.Ok(def);
        }));
    }

    // ------------------------------------------------------------ Transcodification

    private static void MapTranscoding(RouteGroupBuilder api)
    {
        api.MapPut("/metamodel/transcoding", (Workspace ws, Transcoding input, HttpRequest req) => ws.Write(repo =>
        {
            var errors = Transcoder.Errors(repo.Metamodel, input);
            if (errors.Count > 0)
                return Results.BadRequest(new { error = "Matrice de transcodification invalide.", errors = errors.Select(e => e.Message) });
            repo.Metamodel.Transcoding = input;
            repo.Log(User(req), "Métamodèle : matrice de transcodification modifiée");
            return Results.Ok(input);
        }));

        api.MapGet("/metamodel/transcoding/checks", (Workspace ws) => ws.Read((repo, _) => new Transcoder(repo.Metamodel).Check(repo)));

        // À blanc par défaut : appliquer la matrice retype des objets, on ne le fait qu'à la demande expresse.
        api.MapPost("/metamodel/transcoding/reclassify", (Workspace ws, bool? dryRun, HttpRequest req) =>
        {
            if (dryRun ?? true) return ws.Read((repo, _) => Results.Ok(new Transcoder(repo.Metamodel).Reclassify(repo, dryRun: true)));
            return ws.Write(repo =>
            {
                var result = new Transcoder(repo.Metamodel).Reclassify(repo, dryRun: false, User(req));
                new Analyzer(repo).RefreshDeducedImpacts();
                return Results.Ok(result);
            });
        });
    }

    // ------------------------------------------------------------ Réglages, grille, échange

    private static void MapSettingsAndExchange(RouteGroupBuilder api)
    {
        api.MapPut("/metamodel/scoring", (Workspace ws, ScoringGrid grid, HttpRequest req) => ws.Write(repo =>
        {
            if (grid.Criteria.Count == 0) return Bad("La grille doit contenir au moins un critère.");
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
            try
            {
                var node = JsonNode.Parse(await Body(req)) ?? throw new JsonException("document vide");
                // Un métamodèle exporté en version 1 (sans matrice) est migré comme le sont les fichiers de référentiel.
                incoming = RepositoryUpgrader.IsV1Metamodel(node) ? RepositoryUpgrader.MigrateMetamodel(node) : node.Deserialize<Metamodel>();
            }
            catch (JsonException) { return Bad("JSON invalide : le fichier n'est pas un métamodèle lisible."); }
            if (incoming is null || incoming.ObjectTypes.Count == 0) return Bad("Métamodèle vide.");
            return ws.Write(repo =>
            {
                KeepUsedTypes(repo, incoming);
                var errors = Transcoder.Errors(incoming, incoming.Transcoding);
                if (errors.Count > 0)
                    return Results.BadRequest(new { error = "Matrice de transcodification invalide.", errors = errors.Select(e => e.Message) });
                repo.Metamodel = incoming;
                repo.Log(User(req), "Métamodèle importé");
                return Results.Ok(incoming);
            });
        });
    }

    /// <summary>On ne détruit jamais de données : un type utilisé mais absent du métamodèle importé est conservé, archivé.</summary>
    private static void KeepUsedTypes(Repository repo, Metamodel incoming)
    {
        foreach (var used in repo.Objects.Values.Select(o => o.TypeKey).Distinct())
            if (incoming.Type(used) is null && repo.Metamodel.Type(used) is { } kept)
            {
                kept.Archived = true;
                incoming.ObjectTypes.Add(kept);
            }
        foreach (var used in repo.Relationships.Values.Select(r => r.TypeKey).Distinct())
            if (incoming.RelType(used) is null && repo.Metamodel.RelType(used) is { } kept)
            {
                kept.Archived = true;
                incoming.RelationshipTypes.Add(kept);
            }
    }
}
