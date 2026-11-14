using AeTool.Core;
using static AeTool.Tests.Harness;

namespace AeTool.Tests;

public static class DrawingTests
{
    public static void Run()
    {
        Console.WriteLine("Rendu soigné des vues");
        Test("chaque type de relation du métamodèle a un trait et une tête connus", () =>
        {
            foreach (var type in Metamodel.Default().RelationshipTypes)
            {
                True(DrawingDefaults.Lines.Contains(type.SoftLine ?? ""), $"trait inconnu pour {type.Key}");
                True(DrawingDefaults.Heads.Contains(type.Head ?? ""), $"tête inconnue pour {type.Key}");
            }
            var flow = Metamodel.Default().RelType("flux")!;
            Equal(("plein", "fleche"), (flow.SoftLine!, flow.Head!), "flux");
            var realization = Metamodel.Default().RelType("realisation")!;
            Equal(("tirets", "triangle"), (realization.SoftLine!, realization.Head!), "réalisation");
        });
        Test("chaque type d'objet a un gabarit et un pictogramme explicites", () =>
        {
            var m = Metamodel.Default();
            foreach (var type in m.ObjectTypes)
            {
                True(DrawingDefaults.Templates.Contains(type.Visual.Template ?? ""), $"gabarit inconnu pour {type.Key}");
                True(!string.IsNullOrEmpty(type.Visual.Icon), $"pictogramme vide pour {type.Key}");
            }
            Equal("carte", m.Type("application")!.Visual.Template, "application");
            Equal("rond", m.Type("acteur")!.Visual.Template, "acteur");
            Equal("zone", m.Type("zone")!.Visual.Template, "regroupement");
            Equal("service", m.Type("service-technique")!.Visual.Icon, "service technique");
            Equal("defaut", m.Type("autre")!.Visual.Icon, "type non classé");
        });
        Test("le gabarit propre à un élément l'emporte sur celui de son type", () =>
        {
            var merged = Metamodel.Merge(new Visual { Template = "carte", Icon = "application" }, new Visual { Template = "rond" });
            Equal("rond", merged.Template, "gabarit");
            Equal("application", merged.Icon, "pictogramme hérité du type");
        });
    }
}
