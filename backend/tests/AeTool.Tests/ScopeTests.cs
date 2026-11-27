using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AeTool.Api.Endpoints;
using AeTool.Api.Storage;
using AeTool.Connectors;
using AeTool.Core;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using static AeTool.Tests.Harness;

namespace AeTool.Tests;

/// <summary>
/// Périmètre de lecture « métier » (décision 012) : le référentiel sans l'infrastructure. Tests de fuite sur le jeu Hydréane :
/// chaque route de lecture de l'application, appelée par un lecteur « métier », ne renvoie ni l'identifiant ni le nom d'un
/// objet d'infrastructure, dans l'existant comme dans un état projeté. Une route ajoutée plus tard est balayée d'office.
/// </summary>
public static class ScopeTests
{
    private const string AdminPassword = "Administrateur-2024";
    private const string UserPassword = "Utilisateur-2024!";

    public static void Run()
    {
        Console.WriteLine("Périmètre de lecture « métier »");
        var dir = ExampleDatasetTests.Locate();
        if (dir is null) { Console.WriteLine("  (jeu exemples/hydreane introuvable, contrôles ignorés)"); return; }
        var repo = Bench(dir);
        RunProjection(repo);
        RunApi(repo);
    }

    /// <summary>Modèle, classeur, catalogue (DO-003 lié), dessin draw.io, et un scénario qui envisage un serveur.</summary>
    private static Repository Bench(string dir)
    {
        var repo = new Repository();
        using (var s = File.OpenRead(Path.Combine(dir, "hydreane-referentiel.xml"))) ArchimateExchange.Import(s, repo);
        using (var s = File.OpenRead(Path.Combine(dir, "hydreane-referentiel-urbanisation.xlsx"))) UrbaImporter.Import(s, repo);
        var path = Path.Combine(dir, "hydreane-catalogue-donnees-flux.xlsx");
        CatalogueAnalysis a;
        using (var s = File.OpenRead(path)) a = CatalogueImporter.Analyse(s, "catalogue.xlsx", repo);
        var factures = a.Items.Single(i => i.Code == "DO-003").Candidates.Single(c => c.Name == "Factures émises").Id;
        using (var s = File.OpenRead(path))
            CatalogueImporter.Apply(s, "catalogue.xlsx", repo, "test", new Dictionary<string, CatalogueDecision> { ["DO-003"] = new() { Action = CatalogueItem.Link, TargetId = factures } });
        using (var s = File.OpenRead(Path.Combine(dir, "hydreane-facturation-saas.drawio"))) DrawioImporter.Apply(s, "hydreane-facturation-saas.drawio", repo, "test", null);

        var ondine = Id(repo, "ONDINE");
        var server = repo.Objects[Id(repo, "VM-FACT-01")];
        var host = repo.ActiveRelationships.First(r => r.SourceId == server.Id && r.TargetId == ondine);
        var scenario = new Scenario { Id = "scn-infra", ProjectId = repo.ActiveObjects.First(o => repo.Metamodel.IsA(o.TypeKey, Roles.Project)).Id, Name = "Nouvel hébergement", Status = "Validé" };
        scenario.Envisioned["env-vm"] = new ArchObject { Id = "env-vm", Name = "VM-CIBLE-SAAS-77", TypeKey = server.TypeKey };
        scenario.Steps.Add(new TrajectoryStep { Id = "e1", Name = "Bascule", Order = 1, Changes =
        {
            new TrajectoryChange { Id = "c1", Op = ChangeOps.Add, ElementId = "env-vm" },
            new TrajectoryChange { Id = "c2", Op = ChangeOps.Add, Target = "relation", Relation = new PlannedRelation { TypeKey = host.TypeKey, SourceId = "env-vm", TargetId = ondine, Name = "héberge" } },
        } });
        repo.Scenarios[scenario.Id] = scenario;
        return repo;
    }

    private static string Id(Repository repo, string name) => repo.ActiveObjects.First(o => o.Name == name).Id;

    private static HashSet<string> Infrastructure(Repository repo) =>
        repo.Objects.Values.Where(o => ReadScope.IsInfrastructureType(repo.Metamodel, o.TypeKey)).Select(o => o.Id).ToHashSet();

