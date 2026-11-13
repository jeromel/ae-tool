using System.Text.Json;
using AeTool.Connectors;
using AeTool.Core;
using AeTool.Tests;

// Tests automatisés, sans framework tiers (aucune dépendance NuGet) : `dotnet run --project tests/AeTool.Tests`.
// Code de sortie 0 si tout passe, 1 sinon. Option : `--fichier chemin.xml` rejoue les contrôles d'import sur un vrai export Archi.

var failures = new List<string>();
var count = 0;

void Test(string name, Action body)
{
    count++;
    try { body(); Console.WriteLine($"  ok    {name}"); }
    catch (Exception e) { failures.Add(name); Console.WriteLine($"  ÉCHEC {name}\n        {e.Message}"); }
}
void Equal<T>(T expected, T actual, string what = "")
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"{what} attendu « {expected} », obtenu « {actual} »");
}
void True(bool condition, string what) { if (!condition) throw new Exception(what); }

Repository Imported(out ImportReport report, string? xml = null)
{
    var repo = new Repository();
    report = ArchimateExchange.Import(Fixtures.Stream(xml ?? Fixtures.Model()), repo);
    return repo;
}
// État comparable d'un référentiel, hors horodatages et journal.
string Canonical(Repository r) => JsonSerializer.Serialize(new
{
    Objects = r.Objects.Values.OrderBy(o => o.Id).Select(o => new { o.Id, o.TypeKey, o.Name, o.Documentation, o.Properties, o.Archived }),
    Relationships = r.Relationships.Values.OrderBy(x => x.Id),
    Diagrams = r.Diagrams.Values.OrderBy(d => d.Id),
    r.Folders,
});

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

Console.WriteLine("Import Archi (format d'échange ArchiMate)");

