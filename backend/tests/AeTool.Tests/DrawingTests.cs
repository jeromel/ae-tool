using System.Text.Json;
using AeTool.Connectors;
using AeTool.Core;
using static AeTool.Tests.Harness;

namespace AeTool.Tests;

public static class DrawingTests
{
    public static void Run()
    {
        Console.WriteLine("Rendu soigné des vues");
        Test("chaque relation ArchiMate a un trait et une tête par défaut connus", () =>
        {
            foreach (var type in Metamodel.Default().RelationshipTypes)
            {
                var (line, head) = DrawingDefaults.Relation(type.ArchimateType);
                True(new[] { "plein", "tirets", "pointillés" }.Contains(line), $"trait inconnu pour {type.Key}");
                True(DrawingDefaults.Heads.Contains(head), $"tête inconnue pour {type.Key}");
            }
            Equal(("plein", "fleche"), DrawingDefaults.Relation("Flow"), "flux");
            Equal(("tirets", "triangle"), DrawingDefaults.Relation("Realization"), "réalisation");
            Equal(("plein", "aucune"), DrawingDefaults.Relation(null), "relation hors ArchiMate");
        });
        Test("chaque type d'objet a un gabarit et un pictogramme par défaut", () =>
        {
            foreach (var type in Metamodel.Default().ObjectTypes)
            {
                True(DrawingDefaults.Templates.Contains(DrawingDefaults.Template(type.Key)), $"gabarit inconnu pour {type.Key}");
                True(DrawingDefaults.Glyph(type.Key).Length > 0, $"pictogramme vide pour {type.Key}");
            }
            Equal("carte", DrawingDefaults.Template("ApplicationComponent"), "application");
            Equal("rond", DrawingDefaults.Template("BusinessActor"), "acteur");
            Equal("zone", DrawingDefaults.Template("Grouping"), "regroupement");
            Equal("service", DrawingDefaults.Glyph("TechnologyService"), "service technique");
        });
        Test("le gabarit propre à un élément l'emporte sur celui de son type", () =>
        {
            var merged = Metamodel.Merge(new Visual { Template = "carte", Icon = "application" }, new Visual { Template = "rond" });
            Equal("rond", merged.Template, "gabarit");
            Equal("application", merged.Icon, "pictogramme hérité du type");
        });
    }
}
