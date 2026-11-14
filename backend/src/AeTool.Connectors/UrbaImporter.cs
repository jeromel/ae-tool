using System.Globalization;
using System.Text;
using AeTool.Core;

namespace AeTool.Connectors;

/// <summary>
/// Reprise du classeur d'urbanisation (une ligne par application, notation CM + critères sur preuves).
/// Les colonnes calculées du classeur ne sont pas reprises : l'outil les recalcule avec la même méthode.
/// </summary>
public static class UrbaImporter
{
    public const string Prefix = "urba:";
    public const string IdKey = Prefix + "ID";
    private const string SourceTag = "urba";
    private const string DefaultTechnicalType = "logiciel-systeme";

    // Noms de feuilles et de colonnes attendus. À adapter ici si le classeur évolue.
    private const string SheetReferentiel = "Référentiel consolidé";
    private const string SheetPreuves = "Preuves de notation";
    private const string SheetGrille = "Grille des preuves";
    private const string SheetMethode = "Méthode de notation";

    /// <summary>Colonnes descriptives reprises comme propriétés de l'objet.</summary>
    private static readonly string[] DescriptiveColumns =
    {
        "Couche SI", "Domaine métier", "Responsable métier", "Responsable applicatif", "Sachant principal", "Type de solution",
        "Hébergement", "Version actuelle", "Version maximale éditeur", "Écart de version", "Nombre de licences", "Licences à jour",
        "État actuel", "Orientation connue", "Candidat cible", "Horizon connu", "Dépendance personne clé",
        "Nombre d’utilisateurs / population", "Interfaces principales connues", "SDSI métier identifié",
        "Base de données / référentiel utilisé", "Commentaires / données à recueillir", "Nature de la transformation",
        "Source de l’information", "Date de vérification", "Vérifié par",
    };

    public static ImportReport Import(Stream xlsx, Repository repo, string user = "import")
    {
        var report = new ImportReport { Source = "Classeur d'urbanisation (Excel)" };
        Dictionary<string, List<string?[]>> book;
        try { book = XlsxReader.Read(xlsx); }
        catch (Exception e) { report.Error("xlsx-invalide", $"Classeur illisible : {e.Message}"); return report; }

        if (!book.TryGetValue(SheetReferentiel, out var sheet))
        {
            report.Error("feuille-absente", $"Feuille « {SheetReferentiel} » introuvable.");
            return report;
        }
        ImportGrid(book, repo, report);
        ImportThresholds(book, repo, report);
        var byUrbaId = ImportApplications(sheet, repo, report, user);
        ImportEvidences(book, repo, report, byUrbaId);
        foreach (var a in repo.Assessments.Values) ScoringEngine.Complete(repo.Metamodel.Scoring, a);
        repo.Log(user, "Import urbanisation", null,
            string.Join(" ; ", report.Counts.Select(c => $"{c.Key} +{c.Value.Added} ~{c.Value.Updated} ={c.Value.Unchanged}")));
        return report;
    }