Test("importe objets, relations, propriétés, dossiers et vues", () =>
{
    var repo = Imported(out var report);
    Equal(8, repo.Objects.Count, "objets"); Equal(6, repo.Relationships.Count, "relations"); Equal(3, repo.Diagrams.Count, "vues");
    Equal(0, report.Anomalies.Count, "anomalies");
    Equal("2", repo.Objects["id-cap"].Get("pos.order"));
    Equal("Suite ITSM cible.", repo.Objects["id-itsm"].Documentation);
    True(repo.Metamodel.Property("lifecycle.state") is not null, "la définition de propriété doit être créée");
});
Test("un objet présent dans plusieurs vues n'est créé qu'une fois", () =>
{
    var repo = Imported(out _);
    Equal(2, repo.Diagrams.Values.Count(d => d.AllNodes().Any(n => n.ElementId == "id-itsm")));
    Equal(1, repo.Objects.Values.Count(o => o.Name == "ITSM-SUITE"));
});
Test("positions, imbrication, styles, points de passage et libellés sont conservés", () =>
{
    var repo = Imported(out _);
    var pos = repo.Diagrams["id-v-pos"];
    Equal(1, pos.Nodes[0].Children.Count); Equal(400, pos.Nodes[0].W); Equal("#f5deaa", pos.Nodes[0].Style!.Fill!.Hex);
    Equal("Label", pos.Nodes[1].Kind); Equal("Légende", pos.Nodes[1].Label);
    var c = repo.Diagrams["id-v-cible"].Connections[0];
    Equal("id-r3", c.RelationshipId); Equal(200, c.Bendpoints[0].X); Equal("2", c.Style!.LineWidth);
    Equal("Application Cooperation", repo.Diagrams["id-v-cible"].Viewpoint);
});
Test("une relation vers une autre relation est valide et ne casse pas l'analyse", () =>
{
    var repo = Imported(out var report);
    Equal("id-r3", repo.Relationships["id-r5"].TargetId);
    True(report.Anomalies.All(a => a.Code != "reference-cassee"), "aucune référence cassée attendue");
    Equal(0, new Analyzer(repo).Outgoing("id-ticket").Count());
});
Test("rejouer le même import ne crée aucun doublon et ne modifie rien", () =>
{
    var repo = Imported(out _);
    var before = Canonical(repo);
    var second = ArchimateExchange.Import(Fixtures.Stream(Fixtures.Model()), repo);
    Equal(before, Canonical(repo));
    Equal(8, second.Counts["Objets"].Unchanged); Equal(0, second.Counts["Objets"].Added + second.Counts["Objets"].Updated);
    Equal(6, second.Counts["Relations"].Unchanged); Equal(3, second.Counts["Vues"].Unchanged);
});
Test("réimport : modification détectée, objet disparu archivé (suppression logique)", () =>
{
    var repo = Imported(out _);
    var report = ArchimateExchange.Import(Fixtures.Stream(Fixtures.Model(appName: "ITSM-SUITE v2", withLegacy: false)), repo);
    Equal("ITSM-SUITE v2", repo.Objects["id-itsm"].Name);
    Equal(1, report.Counts["Objets"].Updated); Equal(1, report.Counts["Objets"].Archived);
    True(repo.Objects["id-legacy"].Archived, "l'objet absent du fichier doit être archivé, pas supprimé");
    True(repo.Relationships["id-r6"].Archived, "sa relation doit être archivée");
});
Test("réimport : les données saisies dans l'outil sont conservées", () =>
{
    var repo = Imported(out _);
    repo.Metamodel.EnsureProperty("urba:ID", source: "urba");
    repo.Objects["id-itsm"].Set("urba:ID", "APP-001");
    repo.Objects["id-itsm"].VisualOverride = new Visual { Fill = "#ff0000" };
    ArchimateExchange.Import(Fixtures.Stream(Fixtures.Model(appName: "ITSM bis")), repo);
    Equal("APP-001", repo.Objects["id-itsm"].Get("urba:ID")); Equal("#ff0000", repo.Objects["id-itsm"].VisualOverride!.Fill);
});
Test("références cassées, types inconnus et doublons sont signalés, jamais corrigés en silence", () =>
{
    var extra = """
        <relationship identifier="id-bad" source="id-itsm" target="id-fantome" xsi:type="Serving" />
        <relationship identifier="id-r1" source="id-itsm" target="id-cap" xsi:type="Serving" />
        """;
    var xml = Fixtures.Model(extra: extra).Replace("xsi:type=\"Node\"", "xsi:type=\"TypeMaison\"")
        .Replace("elementRef=\"id-ticket\"", "elementRef=\"id-absent\"");
    var repo = Imported(out var report, xml);
    True(report.Anomalies.Any(a => a.Code == "reference-cassee" && a.Ref == "id-bad"), "référence cassée non signalée");
    True(report.Anomalies.Any(a => a.Code == "identifiant-duplique" && a.Ref == "id-r1"), "doublon non signalé");
    True(report.Anomalies.Any(a => a.Code == "type-inconnu"), "type inconnu non signalé");
    True(report.Anomalies.Any(a => a.Code == "occurrence-orpheline"), "occurrence orpheline non signalée");
    Equal("id-fantome", repo.Relationships["id-bad"].TargetId, "la relation cassée est conservée telle quelle :");
    Equal("Realization", repo.Relationships["id-r1"].TypeKey, "la première occurrence fait foi :");
});
Test("relation non conforme au métamodèle : signalée", () =>
{
    var repo = new Repository();
    repo.Metamodel.RelType("Realization")!.AllowedPairs.Add("ApplicationComponent>ApplicationService");
    var report = ArchimateExchange.Import(Fixtures.Stream(Fixtures.Model()), repo);
    Equal(2, report.Anomalies.Count(a => a.Code == "relation-non-conforme"));
});
Test("fichier illisible : erreur explicite, référentiel intact", () =>
{
    var repo = new Repository();
    var report = ArchimateExchange.Import(Fixtures.Stream("<model><pas-ferme>"), repo);
    Equal("xml-invalide", report.Anomalies[0].Code); Equal(0, repo.Objects.Count);
});

Console.WriteLine("Export et réversibilité");

