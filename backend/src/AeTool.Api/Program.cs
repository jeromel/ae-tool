using System.Text;
using System.Text.Json;
using AeTool.Api;
using AeTool.Connectors;
using AeTool.Core;

// API REST de l'outil d'architecture d'entreprise. Tout ce que fait l'interface passe par ces routes.
// Prototype : pas d'authentification ; l'en-tête X-User alimente le journal. Le SSO (OpenID Connect) est à brancher ici.

var builder = WebApplication.CreateBuilder(args);
var dataPath = builder.Configuration["DataPath"] ?? Path.Combine(AppContext.BaseDirectory, "data", "referentiel.json");
builder.Services.AddSingleton<IRepositoryStore>(new JsonFileStore(dataPath));
builder.Services.AddSingleton<Workspace>();
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = 100 * 1024 * 1024);

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();
var api = app.MapGroup("/api");

static string User(HttpRequest r) => r.Headers.TryGetValue("X-User", out var u) && !string.IsNullOrWhiteSpace(u) ? u.ToString() : "anonyme";
static async Task<MemoryStream> Body(HttpRequest r)
{
    var ms = new MemoryStream();
    await r.Body.CopyToAsync(ms);
    ms.Position = 0;
    return ms;
}

// ---------------------------------------------------------------- Synthèse

api.MapGet("/summary", (Workspace ws) => ws.Read((repo, an) => new
{
    repo.ModelName,
    Objects = repo.ActiveObjects.Count(),
    Relationships = repo.ActiveRelationships.Count(),
    Diagrams = repo.ActiveDiagrams.Count(),
    Projects = an.Projects().Count(),
    Applications = repo.Assessments.Count,
    Layers = repo.Metamodel.Layers.OrderBy(l => l.Order).Select(l => new
    {
        l.Key, l.Label,
        Count = repo.ActiveObjects.Count(o => repo.Metamodel.LayerOf(o.TypeKey) == l.Key),
        Types = repo.ActiveObjects.Where(o => repo.Metamodel.LayerOf(o.TypeKey) == l.Key).GroupBy(o => o.TypeKey)
            .Select(g => new { Key = g.Key, Label = repo.Metamodel.Type(g.Key)?.Label ?? g.Key, Count = g.Count() })
            .OrderByDescending(t => t.Count),
    }),
    Journal = repo.Journal.AsEnumerable().Reverse().Take(8),
}));

api.MapGet("/quality", (Workspace ws) => ws.Read((_, an) => an.Quality()));
api.MapGet("/journal", (Workspace ws, int? limit) => ws.Read((repo, _) => repo.Journal.AsEnumerable().Reverse().Take(limit ?? 200).ToList()));

// ---------------------------------------------------------------- Métamodèle

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

// ---------------------------------------------------------------- Objets et relations

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
        Properties = o.Properties.Select(p => new { p.Key, Label = repo.Metamodel.Property(p.Key)?.Label ?? p.Key, p.Value,
            Source = repo.Metamodel.Property(p.Key)?.Source }),
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
    var o = new ArchObject { Id = "id-" + Guid.NewGuid().ToString("N"), TypeKey = input.TypeKey!, Name = input.Name.Trim(), UpdatedAt = DateTime.UtcNow };
    Dto.Apply(o, input);
    repo.Objects[o.Id] = o;
    repo.Log(User(req), "Objet créé", o.Id, o.Name);
    return Results.Ok(o);
}));

api.MapPut("/objects/{id}", (Workspace ws, string id, ObjectInput input, HttpRequest req) => ws.Write(repo =>
{
    if (!repo.Objects.TryGetValue(id, out var o)) return Results.NotFound();
    if (input.TypeKey is not null && repo.Metamodel.Type(input.TypeKey) is null) return Results.BadRequest(new { error = $"Type inconnu : {input.TypeKey}" });
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

api.MapDelete("/relationships/{id}", (Workspace ws, string id, HttpRequest req) => ws.Write(repo =>
{
    if (!repo.Relationships.TryGetValue(id, out var r)) return Results.NotFound();
    r.Archived = true;
    repo.Log(User(req), "Relation archivée", id);
    return Results.NoContent();
}));

// ---------------------------------------------------------------- Vues

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

// ---------------------------------------------------------------- Analyses

api.MapGet("/analysis/impact/{id}", (Workspace ws, string id, int? depth, string? direction) => ws.Read((repo, an) =>
    repo.Objects.ContainsKey(id) ? Results.Ok(an.Impact(id, Math.Clamp(depth ?? 2, 1, 6), direction ?? "tous")) : Results.NotFound()));

api.MapGet("/analysis/graph/{id}", (Workspace ws, string id, int? depth) => ws.Read((repo, an) =>
    repo.Objects.ContainsKey(id) ? Results.Ok(an.Neighborhood(id, Math.Clamp(depth ?? 1, 1, 4))) : Results.NotFound()));

api.MapGet("/analysis/matrix", (Workspace ws, string rows, string cols, bool? nesting) => ws.Read((_, an) =>
    an.CrossMatrix(rows, cols, nesting ?? true)));

// ---------------------------------------------------------------- Projets

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
    if (impact is null) repo.Impacts.Add(impact = new ProjectImpact { ProjectId = id, ObjectId = objectId });
    impact.Nature = input.Nature;
    impact.Comment = input.Comment;
    repo.Log(User(req), "Impact projet qualifié", id, $"{repo.Objects[objectId].Name} : {input.Nature}");
    return Results.Ok(impact);
}));