    // ---- La projection, dans le Core ---------------------------------------------------------------------------------

    private static void RunProjection(Repository repo)
    {
        var infra = Infrastructure(repo);
        var before = JsonSerializer.Serialize(repo.Objects) + JsonSerializer.Serialize(repo.Diagrams);
        var scoped = ReadScope.Project(repo);

        Test("périmètre métier : plus aucun objet d'infrastructure, ni relation, ni forme, ni trait qui y aboutisse", () =>
        {
            True(infra.Count > 50, $"objets d'infrastructure dans le jeu : {infra.Count}");
            Equal(0, scoped.Objects.Keys.Count(infra.Contains), "objets d'infrastructure restants");
            True(scoped.Objects.ContainsKey(Id(repo, "ONDINE")), "ONDINE reste");
            var ids = scoped.Objects.Keys.Concat(scoped.Relationships.Keys).ToHashSet();
            Equal(0, scoped.Relationships.Values.Count(r => !ids.Contains(r.SourceId) || !ids.Contains(r.TargetId)), "relations vers un objet retiré");
            foreach (var d in scoped.Diagrams.Values)
            {
                var nodes = d.AllNodes().Select(n => n.Id).ToHashSet();
                Equal(0, d.AllNodes().Count(n => n.ElementId is { } e && !ids.Contains(e)), "formes d'infrastructure dans " + d.Name);
                Equal(0, d.Connections.Count(c => !nodes.Contains(c.SourceNodeId) || !nodes.Contains(c.TargetNodeId)), "traits orphelins dans " + d.Name);
            }
            Equal(before, JsonSerializer.Serialize(repo.Objects) + JsonSerializer.Serialize(repo.Diagrams), "référentiel d'origine intact");
        });

        Test("périmètre métier : une vue entièrement technique disparaît, une vue mixte le signale, ONDINE a perdu des relations", () =>
        {
            var technical = repo.Diagrams.Values.Where(d => d.AllNodes().Any(n => n.ElementId is not null)
                && d.AllNodes().Where(n => n.ElementId is not null).All(n => infra.Contains(n.ElementId!))).Select(d => d.Id).ToList();
            True(technical.Count > 0, "vues entièrement techniques dans le jeu");
            Equal(0, technical.Count(scoped.Diagrams.ContainsKey), "vues techniques encore listées");
            var mixed = repo.Diagrams.Values.First(d => d.AllNodes().Any(n => n.ElementId is { } e && infra.Contains(e))
                && d.AllNodes().Any(n => n.ElementId is { } e && !infra.Contains(e) && repo.Objects.ContainsKey(e)) && !technical.Contains(d.Id));
            True(scoped.Scope!.Diagrams.Contains(mixed.Id), "vue mixte signalée : " + mixed.Name);
            True(scoped.Scope.Objects.Contains(Id(repo, "ONDINE")), "ONDINE a perdu son hébergement");
            True(scoped.Workstations is null && scoped.Scenarios.Count == 0 && scoped.Journal.Count == 0, "ni postes, ni scénarios, ni journal");
        });

        // Recette 0.15.9-0.15.14, R8 : le parcours ne dit jamais à un lecteur « métier » qu'il manque ce qu'il ne voit pas.
        Test("périmètre métier : le parcours n'annonce aucune lacune qui ne tient qu'à l'accès (hébergement, postes, chaîne technique)", () =>
        {
            foreach (var name in new[] { "Factures émises", "ONDINE" })
            {
                var id = Id(repo, name);
                var full = new LineageReader(repo).Read(id, false).Gaps;
                var gaps = new LineageReader(scoped).Read(id, false).Gaps;
                var masked = new[] { "Hébergement de", "Accès utilisateurs", "chaîne technique", "Passage non vérifié" };
                Equal(0, gaps.Count(g => masked.Any(g.Contains)), $"{name} : lacunes d'infrastructure annoncées ({string.Join(" | ", gaps)})");
                True(full.Any(g => masked.Any(g.Contains)), $"{name} : le contrôle porte (lacunes techniques au périmètre « tout »)");
                Equal(0, gaps.Count(g => !full.Contains(g)), $"{name} : lacune inventée par le périmètre");
            }
        });

        // Recette 0.15.9-0.15.14, question 4 : la même règle sur les autres écrans.
        Test("périmètre métier : la cohérence du catalogue ne compte ni hébergement ni chaîne technique manquants ; le reste est identique", () =>
        {
            var full = new CatalogueQuality(repo).Check().Findings.ToDictionary(f => f.Code, f => f.Items.Count);
            var business = new CatalogueQuality(scoped).Check().Findings.ToDictionary(f => f.Code, f => f.Items.Count);
            var infra = new[] { "flux-sans-chaine", "chaine-sans-arrivee", "flux-technique-inutilise", "hebergement-inconnu", "partage-sans-sous-type" };
            Equal("", string.Join(", ", business.Keys.Where(infra.Contains)), "constats d'infrastructure au périmètre métier");
            True(full.ContainsKey("hebergement-inconnu"), "le contrôle porte : hébergements inconnus au périmètre « tout »");
            Equal(string.Join(", ", full.Where(kv => !infra.Contains(kv.Key)).Select(kv => $"{kv.Key}={kv.Value}").Order()),
                string.Join(", ", business.Select(kv => $"{kv.Key}={kv.Value}").Order()), "autres constats inchangés");
        });

        Test("périmètre métier : une application ou une donnée qui a perdu une relation est signalée, pour qu'un zéro n'y passe pas pour une absence", () =>
        {
            var apps = new SpaceReader(scoped, new Analyzer(scoped)).Applications().ToDictionary(a => a.Name);
            True(apps["ONDINE"].ScopeTrimmed, "ONDINE a perdu son hébergement");
            True(new SpaceReader(repo, new Analyzer(repo)).Applications().All(a => !a.ScopeTrimmed), "rien de signalé au périmètre « tout »");
            var data = new SpaceReader(scoped, new Analyzer(scoped)).Data();
            True(data.Where(d => d.ScopeTrimmed).All(d => scoped.Scope!.Objects.Contains(d.Id)), "signal fidèle à la projection");
            // Une application reliée à son seul serveur : zéro relation visible, mais signalée, donc pas « isolée ».
            var small = Imported(out _);
            small.Objects["app-seule"] = new ArchObject { Id = "app-seule", Name = "App seule", TypeKey = small.Objects["id-itsm"].TypeKey, Source = "saisie" };
            small.Relationships["r-seule"] = new Relationship { Id = "r-seule", TypeKey = small.Relationships["id-r4"].TypeKey, SourceId = "id-srv", TargetId = "app-seule" };
            var row = new SpaceReader(ReadScope.Project(small), new Analyzer(ReadScope.Project(small))).Applications().Single(a => a.Id == "app-seule");
            True(row.Relations == 0 && row.ScopeTrimmed, $"relations {row.Relations}, signalée {row.ScopeTrimmed}");
            var projected = ReadScope.Project(small);
            var orphans = new Analyzer(projected).Quality().Single(q => q.TypeKey == small.Objects["app-seule"].TypeKey).Orphans;
            Equal(new Analyzer(small).Quality().Single(q => q.TypeKey == small.Objects["app-seule"].TypeKey).Orphans, orphans, "« sans relation » identique aux deux périmètres");
        });
    }

