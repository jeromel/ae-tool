using System.Text.Json;
using AeTool.Connectors;
using AeTool.Core;

namespace AeTool.Tests;

/// <summary>
/// Outillage de test maison, sans framework tiers (aucune dépendance NuGet).
/// Chaque domaine a sa classe avec une méthode Run() ; elle s'ajoute dans Program.cs.
/// </summary>
public static class Harness
{
    private static readonly List<string> Failures = new();
    private static int _count;

    public static void Test(string name, Action body)
    {
        _count++;
        try { body(); Console.WriteLine($"  ok    {name}"); }
        catch (Exception e) { Failures.Add(name); Console.WriteLine($"  ÉCHEC {name}\n        {e.Message}"); }
    }

    public static void Equal<TValue>(TValue expected, TValue actual, string what = "")
    {
        if (!EqualityComparer<TValue>.Default.Equals(expected, actual)) throw new Exception($"{what} attendu « {expected} », obtenu « {actual} »");
    }

    public static void True(bool condition, string what) { if (!condition) throw new Exception(what); }

    /// <summary>Référentiel obtenu en important le modèle de test (ou le XML fourni).</summary>
    public static Repository Imported(out ImportReport report, string? xml = null)
    {
        var repo = new Repository();
        report = ArchimateExchange.Import(Fixtures.Stream(xml ?? Fixtures.Model()), repo);
        return repo;
    }

    /// <summary>État comparable d'un référentiel, hors horodatages et journal.</summary>
    public static string Canonical(Repository r) => JsonSerializer.Serialize(new
    {
        Objects = r.Objects.Values.OrderBy(o => o.Id).Select(o => new { o.Id, o.TypeKey, o.Name, o.Documentation, o.Properties, o.Archived }),
        Relationships = r.Relationships.Values.OrderBy(x => x.Id),
        Diagrams = r.Diagrams.Values.OrderBy(d => d.Id),
        r.Folders,
    });

    /// <summary>Affiche le bilan et renvoie le code de sortie : 0 si tout passe, 1 sinon.</summary>
    public static int Finish()
    {
        Console.WriteLine($"\n{_count - Failures.Count}/{_count} tests réussis");
        return Failures.Count == 0 ? 0 : 1;
    }
}
