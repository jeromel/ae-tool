using System.Text.Json;
using AeTool.Connectors;
using AeTool.Core;
using static AeTool.Tests.Harness;

namespace AeTool.Tests;

public static class RealFileTests
{
    public static void Run(string[] args)
    {
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
            Test("fichier réel : aucun concept sans règle de transcodification", () =>
            {
                Equal(0, first.Anomalies.Count(a => a.Code == "concept-non-transcode"), "anomalies concept-non-transcode");
                Equal(0, repo.Objects.Values.Count(o => o.TypeKey == "autre"), "objets classés dans le type non classé");
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
    }
}
