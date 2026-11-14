using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AeTool.Api;
using AeTool.Api.Auth;
using Microsoft.Extensions.Configuration;
using static AeTool.Tests.Harness;

namespace AeTool.Tests;

/// <summary>Comptes, sessions, rôles, CSRF : règles métier d'abord (sans HTTP), puis parcours complets sur l'application démarrée en mémoire.</summary>
public static class AuthTests
{
    private const string AdminPassword = "Administrateur-2024";
    private const string UserPassword = "Utilisateur-2024!";

    // ---- Outillage ---------------------------------------------------------------------------------------------------

    private sealed class FakeClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2025, 1, 6, 9, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FakeAudit : IAuditLog
    {
        public List<string> Entries { get; } = new();
        public void Record(string user, string action, string? targetId = null, string? detail = null) =>
            Entries.Add($"{user}|{action}|{targetId}|{detail}");
    }

    /// <summary>Service de comptes isolé, dans un dossier temporaire, avec horloge et journal factices.</summary>
    private sealed class Rig : IDisposable
    {
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), "ae-auth-" + Guid.NewGuid().ToString("N"));
        public FakeClock Clock { get; } = new();
        public FakeAudit Audit { get; } = new();
        public JsonUserStore Store { get; }
        public SetupCode Setup { get; }
        public AuthService Service { get; }
        public string CodeFile => Path.Combine(Directory, "code-installation.txt");

        public Rig(AuthSettings? settings = null)
        {
            System.IO.Directory.CreateDirectory(Directory);
            Store = new JsonUserStore(Path.Combine(Directory, "utilisateurs.json"));
            Setup = new SetupCode(CodeFile, _ => { });
            Service = new AuthService(Store, Setup, Audit, settings ?? new AuthSettings(AuthMode.Local, null), Clock);
        }

        public string IssueCode()
        {
            Setup.Issue();
            return File.ReadAllText(CodeFile).Trim();
        }

        /// <summary>Premier administrateur « admin » créé par le parcours normal.</summary>
        public UserAccount Bootstrap() => Service.Setup(IssueCode(), "admin", "Administrateur", AdminPassword).Value!;

        public void Dispose() { try { System.IO.Directory.Delete(Directory, recursive: true); } catch (IOException) { /* sans conséquence */ } }
    }

    /// <summary>Un « navigateur » : client HTTP avec cookies, qui récupère le jeton CSRF de la session avant chaque écriture.</summary>
    private sealed class Browser
    {
        private readonly HttpClient _http;
        public Browser(ApiTests.Server server, bool followRedirects = true) => _http = server.NewClient(followRedirects);

        public HttpResponseMessage Get(string path) => _http.GetAsync(path).GetAwaiter().GetResult();

        public JsonElement Session() => Json(Get("/api/auth/session"));

        public HttpResponseMessage Send(HttpMethod method, string path, object? body = null, bool csrf = true)
        {
            var request = new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body) };
            if (csrf && Session().GetProperty("csrfToken").GetString() is { } token) request.Headers.Add("X-CSRF-TOKEN", token);
            return _http.SendAsync(request).GetAwaiter().GetResult();
        }

        public HttpResponseMessage Post(string path, object? body = null, bool csrf = true) => Send(HttpMethod.Post, path, body, csrf);

        public HttpResponseMessage Login(string login, string password) => Post("/api/auth/login", new { login, password });

        public static JsonElement Json(HttpResponseMessage response) =>
            JsonDocument.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult()).RootElement.Clone();

        public static string Text(HttpResponseMessage response) => response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
    }

    private static Browser BootstrapAdmin(ApiTests.Server server)
    {
        var code = File.ReadAllText(Path.Combine(server.DataDirectory, "code-installation.txt")).Trim();
        var admin = new Browser(server);
        Equal(HttpStatusCode.OK, admin.Post("/api/auth/setup", new { code, login = "admin", name = "Administrateur", password = AdminPassword }).StatusCode, "premier lancement");
        Equal(HttpStatusCode.OK, admin.Login("admin", AdminPassword).StatusCode, "connexion administrateur");
        return admin;
    }

    /// <summary>Crée un compte, puis connecte son titulaire après changement du mot de passe temporaire.</summary>
    private static Browser UserWithRole(ApiTests.Server server, Browser admin, string login, string role)
    {
        var created = admin.Post("/api/users", new { login, name = login, role });
        Equal(HttpStatusCode.Created, created.StatusCode, "création du compte " + login);
        var temporary = Browser.Json(created).GetProperty("temporaryPassword").GetString()!;
        var user = new Browser(server);
        Equal(HttpStatusCode.OK, user.Login(login, temporary).StatusCode, "connexion avec le mot de passe temporaire");
        Equal(HttpStatusCode.NoContent, user.Post("/api/auth/password", new { current = temporary, next = UserPassword }).StatusCode, "changement du mot de passe");
        return user;
    }

    // ---- Tests -------------------------------------------------------------------------------------------------------

    public static void Run()
    {
        Console.WriteLine("Comptes et sessions");
        RunAccountRules();
        RunConfiguration();
        RunEntra();
        RunSessions();
        RunAdministration();
        RunProtections();
    }

    private static void RunAccountRules()
    {
        Test("premier lancement : un code faux est refusé, le code juste crée l'administrateur", () =>
        {
            using var rig = new Rig();
            True(rig.Service.SetupRequired, "premier lancement requis sans compte");
            var code = rig.IssueCode();
            var refused = rig.Service.Setup("FAUX-FAUX-FAUX", "admin", "Administrateur", AdminPassword);
            Equal(Failure.Invalid, refused.Failure, "code faux");
            Equal(0, rig.Store.Count, "aucun compte créé");
            var created = rig.Service.Setup(code.ToLowerInvariant(), " Admin ", "Administrateur", AdminPassword);
            True(created.Ok, "code juste (casse et espaces tolérés)");
            Equal(Roles.Administrator, created.Value!.Role, "rôle du premier compte");
            Equal("admin", created.Value.Login, "identifiant normalisé");
            True(!rig.Service.SetupRequired, "premier lancement terminé");
        });

        Test("premier lancement : le code ne sert qu'une fois et son fichier disparaît", () =>
        {
            using var rig = new Rig();
            var code = rig.IssueCode();
            True(File.Exists(rig.CodeFile), "fichier du code présent");
            True(rig.Service.Setup(code, "admin", "Administrateur", AdminPassword).Ok, "première utilisation");
            True(!File.Exists(rig.CodeFile), "fichier du code supprimé");
            var again = rig.Service.Setup(code, "autre", "Autre", AdminPassword);
            Equal(Failure.Conflict, again.Failure, "deuxième utilisation");
            Equal(1, rig.Store.Count, "toujours un seul compte");
        });

        Test("premier lancement : mot de passe trop court ou égal à l'identifiant refusés, le code reste valable", () =>
        {
            using var rig = new Rig();
            var code = rig.IssueCode();
            Equal(Failure.Invalid, rig.Service.Setup(code, "admin", "A", "court").Failure, "trop court");
            Equal(Failure.Invalid, rig.Service.Setup(code, "administrateur", "A", "administrateur").Failure, "égal à l'identifiant (douze caractères)");
            Equal(Failure.Invalid, rig.Service.Setup(code, "a", "A", AdminPassword).Failure, "identifiant trop court");
            True(rig.Service.Setup(code, "admin", "A", AdminPassword).Ok, "le code n'a pas été consommé par les refus");
        });

        Test("connexion : bon mot de passe accepté, mauvais refusé", () =>
        {
            using var rig = new Rig();
            rig.Bootstrap();
            var ok = rig.Service.Login("ADMIN", AdminPassword);
            True(ok.Ok, "bon mot de passe (identifiant insensible à la casse)");
            True(ok.Value!.LastLoginAt is not null, "date de dernière connexion");
            var bad = rig.Service.Login("admin", "mauvais-mot-de-passe");
            Equal(Failure.Denied, bad.Failure, "mauvais mot de passe");
        });

        Test("connexion : compte inconnu et mauvais mot de passe donnent le même message", () =>
        {
            using var rig = new Rig();
            rig.Bootstrap();
            var unknown = rig.Service.Login("fantome", AdminPassword);
            var wrong = rig.Service.Login("admin", "mauvais-mot-de-passe");
            Equal(AuthService.LoginFailure, unknown.Error, "compte inconnu");
            Equal(unknown.Error, wrong.Error, "même message");
            Equal("Identifiant ou mot de passe incorrect.", wrong.Error, "texte du message");
        });

        Test("connexion : verrouillage après 5 échecs pendant 15 minutes, puis déverrouillage", () =>
        {
            using var rig = new Rig();
            rig.Bootstrap();
            for (var i = 0; i < 5; i++) True(!rig.Service.Login("admin", "faux-mot-de-passe").Ok, $"échec {i + 1}");
            var locked = rig.Service.Login("admin", AdminPassword);
            True(!locked.Ok, "le bon mot de passe est refusé pendant le verrouillage");
            Equal(AuthService.LoginFailure, locked.Error, "message identique (le verrouillage ne révèle pas le compte)");
            rig.Clock.Now += TimeSpan.FromMinutes(14);
            True(!rig.Service.Login("admin", AdminPassword).Ok, "toujours verrouillé après 14 minutes");
            rig.Clock.Now += TimeSpan.FromMinutes(2);
            True(rig.Service.Login("admin", AdminPassword).Ok, "déverrouillé après 16 minutes");
        });

        Test("connexion : quatre échecs puis un succès remettent le compteur à zéro", () =>
        {
            using var rig = new Rig();
            rig.Bootstrap();
            for (var i = 0; i < 4; i++) rig.Service.Login("admin", "faux-mot-de-passe");
            True(rig.Service.Login("admin", AdminPassword).Ok, "succès avant le verrouillage");
            for (var i = 0; i < 4; i++) rig.Service.Login("admin", "faux-mot-de-passe");
            True(rig.Service.Login("admin", AdminPassword).Ok, "le compteur est reparti de zéro");
        });

        Test("journal des comptes : aucun mot de passe ni hachage, et rien pour un compte inconnu", () =>
        {
            using var rig = new Rig();
            rig.Bootstrap();
            rig.Service.Login("admin", "faux-mot-de-passe-secret");
            rig.Service.Login("fantome", "autre-secret");
            rig.Service.Login("admin", AdminPassword);
            var created = rig.Service.Create("admin", "alice", "Alice", Roles.Reader).Value!;
            rig.Service.ResetPassword("admin", created.User.Id);
            var journal = string.Join("\n", rig.Audit.Entries);
            True(journal.Contains("|Connexion refusée|"), "échec journalisé");
            True(journal.Contains("|Connexion|"), "succès journalisé");
            True(journal.Contains("|Compte créé|") && journal.Contains("|Mot de passe réinitialisé|"), "création et réinitialisation journalisées");
            True(!journal.Contains("secret") && !journal.Contains(AdminPassword) && !journal.Contains(created.TemporaryPassword!), "aucun mot de passe dans le journal");
            True(!journal.Contains("fantome"), "un identifiant inconnu n'est pas journalisé");
            True(!journal.Contains("AQAAAA"), "aucun hachage dans le journal");
        });

        Test("comptes : création locale avec mot de passe temporaire à changer, changement de mot de passe", () =>
        {
            using var rig = new Rig();
            rig.Bootstrap();
            var created = rig.Service.Create("admin", "Alice", "Alice Martin", Roles.Contributor);
            True(created.Ok, "création");
            Equal("alice", created.Value!.User.Login, "identifiant normalisé");
            Equal(16, created.Value.TemporaryPassword!.Length, "mot de passe temporaire");
            True(created.Value.User.MustChangePassword, "changement obligatoire");
            Equal(Failure.Conflict, rig.Service.Create("admin", "alice", "Doublon", Roles.Reader).Failure, "identifiant déjà pris");
            Equal(Failure.Invalid, rig.Service.Create("admin", "bob", "Bob", "chef").Failure, "rôle inconnu");
            var id = created.Value.User.Id;
            Equal(Failure.Invalid, rig.Service.ChangePassword(id, "mauvais", UserPassword).Failure, "mot de passe actuel faux");
            Equal(Failure.Invalid, rig.Service.ChangePassword(id, created.Value.TemporaryPassword, "alice").Failure, "nouveau mot de passe trop court");
            Equal(Failure.Invalid, rig.Service.ChangePassword(id, created.Value.TemporaryPassword, created.Value.TemporaryPassword).Failure, "nouveau identique à l'ancien");
            True(rig.Service.ChangePassword(id, created.Value.TemporaryPassword, UserPassword).Ok, "changement");
            True(!rig.Store.Find(u => u.Id == id)!.MustChangePassword, "obligation levée");
            True(rig.Service.Login("alice", UserPassword).Ok, "connexion avec le nouveau mot de passe");
            True(!rig.Service.Login("alice", created.Value.TemporaryPassword).Ok, "l'ancien ne marche plus");
        });

        Test("comptes : changement de rôle et désactivation journalisés, compte désactivé refusé", () =>
        {
            using var rig = new Rig();
            rig.Bootstrap();
            var alice = rig.Service.Create("admin", "alice", "Alice", Roles.Reader).Value!;
            rig.Service.ChangePassword(alice.User.Id, alice.TemporaryPassword, UserPassword);
            var promoted = rig.Service.Update("admin", alice.User.Id, "Alice", Roles.Validator, disabled: false);
            Equal(Roles.Validator, promoted.Value!.Role, "nouveau rôle");
            True(rig.Audit.Entries.Any(e => e.Contains("|Rôle modifié|") && e.Contains("lecteur -> valideur")), "changement de rôle journalisé");
            rig.Service.Update("admin", alice.User.Id, "Alice", Roles.Validator, disabled: true);
            True(!rig.Service.Login("alice", UserPassword).Ok, "compte désactivé refusé");
            True(rig.Audit.Entries.Any(e => e.Contains("|Compte désactivé|")), "désactivation journalisée");
            Equal(Failure.Invalid, rig.Service.Update("admin", alice.User.Id, "Alice", "chef", false).Failure, "rôle inconnu");
            Equal(Failure.NotFound, rig.Service.Update("admin", "u-inconnu", "X", Roles.Reader, false).Failure, "compte inconnu");
        });

        Test("comptes : le dernier administrateur actif ne peut être ni rétrogradé ni désactivé", () =>
        {
            using var rig = new Rig();
            var admin = rig.Bootstrap();
            Equal(Failure.Conflict, rig.Service.Update("admin", admin.Id, "Administrateur", Roles.Contributor, false).Failure, "rétrogradation");
            Equal(Failure.Conflict, rig.Service.Update("admin", admin.Id, "Administrateur", Roles.Administrator, true).Failure, "désactivation");
            var second = rig.Service.Create("admin", "second", "Second", Roles.Administrator).Value!;
            True(rig.Service.Update("admin", admin.Id, "Administrateur", Roles.Contributor, false).Ok, "possible quand un autre administrateur actif existe");
            Equal(Failure.Conflict, rig.Service.Update("second", second.User.Id, "Second", Roles.Reader, false).Failure, "le dernier est de nouveau protégé");
            Equal(Roles.Administrator, rig.Store.Find(u => u.Id == second.User.Id)!.Role, "rôle inchangé");
        });

        Test("comptes : réinitialisation par un administrateur (mot de passe temporaire, verrou levé)", () =>
        {
            using var rig = new Rig();
            rig.Bootstrap();
            for (var i = 0; i < 5; i++) rig.Service.Login("admin", "faux-mot-de-passe");
            var admin = rig.Store.Find(u => u.Login == "admin")!;
            var reset = rig.Service.ResetPassword("autre", admin.Id);
            True(reset.Ok, "réinitialisation");
            var login = rig.Service.Login("admin", reset.Value!);
            True(login.Ok, "le mot de passe temporaire fonctionne, le verrou est levé");
            True(login.Value!.MustChangePassword, "changement obligatoire");
            True(!rig.Service.Login("admin", AdminPassword).Ok, "l'ancien mot de passe est invalidé");
            Equal(Failure.NotFound, rig.Service.ResetPassword("admin", "u-inconnu").Failure, "compte inconnu");
        });

        Test("comptes : le fichier utilisateurs.json survit au redémarrage et contient les hachages, jamais les mots de passe", () =>
        {
            using var rig = new Rig();
            rig.Bootstrap();
            var file = File.ReadAllText(Path.Combine(rig.Directory, "utilisateurs.json"));
            True(file.Contains("PasswordHash") && !file.Contains(AdminPassword), "haché, pas en clair");
            var reopened = new JsonUserStore(Path.Combine(rig.Directory, "utilisateurs.json"));
            Equal(1, reopened.Count, "compte rechargé");
            True(!File.Exists(Path.Combine(rig.Directory, "utilisateurs.json.tmp")), "pas de fichier temporaire oublié");
        });
    }

    private static void RunConfiguration()
    {
        static AuthSettings Load(bool development, params (string, string?)[] values) =>
            AuthSettings.Load(new ConfigurationBuilder().AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Item1, v.Item2))).Build(), development);

        Test("mode par défaut : local", () => Equal(AuthMode.Local, Load(false).Mode, "mode"));

        Test("mode ouvert hors développement : démarrage refusé avec un message clair", () =>
        {
            var failure = Assert(() => Load(false, ("Auth:Mode", "ouvert")));
            True(failure.Contains("Development"), "le message cite l'environnement : " + failure);
            True(Load(true, ("Auth:Mode", "ouvert")).IsOpen, "accepté en développement");
        });

        Test("mode ouvert : l'application elle-même refuse de démarrer hors développement", () =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "ae-auth-" + Guid.NewGuid().ToString("N"));
            try
            {
                var failure = Assert(() => AppHost.Build(new[] { "--environment", "Production", "--Auth:Mode=ouvert", "--DataPath", Path.Combine(directory, "referentiel.json") }));
                True(failure.Contains("ouvert"), "message : " + failure);
            }
            finally { try { Directory.Delete(directory, recursive: true); } catch (IOException) { /* sans conséquence, ou rien créé */ } }
        });

        Test("configuration refusée : mode inconnu, Entra incomplet, rôle par défaut inconnu", () =>
        {
            True(Assert(() => Load(true, ("Auth:Mode", "magique"))).Contains("magique"), "mode inconnu");
            True(Assert(() => Load(false, ("Auth:Mode", "entra"), ("Auth:Entra:TenantId", "t"))).Contains("ClientId"), "client manquant");
            True(Assert(() => Load(false, ("Auth:Mode", "entra"), ("Auth:Entra:TenantId", "t"), ("Auth:Entra:ClientId", "c"),
                ("Auth:Entra:ClientSecret", "s"), ("Auth:Entra:DefaultRole", "roi"))).Contains("DefaultRole"), "rôle par défaut");
        });
    }

    /// <summary>Exécute l'action, attend une InvalidOperationException et renvoie son message.</summary>
    private static string Assert(Action action)
    {
        try { action(); }
        catch (InvalidOperationException e) { return e.Message; }
        throw new Exception("une InvalidOperationException était attendue");
    }

    private static void RunEntra()
    {
        var settings = new EntraSettings("tenant", "client", "secret", Roles.Contributor, new[] { "chef@entreprise.example" });

        Test("Entra : la réponse Graph devient un profil (identifiant = userPrincipalName en minuscules)", () =>
        {
            var profile = EntraDirectory.ParseGraph("""{"id":"1","displayName":"Chef SI","mail":"x@y.z","userPrincipalName":"Chef@Entreprise.example"}""");
            Equal("chef@entreprise.example", profile!.Login, "identifiant");
            Equal("Chef SI", profile.DisplayName, "nom");
            Equal("a@b.c", EntraDirectory.ParseGraph("""{"userPrincipalName":"a@b.c"}""")!.DisplayName, "sans nom : l'identifiant");
        });

        Test("Entra : réponse Graph inexploitable refusée", () =>
        {
            True(EntraDirectory.ParseGraph("pas du json") is null, "texte");
            True(EntraDirectory.ParseGraph("[]") is null, "tableau");
            True(EntraDirectory.ParseGraph("""{"displayName":"X"}""") is null, "sans userPrincipalName");
            True(EntraDirectory.ParseGraph("""{"userPrincipalName":"  "}""") is null, "identifiant vide");
        });

        Test("Entra : les administrateurs configurés sont promus à la création, les autres reçoivent le rôle par défaut", () =>
        {
            var now = DateTime.UtcNow;
            var chef = EntraDirectory.NewAccount(new EntraProfile("chef@entreprise.example", "Chef"), settings, now);
            var autre = EntraDirectory.NewAccount(new EntraProfile("autre@entreprise.example", "Autre"), settings, now);
            Equal(Roles.Administrator, chef.Role, "administrateur configuré");
            Equal(Roles.Contributor, autre.Role, "rôle par défaut");
            Equal(AccountOrigin.Entra, autre.Origin, "origine");
            True(autre.PasswordHash is null, "pas de mot de passe local");
        });

        Test("Entra : première connexion crée le compte, la suivante le retrouve, un compte désactivé est refusé", () =>
        {
            using var rig = new Rig(new AuthSettings(AuthMode.Entra, settings));
            var profile = new EntraProfile("autre@entreprise.example", "Autre");
            var first = rig.Service.SignInEntra(profile);
            True(first.Ok && first.Value!.Role == Roles.Contributor, "création au rôle par défaut");
            rig.Service.Update("admin", first.Value!.Id, "Autre", Roles.Reader, false);
            Equal(Roles.Reader, rig.Service.SignInEntra(profile).Value!.Role, "le rôle géré dans l'outil est conservé");
            Equal(1, rig.Store.Count, "un seul compte");
            rig.Service.Update("admin", first.Value.Id, "Autre", Roles.Reader, true);
            Equal(Failure.Denied, rig.Service.SignInEntra(profile).Failure, "compte désactivé");
        });

        Test("Entra : pas de connexion locale ni de réinitialisation de mot de passe, comptes préparés sans mot de passe", () =>
        {
            using var rig = new Rig(new AuthSettings(AuthMode.Entra, settings));
            var prepared = rig.Service.Create("admin", "futur@entreprise.example", "Futur", Roles.Validator).Value!;
            True(prepared.TemporaryPassword is null, "pas de mot de passe temporaire");
            True(!rig.Service.Login("futur@entreprise.example", "nimporte-quoi-12345").Ok, "connexion locale impossible");
            Equal(Failure.Invalid, rig.Service.ResetPassword("admin", prepared.User.Id).Failure, "réinitialisation refusée");
            Equal(Roles.Validator, rig.Service.SignInEntra(new EntraProfile("futur@entreprise.example", "Futur")).Value!.Role, "le compte préparé garde son rôle");
            True(!rig.Service.SetupRequired, "pas de premier lancement en mode Entra");
        });

        Test("Entra : le défi OAuth redirige vers Microsoft avec PKCE, la portée et le chemin de retour", () =>
        {
            using var server = new ApiTests.Server("--Auth:Mode=entra", "--Auth:Entra:TenantId=tenant-test", "--Auth:Entra:ClientId=client-test",
                "--Auth:Entra:ClientSecret=secret-test");
            var response = new Browser(server, followRedirects: false).Get("/api/auth/entra/login");
            Equal(HttpStatusCode.Redirect, response.StatusCode, "redirection");
            var target = response.Headers.Location!.ToString();
            True(target.StartsWith("https://login.microsoftonline.com/tenant-test/oauth2/v2.0/authorize?"), "point d'autorisation : " + target);
            True(target.Contains("response_type=code") && target.Contains("client_id=client-test"), "flux code : " + target);
            True(target.Contains("code_challenge=") && target.Contains("code_challenge_method=S256"), "PKCE : " + target);
            True(target.Contains("scope=openid profile User.Read") || target.Contains("scope=openid%20profile%20User.Read"), "portée : " + target);
            True(target.Contains("redirect_uri=http%3A%2F%2F127.0.0.1") && target.Contains("%2Fsignin-entra"), "retour : " + target);
            True(!target.Contains("secret-test"), "le secret ne part pas dans l'URL");
            var session = Browser.Json(new Browser(server).Get("/api/auth/session"));
            Equal("entra", session.GetProperty("mode").GetString(), "mode annoncé");
            Equal(HttpStatusCode.BadRequest, new Browser(server).Login("a", "b").StatusCode, "pas de connexion locale en mode Entra");
        });

        Test("Entra : le défi n'existe pas hors du mode Entra", () =>
        {
            using var server = new ApiTests.Server("--Auth:Mode=local");
            Equal(HttpStatusCode.NotFound, new Browser(server).Get("/api/auth/entra/login").StatusCode, "défi hors mode Entra");
        });
    }

    private static void RunSessions()
    {
        Test("session anonyme : non authentifié, premier lancement requis, jeton CSRF fourni, 401 sur les données", () =>
        {
            using var server = new ApiTests.Server("--Auth:Mode=local");
            var anonymous = new Browser(server);
            var session = anonymous.Session();
            Equal("local", session.GetProperty("mode").GetString(), "mode");
            True(!session.GetProperty("authenticated").GetBoolean(), "non authentifié");
            True(session.GetProperty("setupRequired").GetBoolean(), "premier lancement requis");
            True(session.GetProperty("user").ValueKind == JsonValueKind.Null, "pas d'utilisateur");
            True(!string.IsNullOrEmpty(session.GetProperty("csrfToken").GetString()), "jeton CSRF");
            var denied = anonymous.Get("/api/summary");
            Equal(HttpStatusCode.Unauthorized, denied.StatusCode, "GET /api/summary sans cookie");
            Equal("Authentification requise.", Browser.Json(denied).GetProperty("error").GetString(), "message JSON");
            True(anonymous.Get("/").StatusCode != HttpStatusCode.Unauthorized, "la page d'accueil n'exige pas de session (404 ici : interface non compilée dans les tests)");
        });

        Test("premier lancement par l'API : code faux refusé, code juste accepté, une seule fois", () =>
        {
            using var server = new ApiTests.Server("--Auth:Mode=local");
            var browser = new Browser(server);
            var code = File.ReadAllText(Path.Combine(server.DataDirectory, "code-installation.txt")).Trim();
            var body = new { code = "FAUX-FAUX-FAUX", login = "admin", name = "Administrateur", password = AdminPassword };
            Equal(HttpStatusCode.BadRequest, browser.Post("/api/auth/setup", body).StatusCode, "code faux");
            Equal(HttpStatusCode.OK, browser.Post("/api/auth/setup", body with { code = code }).StatusCode, "code juste");
            Equal(HttpStatusCode.Conflict, browser.Post("/api/auth/setup", body with { code = code }).StatusCode, "deuxième fois");
            True(!File.Exists(Path.Combine(server.DataDirectory, "code-installation.txt")), "fichier du code supprimé");
            True(!browser.Session().GetProperty("setupRequired").GetBoolean(), "premier lancement terminé");
        });

        Test("connexion et déconnexion par l'API : la session suit le cookie", () =>
        {
            using var server = new ApiTests.Server("--Auth:Mode=local");
            var admin = BootstrapAdmin(server);
            var session = admin.Session();
            True(session.GetProperty("authenticated").GetBoolean(), "authentifié");
            Equal("admin", session.GetProperty("user").GetProperty("login").GetString(), "identifiant");
            Equal("administrateur", session.GetProperty("user").GetProperty("role").GetString(), "rôle");
            Equal(HttpStatusCode.OK, admin.Get("/api/summary").StatusCode, "lecture après connexion");
            Equal(HttpStatusCode.NoContent, admin.Post("/api/auth/logout").StatusCode, "déconnexion");
            True(!admin.Session().GetProperty("authenticated").GetBoolean(), "session fermée");
            Equal(HttpStatusCode.Unauthorized, admin.Get("/api/summary").StatusCode, "lecture après déconnexion");
        });

        Test("connexion par l'API : mauvais mot de passe et compte inconnu, même réponse 401", () =>
        {
            using var server = new ApiTests.Server("--Auth:Mode=local");
            BootstrapAdmin(server);
            var browser = new Browser(server);
            var wrong = browser.Login("admin", "mauvais-mot-de-passe");
            var unknown = browser.Login("fantome", "mauvais-mot-de-passe");
            Equal(HttpStatusCode.Unauthorized, wrong.StatusCode, "mauvais mot de passe");
            Equal(HttpStatusCode.Unauthorized, unknown.StatusCode, "compte inconnu");
            Equal(Browser.Text(wrong), Browser.Text(unknown), "corps identiques");
        });

        Test("cookie de session : HttpOnly, SameSite=Strict, nommé ae.session, sans date d'expiration (session de navigateur)", () =>
        {
            using var server = new ApiTests.Server("--Auth:Mode=local");
            BootstrapAdmin(server);
            var login = new Browser(server).Login("admin", AdminPassword);
            Equal(HttpStatusCode.OK, login.StatusCode, "connexion");
            var cookie = login.Headers.GetValues("Set-Cookie").FirstOrDefault(c => c.StartsWith("ae.session="));
            True(cookie is not null, "cookie ae.session posé");
            True(cookie!.Contains("httponly", StringComparison.OrdinalIgnoreCase), "HttpOnly : " + cookie);
            True(cookie.Contains("samesite=strict", StringComparison.OrdinalIgnoreCase), "SameSite=Strict : " + cookie);
        });

        Test("rôles : lecteur lit mais ne peut rien écrire", () =>
        {
            using var server = new ApiTests.Server("--Auth:Mode=local");
            var admin = BootstrapAdmin(server);
            var reader = UserWithRole(server, admin, "lise", Roles.Reader);
            Equal(HttpStatusCode.OK, reader.Get("/api/summary").StatusCode, "lecture");
            var write = reader.Post("/api/objects", new { name = "X", typeKey = "application" });
            Equal(HttpStatusCode.Forbidden, write.StatusCode, "écriture d'objet");
            Equal("Votre rôle ne permet pas cette action.", Browser.Json(write).GetProperty("error").GetString(), "message");
            Equal(HttpStatusCode.Forbidden, reader.Get("/api/users").StatusCode, "liste des comptes");
            Equal(HttpStatusCode.Forbidden, reader.Get("/api/export/json").StatusCode, "export JSON complet");
            Equal(HttpStatusCode.Forbidden, reader.Post("/api/import/archimate").StatusCode, "import");
        });

        Test("rôles : contributeur écrit des objets mais ne touche ni au métamodèle ni aux comptes", () =>
        {
            using var server = new ApiTests.Server("--Auth:Mode=local");
            var admin = BootstrapAdmin(server);
            var contributor = UserWithRole(server, admin, "paul", Roles.Contributor);
            var typeKey = Browser.Json(contributor.Get("/api/metamodel")).GetProperty("objectTypes")[0].GetProperty("key").GetString();
            Equal(HttpStatusCode.OK, contributor.Post("/api/objects", new { name = "Objet de Paul", typeKey }).StatusCode, "création d'objet");
            Equal(HttpStatusCode.Forbidden, contributor.Send(HttpMethod.Put, "/api/metamodel/settings", new { }).StatusCode, "métamodèle");
            Equal(HttpStatusCode.Forbidden, contributor.Post("/api/users", new { login = "x1x", name = "X", role = "lecteur" }).StatusCode, "création de compte");
            Equal(HttpStatusCode.Forbidden, contributor.Post("/api/validation/id-1").StatusCode, "validation réservée au valideur");
        });

        Test("rôles : valideur franchit le seuil de validation, administrateur peut tout", () =>
        {
            using var server = new ApiTests.Server("--Auth:Mode=local");
            var admin = BootstrapAdmin(server);
            var validator = UserWithRole(server, admin, "vera", Roles.Validator);
            True(validator.Post("/api/validation/id-1").StatusCode != HttpStatusCode.Forbidden, "validation passe le contrôle de rôle");
            Equal(HttpStatusCode.OK, validator.Post("/api/objects", new { name = "Objet de Vera", typeKey = FirstTypeKey(admin) }).StatusCode, "le valideur écrit aussi des objets");
            Equal(HttpStatusCode.Forbidden, validator.Send(HttpMethod.Put, "/api/metamodel/settings", new { }).StatusCode, "mais pas le métamodèle");
            Equal(HttpStatusCode.OK, admin.Get("/api/export/json").StatusCode, "export JSON par l'administrateur");
            Equal(HttpStatusCode.OK, admin.Post("/api/import/archimate", null).StatusCode, "import par l'administrateur");
        });

        Test("journal : l'auteur d'une modification est l'identifiant connecté", () =>
        {
            using var server = new ApiTests.Server("--Auth:Mode=local");
            var admin = BootstrapAdmin(server);
            var contributor = UserWithRole(server, admin, "paul", Roles.Contributor);
            Equal(HttpStatusCode.OK, contributor.Post("/api/objects", new { name = "Objet tracé", typeKey = FirstTypeKey(admin) }).StatusCode, "création");
            var entry = Browser.Json(admin.Get("/api/journal?limit=20")).GetProperty("items").EnumerateArray().First(e => e.GetProperty("action").GetString() == "Objet créé");
            Equal("paul", entry.GetProperty("user").GetString(), "auteur");
        });

        Test("mode ouvert : session administrateur anonyme, tout passe, journal « anonyme »", () =>
        {
            using var server = new ApiTests.Server();
            var browser = new Browser(server);
            var session = browser.Session();
            Equal("ouvert", session.GetProperty("mode").GetString(), "mode");
            True(session.GetProperty("authenticated").GetBoolean(), "authentifié d'office");
            Equal("administrateur", session.GetProperty("user").GetProperty("role").GetString(), "rôle");
            Equal(HttpStatusCode.OK, browser.Post("/api/objects", new { name = "Objet ouvert", typeKey = FirstTypeKey(browser) }, csrf: false).StatusCode, "écriture sans jeton");
            var entry = Browser.Json(browser.Get("/api/journal?limit=5")).GetProperty("items").EnumerateArray().First(e => e.GetProperty("action").GetString() == "Objet créé");
            Equal("anonyme", entry.GetProperty("user").GetString(), "auteur");
            Equal(HttpStatusCode.BadRequest, browser.Post("/api/users", new { login = "abc", name = "A", role = "lecteur" }).StatusCode, "pas de gestion de comptes en mode ouvert");
        });
    }

    private static string FirstTypeKey(Browser browser) =>
        Browser.Json(browser.Get("/api/metamodel")).GetProperty("objectTypes")[0].GetProperty("key").GetString()!;

    private static void RunAdministration()
    {
        Test("administration : création, rôle, désactivation et réinitialisation par l'API, sans hachage dans les réponses", () =>
        {
            using var server = new ApiTests.Server("--Auth:Mode=local");
            var admin = BootstrapAdmin(server);
            var created = admin.Post("/api/users", new { login = "alice", name = "Alice", role = "lecteur" });
            Equal(HttpStatusCode.Created, created.StatusCode, "création");
            var body = Browser.Text(created);
            var id = JsonDocument.Parse(body).RootElement.GetProperty("user").GetProperty("id").GetString()!;
            True(!body.Contains("passwordHash", StringComparison.OrdinalIgnoreCase) && !body.Contains("AQAAAA"), "pas de hachage à la création");
            Equal(HttpStatusCode.Conflict, admin.Post("/api/users", new { login = "ALICE", name = "Doublon", role = "lecteur" }).StatusCode, "doublon");
            Equal(HttpStatusCode.BadRequest, admin.Post("/api/users", new { login = "bob", name = "Bob", role = "roi" }).StatusCode, "rôle inconnu");

            var promoted = admin.Send(HttpMethod.Put, "/api/users/" + id, new { name = "Alice M.", role = "valideur", disabled = false });
            Equal(HttpStatusCode.OK, promoted.StatusCode, "changement de rôle");
            Equal("valideur", Browser.Json(promoted).GetProperty("role").GetString(), "rôle");

            var reset = admin.Post($"/api/users/{id}/reset-password");
            Equal(HttpStatusCode.OK, reset.StatusCode, "réinitialisation");
            Equal(16, Browser.Json(reset).GetProperty("temporaryPassword").GetString()!.Length, "mot de passe temporaire");

            var list = Browser.Text(admin.Get("/api/users"));
            True(!list.Contains("passwordHash", StringComparison.OrdinalIgnoreCase) && !list.Contains("AQAAAA"), "pas de hachage dans la liste");
            Equal(2, JsonDocument.Parse(list).RootElement.GetArrayLength(), "deux comptes");

            var journal = Browser.Text(admin.Get("/api/journal?limit=50"));
            True(journal.Contains("Compte créé") && journal.Contains("Rôle modifié") && journal.Contains("Mot de passe réinitialisé"), "actions journalisées");
            True(!journal.Contains("AQAAAA"), "aucun hachage dans le journal");
            Equal(HttpStatusCode.NotFound, admin.Send(HttpMethod.Put, "/api/users/u-inconnu", new { name = "X", role = "lecteur", disabled = false }).StatusCode, "compte inconnu");
        });

        Test("administration : le dernier administrateur est protégé par l'API", () =>
        {
            using var server = new ApiTests.Server("--Auth:Mode=local");
            var admin = BootstrapAdmin(server);
            var id = Browser.Json(admin.Get("/api/users"))[0].GetProperty("id").GetString();
            Equal(HttpStatusCode.Conflict, admin.Send(HttpMethod.Put, "/api/users/" + id, new { name = "Administrateur", role = "lecteur", disabled = false }).StatusCode, "rétrogradation");
            Equal(HttpStatusCode.Conflict, admin.Send(HttpMethod.Put, "/api/users/" + id, new { name = "Administrateur", role = "administrateur", disabled = true }).StatusCode, "désactivation");
        });

        Test("administration : rôle et désactivation prennent effet tout de suite sur la session ouverte", () =>
        {
            using var server = new ApiTests.Server("--Auth:Mode=local");
            var admin = BootstrapAdmin(server);
            var user = UserWithRole(server, admin, "paul", Roles.Contributor);
            var id = Browser.Json(admin.Get("/api/users")).EnumerateArray().First(u => u.GetProperty("login").GetString() == "paul").GetProperty("id").GetString();
            Equal(HttpStatusCode.OK, user.Post("/api/objects", new { name = "Avant", typeKey = FirstTypeKey(admin) }).StatusCode, "écriture permise");
            admin.Send(HttpMethod.Put, "/api/users/" + id, new { name = "paul", role = "lecteur", disabled = false });
            Equal(HttpStatusCode.Forbidden, user.Post("/api/objects", new { name = "Après", typeKey = FirstTypeKey(admin) }).StatusCode, "écriture refusée après rétrogradation");
            admin.Send(HttpMethod.Put, "/api/users/" + id, new { name = "paul", role = "lecteur", disabled = true });
            Equal(HttpStatusCode.Unauthorized, user.Get("/api/summary").StatusCode, "session coupée après désactivation");
        });

        Test("mot de passe temporaire : tout est refusé sauf le changement de mot de passe", () =>
        {
            using var server = new ApiTests.Server("--Auth:Mode=local");
            var admin = BootstrapAdmin(server);
            var temporary = Browser.Json(admin.Post("/api/users", new { login = "nina", name = "Nina", role = "lecteur" })).GetProperty("temporaryPassword").GetString()!;
            var nina = new Browser(server);
            nina.Login("nina", temporary);
            True(nina.Session().GetProperty("user").GetProperty("mustChangePassword").GetBoolean(), "obligation annoncée");
            Equal(HttpStatusCode.Forbidden, nina.Get("/api/summary").StatusCode, "lecture bloquée");
            Equal(HttpStatusCode.NoContent, nina.Post("/api/auth/password", new { current = temporary, next = UserPassword }).StatusCode, "changement");
            Equal(HttpStatusCode.OK, nina.Get("/api/summary").StatusCode, "lecture rendue");
        });

        Test("l'export JSON du référentiel ne contient ni hachage ni compte", () =>
        {
            using var server = new ApiTests.Server("--Auth:Mode=local");
            var admin = BootstrapAdmin(server);
            var export = Browser.Text(admin.Get("/api/export/json"));
            True(!export.Contains("PasswordHash", StringComparison.OrdinalIgnoreCase) && !export.Contains("AQAAAA"), "aucun hachage");
            True(File.ReadAllText(Path.Combine(server.DataDirectory, "utilisateurs.json")).Contains("PasswordHash"), "les hachages sont dans utilisateurs.json");
        });
    }

    private static void RunProtections()
    {
        Test("CSRF : écriture sans jeton refusée, avec jeton acceptée, jeton d'un autre utilisateur refusé", () =>
        {
            using var server = new ApiTests.Server("--Auth:Mode=local");
            var admin = BootstrapAdmin(server);
            var body = new { name = "Objet CSRF", typeKey = FirstTypeKey(admin) };
            var without = admin.Post("/api/objects", body, csrf: false);
            Equal(HttpStatusCode.BadRequest, without.StatusCode, "sans jeton");
            True(Browser.Json(without).GetProperty("error").GetString()!.Contains("Jeton"), "message");
            Equal(HttpStatusCode.OK, admin.Post("/api/objects", body).StatusCode, "avec jeton");
            Equal(HttpStatusCode.BadRequest, admin.Send(HttpMethod.Put, "/api/users/x", new { name = "x", role = "lecteur", disabled = false }, csrf: false).StatusCode, "PUT sans jeton");
            Equal(HttpStatusCode.BadRequest, admin.Send(HttpMethod.Delete, "/api/objects/x", csrf: false).StatusCode, "DELETE sans jeton");
            Equal(HttpStatusCode.BadRequest, new Browser(server).Post("/api/auth/login", new { login = "admin", password = AdminPassword }, csrf: false).StatusCode, "connexion sans jeton");
        });

        Test("limitation de débit : la onzième tentative de connexion en une minute reçoit 429", () =>
        {
            using var server = new ApiTests.Server("--Auth:Mode=local");
            var browser = new Browser(server);
            var statuses = Enumerable.Range(0, 11).Select(_ => browser.Login("fantome", "mot-de-passe-faux").StatusCode).ToList();
            Equal(10, statuses.Count(s => s == HttpStatusCode.Unauthorized), "dix tentatives traitées");
            Equal(HttpStatusCode.TooManyRequests, statuses[10], "onzième refusée");
            Equal(HttpStatusCode.OK, browser.Get("/api/auth/session").StatusCode, "les autres routes ne sont pas limitées");
        });

        Test("en-têtes de sécurité présents sur l'API et sur la page d'accueil", () =>
        {
            using var server = new ApiTests.Server("--Auth:Mode=local");
            foreach (var path in new[] { "/api/auth/session", "/api/summary", "/" })
            {
                var response = new Browser(server).Get(path);
                Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single(), "nosniff " + path);
                Equal("same-origin", response.Headers.GetValues("Referrer-Policy").Single(), "Referrer-Policy " + path);
                Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single(), "X-Frame-Options " + path);
                True(response.Headers.GetValues("Content-Security-Policy").Single().Contains("default-src 'self'"), "CSP " + path);
            }
        });

        Test("corps trop gros refusé sur les routes de comptes", () =>
        {
            using var server = new ApiTests.Server("--Auth:Mode=local");
            var response = new Browser(server).Login("admin", new string('x', 64 * 1024));
            Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode, "corps de 64 Ko");
        });

        Test("les clés de protection et le fichier du code sont dans le dossier de données", () =>
        {
            using var server = new ApiTests.Server("--Auth:Mode=local");
            BootstrapAdmin(server);
            True(Directory.Exists(Path.Combine(server.DataDirectory, AeTool.Api.KeyStorage.DirectoryName)), "dossier des clés");
            True(File.Exists(Path.Combine(server.DataDirectory, "utilisateurs.json")), "fichier des comptes");
        });
    }
}
