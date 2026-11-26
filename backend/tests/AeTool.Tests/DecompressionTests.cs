using System.IO.Compression;
using System.Text;
using AeTool.Connectors;
using AeTool.Core;
using static AeTool.Tests.Harness;

namespace AeTool.Tests;

/// <summary>
/// Plafond de décompression (docs/securite.md) : un fichier piégé, petit une fois compressé, est refusé avant de saturer
/// la mémoire. Les fichiers piégés sont fabriqués ici, en flux, sans jamais tenir le contenu décompressé en mémoire.
/// </summary>
public static class DecompressionTests
{
    private const int Mo = 1024 * 1024;
    private const string Sst = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    /// <summary>Écrit <paramref name="head"/>, puis <paramref name="padding"/> octets d'espaces, puis <paramref name="tail"/>.</summary>
    private static void WritePadded(Stream target, string head, long padding, string tail)
    {
        target.Write(Encoding.UTF8.GetBytes(head));
        var chunk = new byte[Mo];
        Array.Fill(chunk, (byte)' ');
        for (var left = padding; left > 0; left -= chunk.Length) target.Write(chunk, 0, (int)Math.Min(chunk.Length, left));
        target.Write(Encoding.UTF8.GetBytes(tail));
    }

    /// <summary>Page draw.io compressée comme draw.io l'écrit, gonflée d'espaces (l'encodage d'URL les rendrait en %20 : on n'en a pas besoin pour lire).</summary>
    private static string BombPage(long padding)
    {
        using var ms = new MemoryStream();
        using (var deflate = new DeflateStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            WritePadded(deflate, "<mxGraphModel><root>", padding, "</root></mxGraphModel>");
        return Convert.ToBase64String(ms.ToArray());
    }

    private static Stream Drawio(params string[] packedPages) => new MemoryStream(Encoding.UTF8.GetBytes(
        "<mxfile host=\"drawio\">" + string.Concat(packedPages.Select((p, i) => $"<diagram id=\"p{i}\" name=\"Page {i}\">{p}</diagram>")) + "</mxfile>"));

    /// <summary>Classeur dont les fichiers XML nommés sont gonflés d'espaces.</summary>
    private static Stream BombXlsx(long padding, params string[] entries)
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var name in entries)
            {
                using var s = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
                var root = name.EndsWith("sharedStrings.xml") ? "sst" : name.EndsWith("styles.xml") ? "styleSheet" : "workbook";
                WritePadded(s, $"<{root} xmlns=\"{Sst}\">", padding, $"</{root}>");
            }
        ms.Position = 0;
        return ms;
    }

    private static InvalidDataException Refused(Action read)
    {
        try { read(); }
        catch (InvalidDataException e) { return e; }
        throw new Exception("le fichier piégé aurait dû être refusé");
    }

    public static void Run()
    {
        Console.WriteLine("Plafond de décompression");

        Test("draw.io : une page de 60 Mo décompressés (moins de 100 ko reçus) est refusée", () =>
        {
            var page = BombPage(60L * Mo);
            True(page.Length < 200_000, $"fichier reçu de {page.Length} octets");
            var e = Refused(() => DrawioReader.Read(Drawio(page)));
            True(e.Message.Contains("50 Mo"), e.Message);
        });

        Test("draw.io : le plafond est cumulé sur les pages d'un même fichier", () =>
        {
            var page = BombPage(30L * Mo);
            DrawioReader.Read(Drawio(page));                          // une page de 30 Mo passe
            Refused(() => DrawioReader.Read(Drawio(page, page)));     // deux pages de 30 Mo, non
        });

        Test("draw.io : l'import renvoie une anomalie lisible au lieu de saturer la mémoire", () =>
        {
            var report = AeTool.Connectors.DrawioImporter.Apply(Drawio(BombPage(60L * Mo)), "piege.drawio", new Repository(), "test", null);
            var error = report.Anomalies.Single(a => a.Code == "fichier-illisible");
            True(error.Message.Contains("50 Mo"), error.Message);
        });

        Test("xlsx : un classeur dont un fichier décompresse à 60 Mo est refusé", () =>
        {
            var e = Refused(() => XlsxReader.Read(BombXlsx(60L * Mo, "xl/sharedStrings.xml")));
            True(e.Message.Contains("50 Mo"), e.Message);
        });

        Test("xlsx : le plafond est cumulé sur les fichiers du classeur", () =>
            Refused(() => XlsxReader.Read(BombXlsx(30L * Mo, "xl/sharedStrings.xml", "xl/styles.xml"))));

        Test("xlsx : l'import du classeur d'urbanisation renvoie une anomalie lisible", () =>
        {
            var report = UrbaImporter.Import(BombXlsx(60L * Mo, "xl/sharedStrings.xml"), new Repository());
            True(report.Anomalies.Any(a => a.Code == "xlsx-invalide" && a.Message.Contains("50 Mo")), "anomalie xlsx-invalide");
        });

        Test("budget : un classeur ordinaire passe, le même est refusé sous un plafond de 10 octets", () =>
        {
            var book = XlsxReader.Read(Fixtures.Xlsx(("Feuille", new[] { new string?[] { "a", "b" } })));
            True(book.ContainsKey("Feuille"), "feuille lue");
            var budget = new DecompressionBudget(limit: 10);
            Refused(() => XlsxReader.Read(Fixtures.Xlsx(("Feuille", new[] { new string?[] { "a", "b" } })), budget));
        });
    }
}