    // ---- Les routes, par HTTP ----------------------------------------------------------------------------------------

    private sealed class Browser
    {
        private readonly HttpClient _http;
        public Browser(ApiTests.Server server) => _http = server.NewClient();
        public HttpResponseMessage Get(string path) => _http.GetAsync(path).GetAwaiter().GetResult();
        public JsonElement Session() => Json(Get("/api/auth/session"));
        public HttpResponseMessage Send(HttpMethod method, string path, object? body)
        {
            var request = new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body) };
            if (Session().GetProperty("csrfToken").GetString() is { } token) request.Headers.Add("X-CSRF-TOKEN", token);
            return _http.SendAsync(request).GetAwaiter().GetResult();
        }
        public HttpResponseMessage Post(string path, object? body = null) => Send(HttpMethod.Post, path, body);
        public HttpResponseMessage PostFile(string path, string file)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new ByteArrayContent(File.ReadAllBytes(file)) };
            if (Session().GetProperty("csrfToken").GetString() is { } token) request.Headers.Add("X-CSRF-TOKEN", token);
            return _http.SendAsync(request).GetAwaiter().GetResult();
        }
        public static JsonElement Json(HttpResponseMessage r) => JsonDocument.Parse(r.Content.ReadAsStringAsync().GetAwaiter().GetResult()).RootElement.Clone();
    }

    private static Browser User(ApiTests.Server server, Browser admin, string login, string role, string? readScope)
    {
        var created = admin.Post("/api/users", new { login, name = login, role, readScope });
        Equal(HttpStatusCode.Created, created.StatusCode, "création de " + login);
        var temporary = Browser.Json(created).GetProperty("temporaryPassword").GetString()!;
        var user = new Browser(server);
        user.Post("/api/auth/login", new { login, password = temporary });
        Equal(HttpStatusCode.NoContent, user.Post("/api/auth/password", new { current = temporary, next = UserPassword }).StatusCode, "mot de passe de " + login);
        return user;
    }

    /// <summary>Texte d'une réponse : les chaînes d'un JSON décodées (les accents y sont échappés), sinon le corps tel quel.</summary>
    private static string Text(HttpResponseMessage response)
    {
        var raw = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        try
        {
            var sb = new StringBuilder();
            void Walk(JsonElement e)
            {
                switch (e.ValueKind)
                {
                    case JsonValueKind.Object: foreach (var p in e.EnumerateObject()) { sb.Append(p.Name).Append('\n'); Walk(p.Value); } break;
                    case JsonValueKind.Array: foreach (var x in e.EnumerateArray()) Walk(x); break;
                    case JsonValueKind.String: sb.Append(e.GetString()).Append('\n'); break;
                }
            }
            using var doc = JsonDocument.Parse(raw);
            Walk(doc.RootElement);
            return sb.ToString();
        }
        catch (JsonException) { return raw; }
    }

    private static void RunApi(Repository bench)
    {
        using var server = new ApiTests.Server(path => new JsonFileStore(path).Save(bench), "--Auth:Mode=local");
        var code = File.ReadAllText(Path.Combine(server.DataDirectory, "code-installation.txt")).Trim();
        var admin = new Browser(server);
        admin.Post("/api/auth/setup", new { code, login = "admin", name = "Administrateur", password = AdminPassword });
        admin.Post("/api/auth/login", new { login = "admin", password = AdminPassword });
        var lea = User(server, admin, "lea", "lecteur", null);
        var tom = User(server, admin, "tom", "lecteur", ReadScope.All);
        var cora = User(server, admin, "cora", "contributeur", ReadScope.Business);
        // Un import fait par l'administrateur remplit les comptes rendus d'import, lisibles par un lecteur.
        var model = Path.Combine(ExampleDatasetTests.Locate()!, "hydreane-referentiel.xml");
        Equal(HttpStatusCode.OK, admin.PostFile("/api/import/archimate", model).StatusCode, "réimport du modèle par l'administrateur");

        var infra = Infrastructure(bench);
        infra.Add("env-vm");
        var vm = Id(bench, "VM-FACT-01");
        var ondine = Id(bench, "ONDINE");

        Test("périmètre : un nouveau lecteur est « sans l'infrastructure », l'administrateur l'ouvre, les autres rôles voient tout", () =>
        {
            Equal(ReadScope.Business, lea.Session().GetProperty("user").GetProperty("readScope").GetString(), "lea, lecteur par défaut");
            Equal(ReadScope.All, tom.Session().GetProperty("user").GetProperty("readScope").GetString(), "tom, ouvert par l'administrateur");
            Equal(ReadScope.All, cora.Session().GetProperty("user").GetProperty("readScope").GetString(), "cora, contributeur : le périmètre ne vaut que pour un lecteur");
            Equal(ReadScope.All, admin.Session().GetProperty("user").GetProperty("readScope").GetString(), "administrateur");
            var users = Browser.Json(admin.Get("/api/users")).EnumerateArray().ToDictionary(u => u.GetProperty("login").GetString()!, u => u.GetProperty("readScope").GetString());
            Equal(ReadScope.Business, users["lea"], "liste des comptes");
        });

        Test("périmètre : l'objet d'infrastructure se lit en « tout », pas en « métier » (contrôle du balayage)", () =>
        {
            Equal(HttpStatusCode.OK, tom.Get("/api/objects/" + vm).StatusCode, "tom lit VM-FACT-01");
            True(Text(tom.Get("/api/spaces/infrastructure")).Contains("VM-FACT-01"), "tom voit l'espace Infrastructure");
            Equal(HttpStatusCode.NotFound, lea.Get("/api/objects/" + vm).StatusCode, "lea : introuvable");
            True(!Text(lea.Get("/api/spaces/infrastructure")).Contains("VM-FACT-01"), "lea : espace Infrastructure vide");
        });

        Test("périmètre, EV-06 : chaînes et processus sans couche serveurs ni impact de serveur pour le lecteur « métier » (C9)", () =>
        {
            var full = Text(tom.Get("/api/chaines"));
            // Text() ne garde que les chaînes du JSON : la couche se lit à son nom, « serveurs ».
            True(full.Contains("serveurs") && full.Contains("VM-FACT-01"), "tom : couche serveurs et ses noms");
            var business = Text(lea.Get("/api/chaines"));
            True(!business.Contains("serveurs") && !business.Contains("VM-FACT-01"), "lea : ni couche serveurs ni serveur");
            Equal(HttpStatusCode.NotFound, lea.Get("/api/chaines/impact/" + vm).StatusCode, "lea : impact d'un serveur introuvable");
            True(Text(tom.Get("/api/chaines/impact/" + vm)).Contains("serveurs"), "tom : impact du serveur");
        });

        Test("périmètre : scénarios, trajectoires et exports complets fermés ; l'export d'une vue reste ouvert et filtré", () =>
        {
            foreach (var p in new[] { "/api/trajectoires/etats", "/api/export/archimate", "/api/export/objects.csv", "/api/projects/x/scenarios", "/api/import/reports" })
                Equal(HttpStatusCode.Forbidden, lea.Get(p).StatusCode, p);
            var view = bench.Diagrams.Values.First(d => d.AllNodes().Any(n => n.ElementId == vm) && d.AllNodes().Any(n => n.ElementId == ondine));
            var export = lea.Get("/api/export/archimate?diagram=" + Uri.EscapeDataString(view.Id));
            Equal(HttpStatusCode.OK, export.StatusCode, "export de la vue " + view.Name);
            var xml = export.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            True(xml.Contains(ondine) && !xml.Contains(vm), "la vue exportée garde ONDINE, sans VM-FACT-01");
            Equal(HttpStatusCode.OK, tom.Get("/api/export/objects.csv").StatusCode, "export complet ouvert au lecteur « tout »");
        });

        // Ce qui ne doit jamais sortir : les identifiants d'infrastructure, et leurs noms quand ils ne figurent nulle part
        // ailleurs dans ce que le périmètre « métier » laisse voir (un nom repris dans la description d'une application n'est pas une fuite).
        var visible = JsonSerializer.Serialize(ReadScope.Project(bench), new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        var names = bench.Objects.Values.Where(o => infra.Contains(o.Id)).Select(o => o.Name)
            .Concat(bench.Scenarios.Values.SelectMany(s => s.Envisioned.Values).Where(o => infra.Contains(o.Id)).Select(o => o.Name))
            .Where(n => n.Length >= 5 && !visible.Contains(n, StringComparison.OrdinalIgnoreCase)).Distinct().ToList();
        var routes = OpenApiEndpoints.Routes(server.Services.GetRequiredService<EndpointDataSource>())
            .Where(r => r.Method == "GET" && !r.Path.StartsWith("/api/auth/entra")).Select(r => r.Path).Distinct().ToList();

        Test($"fuite : aucune des {routes.Count} routes de lecture ne renvoie un objet d'infrastructure à un lecteur « métier », existant et état projeté", () =>
        {
            True(names.Count > 30, $"noms d'infrastructure contrôlés : {names.Count}");
            var data = Id(bench, "Factures émises");
            var mixed = bench.Diagrams.Values.First(d => d.AllNodes().Any(n => n.ElementId == vm) && d.AllNodes().Any(n => n.ElementId == ondine)).Id;
            var technical = bench.Diagrams.Values.First(d => d.AllNodes().Any(n => n.ElementId is not null)
                && d.AllNodes().Where(n => n.ElementId is not null).All(n => infra.Contains(n.ElementId!))).Id;
            var relation = bench.ActiveRelationships.First(r => r.SourceId == vm || r.TargetId == vm).Id;
            var project = bench.ActiveObjects.First(o => bench.Metamodel.IsA(o.TypeKey, Roles.Project)).Id;
            var ids = new[] { ondine, vm, data, mixed, technical, relation, project, "env-vm" };
            var keys = bench.Metamodel.RelationshipTypes.Select(t => t.Key).Take(4).Append("x").ToArray();
            IEnumerable<string> Urls(string template)
            {
                if (template.Contains("{id}")) return ids.Select(v => template.Replace("{id}", Uri.EscapeDataString(v)));
                if (template.Contains("{key}")) return keys.Select(v => template.Replace("{key}", v));
                if (template.Contains('{')) return new[] { System.Text.RegularExpressions.Regex.Replace(template, @"\{[^}]+\}", "x") };
                if (template is "/api/objects/search" or "/api/objects")
                    return new[] { template, template + "?q=VM", template + "?q=Oracle", template + "?q=" + Uri.EscapeDataString(names[0]) };
                if (template == "/api/analysis/matrix") return new[] { template + "?rows=application&cols=application", template + "?rows=application&cols=noeud" };
                return new[] { template };
            }
            var leaks = new List<string>();
            var calls = 0;
            AeTool.Api.ScopeContext.Unfiltered.Clear();
            foreach (var template in routes)
                foreach (var url in Urls(template))
                    foreach (var etat in new[] { "", "scn-infra:e1" })
                    {
                        var full = etat == "" ? url : url + (url.Contains('?') ? "&" : "?") + "etat=" + etat;
                        var text = Text(lea.Get(full));
                        calls++;
                        // L'identifiant que l'appelant a fourni lui-même peut revenir en écho : ce n'est pas une fuite.
                        var hit = infra.FirstOrDefault(i => text.Contains(i) && !full.Contains(Uri.EscapeDataString(i)))
                                  ?? names.FirstOrDefault(n => text.Contains(n, StringComparison.OrdinalIgnoreCase));
                        if (hit is not null) leaks.Add($"{full} → {hit}");
                    }
            Equal("", string.Join(" | ", leaks.Take(15)), $"fuites ({leaks.Count} sur {calls} appels)");
        });

        Test("structure : toute route de lecture servie au périmètre « métier » passe par la lecture filtrée, sauf celles qui ne lisent pas le référentiel", () =>
        {
            // Liste fermée : une route ajoutée qui lirait les données sans Workspace.Read apparaît ici et fait échouer le test.
            var allowed = new[] { "/api/auth/session", "/api/health", "/api/openapi.json", "/api/metamodel", "/api/metamodel/catalog" };
            var seen = AeTool.Api.ScopeContext.Unfiltered.Keys.ToList();
            True(seen.Count > 0, "le témoin relève bien quelque chose (session)");
            Equal("", string.Join(", ", seen.Where(p => !allowed.Contains(p)).OrderBy(p => p)), "lectures servies sans le filtre");
        });

        Test("fuite : sur une route à identifiant, un objet d'infrastructure répond comme un identifiant qui n'existe pas", () =>
        {
            // Sinon la réponse trahirait l'existence de l'objet (« introuvable » d'un côté, « pas dans votre accès » de l'autre).
            var differ = new List<string>();
            foreach (var template in routes.Where(r => r.Contains("{id}")))
                foreach (var id in new[] { vm, "env-vm" })
                {
                    var known = lea.Get(template.Replace("{id}", id));
                    var unknown = lea.Get(template.Replace("{id}", "id-inconnu-0000"));
                    var a = $"{(int)known.StatusCode} {Text(known).Replace(id, "§")}";
                    var b = $"{(int)unknown.StatusCode} {Text(unknown).Replace("id-inconnu-0000", "§")}";
                    if (a != b) differ.Add($"{template} ({id}) : {a[..Math.Min(80, a.Length)]} ≠ {b[..Math.Min(80, b.Length)]}");
                }
            Equal("", string.Join(" | ", differ), "réponses différentes");
        });
    }
}
