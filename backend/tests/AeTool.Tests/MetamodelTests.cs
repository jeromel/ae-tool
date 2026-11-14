using AeTool.Core;
using static AeTool.Tests.Harness;

namespace AeTool.Tests;

public static class MetamodelTests
{
    private static readonly Dictionary<string, string[]> ExpectedObjectRoles = new()
    {
        [Roles.Application] = new[] { "application" },
        [Roles.Capability] = new[] { "capacite" },
        [Roles.Project] = new[] { "projet" },
        [Roles.Data] = new[] { "objet-metier", "objet-donnees" },
        [Roles.DataCarrier] = new[] { "artefact" },
        [Roles.Infrastructure] = new[]
        {
            "noeud", "equipement", "logiciel-systeme", "collaboration-technique", "interface-technique", "chemin", "reseau", "fonction-technique",
            "traitement-technique", "interaction-technique", "evenement-technique", "service-technique", "equipement-industriel", "installation",
            "reseau-distribution",
        },
        [Roles.Backdrop] = new[] { "capacite", "zone", "lieu", "jonction-et", "jonction-ou" },
    };

    public static void Run()
    {
        Console.WriteLine("Métamodèle propre et rôles");

        Test("métamodèle par défaut : chaque concept d'élément ArchiMate a exactement une règle sans condition", () =>
        {
            var t = Metamodel.Default().Transcoding;
            foreach (var concept in Archimate.Elements)
                Equal(1, t.Elements.Count(r => r.ArchimateType == concept && !r.Conditional), $"règles sans condition pour {concept}");
            foreach (var concept in Archimate.Relationships)
                Equal(1, t.Relations.Count(r => r.ArchimateType == concept && r.Constraints == 0), $"règles de relation pour {concept}");
        });
        Test("métamodèle par défaut : chaque règle vise un type existant et chaque type sauf « autre » a une règle", () =>
        {
            var m = Metamodel.Default();
            True(m.Transcoding.Elements.All(r => m.Type(r.TypeKey) is not null), "règle d'élément vers un type inconnu");
            True(m.Transcoding.Relations.All(r => m.RelType(r.TypeKey) is not null), "règle de relation vers un type inconnu");
            True(m.ObjectTypes.Where(t => t.Key != "autre").All(t => m.Transcoding.Elements.Any(r => r.TypeKey == t.Key)), "type d'objet sans règle");
            True(m.RelationshipTypes.All(t => m.Transcoding.Relations.Any(r => r.TypeKey == t.Key)), "type de relation sans règle");
            Equal(0, m.Transcoding.Elements.Count(r => r.TypeKey == "autre"), "règles du type « autre »");
            Equal(0, Transcoder.Errors(m, m.Transcoding).Count, "erreurs de structure de la matrice par défaut");
        });
        Test("métamodèle par défaut : les rôles sont ceux de la spécification", () =>
        {
            var m = Metamodel.Default();
            foreach (var (role, keys) in ExpectedObjectRoles)
                Equal(string.Join(",", keys.OrderBy(k => k)), string.Join(",", m.TypesWith(role).Select(t => t.Key).OrderBy(k => k)), $"types du rôle {role}");
            Equal("composition,agregation", string.Join(",", m.RelationshipTypes.Where(r => r.Roles.Contains(Roles.Decomposition)).Select(r => r.Key)), "décomposition");
            Equal("flux", string.Join(",", m.RelTypeKeysWith(Roles.Flow)), "flux");
            Equal(0, m.ObjectTypes.SelectMany(t => t.Roles).Count(r => !Roles.IsObjectRole(r)), "rôles inconnus");
        });
        Test("métamodèle par défaut : une clé propre par type, des libellés de lecture inverse, aucun nom ArchiMate comme clé", () =>
        {
            var m = Metamodel.Default();
            Equal(m.ObjectTypes.Count, m.ObjectTypes.Select(t => t.Key).Distinct().Count(), "clés de types distinctes");
            True(m.ObjectTypes.All(t => !Archimate.IsElement(t.Key)), "une clé de type est un concept ArchiMate");
            True(m.RelationshipTypes.All(t => !Archimate.IsRelationship(t.Key) && !string.IsNullOrEmpty(t.ReverseLabel)), "clé ou lecture inverse de relation");
            True(m.ObjectTypes.All(t => m.Layers.Any(l => l.Key == t.LayerKey)), "couche inconnue");
        });
        Test("IsA, TypesWith, FirstTypeWith et RelIsA lisent les rôles", () =>
        {
            var m = Metamodel.Default();
            True(m.IsA("application", Roles.Application), "application");
            True(!m.IsA("noeud", Roles.Application), "un nœud n'est pas une application");
            True(!m.IsA("inconnu", Roles.Application), "type inconnu");
            Equal("application", m.FirstTypeWith(Roles.Application)!.Key, "premier type d'application");
            True(m.RelIsA("flux", Roles.Flow) && !m.RelIsA("service", Roles.Flow), "rôle de relation");
            m.Type("application")!.Archived = true;
            m.ObjectTypes.Add(new ObjectType { Key = "appli-2", Label = "Appli 2", Roles = { Roles.Application } });
            Equal("appli-2", m.FirstTypeWith(Roles.Application)!.Key, "un type actif est préféré");
        });
        Test("clé dérivée d'un libellé : minuscules, sans accents, tirets, suffixe si elle existe", () =>
        {
            Equal("equipement-special", Metamodel.DeriveKey("Équipement spécial !", Array.Empty<string>()), "libellé accentué");
            Equal("type-2", Metamodel.DeriveKey("???", new[] { "type" }), "libellé sans lettre");
            Equal("zone-3", Metamodel.DeriveKey("Zone", new[] { "zone", "zone-2" }), "suffixe croissant");
        });
        Test("les fonctions suivent les rôles : un type ajouté avec le rôle application apparaît dans l'espace Applications", () =>
        {
            var repo = Imported(out _);
            repo.Metamodel.ObjectTypes.Add(new ObjectType { Key = "appli-maison", Label = "Appli maison", LayerKey = "applicative", Roles = { Roles.Application } });
            repo.Objects["m1"] = new ArchObject { Id = "m1", TypeKey = "appli-maison", Name = "MAISON" };
            Equal(4, new SpaceReader(repo, new Analyzer(repo)).Applications().Count, "applications avec le nouveau type");
            repo.Metamodel.Type("application")!.Roles.Clear();
            Equal(1, new SpaceReader(repo, new Analyzer(repo)).Applications().Count, "applications une fois le rôle retiré à l'ancien type");
        });
        Test("matrice : une clé de type et « role: » donnent le même résultat", () =>
        {
            var an = new Analyzer(Imported(out _));
            Equal(
                System.Text.Json.JsonSerializer.Serialize(an.CrossMatrix("capacite", "application", true)),
                System.Text.Json.JsonSerializer.Serialize(an.CrossMatrix("role:capacite", "role:application", true)), "matrice");
            Equal(1, an.CrossMatrix("role:application", "role:application", true, "role:flux").Rows.Count, "matrice des flux");
            Equal(1, an.CrossMatrix("application", "application", true, "flux").Rows.Count, "relation désignée par sa clé");
        });
        Test("projets, impacts et fond de carte suivent les rôles", () =>
        {
            var repo = Imported(out _);
            var an = new Analyzer(repo);
            Equal(2, an.Projects().Count(), "projets");
            repo.Metamodel.Type("capacite")!.Roles.Remove(Roles.Backdrop);
            an.RefreshDeducedImpacts();
            True(repo.Impacts.Any(i => i.ProjectId == "id-prj1" && i.ObjectId == "id-cap"), "la capacité n'est plus fond de carte : elle vaut impact");
            repo.Metamodel.Type("projet")!.Roles.Clear();
            Equal(0, new Analyzer(repo).Projects().Count(), "projets sans le rôle");
        });
    }
}
