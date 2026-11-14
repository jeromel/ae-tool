using AeTool.Core;
using static AeTool.Tests.Harness;

namespace AeTool.Tests;

public static class ReconciliationTests
{
    private const string Key = "cmdb";

    /// <summary>Référentiel du jeu d'essai, profil et clés de types lus dans le référentiel (jamais écrits en dur).</summary>
    private sealed record Setup(Repository Repo, SourceProfile Profile, string NodeType, string AppType);

    private static Setup Make(bool master = false)
    {
        var repo = Imported(out _);
        var node = repo.Objects["id-srv"].TypeKey;
        var app = repo.Objects["id-itsm"].TypeKey;
        var profile = new SourceProfile
        {
            Key = Key, Label = "CMDB d'essai", Kind = SourceProfile.KindFile, SourceIsMaster = master,
            Mapping = new SourceMapping
            {
                IdField = "id", NameField = "nom", TypeField = "categorie",
                Types = { new TypeMapping { SourceValue = "Serveur", TypeKey = node }, new TypeMapping { SourceValue = "Application", TypeKey = app } },
                Fields = { new FieldMapping { SourceField = "serie", PropertyKey = "cmdb:serie", Label = "Numéro de série" } },
            },
        };
        return new Setup(repo, profile, node, app);
    }

    private static SourceRecord Rec(string id, string name, string? category = "Serveur", string? serial = null) =>
        new(id, name, category, new Dictionary<string, string?> { ["serie"] = serial });

    private static ReconciliationLine Line(ReconciliationReport report, string id) => report.Lines.Single(l => l.ExternalId == id);

    private static ReconciliationReport Compare(Setup s, params SourceRecord[] records) => Reconciler.Compare(s.Repo, s.Profile, records);

    private static ApplyResult Apply(Setup s, ReconciliationReport report, SourceRecord[] records, ApplyRequest request) =>
        Reconciler.Apply(s.Repo, s.Profile, report, records, request, "testeur");

