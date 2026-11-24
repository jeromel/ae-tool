using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using static AeTool.Tests.Harness;

namespace AeTool.Tests;

/// <summary>
/// Le dépôt reste neutre : il ne cite aucune organisation réelle, ni ses sites, projets ou outils internes. Les termes
/// interdits ne figurent ici que par leur empreinte SHA-256 (mot en minuscules) ; les exemples utilisent l'entreprise
/// fictive Hydréane. Chaque fichier suivi par git est lu, y compris le contenu des classeurs et présentations.
/// </summary>
public static class NeutralityTests
{
    private static readonly HashSet<string> Forbidden = new(StringComparer.Ordinal)
    {
        "9d9aa5587aff66679185129358e5e79f04b690db67edee9e00b062186471b6c0",
        "da2774afe9bead4975cf6bea1cd864bd395808d64950ae2c7be820fa019e4cbb",
        "caa22ef5a129da7f1dd3c44e732ddc9cbe141c2f45428590ef8a2ddf12562d55",
        "c4289629b08bc4d61411aaa6d6d4a0c3c5f8c1e848e282976e29b6bed5aeedc7",
        "1ad25d0002690dc02e2708a297d8c9df1f160d376f663309cc261c7c921367e7",
        "b9fba912df2040294b40e0b74a45aae5aa709dce44d2d907d5c7c06b51bfb042",
        "efc6cea9d7cd03263824cbe7cd9291741832e85819f4a12739c1eafd22863e45",
        "66c8aec217cac5fa9829e4e314c2dfe6f3b3710cbf2c4b4df462c333565b4e27",
        "92876457e9332a91ce496e6783304fefed2f1662481c3911161b34f669106676",
        "896e1beb463d8b24a9db998fa895fe02e77697161da132c409fc5a6634fc4bd8",
        "358a1069cb41d32405e67a58b0876b14950f13528e578d9bce6087bd05d51128",
    };

    private static readonly Regex Words = new(@"\p{L}+", RegexOptions.Compiled);
    private static readonly Regex CamelParts = new(@"\p{Lu}?\p{Ll}+|\p{Lu}+(?!\p{Ll})", RegexOptions.Compiled);
    private static readonly string[] Skipped = { "package-lock.json" };
    private static readonly string[] Zipped = { ".xlsx", ".pptx", ".docx" };
    private static readonly string[] Binary = { ".png", ".jpg", ".jpeg", ".gif", ".ico", ".woff", ".woff2", ".ttf", ".pdf", ".bundle", ".gz", ".zip" };

    public static void Run()
    {
        Console.WriteLine("Neutralité du dépôt");
        var root = Root();
        if (root is null) { Console.WriteLine("  (dépôt git introuvable, contrôle ignoré)"); return; }

        Test("aucun fichier suivi ne cite l'organisation réelle, ses sites, ses projets ou ses outils internes", () =>
        {
            var hits = new List<string>();
            foreach (var file in TrackedFiles(root))
            {
                var name = Path.GetFileName(file);
                if (Skipped.Contains(name) || Binary.Any(e => name.EndsWith(e, StringComparison.OrdinalIgnoreCase))) continue;
                var path = Path.Combine(root, file);
                if (!File.Exists(path)) continue;
                if (Hit(file)) hits.Add(file + " (nom du fichier)");
                foreach (var (part, text) in Texts(path, name))
                    if (Hit(text)) hits.Add(part is null ? file : $"{file} › {part}");
            }
            Equal("", string.Join(", ", hits.Distinct()), "fichiers à neutraliser");
        });
    }

    /// <summary>Vrai si le texte contient un terme interdit : mot entier, partie d'un nom en CamelCase, ou deux ou trois parties accolées.</summary>
    public static bool Hit(string text)
    {
        foreach (Match w in Words.Matches(text))
        {
            if (Forbidden.Contains(Hash(w.Value))) return true;
            var parts = CamelParts.Matches(w.Value).Select(m => m.Value).ToList();
            if (parts.Count < 2) continue;
            for (var i = 0; i < parts.Count; i++)
            {
                var joined = "";
                for (var j = i; j < Math.Min(parts.Count, i + 3); j++)
                {
                    joined += parts[j];
                    if (Forbidden.Contains(Hash(joined))) return true;
                }
            }
        }
        return false;
    }

    private static string Hash(string word) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(word.ToLowerInvariant()))).ToLowerInvariant();

    private static IEnumerable<(string? Part, string Text)> Texts(string path, string name)
    {
        if (Zipped.Any(e => name.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
        {
            using var zip = ZipFile.OpenRead(path);
            foreach (var entry in zip.Entries.Where(e => e.FullName.EndsWith(".xml") || e.FullName.EndsWith(".rels")))
            {
                using var reader = new StreamReader(entry.Open());
                yield return (entry.FullName, reader.ReadToEnd());
            }
            yield break;
        }
        yield return (null, File.ReadAllText(path));
    }

    private static IEnumerable<string> TrackedFiles(string root)
    {
        var git = Process.Start(new ProcessStartInfo("git", "ls-files -z")
        {
            WorkingDirectory = root, RedirectStandardOutput = true, UseShellExecute = false, StandardOutputEncoding = Encoding.UTF8,
        })!;
        var output = git.StandardOutput.ReadToEnd();
        git.WaitForExit();
        return output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    private static string? Root()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, ".git"))) return d.FullName;
        return null;
    }
}
