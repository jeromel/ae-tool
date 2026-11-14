using System.Text;
using System.Text.Json.Nodes;
using AeTool.Connectors;
using AeTool.Core;
using static AeTool.Tests.Harness;

namespace AeTool.Tests;

/// <summary>Circuit de validation (EF-51) : règles d'état, états d'entrée selon la source, migration des statuts.</summary>
public static class ValidationTests
{
    private static readonly DateTime Now = new(2025, 4, 1, 8, 0, 0, DateTimeKind.Utc);

    private static ArchObject Object(string status = "Brouillon") => new() { Id = "o1", TypeKey = "application", Name = "Appli", ValidationStatus = status };

    private static void Refused(Transition t, string contains, string what)
    {
        True(!t.Ok, what + " : doit être refusé");
        True(t.Error!.Contains(contains), $"{what} : message « {t.Error} » sans « {contains} »");
    }

    public static void Run(string[] args)
    {
        Console.WriteLine("Circuit de validation");

        Test("proposer : brouillon → proposé, avec l'auteur et la date ; un ancien refus est effacé", () =>
        {
            var o = Object();
            o.ValidationComment = "Complétez la description.";
            True(Validation.Propose(o, "ann", Now).Ok, "transition");
            Equal(("Proposé", "ann", (DateTime?)Now, (string?)null), (o.ValidationStatus, o.ProposedBy, o.ProposedAt, o.ValidationComment), "effets");
        });

        Test("proposer : refusé si l'objet est déjà proposé, validé ou archivé", () =>
        {
            Refused(Validation.Propose(Object("Proposé"), "ann", Now), "déjà proposé", "proposé");
            Refused(Validation.Propose(Object("Validé"), "ann", Now), "déjà validé", "validé");
            var archived = Object();
            archived.Archived = true;
            Refused(Validation.Propose(archived, "ann", Now), "archivé", "archivé");
            Equal("Brouillon", archived.ValidationStatus, "état inchangé après refus");
        });

        Test("valider : proposé → validé, ou brouillon → validé directement ; le commentaire est effacé", () =>
        {
            foreach (var from in new[] { "Proposé", "Brouillon" })
            {
                var o = Object(from);
                o.ValidationComment = "ancien";
                True(Validation.Validate(o, "val", Now).Ok, "depuis " + from);
                Equal(("Validé", "val", (DateTime?)Now, (string?)null), (o.ValidationStatus, o.ValidatedBy, o.ValidatedAt, o.ValidationComment), "effets depuis " + from);
            }
        });

        Test("valider : refusé si déjà validé ou archivé", () =>
        {
            Refused(Validation.Validate(Object("Validé"), "val", Now), "déjà validé", "validé");
            var archived = Object("Proposé");
            archived.Archived = true;
            Refused(Validation.Validate(archived, "val", Now), "archivé", "archivé");
        });

        Test("refuser : proposé → brouillon avec commentaire obligatoire", () =>
        {
            var o = Object("Proposé");
            (o.ProposedBy, o.ProposedAt) = ("ann", Now);
            True(Validation.Reject(o, "  Description trop courte.  ", Now).Ok, "transition");
            Equal(("Brouillon", "Description trop courte.", (string?)null), (o.ValidationStatus, o.ValidationComment, o.ProposedBy), "effets : commentaire nettoyé, proposition effacée");
        });

        Test("refuser : sans commentaire (absent, vide, espaces) ou hors de l'état proposé, rien ne change", () =>
        {
            foreach (var comment in new string?[] { null, "", "   " })
            {
                var o = Object("Proposé");
                Refused(Validation.Reject(o, comment, Now), "obligatoire", $"commentaire « {comment} »");
                Equal("Proposé", o.ValidationStatus, "état inchangé");
            }
            Refused(Validation.Reject(Object("Brouillon"), "x", Now), "proposé", "brouillon");
            Refused(Validation.Reject(Object("Validé"), "x", Now), "proposé", "validé");
        });

        Test("modification de fond d'un objet validé : un non-valideur le renvoie à « proposé », un valideur non", () =>
        {
            var o = Object("Validé");
            (o.ValidatedBy, o.ValidatedAt) = ("val", Now);
            var before = ChangeSet.Capture(o);
            o.Name = "Autre nom";
            True(!Validation.AfterChange(o, before, byValidator: true, "val", Now), "valideur : pas de changement d'état");
            Equal("Validé", o.ValidationStatus, "reste validé");
            True(Validation.AfterChange(o, before, byValidator: false, "ann", Now), "contributeur : retour à proposé");
            Equal(("Proposé", "ann", (string?)null), (o.ValidationStatus, o.ProposedBy, o.ValidatedBy), "effets : validation retirée");
        });

        Test("le visuel n'est pas une modification de fond ; un brouillon ou un proposé modifié ne change pas d'état", () =>
        {
            var v = Object("Validé");
            var before = ChangeSet.Capture(v);
            v.VisualOverride = new Visual { Fill = "#abcdef" };
            True(!Validation.AfterChange(v, before, false, "ann", Now), "visuel");
            Equal("Validé", v.ValidationStatus, "toujours validé");
            foreach (var status in new[] { "Brouillon", "Proposé" })
            {
                var o = Object(status);
                var b = ChangeSet.Capture(o);
                o.Documentation = "texte";
                True(!Validation.AfterChange(o, b, false, "ann", Now), "modification en " + status);
                Equal(status, o.ValidationStatus, "état " + status);
            }
        });

        Test("état d'entrée : proposé garde l'auteur de la proposition, validé celui de la validation ; réglage inconnu = validé", () =>
        {
            var p = Object();
            Validation.Enter(p, Validation.Proposed, "import", Now);
            Equal(("Proposé", "import", (string?)null), (p.ValidationStatus, p.ProposedBy, p.ValidatedBy), "proposé");
            var v = Object();
            Validation.Enter(v, Validation.Validated, "import", Now);
            Equal(("Validé", "import"), (v.ValidationStatus, v.ValidatedBy), "validé");
            var m = Metamodel.Default();
            Equal("Validé", Validation.ArchiImportStatus(m), "défaut");
            m.Settings.ArchiImportStatus = "Brouillon";
            Equal("Brouillon", Validation.ArchiImportStatus(m), "réglage respecté");
            m.Settings.ArchiImportStatus = "n'importe quoi";
            Equal("Validé", Validation.ArchiImportStatus(m), "valeur inconnue");
        });

        Test("import Archi : les objets entrent validés, ou selon le réglage du métamodèle ; un réimport ne touche pas à l'état", () =>
        {
            var repo = Imported(out _);
            True(repo.Objects.Values.All(o => o.ValidationStatus == "Validé" && o.ValidatedBy == "import"), "tous validés par défaut");
            var o = repo.Objects["id-itsm"];
            o.ValidationStatus = "Proposé";
            ArchimateExchange.Import(Fixtures.Stream(Fixtures.Model(appName: "ITSM-RENOMMÉE")), repo);
            Equal("Proposé", o.ValidationStatus, "état conservé au réimport d'un objet modifié");
            var custom = new Repository();
            custom.Metamodel.Settings.ArchiImportStatus = "Brouillon";
            ArchimateExchange.Import(Fixtures.Stream(Fixtures.Model()), custom);
            True(custom.Objects.Values.All(x => x.ValidationStatus == "Brouillon"), "réglage Brouillon");
        });

        Test("migration v3 : objets Archi encore en brouillon → validé, urbanisation → proposé, le reste intact", () =>
        {
            const string Json = """
                {"SchemaVersion":2,"Objects":{
                  "a":{"Id":"a","TypeKey":"application","Name":"A","Source":"archi","ValidationStatus":"Brouillon"},
                  "b":{"Id":"b","TypeKey":"application","Name":"B","Source":"urba","ValidationStatus":"Brouillon"},
                  "c":{"Id":"c","TypeKey":"application","Name":"C","Source":"saisie","ValidationStatus":"Brouillon"},
                  "d":{"Id":"d","TypeKey":"application","Name":"D","Source":"archi","ValidationStatus":"Proposé"},
                  "e":{"Id":"e","TypeKey":"application","Name":"E","Source":"cmdb","ValidationStatus":"Proposé"},
                  "f":{"Id":"f","TypeKey":"application","Name":"F","Source":"archi"}},
                 "Relationships":{},"Diagrams":{},"Folders":[],"Impacts":[],"Assessments":{},"Journal":[]}
                """;
            var repo = RepositoryUpgrader.Read(new MemoryStream(Encoding.UTF8.GetBytes(Json)), out var upgraded, out var from);
            True(upgraded, "migration signalée");
            Equal(2, from, "version d'origine");
            Equal(Repository.CurrentSchemaVersion, repo.SchemaVersion, "version écrite");
            string S(string id) => repo.Objects[id].ValidationStatus;
            Equal("Validé,Proposé,Brouillon,Proposé,Proposé,Validé", string.Join(",", "abcdef".Select(c => S(c.ToString()))), "états");
            Equal(("migration", "migration"), (repo.Objects["a"].ValidatedBy!, repo.Objects["b"].ProposedBy!), "auteur de la migration");
            Equal(null, repo.Objects["c"].ValidatedBy, "objet saisi intact");
        });

        Test("migration v3 : un fichier déjà migré ne l'est pas deux fois, et un fichier v1 passe par les trois versions", () =>
        {
            var repo = Imported(out _);
            repo.Objects["id-itsm"].ValidationStatus = "Brouillon";
            var json = System.Text.Json.JsonSerializer.Serialize(repo);
            var again = RepositoryUpgrader.Read(new MemoryStream(Encoding.UTF8.GetBytes(json)), out var upgraded);
            True(!upgraded, "pas de seconde migration");
            Equal("Brouillon", again.Objects["id-itsm"].ValidationStatus, "état posé à la main non écrasé");
            var node = JsonNode.Parse(json)!.AsObject();
            node.Remove("SchemaVersion");
            var v1 = RepositoryUpgrader.Read(new MemoryStream(Encoding.UTF8.GetBytes(node.ToJsonString())), out _, out var fromVersion);
            Equal(1, fromVersion, "version d'origine");
            Equal(Repository.CurrentSchemaVersion, v1.SchemaVersion, "version courante");
        });

        Test("reprise du classeur d'urbanisation : une application créée entre à l'état proposé, avec l'auteur de la reprise", () =>
        {
            var repo = new Repository();
            var report = UrbaImporter.Import(Fixtures.Xlsx(
                ("Référentiel consolidé", new[]
                {
                    new string?[] { "ID", "Application / composant", "Couche SI", "CM proposé - Criticité métier (1-4)" },
                    new string?[] { "APP-001", "Nouvelle appli", "Applicative", "3" },
                })), repo, "alice");
            Equal(0, report.Anomalies.Count(a => a.Severity == "erreur"), "erreurs de reprise");
            var created = repo.Objects.Values.Single(o => o.Source == "urba");
            Equal(("Proposé", "alice"), (created.ValidationStatus, created.ProposedBy!), "état d'entrée");
        });

        var index = Array.IndexOf(args, "--v1");
        if (index >= 0 && index + 1 < args.Length) RealFile(args[index + 1]);
    }

