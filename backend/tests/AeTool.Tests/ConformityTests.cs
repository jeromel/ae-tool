using AeTool.Core;
using static AeTool.Tests.Harness;

namespace AeTool.Tests;

public static class ConformityTests
{
    public static void Run()
    {
        Console.WriteLine("Conformité au métamodèle");

        Repository Prepared()
        {
            var repo = Imported(out _);
            var m = repo.Metamodel;
            m.PropertyDefinitions.Add(new PropertyDefinition { Key = "niveau", Label = "Niveau", DataType = PropertyTypes.List, AllowedValues = { "A", "B" } });
            m.PropertyDefinitions.Add(new PropertyDefinition { Key = "cout", Label = "Coût", DataType = PropertyTypes.Number });
            m.PropertyDefinitions.Add(new PropertyDefinition { Key = "echeance", Label = "Échéance", DataType = PropertyTypes.Date });
            m.PropertyDefinitions.Add(new PropertyDefinition { Key = "actif", Label = "Actif", DataType = PropertyTypes.Boolean });
            m.PropertyDefinitions.Add(new PropertyDefinition { Key = "responsable", Label = "Responsable", Required = true, AppliesTo = { "application" } });
            return repo;
        }

        List<ConformityIssue> Issues(Repository repo, string code) => Conformity.Check(repo).Where(i => i.Code == code).ToList();

        Test("un référentiel conforme n'a aucun écart", () =>
        {
            var repo = Imported(out _);
            Equal(0, Conformity.Check(repo).Count, "écarts");
        });
        Test("relation entre deux types non autorisés par les couples", () =>
        {
            var repo = Prepared();
            repo.Metamodel.RelType("realisation")!.AllowedPairs.Add("application>capacite");
            Equal(0, Issues(repo, "relation-non-autorisee").Count, "les réalisations application > capacité sont autorisées");
            repo.Relationships["id-r1"].SourceId = "id-srv";
            var issue = Issues(repo, "relation-non-autorisee").Single();
            Equal("id-r1", issue.Id, "relation fautive");
            Equal("realisation", issue.Key, "clé du type en cause");
        });
        Test("propriété obligatoire absente, limitée aux types concernés", () =>
        {
            var repo = Prepared();
            var missing = Issues(repo, "propriete-obligatoire-absente");
            Equal(3, missing.Count, "les trois applications n'ont pas de responsable");
            True(missing.All(i => i.Key == "responsable" && i.Kind == "objet"), "clé et nature");
            repo.Objects["id-itsm"].Set("responsable", "Marie");
            Equal(2, Issues(repo, "propriete-obligatoire-absente").Count, "une fois renseignée");
            repo.Objects["id-report"].Set("responsable", "  ");
            Equal(2, Issues(repo, "propriete-obligatoire-absente").Count, "une valeur blanche ne compte pas");
        });
        Test("valeur hors liste, nombre, date et booléen invalides", () =>
        {
            var repo = Prepared();
            var o = repo.Objects["id-itsm"];
            o.Set("niveau", "C"); o.Set("cout", "beaucoup"); o.Set("echeance", "31/12/2025"); o.Set("actif", "peut-être");
            Equal(1, Issues(repo, "valeur-hors-liste").Count, "liste");
            Equal(1, Issues(repo, "valeur-nombre-invalide").Count, "nombre");
            Equal(1, Issues(repo, "valeur-date-invalide").Count, "date");
            Equal(1, Issues(repo, "valeur-booleen-invalide").Count, "booléen");
            o.Set("niveau", "A"); o.Set("cout", "12,5"); o.Set("echeance", "2025-12-31"); o.Set("actif", "Oui");
            Equal(0, Conformity.Check(repo).Count(i => i.Code.StartsWith("valeur-")), "valeurs conformes");
        });
        Test("objet dont le type est inconnu ou archivé", () =>
        {
            var repo = Prepared();
            repo.Objects["x"] = new ArchObject { Id = "x", TypeKey = "disparu", Name = "X" };
            repo.Metamodel.Type("noeud")!.Archived = true;
            Equal("x", Issues(repo, "type-inconnu").Single().Id, "type inconnu");
            Equal("id-srv", Issues(repo, "type-archive").Single().Id, "type archivé");
        });
        Test("un objet archivé n'est pas contrôlé", () =>
        {
            var repo = Prepared();
            repo.Objects["id-itsm"].Archived = true;
            Equal(2, Issues(repo, "propriete-obligatoire-absente").Count, "objets actifs seulement");
        });
    }
}
