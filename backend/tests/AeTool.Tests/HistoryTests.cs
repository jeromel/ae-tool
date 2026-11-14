using AeTool.Core;
using static AeTool.Tests.Harness;

namespace AeTool.Tests;

/// <summary>Historique avant/après (EF-52) : calcul pur des champs modifiés, libellés lisibles, journal sans plafond.</summary>
public static class HistoryTests
{
    private static readonly Metamodel M = Metamodel.Default();

    private static string AppType => M.TypesWith(Roles.Application).First().Key;
    private static string AppLabel => M.Type(AppType)!.Label;

    private static ArchObject App(string name = "Appli") => new() { Id = "o1", TypeKey = AppType, Name = name };

    private static Dictionary<string, FieldChange> ByField(List<FieldChange> changes) => changes.ToDictionary(c => c.Field);

    public static void Run()
    {
        Console.WriteLine("Historique avant/après");

        Test("création : chaque champ renseigné est listé, sans valeur avant, avec le libellé du type", () =>
        {
            var o = App("Portail");
            o.Documentation = "Portail des agents";
            o.Owner = "Dupont";
            o.Set("lifecycle.state", "Actif");
            var changes = ByField(ChangeSet.Between(M, null, ChangeSet.Capture(o)));
            Equal(new FieldChange("Nom", null, "Portail"), changes["Nom"], "nom");
            Equal(new FieldChange("Type", null, AppLabel), changes["Type"], "libellé du type et non sa clé");
            Equal("Portail des agents", changes["Description"].After, "description");
            Equal("Dupont", changes["Responsable"].After, "responsable");
            True(!changes.ContainsKey("Visuel propre") && !changes.ContainsKey("Archivage"), "champs vides non listés");
        });

        Test("modification : avant et après exacts pour nom, description, responsable, type", () =>
        {
            var o = App("Ancien nom");
            o.Documentation = "Avant";
            var before = ChangeSet.Capture(o);
            o.Name = "Nouveau nom";
            o.Documentation = "Après";
            o.Owner = "Martin";
            o.TypeKey = M.ObjectTypes.First(t => t.Key != AppType && !t.Archived).Key;
            var changes = ByField(ChangeSet.Between(M, before, ChangeSet.Capture(o)));
            Equal(new FieldChange("Nom", "Ancien nom", "Nouveau nom"), changes["Nom"], "nom");
            Equal(new FieldChange("Description", "Avant", "Après"), changes["Description"], "description");
            Equal(new FieldChange("Responsable", null, "Martin"), changes["Responsable"], "responsable");
            Equal(AppLabel, changes["Type"].Before, "type avant");
            Equal(M.Type(o.TypeKey)!.Label, changes["Type"].After, "type après");
        });

        Test("propriétés : ajoutée, modifiée, retirée ; le libellé de la définition remplace la clé", () =>
        {
            var m = Metamodel.Default();
            m.EnsureProperty("cycle.etat", "Cycle de vie");
            var o = App();
            o.Set("cycle.etat", "Actif");
            o.Set("autre", "x");
            var before = ChangeSet.Capture(o);
            o.Set("cycle.etat", "Retiré");
            o.Properties.RemoveAll(p => p.Key == "autre");
            o.Set("nouvelle", "1");
            var changes = ByField(ChangeSet.Between(m, before, ChangeSet.Capture(o)));
            Equal(new FieldChange("Cycle de vie", "Actif", "Retiré"), changes["Cycle de vie"], "modifiée, libellé lisible");
            Equal(new FieldChange("autre", "x", null), changes["autre"], "retirée (sans définition : la clé)");
            Equal(new FieldChange("nouvelle", null, "1"), changes["nouvelle"], "ajoutée");
            Equal(3, changes.Count, "rien d'autre");
        });

        Test("visuel propre : posé, modifié, retiré", () =>
        {
            var o = App();
            var none = ChangeSet.Capture(o);
            o.VisualOverride = new Visual { Fill = "#ff0000", Shape = "ellipse" };
            var withVisual = ChangeSet.Capture(o);
            var set = ChangeSet.Between(M, none, withVisual).Single();
            Equal(("Visuel propre", null, "forme ellipse, fond #ff0000"), (set.Field, set.Before, set.After), "posé");
            o.VisualOverride = null;
            Equal("forme ellipse, fond #ff0000", ChangeSet.Between(M, withVisual, ChangeSet.Capture(o)).Single().Before, "retiré");
        });

        Test("rien ne change : aucun champ, et vide équivaut à absent", () =>
        {
            var o = App();
            o.Set("p", null);
            var before = ChangeSet.Capture(o);
            o.Documentation = "  ";
            o.Owner = "";
            o.Set("p", "");
            Equal(0, ChangeSet.Between(M, before, ChangeSet.Capture(o)).Count, "champs");
            Equal(0, ChangeSet.Between(M, ChangeSet.Capture(o), ChangeSet.Capture(o)).Count, "copie identique");
        });

        Test("la copie est figée : modifier l'objet ensuite ne change pas l'état d'avant", () =>
        {
            var o = App("A");
            o.Set("p", "1");
            o.VisualOverride = new Visual { Fill = "#111111" };
            var before = ChangeSet.Capture(o);
            o.Name = "B";
            o.Set("p", "2");
            o.VisualOverride.Fill = "#222222";
            Equal("A", before.Name, "nom figé");
            Equal("1", before.Properties.Single().Value, "propriété figée");
            Equal("#111111", before.Visual!.Fill, "visuel figé");
        });

        Test("archivage : « Actif » puis « Archivé » ; lien avec une source externe lisible", () =>
        {
            var o = App();
            var before = ChangeSet.Capture(o);
            o.Archived = true;
            o.ExternalRefs["cmdb"] = "N/9";
            var changes = ByField(ChangeSet.Between(M, before, ChangeSet.Capture(o)));
            Equal(new FieldChange("Archivage", "Actif", "Archivé"), changes["Archivage"], "archivage");
            Equal("N/9", changes["Lien avec la source « cmdb »"].After, "lien");
        });

        Test("état de validation : une transition apparaît comme un champ", () =>
        {
            var o = App();
            var before = ChangeSet.Capture(o);
            o.ValidationStatus = Validation.Proposed;
            Equal(new FieldChange("État de validation", "Brouillon", "Proposé"), ChangeSet.Between(M, before, ChangeSet.Capture(o)).Single(), "transition");
        });

        Test("fond ou pas : le visuel, l'archivage et l'état ne sont pas une modification de fond", () =>
        {
            var o = App();
            var before = ChangeSet.Capture(o);
            o.VisualOverride = new Visual { Fill = "#123456" };
            o.Archived = true;
            o.ValidationStatus = Validation.Validated;
            True(!ChangeSet.IsSubstantive(before, ChangeSet.Capture(o)), "visuel, archivage, état");
            foreach (var change in new Action<ArchObject>[] { x => x.Name = "Z", x => x.Documentation = "d", x => x.Owner = "o", x => x.Set("k", "v"), x => x.TypeKey = "autre" })
            {
                var p = App();
                var b = ChangeSet.Capture(p);
                change(p);
                True(ChangeSet.IsSubstantive(b, ChangeSet.Capture(p)), "nom, description, responsable, propriété, type");
            }
        });

        Test("relation : création lisible (source, cible, type), modification, archivage", () =>
        {
            var repo = Imported(out _);
            var r = repo.Relationships["id-r1"];
            var created = ByField(ChangeSet.Between(repo.Metamodel, null, ChangeSet.Capture(repo, r)));
            Equal("ITSM-SUITE", created["Source"].After, "source en nom");
            Equal("Gérer les services numériques", created["Cible"].After, "cible en nom");
            Equal(repo.Metamodel.RelType(r.TypeKey)!.Label, created["Type"].After, "libellé du type de relation");
            var before = ChangeSet.Capture(repo, r);
            r.Name = "Réalise";
            r.Properties.Add(new PropertyValue { Key = "connect.protocol", Value = "HTTPS" });
            var changes = ByField(ChangeSet.Between(repo.Metamodel, before, ChangeSet.Capture(repo, r)));
            Equal(new FieldChange("Nom", null, "Réalise"), changes["Nom"], "nom");
            Equal("HTTPS", changes["connect.protocol"].After, "propriété");
            True(!changes.ContainsKey("Source"), "source seulement à la création");
            var archived = ChangeSet.Capture(repo, r);
            r.Archived = true;
            Equal(new FieldChange("Archivage", "Actif", "Archivé"), ChangeSet.Between(repo.Metamodel, archived, ChangeSet.Capture(repo, r)).Single(), "archivage");
        });

        Test("notation : criticité, besoin, décision, nombre de preuves modifiées ; rien si rien ne change", () =>
        {
            var before = new Assessment { ObjectId = "o1", Cm = 2, Need = "À confirmer", Evidences = { new Evidence { Code = "C1", SubCriterion = "Support", Value = "NC" }, new Evidence { Code = "C1", SubCriterion = "Socle", Value = "1" } } };
            var after = new Assessment { ObjectId = "o1", Cm = 3, Need = "Oui", RetainedDecision = "Maintenir",
                Evidences = { new Evidence { Code = "C1", SubCriterion = "Support", Value = "0.5" }, new Evidence { Code = "C1", SubCriterion = "Socle", Value = "1" } } };
            var changes = ByField(ChangeSet.Between(before, after));
            Equal(new FieldChange("Criticité métier", "2", "3"), changes["Criticité métier"], "criticité");
            Equal(new FieldChange("Besoin", "À confirmer", "Oui"), changes["Besoin"], "besoin");
            Equal("Maintenir", changes["Décision retenue"].After, "décision");
            Equal("1", changes["Preuves modifiées"].After, "une seule preuve a changé");
            Equal(0, ChangeSet.Between(after, after).Count, "identique");
            True(ChangeSet.Between(null, after).Count >= 3, "première notation");
        });

        Test("journal : la surcharge porte nature et champs, une liste vide donne null, et le journal n'est plus plafonné", () =>
        {
            var repo = new Repository();
            repo.Log("ann", "Objet modifié", JournalKinds.Object, "o1", new[] { new FieldChange("Nom", "a", "b") }, "détail");
            repo.Log("ann", "Objet modifié", JournalKinds.Object, "o1", Array.Empty<FieldChange>());
            var e = repo.Journal[0];
            Equal(("objet", "o1", "détail", 1), (e.TargetKind!, e.TargetId!, e.Detail!, e.Changes!.Count), "entrée avec champs");
            Equal(null, repo.Journal[1].Changes, "aucun champ : null");
            for (var i = 0; i < 6000; i++) repo.Log("ann", "Import Archi");
            Equal(6002, repo.Journal.Count, "plus de plafond de 5 000");
            Equal("import", repo.Journal[^1].TargetKind, "nature déduite d'un import");
            repo.Log("ann", "Métamodèle : type créé", "x");
            Equal("métamodèle", repo.Journal[^1].TargetKind, "nature déduite du métamodèle");
        });

        Test("recherche dans le journal : auteur, nature, texte (sans accents, dans les champs), période, plus récent d'abord", () =>
        {
            JournalEntry E(int day, string user, string action, string kind, string? detail = null, params FieldChange[] changes) =>
                new() { At = new DateTime(2025, 3, day, 10, 0, 0, DateTimeKind.Utc), User = user, Action = action, TargetKind = kind, Detail = detail, Changes = changes.Length == 0 ? null : changes.ToList() };
            var journal = new List<JournalEntry>
            {
                E(1, "ann", "Objet créé", "objet", "Portail"),
                E(2, "bob", "Objet modifié", "objet", "Portail", new FieldChange("Responsable", "Dupont", "Hélène")),
                E(3, "ann", "Import Archi", "import"),
                E(4, "Ann", "Notation modifiée", "notation", "Portail"),
            };
            List<string> Days(JournalFilter f) => JournalSearch.Apply(journal, f).Select(e => e.At.Day.ToString()).ToList();
            Equal("4,3,2,1", string.Join(",", Days(new JournalFilter(null, null, null, null, null))), "ordre");
            Equal("4,3,1", string.Join(",", Days(new JournalFilter("ann", null, null, null, null))), "auteur sans casse");
            Equal("2,1", string.Join(",", Days(new JournalFilter(null, "objet", null, null, null))), "nature");
            Equal("2", string.Join(",", Days(new JournalFilter(null, null, "helene", null, null))), "texte sans accents dans une valeur modifiée");
            Equal("3,2", string.Join(",", Days(new JournalFilter(null, null, null, new DateTime(2025, 3, 2), new DateTime(2025, 3, 3)))), "période, jour de fin inclus");
            Equal("", string.Join(",", Days(new JournalFilter("bob", "import", null, null, null))), "critères cumulés");
        });
    }
}
