using System.Text;
using AeTool.Connectors;
using static AeTool.Tests.Harness;

namespace AeTool.Tests;

public static class TabularTests
{
    private static SourceTableView Read(string text, Encoding? encoding = null, bool bom = false)
    {
        var bytes = (encoding ?? new UTF8Encoding(false)).GetBytes(text);
        if (bom) bytes = Encoding.UTF8.GetPreamble().Concat(bytes).ToArray();
        return new SourceTableView(TabularReader.Read(new MemoryStream(bytes), "test.csv"));
    }

    /// <summary>Lecture commode d'un résultat dans les assertions.</summary>
    private sealed class SourceTableView
    {
        public AeTool.Core.SourceTable Table { get; }
        public SourceTableView(AeTool.Core.SourceTable table) => Table = table;
        public string Columns => string.Join("|", Table.Columns);
        public string Cell(int row, string column) => Table.Rows[row][column] ?? "<vide>";
    }

    public static void Run()
    {
        Console.WriteLine("Adaptateur fichier (CSV, Excel)");

        Test("classeur : une cellule au format date est lue en date ISO, un nombre ordinaire reste un nombre", () =>
        {
            var book = AeTool.Connectors.XlsxReader.Read(Fixtures.Xlsx(("Preuves", new[] { new string?[] { "date:46307", "datefr:46307.5", "46307", "texte" } })));
            var row = book["Preuves"][0];
            Equal("2026-10-12", row[0], "format de date prédéfini");
            Equal("2026-10-12 12:00", row[1], "format personnalisé, avec heure");
            Equal("46307", row[2], "nombre sans format de date");
            Equal("texte", row[3], "texte");
            True(!AeTool.Connectors.XlsxReader.IsDateFormat("0.0") && !AeTool.Connectors.XlsxReader.IsDateFormat("\"jours\" 0")
                 && AeTool.Connectors.XlsxReader.IsDateFormat("mm-dd-yy") && AeTool.Connectors.XlsxReader.IsDateFormat("[$-fr-FR]dddd d mmmm"), "détection du format");
        });

        Test("séparateur détecté : point-virgule, virgule, tabulation, barre verticale", () =>
        {
            foreach (var d in new[] { ";", ",", "\t", "|" })
            {
                var t = Read($"id{d}nom{d}serie\n1{d}A{d}S1\n");
                Equal("id|nom|serie", t.Columns, $"colonnes avec « {d} »");
                Equal("S1", t.Cell(0, "serie"), "valeur");
            }
        });

        Test("séparateur : un séparateur entre guillemets dans l'en-tête ne compte pas", () =>
        {
            var t = Read("\"a,b,c\";nom\n1;A\n");
            Equal("a,b,c|nom", t.Columns, "colonnes");
        });

        Test("guillemets doublés et retours à la ligne dans un champ", () =>
        {
            var t = Read("id;nom;note\n1;\"Il dit \"\"oui\"\"\";\"ligne 1\nligne 2; suite\"\n2;B;x\r\n");
            Equal(2, t.Table.Rows.Count, "lignes");
            Equal("Il dit \"oui\"", t.Cell(0, "nom"), "guillemets doublés");
            Equal("ligne 1\nligne 2; suite", t.Cell(0, "note"), "retour à la ligne");
            Equal("x", t.Cell(1, "note"), "ligne suivante (CRLF)");
        });

        Test("UTF-8 avec BOM, sans BOM, et repli sur Latin-1", () =>
        {
            Equal("id", Read("id;nom\n1;Éléphant\n", bom: true).Table.Columns[0], "le BOM n'entre pas dans le nom de colonne");
            Equal("Éléphant", Read("id;nom\n1;Éléphant\n").Cell(0, "nom"), "UTF-8");
            Equal("Éléphant à côté", Read("id;nom\n1;Éléphant à côté\n", Encoding.Latin1).Cell(0, "nom"), "Latin-1");
        });

        Test("en-têtes : espaces retirés, doublons suffixés, colonne sans titre nommée", () =>
        {
            var t = Read(" id ;nom;nom;;nom\n1;A;B;C;D\n");
            Equal("id|nom|nom (2)|Colonne 4|nom (3)", t.Columns, "colonnes");
            Equal("B", t.Cell(0, "nom (2)"), "valeur du doublon");
        });

        Test("lignes entièrement vides ignorées, cellules vides lues comme absentes, lignes courtes complétées", () =>
        {
            var t = Read("id;nom;serie\n1;A;\n;;\n\n2;B\n");
            Equal(2, t.Table.Rows.Count, "lignes");
            Equal("<vide>", t.Cell(0, "serie"), "cellule vide");
            Equal("<vide>", t.Cell(1, "serie"), "ligne courte");
        });

        Test("lignes avec trop de valeurs : avertissement", () =>
        {
            var t = Read("id;nom\n1;A;extra\n");
            True(t.Table.Issues.Any(i => i.Code == "lignes-irregulieres"), "avertissement attendu");
        });

        Test("classeur xlsx : première feuille non vide, première ligne = en-têtes", () =>
        {
            using var book = Fixtures.Xlsx(("Vide", Array.Empty<string?[]>()), ("Export", new[]
            {
                new string?[] { "ID", "Nom", "Série" },
                new string?[] { "10", "SRV-1", "S-1" },
                new string?[] { null, null, null },
                new string?[] { "11", "SRV-2", null },
            }));
            var t = new SourceTableView(TabularReader.Read(book, "export.xlsx"));
            Equal("ID|Nom|Série", t.Columns, "colonnes");
            Equal(2, t.Table.Rows.Count, "lignes");
            Equal("SRV-2", t.Cell(1, "Nom"), "valeur");
            Equal("<vide>", t.Cell(1, "Série"), "cellule vide");
        });

        Test("fichier illisible : anomalie explicite, sans détail technique", () =>
        {
            var broken = new SourceTableView(TabularReader.Read(new MemoryStream(Encoding.ASCII.GetBytes("PK\u0003\u0004pas un classeur")), "x.xlsx"));
            Equal(0, broken.Table.Rows.Count, "lignes");
            True(broken.Table.Issues.Any(i => i.Severity == "erreur" && i.Code == "classeur-illisible"), "erreur attendue");
            True(Read("").Table.Issues.Any(i => i.Code == "fichier-vide"), "fichier vide");
        });

        Test("limites : 100 000 lignes et 200 colonnes", () =>
        {
            var sb = new StringBuilder("id;nom\n");
            for (var i = 0; i < TabularReader.MaxRows + 5; i++) sb.Append(i).Append(";N").Append(i).Append('\n');
            var rows = Read(sb.ToString());
            Equal(TabularReader.MaxRows, rows.Table.Rows.Count, "lignes lues");
            True(rows.Table.Truncated && rows.Table.Issues.Any(i => i.Code == "limite-lignes"), "anomalie de lignes");

            var header = string.Join(";", Enumerable.Range(0, 250).Select(i => "c" + i));
            var columns = Read(header + "\n" + string.Join(";", Enumerable.Range(0, 250)) + "\n");
            Equal(TabularReader.MaxColumns, columns.Table.Columns.Count, "colonnes lues");
            True(columns.Table.Issues.Any(i => i.Code == "limite-colonnes"), "anomalie de colonnes");
        });
    }
}
