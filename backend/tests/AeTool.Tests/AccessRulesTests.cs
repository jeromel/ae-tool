using AeTool.Api.Auth;
using static AeTool.Tests.Harness;

namespace AeTool.Tests;

/// <summary>La table des habilitations : une vérification par ligne de la spécification, plus le refus par défaut.</summary>
public static class AccessRulesTests
{
    private static void Expect(string? expected, string method, string path) =>
        Equal(expected, AccessRules.MinimumRole(method, path), $"{method} {path}");

    public static void Run()
    {
        Console.WriteLine("Habilitations (table des rôles)");

        Test("la hiérarchie des rôles est stricte et un rôle inconnu ne vaut rien", () =>
        {
            True(Roles.AtLeast(Roles.Administrator, Roles.Reader), "administrateur >= lecteur");
            True(Roles.AtLeast(Roles.Validator, Roles.Contributor), "valideur >= contributeur");
            True(!Roles.AtLeast(Roles.Contributor, Roles.Validator), "contributeur < valideur");
            True(!Roles.AtLeast("pirate", Roles.Reader), "rôle inconnu");
            True(!Roles.AtLeast(null, Roles.Reader), "pas de rôle");
        });

        Test("requêtes anonymes : session, connexion, premier lancement, défi Entra", () =>
        {
            Expect(null, "GET", "/api/auth/session");
            Expect(null, "POST", "/api/auth/login");
            Expect(null, "POST", "/api/auth/setup");
            Expect(null, "GET", "/api/auth/entra/login");
        });

        Test("autres routes d'authentification : lecteur", () =>
        {
            Expect(Roles.Reader, "POST", "/api/auth/logout");
            Expect(Roles.Reader, "POST", "/api/auth/password");
            Expect(Roles.Reader, "GET", "/api/auth/login"); // la connexion se fait en POST seulement
            Expect(Roles.Reader, "POST", "/api/auth/session");
        });

        Test("comptes, administration et sources : administrateur, quelle que soit la méthode", () =>
        {
            foreach (var method in new[] { "GET", "POST", "PUT", "DELETE" })
            {
                Expect(Roles.Administrator, method, "/api/users");
                Expect(Roles.Administrator, method, "/api/users/u-1/reset-password");
                Expect(Roles.Administrator, method, "/api/admin/reset");
                Expect(Roles.Administrator, method, "/api/sources/xyz");
            }
        });

        Test("export JSON complet : administrateur ; les autres exports : lecteur", () =>
        {
            Expect(Roles.Administrator, "GET", "/api/export/json");
            Expect(Roles.Reader, "GET", "/api/export/archimate");
            Expect(Roles.Reader, "GET", "/api/export/objects.csv");
        });

        Test("lectures : lecteur", () =>
        {
            foreach (var path in new[] { "/api/summary", "/api/objects", "/api/objects/id-1", "/api/portfolio", "/api/metamodel", "/api/validation/pending", "/api/objects/id-1/history", "/api/import/reports" })
                Expect(Roles.Reader, "GET", path);
            Expect(Roles.Reader, "HEAD", "/api/summary");
        });

        Test("écritures sur objets, relations, projets, portefeuille : contributeur", () =>
        {
            Expect(Roles.Contributor, "POST", "/api/objects");
            Expect(Roles.Contributor, "PUT", "/api/objects/id-1");
            Expect(Roles.Contributor, "DELETE", "/api/objects/id-1");
            Expect(Roles.Contributor, "POST", "/api/relationships");
            Expect(Roles.Contributor, "DELETE", "/api/relationships/r-1");
            Expect(Roles.Contributor, "PUT", "/api/projects/p-1/impacts/o-1");
            Expect(Roles.Contributor, "PUT", "/api/portfolio/id-1");
        });

        Test("écritures de validation : valideur", () =>
        {
            Expect(Roles.Validator, "POST", "/api/validation/id-1");
            Expect(Roles.Validator, "PUT", "/api/validation/id-1/status");
        });

        Test("journal : valideur ; sauvegardes et état du stockage : administrateur ; proposer : contributeur", () =>
        {
            Expect(Roles.Validator, "GET", "/api/journal");
            Expect(Roles.Validator, "GET", "/api/Journal/");
            Expect(Roles.Administrator, "GET", "/api/admin/status");
            Expect(Roles.Administrator, "GET", "/api/admin/backups");
            Expect(Roles.Administrator, "POST", "/api/admin/backups");
            Expect(Roles.Administrator, "GET", "/api/admin/backups/referentiel-20250101-000000-auto.json.gz");
            Expect(Roles.Administrator, "POST", "/api/admin/backups/x/restore");
            Expect(Roles.Contributor, "POST", "/api/objects/id-1/propose");
            Expect(Roles.Validator, "POST", "/api/validation/objects/id-1/validate");
            Expect(Roles.Validator, "POST", "/api/validation/objects/id-1/reject");
            Expect(Roles.Validator, "POST", "/api/validation/validate");
        });

        Test("métamodèle et imports : administrateur", () =>
        {
            Expect(Roles.Administrator, "PUT", "/api/metamodel/scoring");
            Expect(Roles.Administrator, "POST", "/api/metamodel/import");
            Expect(Roles.Administrator, "POST", "/api/import/archimate");
            Expect(Roles.Administrator, "POST", "/api/import/urba");
        });

        Test("refus par défaut : toute écriture inconnue exige administrateur", () =>
        {
            Expect(Roles.Administrator, "POST", "/api/nouveau-domaine");
            Expect(Roles.Administrator, "PUT", "/api/objectsx"); // pas un sous-chemin de /api/objects
            Expect(Roles.Administrator, "PATCH", "/api/nouveau-domaine/id-1");
            Expect(Roles.Administrator, "OPTIONS", "/api/inconnu");
        });

        Test("la casse et le « / » final ne contournent pas la table", () =>
        {
            Expect(Roles.Administrator, "GET", "/API/Users");
            Expect(Roles.Administrator, "GET", "/api/users/");
            Expect(Roles.Administrator, "POST", "/Api/Metamodel/Import");
            Expect(Roles.Administrator, "GET", "/api/EXPORT/json");
        });

        Test("l'interface (hors /api) reste accessible sans session", () =>
        {
            Expect(null, "GET", "/");
            Expect(null, "GET", "/index.html");
            Expect(null, "GET", "/connexion");
            Expect(null, "GET", "/signin-entra");
        });
    }
}
