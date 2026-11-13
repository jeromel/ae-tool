using System.Globalization;

namespace AeTool.Core;

// Méthode de notation d'urbanisation : CM + critères notés sur preuves, quadrant, règle de garde,
// surcouches sécurité et patrimoine. Tous les paramètres (critères, poids, seuils) sont des données.

public sealed class SubCriterion
{
    public string Label { get; set; } = "";
    public double Weight { get; set; }
    public string? ExpectedEvidence { get; set; }
}

public sealed class Criterion
{
    public string Code { get; set; } = "";
    public string Label { get; set; } = "";
    public string? ScoredBy { get; set; }
    public string? RedTrigger { get; set; }
    public List<SubCriterion> SubCriteria { get; set; } = new();
}

public sealed class ScoringThresholds
{
    public double Note2 { get; set; } = 0.40;
    public double Note3 { get; set; } = 0.65;
    public double Note4 { get; set; } = 0.85;
    public double Mastery { get; set; } = 2.5;
    public double PriorityHigh { get; set; } = 6;   // strictement supérieur
    public double PriorityMedium { get; set; } = 4; // supérieur ou égal
    public int GuardCm { get; set; } = 3;
    public int LowSecurityNote { get; set; } = 2;
}

public sealed class ScoringGrid
{
    public List<Criterion> Criteria { get; set; } = new();
    public ScoringThresholds Thresholds { get; set; } = new();
    public List<string> MasteryCriteria { get; set; } = new() { "C1", "C2" };
    public string SecurityCriterion { get; set; } = "C3";
    public string HeritageCriterion { get; set; } = "C4";
    public string StatePropertyKey { get; set; } = "urba:État actuel";
    public List<string> RetiredStates { get; set; } = new() { "Retirée", "Usage arrêté" };
    public List<string> Decisions { get; set; } = new()
        { "Maintenir", "Moderniser / Migrer", "Migrer", "Tolérer", "Éliminer", "À instruire" };

    public static ScoringGrid Default()
    {
        var g = new ScoringGrid();
        Criterion C(string code, string label, string by, string red, params (string, double, string)[] subs)
        {
            var c = new Criterion { Code = code, Label = label, ScoredBy = by, RedTrigger = red };
            foreach (var (l, w, e) in subs) c.SubCriteria.Add(new SubCriterion { Label = l, Weight = w, ExpectedEvidence = e });
            return c;
        }
        g.Criteria.Add(C("C1", "Santé & pérennité", "DSI / exploitation",
            "Composant critique hors support, sans mesure compensatoire ni trajectoire validée.",
            ("Support éditeur / communauté", 25, "Version supportée et trajectoire connue"),
            ("Obsolescence du socle", 20, "OS, SGBD, middleware maintenus"),
            ("Maintenabilité du code", 20, "Code accessible, dette et dépendances connues"),
            ("Industrialisation", 15, "Build, déploiement et tests reproductibles"),
            ("Observabilité", 10, "Journaux, métriques et alertes exploitables"),
            ("Capacité / performance", 10, "Mesurée, seuils connus, saturation maîtrisée")));
        g.Criteria.Add(C("C2", "Maîtrise & continuité", "DSI / exploitation",
            "CM ≥ 3 sans PRA testé, ou dépendance à un sachant sans transfert organisé.",
            ("DAT à jour et validé", 15, "Architecture, flux, composants, dépendances"),
            ("DEX à jour et utilisable", 15, "Exploitation courante, incidents, démarrage / arrêt"),
            ("PRA formalisé", 15, "Scénarios, objectifs, responsabilités"),
            ("PRA testé annuellement", 15, "Compte rendu, écarts et actions"),
            ("Restauration testée", 10, "Preuve de restauration récente"),
            ("Supervision et astreinte", 10, "Alertes actionnables, escalade connue"),
            ("Compétences et réversibilité", 10, "Deux sachants ou transfert organisé"),
            ("Gestion des changements", 10, "Versions, retour arrière, traçabilité")));
        g.Criteria.Add(C("C3", "Sécurité & conformité", "RSSI / DPO",
            "Données classifiées ou personnelles sensibles sans classification, sans contrôle d'accès ni mesure compensatoire.",
            ("Qualification DICT", 15, "D, I, C et T documentées"),
            ("Classification des données", 15, "Niveau et règles de manipulation définis"),
            ("RGPD", 15, "Données, registre, durées et droits identifiés"),
            ("Identités et habilitations", 15, "Moindre privilège, revues, comptes maîtrisés"),
            ("Vulnérabilités et correctifs", 15, "Scans, suivi, délais de correction"),
            ("Chiffrement et secrets", 10, "En transit, au repos si requis"),
            ("Journalisation sécurité", 10, "Événements utiles, conservation, exploitation"),
            ("Exigences fournisseurs", 5, "Localisation, notification, réversibilité")));
        g.Criteria.Add(C("C4", "Patrimoine & urbanisation", "Architecte d'entreprise",
            "Actif à forte PI ou valorisation potentielle sans maîtrise des droits, du code source ou de la réversibilité.",
            ("Propriété intellectuelle de l’organisation", 20, "Code, algorithme, modèle, données, savoir-faire"),
            ("Caractère différenciant", 15, "Avantage métier difficile à reproduire"),
            ("Potentiel de valorisation", 15, "Réutilisation, licence ou commercialisation"),
            ("Maîtrise des droits", 10, "Contrats, licences, auteurs, exploitation"),
            ("Alignement à la cible", 15, "Technologies et patterns compatibles"),
            ("Interopérabilité", 10, "API, formats et couplage maîtrisés"),
            ("Réutilisation / modularité", 10, "Composants réutilisables"),
            ("Souveraineté / réversibilité", 5, "Export, reprise, dépendance acceptable")));
        return g;
    }
}