    /// <summary>Migre le vrai fichier de l'utilisateur et vérifie que les états d'entrée sont ceux de la règle.</summary>
    private static void RealFile(string path)
    {
        Console.WriteLine($"Migration des statuts sur le fichier réel : {Path.GetFileName(path)}");
        Test("fichier réel : chaque objet Archi validé, chaque objet d'urbanisation proposé, effectifs conservés", () =>
        {
            var original = JsonNode.Parse(File.ReadAllText(path))!["Objects"]!.AsObject();
            using var s = File.OpenRead(path);
            var repo = RepositoryUpgrader.Read(s, out _);
            Equal(original.Count, repo.Objects.Count, "objets");
            var archi = original.Count(kv => kv.Value!["Source"]?.GetValue<string>() == "archi" && (kv.Value!["ValidationStatus"]?.GetValue<string>() ?? "Brouillon") == "Brouillon");
            var urba = original.Count(kv => kv.Value!["Source"]?.GetValue<string>() == "urba" && (kv.Value!["ValidationStatus"]?.GetValue<string>() ?? "Brouillon") == "Brouillon");
            Console.WriteLine($"        {archi} objet(s) Archi → Validé, {urba} objet(s) d'urbanisation → Proposé");
            Equal(archi, repo.Objects.Values.Count(o => o.Source == "archi" && o.ValidationStatus == "Validé" && o.ValidatedBy == "migration"), "validés");
            Equal(urba, repo.Objects.Values.Count(o => o.Source == "urba" && o.ValidationStatus == "Proposé"), "proposés");
            Equal(0, repo.Objects.Values.Count(o => o.Source == "archi" && o.ValidationStatus != "Validé"), "Archi non validés");
        });
    }
}