    private static Dictionary<string, string> ImportApplications(List<string?[]> sheet, Repository repo, ImportReport report, string user)
    {
        var byUrbaId = new Dictionary<string, string>();
        var headerIndex = sheet.FindIndex(r => r.Length > 1 && r[0]?.Trim() == "ID");
        if (headerIndex < 0) { report.Error("entete-absente", $"Ligne d'en-tête (première cellule « ID ») introuvable dans « {SheetReferentiel} »."); return byUrbaId; }
        var header = sheet[headerIndex].Select(h => h?.Trim() ?? "").ToArray();
        int Col(string startsWith) => Array.FindIndex(header, h => h.StartsWith(startsWith, StringComparison.Ordinal));
        string? Cell(string?[] row, int col) => col >= 0 && col < row.Length && !string.IsNullOrWhiteSpace(row[col]) ? row[col]!.Trim() : null;

        var colName = Col("Application");
        foreach (var missing in DescriptiveColumns.Where(c => Col(c) < 0))
            report.Warn("colonne-absente", $"Colonne « {missing} » absente du classeur : non reprise.");
        foreach (var column in DescriptiveColumns) repo.Metamodel.EnsureProperty(Prefix + column, column, SourceTag);
        repo.Metamodel.EnsureProperty(IdKey, "Identifiant urbanisation", SourceTag);

        // Types choisis par rôle : une application créée prend le premier type d'application ; un composant technique, le type
        // « logiciel-systeme » du métamodèle par défaut s'il existe, sinon le premier type d'infrastructure.
        var appTypes = repo.Metamodel.TypeKeysWith(Roles.Application);
        var applicationType = repo.Metamodel.FirstTypeWith(Roles.Application)?.Key;
        var technicalType = (repo.Metamodel.Type(DefaultTechnicalType) ?? repo.Metamodel.FirstTypeWith(Roles.Infrastructure))?.Key ?? applicationType;
        if (applicationType is null)
        {
            report.Error("type-application-absent", "Aucun type du métamodèle ne porte le rôle « application » : reprise des applications impossible.");
            return byUrbaId;
        }
        var existingByUrbaId = repo.Objects.Values.Where(o => o.Get(IdKey) is not null)
            .GroupBy(o => o.Get(IdKey)!).ToDictionary(g => g.Key, g => g.First());
        var existingByName = repo.ActiveObjects.Where(o => appTypes.Contains(o.TypeKey))
            .GroupBy(o => Normalize(o.Name)).ToDictionary(g => g.Key, g => g.ToList());
        var tally = report.For("Applications");
        var assessments = report.For("Évaluations");
        var seen = new HashSet<string>();

        foreach (var row in sheet.Skip(headerIndex + 1))
        {
            var urbaId = Cell(row, 0);
            var name = Cell(row, colName);
            if (urbaId is null) continue;
            if (name is null) { report.Error("nom-absent", "Ligne sans nom d'application ; ignorée.", urbaId); continue; }
            if (!seen.Add(urbaId)) { report.Error("identifiant-duplique", "Identifiant présent sur plusieurs lignes ; seule la première est reprise.", urbaId); continue; }

            // Rapprochement : identifiant d'urbanisation déjà connu, sinon nom exact d'une application du référentiel.
            var added = false;
            if (!existingByUrbaId.TryGetValue(urbaId, out var obj))
            {
                if (existingByName.TryGetValue(Normalize(name), out var candidates))
                {
                    obj = candidates[0];
                    if (candidates.Count > 1)
                        report.Warn("rapprochement-ambigu", $"« {name} » correspond à {candidates.Count} objets du référentiel ; rattaché au premier.", urbaId);
                }
                else
                {
                    var layer = Cell(row, Col("Couche SI")) ?? "";
                    obj = new ArchObject
                    {
                        Id = "id-urba-" + urbaId.ToLowerInvariant(), Name = name, Source = SourceTag,
                        TypeKey = layer.StartsWith("Applicative", StringComparison.Ordinal) ? applicationType : technicalType!,
                    };
                    // L'application n'est pas dans le modèle Archi : un valideur doit confirmer sa création.
                    Validation.Enter(obj, Validation.Proposed, user, DateTime.UtcNow);
                    repo.Objects[obj.Id] = obj;
                    added = true;
                    report.Notes.Add($"{urbaId} « {name} » : absent du modèle Archi, créé dans le référentiel.");
                }
            }
            byUrbaId[urbaId] = obj.Id;

            var before = Snapshot(obj);
            obj.Set(IdKey, urbaId);
            obj.Archived = false;
            foreach (var column in DescriptiveColumns)
            {
                var col = Col(column);
                if (col >= 0) obj.Set(Prefix + column, Cell(row, col));
            }
            obj.Owner = Cell(row, Col("Responsable applicatif")) ?? obj.Owner;
            var changed = before != Snapshot(obj);
            if (changed) obj.UpdatedAt = DateTime.UtcNow;
            if (added) tally.Added++; else if (changed) tally.Updated++; else tally.Unchanged++;

            var isNew = !repo.Assessments.TryGetValue(obj.Id, out var assessment);
            assessment ??= new Assessment { ObjectId = obj.Id };
            var beforeAssessment = System.Text.Json.JsonSerializer.Serialize(assessment);
            var cm = Cell(row, Col("CM"));
            assessment.Cm = int.TryParse(cm, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;
            if (cm is not null && assessment.Cm is null) report.Warn("valeur-non-conforme", $"CM « {cm} » n'est pas un entier ; laissé vide.", urbaId);
            assessment.Need = Cell(row, Col("Besoin maintenu")) ?? "À confirmer";
            foreach (var criterion in repo.Metamodel.Scoring.Criteria)
            {
                var value = Cell(row, Col("Déclencheur rouge " + criterion.Code)) ?? "NC";
                if (value == "Sans objet") value = "NC"; // application retirée
                if (value is not ("Oui" or "Non" or "NC"))
                {
                    report.Warn("valeur-non-conforme", $"Déclencheur rouge {criterion.Code} « {value} » hors liste (Oui, Non, NC) ; repris comme NC.", urbaId);
                    value = "NC";
                }
                assessment.RedTriggers[criterion.Code] = value;
            }
            assessment.HeritageStake = Cell(row, Col("Enjeu patrimonial")) ?? "À confirmer";
            assessment.RetainedDecision = Cell(row, Col("Décision retenue"));
            assessment.Justification = Cell(row, Col("Preuves / justification nouvelle grille")) ?? Cell(row, Col("Justification de la notation"));
            assessment.ValidationStatus = Cell(row, Col("Statut de validation"));
            repo.Assessments[obj.Id] = assessment;
            if (isNew) assessments.Added++;
            else if (beforeAssessment != System.Text.Json.JsonSerializer.Serialize(assessment)) assessments.Updated++;
            else assessments.Unchanged++;
        }
        return byUrbaId;
    }

    private static void ImportEvidences(Dictionary<string, List<string?[]>> book, Repository repo, ImportReport report, Dictionary<string, string> byUrbaId)
    {
        if (!book.TryGetValue(SheetPreuves, out var sheet)) { report.Warn("feuille-absente", $"Feuille « {SheetPreuves} » absente : preuves non reprises."); return; }
        var headerIndex = sheet.FindIndex(r => r.Length > 5 && r[0]?.Trim() == "ID");
        if (headerIndex < 0) { report.Warn("entete-absente", $"En-tête introuvable dans « {SheetPreuves} »."); return; }
        var tally = report.For("Preuves");
        var unresolved = 0;
        var touched = new HashSet<string>();
        string? Cell(string?[] row, int col) => col < row.Length && !string.IsNullOrWhiteSpace(row[col]) ? row[col]!.Trim() : null;

        foreach (var row in sheet.Skip(headerIndex + 1))
        {
            var urbaId = Cell(row, 0);
            var code = Cell(row, 2);
            var sub = Cell(row, 3);
            if (urbaId is null || code is null || sub is null) { if (row.Any(c => !string.IsNullOrWhiteSpace(c))) unresolved++; continue; }
            if (!byUrbaId.TryGetValue(urbaId, out var objectId) || !repo.Assessments.TryGetValue(objectId, out var assessment)) { unresolved++; continue; }
            if (!touched.Add($"{objectId}|{code}|{sub}"))
            {
                report.Error("preuve-en-double", $"Sous-critère « {sub} » présent deux fois pour {urbaId} ; seule la première ligne est reprise.", urbaId);
                continue;
            }
            var incoming = new Evidence
            {
                Code = code, SubCriterion = sub, Value = Cell(row, 5) ?? "NC", Reference = Cell(row, 6),
                Date = Cell(row, 7), VerifiedBy = Cell(row, 8),
            };
            var existing = assessment.Evidences.FirstOrDefault(e => e.Code == code && e.SubCriterion == sub);
            if (existing is null) { assessment.Evidences.Add(incoming); tally.Added++; }
            // Une preuve déjà saisie dans l'outil n'est pas écrasée par un « NC » du classeur.
            else if (incoming.Value == "NC" || (existing.Value == incoming.Value && existing.Reference == incoming.Reference)) tally.Unchanged++;
            else { assessment.Evidences[assessment.Evidences.IndexOf(existing)] = incoming; tally.Updated++; }
        }
        if (unresolved > 0)
            report.Warn("preuves-non-rattachees", $"{unresolved} ligne(s) de preuve sans identifiant d'application lisible : " +
                "si la colonne ID est une formule, ouvrir puis enregistrer le classeur dans Excel pour mettre les valeurs en cache.");
    }

    private static void ImportGrid(Dictionary<string, List<string?[]>> book, Repository repo, ImportReport report)
    {
        if (!book.TryGetValue(SheetGrille, out var sheet)) return;
        var headerIndex = sheet.FindIndex(r => r.Length > 3 && r[0]?.Trim() == "Code");
        if (headerIndex < 0) return;
        var criteria = new List<Criterion>();
        foreach (var row in sheet.Skip(headerIndex + 1))
        {
            if (row.Length < 4 || string.IsNullOrWhiteSpace(row[0]) || string.IsNullOrWhiteSpace(row[2])) continue;
            if (!double.TryParse(row[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var weight))
            {
                report.Warn("valeur-non-conforme", $"Poids illisible pour « {row[2]} » ; sous-critère ignoré.");
                continue;
            }
            var code = row[0]!.Trim();
            var criterion = criteria.FirstOrDefault(c => c.Code == code);
            if (criterion is null)
            {
                var known = repo.Metamodel.Scoring.Criteria.FirstOrDefault(c => c.Code == code);
                criteria.Add(criterion = new Criterion { Code = code, Label = row[1]?.Trim() ?? code, ScoredBy = known?.ScoredBy, RedTrigger = known?.RedTrigger });
            }
            criterion.SubCriteria.Add(new SubCriterion { Label = row[2]!.Trim(), Weight = weight, ExpectedEvidence = row.Length > 4 ? row[4]?.Trim() : null });
        }
        if (criteria.Count == 0) return;
        foreach (var c in criteria.Where(c => Math.Abs(c.SubCriteria.Sum(s => s.Weight) - 100) > 0.001))
            report.Warn("grille-poids", $"Les poids du critère {c.Code} totalisent {c.SubCriteria.Sum(s => s.Weight)} au lieu de 100.");
        repo.Metamodel.Scoring.Criteria = criteria;
        report.For("Sous-critères de la grille").Updated = criteria.Sum(c => c.SubCriteria.Count);
    }

    private static void ImportThresholds(Dictionary<string, List<string?[]>> book, Repository repo, ImportReport report)
    {
        if (!book.TryGetValue(SheetMethode, out var sheet)) return;
        var t = repo.Metamodel.Scoring.Thresholds;
        var read = 0;
        foreach (var row in sheet)
        {
            // Les seuils sont une paire libellé / valeur placée en fin de ligne.
            for (var i = 0; i + 1 < row.Length; i++)
            {
                var label = row[i]?.Trim();
                if (label is null || !double.TryParse(row[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) continue;
                var known = true;
                switch (label)
                {
                    case "Seuil note 2": t.Note2 = v; break;
                    case "Seuil note 3": t.Note3 = v; break;
                    case "Seuil note 4": t.Note4 = v; break;
                    case "Seuil maîtrise technique": t.Mastery = v; break;
                    case "Seuil priorité haute (strict)": t.PriorityHigh = v; break;
                    case "Seuil priorité moyenne": t.PriorityMedium = v; break;
                    case "CM règle de garde": t.GuardCm = (int)v; break;
                    case "Note basse C3 (hypothèse)": t.LowSecurityNote = (int)v; break;
                    default: known = false; break;
                }
                if (known) read++;
            }
        }
        report.For("Seuils de notation").Updated = read;
    }

    private static string Snapshot(ArchObject o) => System.Text.Json.JsonSerializer.Serialize(new { o.Properties, o.Owner, o.Archived });

    /// <summary>Libellé en minuscules et sans accents, pour comparer et rechercher.</summary>
    public static string Normalize(string s) => Text.Plain(s).Trim();
}