public sealed class Evidence
{
    public string Code { get; set; } = "";
    public string SubCriterion { get; set; } = "";
    /// <summary>« 0 », « 0.5 », « 1 », « N/A » ou « NC ». NC ne vaut jamais zéro.</summary>
    public string Value { get; set; } = "NC";
    public string? Reference { get; set; }
    public string? Date { get; set; }
    public string? VerifiedBy { get; set; }
}

public sealed class Assessment
{
    public string ObjectId { get; set; } = "";
    public int? Cm { get; set; }
    public string Need { get; set; } = "À confirmer"; // Oui | Non | À confirmer
    public List<Evidence> Evidences { get; set; } = new();
    public Dictionary<string, string> RedTriggers { get; set; } = new(); // code -> Oui | Non | NC
    public string HeritageStake { get; set; } = "À confirmer";
    public string? RetainedDecision { get; set; }
    public string? Justification { get; set; }
    public string? ValidationStatus { get; set; }
}

public sealed class ScoringResult
{
    public bool Retired { get; set; }
    public Dictionary<string, double?> Rates { get; set; } = new();
    public Dictionary<string, int?> Notes { get; set; } = new();
    public double? Mastery { get; set; }
    public string? Quadrant { get; set; }
    public double? Fragility { get; set; }
    public double? Urgency { get; set; }
    public string? Priority { get; set; }
    public string? ComputedDecision { get; set; }
    public string? SecurityAction { get; set; }
    public string? HeritageAction { get; set; }
    public List<string> Missing { get; set; } = new();
    public string Control { get; set; } = "";
}