// ---------------------------------------------------------------- Portefeuille applicatif

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

// ---------------------------------------------------------------- Imports et exports

api.MapPost("/import/archimate", async (Workspace ws, HttpRequest req) =>
{
    var body = await Body(req);
    return ws.Import(repo => ArchimateExchange.Import(body, repo, User(req)));
});

api.MapPost("/import/urba", async (Workspace ws, HttpRequest req) =>
{
    var body = await Body(req);
    return ws.Import(repo => UrbaImporter.Import(body, repo, User(req)));
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

// Remise à zéro : uniquement en environnement de développement.
if (app.Environment.IsDevelopment())
    api.MapPost("/admin/reset", (Workspace ws) => { ws.Reset(); return Results.NoContent(); });

app.MapFallbackToFile("index.html");
app.Run();

// ---------------------------------------------------------------- Entrées et projections

public sealed record ObjectInput(string? Name, string? TypeKey, string? Documentation, string? Owner, string? ValidationStatus,
    List<PropertyValue>? Properties, Visual? VisualOverride, bool? ClearVisualOverride);
public sealed record RelationshipInput(string TypeKey, string SourceId, string TargetId, string? Name);
public sealed record ImpactInput(string Nature, string? Comment);
public sealed record BriefDto(string Id, string Name, string TypeKey, string TypeLabel, string Layer, string LayerLabel, string Source, string Status, string? Owner);

public static class Dto
{
    public static BriefDto Brief(Repository repo, ArchObject o)
    {
        var type = repo.Metamodel.Type(o.TypeKey);
        var layer = type?.LayerKey ?? "autre";
        return new BriefDto(o.Id, o.Name, o.TypeKey, type?.Label ?? o.TypeKey, layer,
            repo.Metamodel.Layers.FirstOrDefault(l => l.Key == layer)?.Label ?? layer, o.Source, o.ValidationStatus, o.Owner);
    }

    public static void Apply(ArchObject o, ObjectInput input)
    {
        if (input.Documentation is not null) o.Documentation = input.Documentation;
        if (input.Owner is not null) o.Owner = input.Owner;
        if (input.ValidationStatus is not null) o.ValidationStatus = input.ValidationStatus;
        if (input.Properties is not null) o.Properties = input.Properties;
        if (input.VisualOverride is not null) o.VisualOverride = input.VisualOverride;
    }

    /// <summary>
    /// Vue prête à dessiner. Ordre de priorité du visuel : réglage du diagramme, puis visuel de l'élément, puis visuel du type.
    /// Avec <paramref name="metamodelVisuals"/>, le réglage du diagramme est ignoré.
    /// </summary>
    public static object Diagram(Repository repo, Diagram d, bool metamodelVisuals)
    {
        var nodes = new List<object>();
        void Visit(DiagramNode n, int depth)
        {
            repo.Objects.TryGetValue(n.ElementId ?? "", out var o);
            var visual = o is null ? new Visual() : repo.Metamodel.EffectiveVisual(o);
            var useStyle = !metamodelVisuals || o is null;
            nodes.Add(new
            {
                n.Id, n.Kind, n.ElementId, Name = o?.Name ?? n.Label ?? "", TypeKey = o?.TypeKey,
                TypeLabel = o is null ? null : repo.Metamodel.Type(o.TypeKey)?.Label, n.ViewRefId,
                n.X, n.Y, n.W, n.H, Depth = depth,
                Fill = (useStyle ? n.Style?.Fill?.Hex : null) ?? visual.Fill ?? "#ffffff",
                Stroke = (useStyle ? n.Style?.Line?.Hex : null) ?? visual.Stroke ?? "#5c5c5c",
                TextColor = (useStyle ? n.Style?.Font?.Color?.Hex : null) ?? visual.TextColor ?? "#000000",
                FontSize = int.TryParse(n.Style?.Font?.Size, out var size) ? size : 9,
                Bold = n.Style?.Font?.Style?.Contains("bold") == true,
                Shape = visual.Shape ?? "rectangle", visual.Icon,
                HasChildren = n.Children.Count > 0,
            });
            foreach (var c in n.Children) Visit(c, depth + 1);
        }
        foreach (var n in d.Nodes) Visit(n, 0);
        return new
        {
            d.Id, d.Name, d.Documentation, d.Viewpoint, Nodes = nodes,
            Connections = d.Connections.Select(c =>
            {
                repo.Relationships.TryGetValue(c.RelationshipId ?? "", out var r);
                return new
                {
                    c.Id, c.SourceNodeId, c.TargetNodeId, c.Bendpoints, TypeKey = r?.TypeKey, r?.Name,
                    LineStyle = r is null ? "plein" : repo.Metamodel.RelType(r.TypeKey)?.LineStyle ?? "plein",
                    Color = c.Style?.Line?.Hex ?? "#000000",
                };
            }),
        };
    }
}
