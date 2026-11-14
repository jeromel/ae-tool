using System.Text.Json;
using AeTool.Connectors;
using AeTool.Core;
using static AeTool.Tests.Harness;

namespace AeTool.Tests;

public static class UrbaTests
{
    public static void Run()
    {
        Console.WriteLine("Reprise du classeur d'urbanisation");

        Stream Workbook(string appName = "ITSM-SUITE", string proof = "1", string? reference = "DAT v3") => Fixtures.Xlsx(
            ("Référentiel consolidé", new[]
            {
                new string?[] { "Légende" },
                new string?[] { "ID", "Application / composant", "Couche SI", "Domaine métier", "État actuel", "CM proposé - Criticité métier (1-4)",
                    "Décision retenue", "Besoin maintenu ?", "Déclencheur rouge C1", "Déclencheur rouge C2", "Déclencheur rouge C3", "Déclencheur rouge C4",
                    "Enjeu patrimonial à traiter ?" },
                new string?[] { "APP-001", appName, "Applicative", "ITSM", "En exploitation", "3", "Maintenir", "Oui", "Non", "Non", "Non", "Non", "Non" },
                new string?[] { "APP-002", "Sauvegarde", "Technique", "Infrastructure", "Retirée", null, null, "Sans objet", "NC", "NC", "NC", "NC", null },
            }),
            ("Grille des preuves", new[]
            {
                new string?[] { "Code", "Critère", "Sous-critère", "Poids", "Preuve attendue" },
                new string?[] { "C1", "Santé", "Support", "60", "Version supportée" }, new string?[] { "C1", "Santé", "Socle", "40", "OS maintenu" },
                new string?[] { "C2", "Maîtrise", "DAT", "100", "DAT à jour" },
            }),
            ("Méthode de notation", new[] { new string?[] { "Paramètre", "Valeur", "Seuil maîtrise technique", "3" } }),
            ("Preuves de notation", new[]
            {
                new string?[] { "ID", "Application", "Code", "Sous-critère", "Poids", "Preuve", "Référence / justification" },
                new string?[] { "APP-001", appName, "C1", "Support", "60", proof, reference },
                new string?[] { "APP-001", appName, "C1", "Socle", "40", "0.5", "Inventaire" },
                new string?[] { "APP-001", appName, "C2", "DAT", "100", "1", "DAT v3" },
            }));

        Test("lecteur xlsx : feuilles, cellules vides et nombres", () =>
        {
            var book = XlsxReader.Read(Workbook());
            Equal(4, book.Count); Equal("APP-001", book["Référentiel consolidé"][2][0]); Equal(null, book["Référentiel consolidé"][3][5]);
        });
        Test("rapproche par nom, crée les applications absentes, reprend grille, seuils et preuves", () =>
        {
            var repo = Imported(out _);
            var report = UrbaImporter.Import(Workbook(), repo);
            Equal(0, report.Anomalies.Count(a => a.Severity == "erreur"), "erreurs");
            Equal("APP-001", repo.Objects["id-itsm"].Get(UrbaImporter.IdKey));
            Equal("ITSM", repo.Objects["id-itsm"].Get("urba:Domaine métier"));
            Equal("logiciel-systeme", repo.Objects["id-urba-app-002"].TypeKey);
            Equal(2, repo.Metamodel.Scoring.Criteria.Count); Equal(3.0, repo.Metamodel.Scoring.Thresholds.Mastery);
            var an = new Analyzer(repo);
            var r = ScoringEngine.Evaluate(repo.Metamodel.Scoring, repo.Assessments["id-itsm"], an.IsRetired(repo.Objects["id-itsm"]));
            Equal(0.8, r.Rates["C1"]!.Value, "taux C1"); Equal(3, r.Notes["C1"]); Equal(4, r.Notes["C2"]);
            Equal(3.5, r.Mastery!.Value); Equal("Maintenir", r.ComputedDecision); Equal("Conforme au calcul", r.Control);
            True(an.IsRetired(repo.Objects["id-urba-app-002"]), "l'application retirée doit être reconnue");
        });
        Test("reprise rejouable : aucun doublon, mise à jour détectée", () =>
        {
            var repo = Imported(out _);
            UrbaImporter.Import(Workbook(), repo);
            var objects = repo.Objects.Count;
            var second = UrbaImporter.Import(Workbook(), repo);
            Equal(objects, repo.Objects.Count); Equal(2, second.Counts["Applications"].Unchanged); Equal(2, second.Counts["Évaluations"].Unchanged);
            // Renommée dans le classeur : rattachée par son identifiant d'urbanisation, pas recréée.
            UrbaImporter.Import(Workbook(appName: "ITSM renommé"), repo);
            Equal(objects, repo.Objects.Count);
        });
        Test("un NC du classeur n'écrase pas une preuve saisie dans l'outil", () =>
        {
            var repo = Imported(out _);
            UrbaImporter.Import(Workbook(), repo);
            UrbaImporter.Import(Workbook(proof: "NC", reference: null), repo);
            Equal("1", repo.Assessments["id-itsm"].Evidences.First(e => e.SubCriterion == "Support").Value);
        });
        Test("classeur sans la feuille attendue : erreur explicite", () =>
        {
            var report = UrbaImporter.Import(Fixtures.Xlsx(("Autre", new[] { new string?[] { "x" } })), new Repository());
            Equal("feuille-absente", report.Anomalies.Single().Code);
        });
    }
}
