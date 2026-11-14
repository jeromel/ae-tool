using System.Text.Json;
using AeTool.Connectors;
using AeTool.Core;
using static AeTool.Tests.Harness;

namespace AeTool.Tests;

public static class ScoringTests
{
    public static void Run()
    {
        Console.WriteLine("Notation d'urbanisation");
        var grid = ScoringGrid.Default();

        Test("les poids de chaque critère totalisent 100", () =>
        {
            foreach (var c in grid.Criteria) Equal(100.0, c.SubCriteria.Sum(s => s.Weight), c.Code);
        });
        Test("conversion du taux en note : seuils 40 %, 65 %, 85 %", () =>
        {
            Equal(1, ScoringEngine.Note(0.399, grid.Thresholds)); Equal(2, ScoringEngine.Note(0.40, grid.Thresholds));
            Equal(2, ScoringEngine.Note(0.649, grid.Thresholds)); Equal(3, ScoringEngine.Note(0.65, grid.Thresholds));
            Equal(3, ScoringEngine.Note(0.849, grid.Thresholds)); Equal(4, ScoringEngine.Note(0.85, grid.Thresholds));
            Equal(null, ScoringEngine.Note(null, grid.Thresholds));
        });

        Assessment Scored(double value, string reference = "DAT v2")
        {
            var a = ScoringEngine.Blank(grid, "x");
            foreach (var e in a.Evidences) { e.Value = value.ToString(System.Globalization.CultureInfo.InvariantCulture); e.Reference = reference; }
            foreach (var c in grid.Criteria) a.RedTriggers[c.Code] = "Non";
            a.Cm = 2; a.Need = "Oui"; a.HeritageStake = "Non";
            return a;
        }

        Test("NC ne vaut jamais zéro : une preuve inconnue bloque la note du critère", () =>
        {
            var a = Scored(1);
            a.Evidences.First(e => e.Code == "C1").Value = "NC";
            var r = ScoringEngine.Evaluate(grid, a, false);
            Equal(null, r.Notes["C1"]); Equal(4, r.Notes["C2"]); Equal(null, r.Mastery); Equal(null, r.ComputedDecision);
            True(r.Missing.Contains("Preuves / références"), "les preuves manquantes doivent être signalées");
        });
        Test("une preuve chiffrée sans référence n'est pas retenue", () =>
        {
            var a = Scored(1);
            a.Evidences.First(e => e.Code == "C2").Reference = " ";
            Equal(null, ScoringEngine.Evaluate(grid, a, false).Notes["C2"]);
        });
        Test("N/A sort du dénominateur", () =>
        {
            var a = Scored(0);
            var c1 = a.Evidences.Where(e => e.Code == "C1").ToList();
            c1[0].Value = "1";              // poids 25
            foreach (var e in c1.Skip(2)) e.Value = "N/A"; // reste : 25 + 20 applicables
            Equal(25.0 / 45.0, ScoringEngine.Rate(grid.Criteria[0], a.Evidences)!.Value);
        });
        Test("taux pondéré : 0,5 partout donne 50 %, note 2", () =>
        {
            var r = ScoringEngine.Evaluate(grid, Scored(0.5), false);
            Equal(0.5, r.Rates["C3"]!.Value); Equal(2, r.Notes["C3"]);
        });
        Test("quadrant : les quatre orientations", () =>
        {
            string? Quadrant(double v, string need) { var a = Scored(v); a.Need = need; a.Cm = 1; return ScoringEngine.Evaluate(grid, a, false).ComputedDecision; }
            Equal("Maintenir", Quadrant(1, "Oui")); Equal("Moderniser / Migrer", Quadrant(0, "Oui"));
            Equal("Tolérer", Quadrant(1, "Non")); Equal("Éliminer", Quadrant(0, "Non"));
        });
        Test("règle de garde : CM >= 3 et besoin non maintenu donne À instruire", () =>
        {
            var a = Scored(1); a.Cm = 3; a.Need = "Non";
            Equal("À instruire", ScoringEngine.Evaluate(grid, a, false).ComputedDecision);
        });
        Test("déclencheur rouge inconnu : pas de décision définitive", () =>
        {
            var a = Scored(1); a.RedTriggers["C4"] = "NC";
            var r = ScoringEngine.Evaluate(grid, a, false);
            Equal(null, r.ComputedDecision); Equal("Maintenir", r.Quadrant);
        });
        Test("exemple Facturation du support de méthode (diapositive 15)", () =>
        {
            // Notes de l'exemple : C1 = 1, C2 = 1, C3 = 2, C4 = 1 ; CM 3 ; besoin Oui ; rouge C2.
            var a = new Assessment { Cm = 3, Need = "Oui", HeritageStake = "Non", RetainedDecision = "Migrer", Justification = "Sortie de la base historique" };
            foreach (var c in grid.Criteria) a.RedTriggers[c.Code] = c.Code == "C2" ? "Oui" : "Non";
            var res = new ScoringResult();
            res.Notes["C1"] = ScoringEngine.Note(0.33, grid.Thresholds); res.Notes["C2"] = ScoringEngine.Note(0.35, grid.Thresholds);
            res.Notes["C3"] = ScoringEngine.Note(0.42, grid.Thresholds); res.Notes["C4"] = ScoringEngine.Note(0.25, grid.Thresholds);
            ScoringEngine.Decide(grid, a, res);
            Equal(1.0, res.Mastery!.Value, "maîtrise"); Equal("Moderniser / Migrer", res.Quadrant);
            Equal("À instruire", res.ComputedDecision); Equal(9.0, res.Urgency!.Value, "urgence"); Equal("Haute", res.Priority);
            Equal("Sécuriser", res.SecurityAction); Equal(null, res.HeritageAction);
            Equal("Écart justifié, à revoir en comité", res.Control);
        });
        Test("priorité : haute si U > 6, moyenne de 4 à 6 inclus, basse sinon", () =>
        {
            string? Priority(int cm, int c1, int c2)
            {
                var res = new ScoringResult(); res.Notes["C1"] = c1; res.Notes["C2"] = c2; res.Notes["C3"] = 4; res.Notes["C4"] = 4;
                return ScoringEngine.Decide(grid, new Assessment { Cm = cm, Need = "Oui" }, res).Priority;
            }
            Equal("Moyenne", Priority(3, 2, 2)); // U = 6
            Equal("Moyenne", Priority(4, 3, 3)); // U = 4
            Equal("Basse", Priority(3, 3, 3));   // U = 3
            Equal("Haute", Priority(4, 2, 2));   // U = 8
        });
        Test("application retirée : aucune note, contrôle sans objet", () =>
        {
            var r = ScoringEngine.Evaluate(grid, Scored(1), retired: true);
            Equal("Sans objet", r.Control); Equal(0, r.Notes.Count);
        });
    }
}