Test("export puis réimport dans un référentiel vierge : état identique", () =>
{
    var repo = Imported(out _);
    var xml = ArchimateExchange.Export(repo).ToString();
    var copy = new Repository();
    var report = ArchimateExchange.Import(Fixtures.Stream(xml), copy);
    Equal(0, report.Anomalies.Count, "anomalies au réimport");
    Equal(Canonical(repo), Canonical(copy));
});
Test("export d'une seule vue : uniquement ses objets et relations", () =>
{
    var repo = Imported(out _);
    var copy = new Repository();
    ArchimateExchange.Import(Fixtures.Stream(ArchimateExchange.Export(repo, new[] { "id-v-bi" }).ToString()), copy);
    Equal(2, copy.Objects.Count); Equal(1, copy.Diagrams.Count);
});
Test("type hors ArchiMate : exporté avec le type de repli", () =>
{
    var repo = Imported(out _);
    repo.Metamodel.ObjectTypes.Add(new ObjectType { Key = "ZoneOT", Label = "Zone OT", ArchimateType = null });
    repo.Objects["z"] = new ArchObject { Id = "z", TypeKey = "ZoneOT", Name = "Zone 3" };
    True(ArchimateExchange.Export(repo).ToString().Contains("identifier=\"z\" xsi:type=\"Grouping\""), "type de repli attendu");
});
Test("impact qualifié : exporté en association nommée", () =>
{
    var repo = Imported(out _);
    repo.Impacts.First(i => i.ProjectId == "id-prj1" && i.ObjectId == "id-itsm").Nature = "Création";
    True(ArchimateExchange.Export(repo).ToString().Contains("Impact : Création"), "association d'impact attendue");
});
Test("le référentiel complet se sérialise et se relit en JSON", () =>
{
    var repo = Imported(out _);
    var copy = JsonSerializer.Deserialize<Repository>(JsonSerializer.Serialize(repo))!;
    Equal(Canonical(repo), Canonical(copy)); Equal(repo.Metamodel.ObjectTypes.Count, copy.Metamodel.ObjectTypes.Count);
    Equal(4, copy.Metamodel.Scoring.Criteria.Count); Equal(2, copy.Metamodel.Scoring.MasteryCriteria.Count);
});

Console.WriteLine("Projets, analyses et métamodèle");

