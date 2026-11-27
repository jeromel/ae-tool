using AeTool.Core;
using static AeTool.Tests.Harness;

namespace AeTool.Tests;

/// <summary>« Une donnée a un seul maître » : ce que l'outil refuse de modifier, et ce qu'il conserve malgré une omission.</summary>
public static class MasteryTests
{
    private static (Metamodel Metamodel, ArchObject Archi, ArchObject Typed) Rig()
    {
        var metamodel = Metamodel.Default();
        metamodel.PropertyDefinitions.Add(new PropertyDefinition { Key = "pos.niveau", Label = "Niveau", Source = "archi" });
        metamodel.PropertyDefinitions.Add(new PropertyDefinition { Key = "cmdb.site", Label = "Site (CMDB)", Source = "cmdb" });
        metamodel.PropertyDefinitions.Add(new PropertyDefinition { Key = "cout", Label = "Coût", Source = "saisie" });
        var archi = new ArchObject { Id = "a", Name = "Portail", Documentation = "Venu d'Archi", Source = "archi", TypeKey = "application" };
        archi.Set("pos.niveau", "zone");
        archi.Set("libre.archi", "non déclarée");
        archi.Set("cmdb.site", "Site A");
        archi.Set("cout", "12");
        var typed = new ArchObject { Id = "s", Name = "Saisi ici", Source = "saisie", TypeKey = "application" };
        typed.Set("cmdb.site", "Site B");
        typed.Set("libre", "non déclarée");
        return (metamodel, archi, typed);
    }

    private static List<PropertyValue> Props(params (string Key, string? Value)[] values) =>
        values.Select(v => new PropertyValue { Key = v.Key, Value = v.Value }).ToList();

    public static void Run()
    {
        Console.WriteLine("Un seul maître par donnée");

        Test("objet venu d'Archi : nom et description ne changent pas ici ; les renvoyer tels quels ou les omettre est permis", () =>
        {
            var (m, archi, _) = Rig();
            Equal(2, Mastery.Violations(m, archi, "Autre nom", "Autre description", null).Count, "nom et description modifiés");
            Equal(0, Mastery.Violations(m, archi, " Portail ", "Venu d'Archi", null).Count, "valeurs identiques, aux espaces près");
            Equal(0, Mastery.Violations(m, archi, null, null, null).Count, "champs absents");
        });

        Test("propriété venue d'Archi, déclarée ou non : sa valeur ne change pas ici", () =>
        {
            var (m, archi, _) = Rig();
            Equal("archi", Mastery.MasterOfProperty(m, archi, "pos.niveau"), "déclarée, source Archi");
            Equal("archi", Mastery.MasterOfProperty(m, archi, "libre.archi"), "non déclarée, sur un objet venu d'Archi");
            var problems = Mastery.Violations(m, archi, null, null, Props(("pos.niveau", "quartier"), ("libre.archi", "autre")));
            Equal(2, problems.Count, "deux valeurs modifiées");
            True(problems[0].Contains("Niveau") && problems[0].Contains("Archi"), "le message nomme la propriété et son maître : " + problems[0]);
            Equal(0, Mastery.Violations(m, archi, null, null, Props(("pos.niveau", " zone "))).Count, "renvoyée telle quelle");
        });

        Test("propriété d'une source externe : même règle, y compris sur un objet saisi dans l'outil", () =>
        {
            var (m, _, typed) = Rig();
            Equal("cmdb", Mastery.MasterOfProperty(m, typed, "cmdb.site"), "maître : la source");
            var problems = Mastery.Violations(m, typed, null, null, Props(("cmdb.site", "Site C")));
            True(problems.Count == 1 && problems[0].Contains("cmdb"), "refus qui nomme la source");
        });

        // Décision 014 : le maître se juge objet par objet. Archi n'a aucune valeur sur un objet créé ici.
        Test("propriété déclarée par Archi sur un objet créé ici : elle se saisit dans l'outil", () =>
        {
            var (m, archi, typed) = Rig();
            Equal(null, Mastery.MasterOfProperty(m, typed, "pos.niveau"), "objet saisi : l'outil fait foi");
            Equal(0, Mastery.Violations(m, typed, null, null, Props(("pos.niveau", "zone"))).Count, "ajout accepté");
            True(Mastery.Keep(m, typed, Props(("pos.niveau", "zone")))!.Any(p => p.Key == "pos.niveau" && p.Value == "zone"), "valeur enregistrée");
            Equal("archi", Mastery.MasterOfProperty(m, archi, "pos.niveau"), "objet venu d'Archi : toujours Archi");
        });

        Test("ce qui se saisit dans l'outil reste libre : propriété saisie d'un objet Archi, tout un objet saisi", () =>
        {
            var (m, archi, typed) = Rig();
            Equal(null, Mastery.MasterOfProperty(m, archi, "cout"), "propriété saisie sur un objet venu d'Archi");
            Equal(null, Mastery.MasterOfProperty(m, typed, "libre"), "propriété non déclarée d'un objet saisi");
            Equal(0, Mastery.Violations(m, archi, null, null, Props(("cout", "15"))).Count, "modifier une propriété saisie");
            Equal(0, Mastery.Violations(m, typed, "Nouveau nom", "Nouvelle description", Props(("libre", "x"))).Count, "objet saisi");
        });

        Test("omettre une propriété maîtrisée ailleurs ne l'efface pas ; omettre une propriété saisie la retire", () =>
        {
            var (m, archi, _) = Rig();
            Equal(null, Mastery.Keep(m, archi, null), "demande qui ne touche pas aux propriétés");
            var kept = Mastery.Keep(m, archi, Props(("cout", "15")))!;
            Equal("cmdb.site,cout,libre.archi,pos.niveau", string.Join(",", kept.Select(p => p.Key).Order()), "propriétés conservées");
            Equal("15", kept.Single(p => p.Key == "cout").Value, "valeur saisie prise en compte");
            var emptied = Mastery.Keep(m, archi, Props())!;
            Equal("cmdb.site,libre.archi,pos.niveau", string.Join(",", emptied.Select(p => p.Key).Order()), "liste vide : seule la propriété saisie disparaît");
        });

        Test("deux propriétés de même nom venues d'Archi : l'une ou l'autre valeur est acceptée, et les deux sont conservées", () =>
        {
            var (m, archi, _) = Rig();
            archi.Properties.Add(new PropertyValue { Key = "vendor.name", Value = "Éditeur X" });
            archi.Properties.Add(new PropertyValue { Key = "vendor.name", Value = "Éditeur X SA" });
            Equal(0, Mastery.Violations(m, archi, null, null, Props(("vendor.name", "Éditeur X SA"))).Count, "la seconde valeur, que l'écran renvoie");
            Equal(0, Mastery.Violations(m, archi, null, null, Props(("vendor.name", "Éditeur X"))).Count, "la première valeur");
            Equal(1, Mastery.Violations(m, archi, null, null, Props(("vendor.name", "Autre"))).Count, "une autre valeur");
            Equal(1, Mastery.Violations(m, archi, null, null, Props(("vendor.name", ""))).Count, "vider la valeur");
            var kept = Mastery.Keep(m, archi, Props(("vendor.name", "Éditeur X SA"), ("cout", "15")))!;
            Equal(2, kept.Count(p => p.Key == "vendor.name"), "les deux propriétés restent");
            Equal("Éditeur X|Éditeur X SA", string.Join("|", kept.Where(p => p.Key == "vendor.name").Select(p => p.Value)), "dans leur ordre et avec leurs valeurs");
        });
    }
}