public static class ScoringEngine
{
    /// <summary>Taux d'un critère : somme(preuve × poids) / somme(poids applicables). Null si une preuve manque.</summary>
    public static double? Rate(Criterion criterion, IEnumerable<Evidence> evidences)
    {
        var byLabel = evidences.Where(e => e.Code == criterion.Code)
            .GroupBy(e => e.SubCriterion).ToDictionary(g => g.Key, g => g.ToList());
        double score = 0, applicable = 0;
        foreach (var sub in criterion.SubCriteria)
        {
            if (!byLabel.TryGetValue(sub.Label, out var list) || list.Count != 1) return null; // absente ou en doublon
            var e = list[0];
            var v = (e.Value ?? "").Trim().Replace(',', '.');
            if (v.Equals("N/A", StringComparison.OrdinalIgnoreCase)) continue;
            if (!double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var n)) return null; // NC ou vide
            if (n != 0 && n != 0.5 && n != 1) return null;
            if (string.IsNullOrWhiteSpace(e.Reference)) return null; // une preuve sans référence n'est pas une preuve
            score += n * sub.Weight;
            applicable += sub.Weight;
        }
        return applicable == 0 ? null : score / applicable;
    }

    public static int? Note(double? rate, ScoringThresholds t) => rate switch
    {
        null => null,
        var r when r < t.Note2 => 1,
        var r when r < t.Note3 => 2,
        var r when r < t.Note4 => 3,
        _ => 4,
    };

    public static ScoringResult Evaluate(ScoringGrid grid, Assessment a, bool retired)
    {
        var res = new ScoringResult { Retired = retired };
        if (retired) { res.Control = "Sans objet"; return res; }

        foreach (var c in grid.Criteria)
        {
            var rate = Rate(c, a.Evidences);
            res.Rates[c.Code] = rate;
            res.Notes[c.Code] = Note(rate, grid.Thresholds);
        }
        return Decide(grid, a, res);
    }

    /// <summary>Applique quadrant, priorité, règle de garde et surcouches à partir de notes déjà établies.</summary>
    public static ScoringResult Decide(ScoringGrid grid, Assessment a, ScoringResult res)
    {
        var t = grid.Thresholds;
        var masteryNotes = grid.MasteryCriteria.Select(c => res.Notes.GetValueOrDefault(c)).ToList();
        if (masteryNotes.Count > 0 && masteryNotes.All(n => n.HasValue))
            res.Mastery = masteryNotes.Average(n => (double)n!.Value);

        if (res.Mastery.HasValue && a.Need is "Oui" or "Non")
        {
            res.Quadrant = a.Need == "Oui"
                ? (res.Mastery >= t.Mastery ? "Maintenir" : "Moderniser / Migrer")
                : (res.Mastery >= t.Mastery ? "Tolérer" : "Éliminer");
        }
        if (res.Mastery is double mastery)
        {
            res.Fragility = 4 - mastery;
            if (a.Cm is int cm)
            {
                res.Urgency = cm * res.Fragility;
                res.Priority = res.Urgency > t.PriorityHigh ? "Haute" : res.Urgency >= t.PriorityMedium ? "Moyenne" : "Basse";
            }
        }

        var reds = grid.Criteria.Select(c => a.RedTriggers.GetValueOrDefault(c.Code, "NC")).ToList();
        var anyRed = reds.Contains("Oui");
        var guard = a.Cm is int c2 && c2 >= t.GuardCm && a.Need == "Non";
        if (anyRed || guard) res.ComputedDecision = "À instruire";
        else if (res.Quadrant is not null && reds.All(r => r == "Non") && a.Cm.HasValue) res.ComputedDecision = res.Quadrant;

        var redSec = a.RedTriggers.GetValueOrDefault(grid.SecurityCriterion, "NC");
        var secNote = res.Notes.GetValueOrDefault(grid.SecurityCriterion);
        if (redSec == "Oui" || (secNote is int sn && sn <= t.LowSecurityNote)) res.SecurityAction = "Sécuriser";
        else if (secNote is null || redSec == "NC") res.SecurityAction = "À confirmer";

        var redHer = a.RedTriggers.GetValueOrDefault(grid.HeritageCriterion, "NC");
        if (redHer == "Oui" || a.HeritageStake == "Oui") res.HeritageAction = "Protéger / Valoriser";
        else if (a.HeritageStake == "À confirmer" || redHer == "NC") res.HeritageAction = "À confirmer";

        if (res.Notes.Values.Any(n => n is null)) res.Missing.Add("Preuves / références");
        if (a.Need is not ("Oui" or "Non")) res.Missing.Add("Besoin métier");
        if (reds.Contains("NC")) res.Missing.Add("Déclencheurs rouges");
        if (a.HeritageStake is not ("Oui" or "Non")) res.Missing.Add("Enjeu patrimonial");
        if (!a.Cm.HasValue) res.Missing.Add("CM à valider");

        var retained = a.RetainedDecision;
        var sameDecision = retained == res.ComputedDecision || (retained == "Migrer" && res.ComputedDecision == "Moderniser / Migrer");
        res.Control = res.ComputedDecision is null ? "Calcul incomplet"
            : string.IsNullOrWhiteSpace(retained) ? "Décision de comité manquante"
            : res.ComputedDecision == "À instruire" || !sameDecision
                ? (string.IsNullOrWhiteSpace(a.Justification) ? "Justification requise" : "Écart justifié, à revoir en comité")
                : "Conforme au calcul";
        return res;
    }

    /// <summary>Évaluation prête à saisir : toutes les preuves de la grille à NC, déclencheurs à NC.</summary>
    public static Assessment Blank(ScoringGrid grid, string objectId)
    {
        var a = new Assessment { ObjectId = objectId };
        Complete(grid, a);
        return a;
    }

    public static void Complete(ScoringGrid grid, Assessment a)
    {
        foreach (var c in grid.Criteria)
        {
            a.RedTriggers.TryAdd(c.Code, "NC");
            foreach (var s in c.SubCriteria)
                if (!a.Evidences.Any(e => e.Code == c.Code && e.SubCriterion == s.Label))
                    a.Evidences.Add(new Evidence { Code = c.Code, SubCriterion = s.Label });
        }
    }
}