    public static void Run()
    {
        Console.WriteLine("Rapprochement avec une source externe");

        Test("clé de comparaison : accents, casse, ponctuation et espaces ignorés", () =>
        {
            Equal(Text.Key("Système  Étendu"), Text.Key("systeme-etendu"), "accents et ponctuation");
            Equal(Text.Key("SRV_APP 01"), Text.Key("srv app  01"), "séparateurs");
            True(Text.Key("---") != Text.Key("***"), "libellés de ponctuation seule distincts");
        });

        Test("recherche sans accents : « Sécurité » se trouve en tapant « securite »", () =>
        {
            Equal("securite d'acces", Text.Plain("Sécurité d'accès"), "minuscules sans accents");
            Equal("oeuvre", Text.Plain("Œuvre"), "ligature");
            Equal("reseau-d-acces", Text.Slug("Réseau d'accès"), "clé technique");
            Equal("depot", AeTool.Connectors.UrbaImporter.Normalize("  Dépôt "), "reprise du classeur");
        });

        Test("lecture des lignes : identifiant ou nom absent, identifiant en double, colonne absente", () =>
        {
            var s = Make();
            var rows = new List<Dictionary<string, string?>>
            {
                new() { ["id"] = "1", ["nom"] = "A", ["categorie"] = "Serveur", ["serie"] = "S1" },
                new() { ["id"] = "1", ["nom"] = "A bis" },
                new() { ["id"] = null, ["nom"] = "Sans id" },
                new() { ["id"] = "3", ["nom"] = "  " },
            };
            var set = SourceRecords.From(rows, s.Profile.Mapping);
            Equal(1, set.Records.Count, "enregistrements retenus");
            Equal("S1", set.Records[0].Fields["serie"], "propriété lue");
            var codes = set.Issues.Select(i => i.Code).OrderBy(c => c).ToList();
            Equal("identifiant-absent,identifiant-duplique,nom-absent", string.Join(",", codes), "anomalies");
        });

        Test("une colonne de l'identifiant absente de la source est une erreur, une propriété absente un avertissement", () =>
        {
            var s = Make();
            var rows = new List<Dictionary<string, string?>> { new() { ["id"] = "1", ["nom"] = "A" } };
            var issues = SourceRecords.From(rows, s.Profile.Mapping).Issues;
            True(issues.Any(i => i.Code == "colonne-absente" && i.Ref == "categorie" && i.Severity == "avertissement"), "nature absente");
            True(issues.Any(i => i.Code == "colonne-absente" && i.Ref == "serie"), "propriété absente");
            Equal(0, issues.Count(i => i.Severity == "erreur"), "aucune erreur");
        });

        Test("statut lié : l'identifiant externe est déjà porté par un objet, écart de nom signalé", () =>
        {
            var s = Make();
            s.Repo.Objects["id-srv"].ExternalRefs[Key] = "42";
            var line = Line(Compare(s, Rec("42", "SRV-APP-01 renommé")), "42");
            Equal(LineStatus.Linked, line.Status, "statut");
            Equal("id-srv", line.ObjectId, "objet");
            Equal("name", line.Differences.Single().Field, "écart de nom");
        });

        Test("statut proposé : même nom, accents et casse ignorés ; un seul objet candidat", () =>
        {
            var s = Make();
            s.Repo.Objects["id-sys"] = new ArchObject { Id = "id-sys", TypeKey = s.NodeType, Name = "Système Étendu" };
            var report = Compare(s, Rec("1", "SYSTEME ETENDU"), Rec("2", "srv app 01"));
            Equal(LineStatus.Proposed, Line(report, "1").Status, "accents et casse");
            Equal("id-sys", Line(report, "1").ObjectId, "objet proposé");
            Equal("id-srv", Line(report, "2").ObjectId, "ponctuation");
        });

        Test("statut ambigu : plusieurs objets portent le nom", () =>
        {
            var s = Make();
            s.Repo.Objects["id-dup"] = new ArchObject { Id = "id-dup", TypeKey = s.NodeType, Name = "srv-app-01" };
            var line = Line(Compare(s, Rec("1", "SRV-APP-01")), "1");
            Equal(LineStatus.Ambiguous, line.Status, "statut");
            Equal(2, line.Candidates.Count, "candidats");
        });

        Test("statut ambigu : deux lignes de la source visent le même objet", () =>
        {
            var s = Make();
            var report = Compare(s, Rec("1", "SRV-APP-01"), Rec("2", "srv app 01"));
            Equal(2, report.Counts[LineStatus.Ambiguous], "lignes ambiguës");
            Equal(0, report.Counts[LineStatus.Proposed], "aucune proposition");
        });

        Test("statut nouveau : aucun objet correspondant, type résolu par la valeur de nature", () =>
        {
            var s = Make();
            var line = Line(Compare(s, Rec("9", "NOUVEAU-LOGICIEL", "Application")), "9");
            Equal(LineStatus.New, line.Status, "statut");
            Equal(s.AppType, line.TypeKey, "type résolu");
        });

        Test("statut invalide : nature sans type ; avec un type par défaut la ligne devient nouvelle", () =>
        {
            var s = Make();
            var line = Line(Compare(s, Rec("9", "IMPRIMANTE-1", "Imprimante")), "9");
            Equal(LineStatus.Invalid, line.Status, "sans défaut");
            s.Profile.Mapping.DefaultTypeKey = s.NodeType;
            Equal(LineStatus.New, Line(Compare(s, Rec("9", "IMPRIMANTE-1", "Imprimante")), "9").Status, "avec défaut");
            Equal(LineStatus.New, Line(Compare(s, Rec("9", "IMPRIMANTE-1", null)), "9").Status, "nature vide");
        });

        Test("statut invalide : un type du profil absent du métamodèle est refusé", () =>
        {
            var s = Make();
            s.Profile.Mapping.Types[0].TypeKey = "TypeQuiNExistePas";
            Equal(LineStatus.Invalid, Line(Compare(s, Rec("9", "ZZZ")), "9").Status, "statut");
        });

        Test("statut ignoré : identifiant listé dans le profil", () =>
        {
            var s = Make();
            s.Profile.IgnoredIds.Add("5");
            Equal(LineStatus.Ignored, Line(Compare(s, Rec("5", "SRV-APP-01")), "5").Status, "statut");
        });

        Test("recherche par nom : type résolu d'abord, puis autres types visés avec écart de type", () =>
        {
            var s = Make();
            var line = Line(Compare(s, Rec("1", "ITSM-SUITE", "Serveur")), "1");
            Equal(LineStatus.Proposed, line.Status, "statut");
            var diff = line.Differences.Single(d => d.Field == "type");
            Equal(s.AppType, diff.RepositoryValue, "type du référentiel");
            Equal(s.NodeType, diff.SourceValue, "type de la source");
            True(Line(Compare(s, Rec("1", "ITSM-SUITE", "Application")), "1").Differences.All(d => d.Field != "type"), "même type : pas d'écart de type");
        });

        Test("écarts de propriétés : valeur du référentiel et valeur de la source, vide et absent équivalents", () =>
        {
            var s = Make();
            s.Repo.Objects["id-srv"].ExternalRefs[Key] = "42";
            s.Repo.Objects["id-srv"].Set("cmdb:serie", "AAA");
            var diff = Line(Compare(s, Rec("42", "SRV-APP-01", serial: "BBB")), "42").Differences.Single();
            Equal("cmdb:serie", diff.Field, "propriété");
            Equal("AAA", diff.RepositoryValue, "référentiel");
            Equal("BBB", diff.SourceValue, "source");
            s.Repo.Objects["id-srv"].Set("cmdb:serie", " BBB ");
            Equal(0, Line(Compare(s, Rec("42", "SRV-APP-01", serial: "BBB")), "42").Differences.Count, "espaces ignorés");
            s.Repo.Objects["id-srv"].Properties.Clear();
            Equal(0, Line(Compare(s, Rec("42", "SRV-APP-01", serial: "")), "42").Differences.Count, "vide = absent");
        });

        Test("objets disparus de la source et objets sans lien", () =>
        {
            var s = Make();
            s.Repo.Objects["id-srv"].ExternalRefs[Key] = "gone";
            var report = Compare(s, Rec("1", "ITSM-SUITE", "Application"));
            Equal("id-srv", report.Missing.Single().Id, "objet disparu");
            Equal("gone", report.Missing.Single().ExternalId, "identifiant");
            True(report.Unlinked.Any(o => o.Id == "id-itsm"), "application sans lien");
            True(report.Unlinked.All(o => o.Id != "id-srv"), "l'objet lié n'est pas dans les objets sans lien");
            True(report.Unlinked.All(o => o.Id != "id-cap"), "types non visés exclus");
            Equal(report.Unlinked.Count, report.UnlinkedCount, "effectif");
        });

        Test("objets sans lien : effectif complet, échantillon limité à 200", () =>
        {
            var s = Make();
            for (var i = 0; i < 250; i++) s.Repo.Objects[$"id-x{i}"] = new ArchObject { Id = $"id-x{i}", TypeKey = s.NodeType, Name = $"X{i:000}" };
            var report = Compare(s);
            True(report.UnlinkedCount >= 251, "effectif");
            Equal(200, report.Unlinked.Count, "échantillon");
        });

        Test("application : lier l'objet proposé, ou un objet choisi pour une ligne ambiguë", () =>
        {
            var s = Make();
            s.Repo.Objects["id-dup"] = new ArchObject { Id = "id-dup", TypeKey = s.NodeType, Name = "srv-app-01" };
            var recs = new[] { Rec("1", "ITSM-SUITE", "Application"), Rec("2", "SRV-APP-01") };
            var report = Compare(s, recs);
            var result = Apply(s, report, recs, new ApplyRequest { Decisions = { new() { ExternalId = "1", Action = "lier" }, new() { ExternalId = "2", Action = "lier", ObjectId = "id-dup" } } });
            Equal(2, result.Linked, "liés");
            Equal("1", s.Repo.Objects["id-itsm"].ExternalRefs[Key], "lien posé sur l'objet proposé");
            Equal("2", s.Repo.Objects["id-dup"].ExternalRefs[Key], "lien posé sur l'objet choisi");
        });

        Test("application : créer un objet proposé, typé, avec ses propriétés et son lien", () =>
        {
            var s = Make();
            var recs = new[] { Rec("N/9", "NOUVEAU", "Application", "SER9") };
            var result = Apply(s, Compare(s, recs), recs, new ApplyRequest { Decisions = { new() { ExternalId = "N/9", Action = "creer" } } });
            Equal(1, result.Created, "créés");
            var o = s.Repo.Objects["id-cmdb-n-9"];
            Equal(s.AppType, o.TypeKey, "type");
            Equal(Key, o.Source, "source");
            Equal("Proposé", o.ValidationStatus, "statut de validation");
            Equal("SER9", o.Get("cmdb:serie"), "propriété reprise");
            Equal("N/9", o.ExternalRefs[Key], "lien");
            True(s.Repo.Metamodel.Property("cmdb:serie") is { Source: Key, Label: "Numéro de série" }, "définition de propriété créée");
        });

        Test("application : une entrée de journal par objet touché, avec l'avant/après ; un objet créé entre à l'état proposé", () =>
        {
            var s = Make();
            var recs = new[] { Rec("N/9", "NOUVEAU", "Application", "SER9"), Rec("1", "ITSM-SUITE", "Application", "SER1") };
            Apply(s, Compare(s, recs), recs, new ApplyRequest
            {
                Decisions = { new() { ExternalId = "N/9", Action = "creer" }, new() { ExternalId = "1", Action = "lier" } }, SyncLinked = true,
            });
            Equal("Proposé", s.Repo.Objects["id-cmdb-n-9"].ValidationStatus, "état d'entrée");
            var created = s.Repo.Journal.Single(e => e.TargetId == "id-cmdb-n-9");
            Equal(("objet", "Objet créé par rapprochement"), (created.TargetKind!, created.Action), "création journalisée");
            Equal("SER9", created.Changes!.Single(c => c.Field == "Numéro de série").After, "propriété reprise, libellé lisible");
            Equal("Proposé", created.Changes!.Single(c => c.Field == "État de validation").After, "état d'entrée");
            var linked = s.Repo.Journal.Single(e => e.TargetId == "id-itsm");
            Equal("Objet modifié par rapprochement", linked.Action, "objet lié et mis à jour");
            Equal(new FieldChange("Numéro de série", null, "SER1"), linked.Changes!.Single(c => c.Field == "Numéro de série"), "avant/après de la propriété");
            True(linked.Changes!.Any(c => c.Field == "Lien avec la source « cmdb »"), "lien posé");
            True(s.Repo.Journal.Any(e => e.TargetKind == "import" && e.Action.StartsWith("Rapprochement")), "résumé conservé");
            var count = s.Repo.Journal.Count;
            Apply(s, Compare(s, recs), recs, new ApplyRequest { SyncLinked = true });
            Equal(count, s.Repo.Journal.Count, "un rejeu sans écart ne journalise rien");
        });

        Test("application : créer une ligne invalide est refusé", () =>
        {
            var s = Make();
            var recs = new[] { Rec("1", "IMPRIMANTE", "Imprimante") };
            var result = Apply(s, Compare(s, recs), recs, new ApplyRequest { Decisions = { new() { ExternalId = "1", Action = "creer" } } });
            Equal(0, result.Created, "créés");
            Equal(1, result.Messages.Count, "refus expliqué");
        });

        Test("application : ignorer puis délier", () =>
        {
            var s = Make();
            s.Repo.Objects["id-srv"].ExternalRefs[Key] = "42";
            var recs = new[] { Rec("42", "SRV-APP-01"), Rec("7", "AUTRE") };
            var report = Compare(s, recs);
            var result = Apply(s, report, recs, new ApplyRequest { Decisions = { new() { ExternalId = "7", Action = "ignorer" }, new() { ExternalId = "42", Action = "delier" } } });
            Equal(1, result.Ignored, "ignorés");
            Equal(1, result.Unlinked, "déliés");
            True(s.Profile.IgnoredIds.Contains("7"), "ligne ignorée mémorisée dans le profil");
            True(s.Repo.Objects["id-srv"].ExternalRefs.Count == 0, "lien supprimé, objet conservé");
            Equal(LineStatus.Ignored, Line(Compare(s, recs), "7").Status, "ligne ignorée au rapprochement suivant");
            Apply(s, report, recs, new ApplyRequest { Decisions = { new() { ExternalId = "7", Action = "delier" } } });
            True(s.Profile.IgnoredIds.Count == 0, "délier une ligne ignorée la remet en jeu");
        });

        Test("application : ignorer une ligne déjà liée est refusé", () =>
        {
            var s = Make();
            s.Repo.Objects["id-srv"].ExternalRefs[Key] = "42";
            var recs = new[] { Rec("42", "SRV-APP-01") };
            var result = Apply(s, Compare(s, recs), recs, new ApplyRequest { Decisions = { new() { ExternalId = "42", Action = "ignorer" } } });
            Equal(0, result.Ignored, "ignorés");
            Equal(1, result.Messages.Count, "refus expliqué");
        });

        Test("application : un objet déjà lié à un autre identifiant ne peut pas être relié", () =>
        {
            var s = Make();
            s.Repo.Objects["id-srv"].ExternalRefs[Key] = "42";
            var recs = new[] { Rec("43", "AUTRE") };
            var result = Apply(s, Compare(s, recs), recs, new ApplyRequest { Decisions = { new() { ExternalId = "43", Action = "lier", ObjectId = "id-srv" } } });
            Equal(0, result.Linked, "liés");
            Equal("42", s.Repo.Objects["id-srv"].ExternalRefs[Key], "lien d'origine intact");
        });

        Test("application : tout accepter lie les propositions restantes seulement", () =>
        {
            var s = Make();
            var recs = new[] { Rec("1", "ITSM-SUITE", "Application"), Rec("2", "SRV-APP-01"), Rec("3", "INCONNU") };
            var result = Apply(s, Compare(s, recs), recs, new ApplyRequest { AcceptProposals = true });
            Equal(2, result.Linked, "liés");
            Equal(0, result.Created, "rien n'est créé sans décision");
            Equal("2", s.Repo.Objects["id-srv"].ExternalRefs[Key], "lien");
        });

        Test("synchronisation : les propriétés reprises sont recopiées, une valeur vide efface", () =>
        {
            var s = Make();
            s.Repo.Objects["id-srv"].ExternalRefs[Key] = "42";
            s.Repo.Objects["id-itsm"].ExternalRefs[Key] = "43";
            s.Repo.Objects["id-itsm"].Set("cmdb:serie", "ANCIEN");
            var recs = new[] { Rec("42", "SRV-APP-01", serial: "NEUF"), Rec("43", "ITSM-SUITE", "Application", serial: null) };
            var result = Apply(s, Compare(s, recs), recs, new ApplyRequest());
            Equal(2, result.Updated, "mis à jour");
            Equal("NEUF", s.Repo.Objects["id-srv"].Get("cmdb:serie"), "valeur recopiée");
            Equal(null, s.Repo.Objects["id-itsm"].Get("cmdb:serie"), "valeur effacée");
            var off = Apply(s, Compare(s, recs), recs, new ApplyRequest { SyncLinked = false });
            Equal(0, off.Updated, "sans synchronisation");
        });

        Test("source maître : le nom de l'objet lié est aligné ; sinon il est conservé", () =>
        {
            foreach (var master in new[] { true, false })
            {
                var s = Make(master);
                s.Repo.Objects["id-srv"].ExternalRefs[Key] = "42";
                var recs = new[] { Rec("42", "SRV-NOUVEAU-NOM") };
                Apply(s, Compare(s, recs), recs, new ApplyRequest());
                Equal(master ? "SRV-NOUVEAU-NOM" : "SRV-APP-01", s.Repo.Objects["id-srv"].Name, $"nom (source maître : {master})");
            }
        });

        Test("rien n'est supprimé : un objet disparu de la source reste, seulement signalé", () =>
        {
            var s = Make();
            s.Repo.Objects["id-srv"].ExternalRefs[Key] = "gone";
            var before = s.Repo.Objects.Count;
            var recs = new[] { Rec("1", "NOUVEAU") };
            Apply(s, Compare(s, recs), recs, new ApplyRequest { AcceptProposals = true });
            Equal(before, s.Repo.Objects.Count, "objets");
            True(!s.Repo.Objects["id-srv"].Archived, "non archivé");
            Equal(1, Compare(s, recs).Missing.Count, "toujours signalé");
        });

        Test("rejeu : aucun doublon, aucun écart, aucune modification", () =>
        {
            var s = Make();
            var recs = new[]
            {
                Rec("1", "SRV-APP-01", serial: "S1"), Rec("2", "ITSM-SUITE", "Application", "S2"),
                Rec("3", "NOUVEAU-SERVEUR", serial: "S3"), Rec("4", "A-IGNORER"),
            };
            var request = new ApplyRequest { AcceptProposals = true, Decisions = { new() { ExternalId = "3", Action = "creer" }, new() { ExternalId = "4", Action = "ignorer" } } };
            Apply(s, Compare(s, recs), recs, request);
            var state = Canonical(s.Repo);
            var count = s.Repo.Objects.Count;

            var replay = Compare(s, recs);
            Equal(3, replay.Counts[LineStatus.Linked], "lignes liées");
            Equal(1, replay.Counts[LineStatus.Ignored], "ligne ignorée");
            Equal(0, replay.Lines.Sum(l => l.Differences.Count), "écarts");
            var result = Apply(s, replay, recs, request);
            Equal(false, result.Changed, "bilan du rejeu");
            Equal(count, s.Repo.Objects.Count, "pas de doublon");
            Equal(state, Canonical(s.Repo), "état inchangé");
        });

        Test("journal : un résumé chiffré par application, rien si rien ne change", () =>
        {
            var s = Make();
            var recs = new[] { Rec("1", "SRV-APP-01", serial: "S1") };
            Apply(s, Compare(s, recs), recs, new ApplyRequest { AcceptProposals = true });
            var entry = s.Repo.Journal.Single(j => j.Action == "Rapprochement avec une source externe");
            Equal("testeur", entry.User, "auteur");
            True(entry.Detail!.Contains("1 lié(s)") && entry.Detail.Contains("1 mis à jour") && entry.Detail.Contains(Key), "résumé chiffré : " + entry.Detail);
            var before = s.Repo.Journal.Count;
            Apply(s, Compare(s, recs), recs, new ApplyRequest { AcceptProposals = true });
            Equal(before, s.Repo.Journal.Count, "rejeu sans écriture au journal");
        });

        Test("identifiant externe assaini pour l'identifiant d'objet", () =>
        {
            Equal("a-b-c", Reconciler.Sanitize("A/B  C"), "séparateurs");
            Equal("x", Reconciler.Sanitize("///"), "vide");
            Equal(80, Reconciler.Sanitize(new string('a', 200)).Length, "longueur");
        });
    }
}