Test("impacts déduits des vues du dossier projet, fond de carte exclu", () =>
{
    var repo = Imported(out _);
    var an = new Analyzer(repo);
    var impacted = an.ImpactsOf("id-prj1").Select(i => i.ObjectId).OrderBy(x => x).ToList();
    Equal("id-itsm,id-report", string.Join(",", impacted)); // la capacité (fond de carte) est exclue
    True(repo.Impacts.All(i => i.Nature == ImpactNature.ToQualify && i.Origin == "vue"), "impacts déduits à qualifier");
});
Test("une nature qualifiée survit au réimport ; un impact non qualifié disparu est retiré", () =>
{
    var repo = Imported(out _);
    repo.Impacts.First(i => i.ProjectId == "id-prj1" && i.ObjectId == "id-report").Nature = "Évolution";
    var withoutNodes = Fixtures.Model().Replace("""<node identifier="id-n4" elementRef="id-itsm" xsi:type="Element" x="10" y="10" w="120" h="55" />""", "")
        .Replace("""<node identifier="id-n5" elementRef="id-report" xsi:type="Element" x="300" y="10" w="120" h="55" />""", "")
        .Replace("""source="id-n4" target="id-n5">""", """source="id-n6" target="id-n6">""");
    ArchimateExchange.Import(Fixtures.Stream(withoutNodes), repo);
    var left = repo.Impacts.Where(i => i.ProjectId == "id-prj1").ToList();
    Equal(1, left.Count); Equal("Évolution", left[0].Nature);
});
Test("adhérence : objet partagé entre deux projets", () =>
{
    var repo = Imported(out _);
    var adherences = new Analyzer(repo).Adherences();
    Equal(1, adherences.Count); Equal("id-report", adherences[0].SharedObjectIds.Single());
});
Test("adhérence : objets directement reliés", () =>
{
    var repo = Imported(out _);
    repo.Impacts.RemoveAll(i => i.ObjectId == "id-report" && i.ProjectId == "id-prj2");
    var a = new Analyzer(repo).Adherences().Single();
    Equal(0, a.SharedObjectIds.Count);
    True(a.LinkedPairs.Any(p => p.Contains("id-itsm") && p.Contains("id-ticket")), "ITSM et Ticket sont reliés par un accès");
});
Test("analyse d'impact amont et aval, profondeur réglable", () =>
{
    var an = new Analyzer(Imported(out _));
    Equal("id-itsm", an.Impact("id-srv", 1, "aval").Single().Id);
    Equal(4, an.Impact("id-srv", 2, "aval").Count);
    Equal("id-itsm,id-legacy", string.Join(",", an.Impact("id-cap", 1, "amont").Select(r => r.Id).OrderBy(x => x)));
    Equal(3, an.Impact("id-cap", 2, "tous").Count);
});
Test("graphe de voisinage", () =>
{
    var g = new Analyzer(Imported(out _)).Neighborhood("id-itsm", 1);
    Equal(5, g.Nodes.Count); Equal(4, g.Edges.Count);
});
Test("matrice capacité × application : relation (R) et imbrication dans une vue (V)", () =>
{
    var an = new Analyzer(Imported(out _));
    var m = an.CrossMatrix("Capability", "ApplicationComponent", includeNesting: true);
    Equal(1, m.Rows.Count); Equal(2, m.Columns.Count);
    Equal("RV", m.Cells[0][m.Columns.FindIndex(c => c.Id == "id-itsm")]);
    Equal("R", m.Cells[0][m.Columns.FindIndex(c => c.Id == "id-legacy")]);
});
Test("visuel : celui de l'élément prime sur celui du type, attribut par attribut", () =>
{
    var repo = Imported(out _);
    var o = repo.Objects["id-itsm"];
    Equal("#b5ffff", repo.Metamodel.EffectiveVisual(o).Fill);
    o.VisualOverride = new Visual { Fill = "#123456" };
    var v = repo.Metamodel.EffectiveVisual(o);
    Equal("#123456", v.Fill); Equal("#5c5c5c", v.Stroke); Equal("rectangle", v.Shape);
});
Test("qualité : complétude et orphelins par type", () =>
{
    var q = new Analyzer(Imported(out _)).Quality().First(t => t.TypeKey == "ApplicationComponent");
    Equal(3, q.Count); Equal(1, q.Documented); Equal(1, q.NotInDiagram); Equal(0, q.Orphans);
});

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
    Equal("SystemSoftware", repo.Objects["id-urba-app-002"].TypeKey);
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

// ---- Contrôle optionnel sur un vrai export Archi (non versionné) ----
var fileIndex = Array.IndexOf(args, "--fichier");
if (fileIndex >= 0 && fileIndex + 1 < args.Length)
{
    var path = args[fileIndex + 1];
    Console.WriteLine($"Contrôle sur fichier réel : {Path.GetFileName(path)}");
    var repo = new Repository();
    ImportReport first = new();
    Test("import du fichier réel", () =>
    {
        using var s = File.OpenRead(path);
        first = ArchimateExchange.Import(s, repo);
        foreach (var c in first.Counts) Console.WriteLine($"        {c.Key} : {c.Value.Added}");
        Console.WriteLine($"        anomalies : {first.Anomalies.Count}");
        foreach (var g in first.Anomalies.GroupBy(a => a.Code)) Console.WriteLine($"          {g.Key} × {g.Count()}");
    });
    Test("rejeu du fichier réel : zéro ajout, zéro modification", () =>
    {
        using var s = File.OpenRead(path);
        var second = ArchimateExchange.Import(s, repo);
        Equal(0, second.Counts.Values.Sum(c => c.Added + c.Updated + c.Archived), "écarts au rejeu");
    });
    Test("export puis réimport du fichier réel : état identique", () =>
    {
        var copy = new Repository();
        using var ms = new MemoryStream();
        ArchimateExchange.Export(repo).Save(ms);
        ms.Position = 0;
        var report = ArchimateExchange.Import(ms, copy);
        Equal(0, report.Anomalies.Count(a => a.Severity == "erreur"), "erreurs au réimport");
        Equal(Canonical(repo), Canonical(copy));
    });
}

Console.WriteLine($"\n{count - failures.Count}/{count} tests réussis");
return failures.Count == 0 ? 0 : 1;
