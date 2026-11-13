using System.IO.Compression;
using System.Text;

namespace AeTool.Tests;

/// <summary>Jeux d'essai synthétiques : aucun nom réel, la structure seule est représentative.</summary>
public static class Fixtures
{
    /// <summary>Petit modèle au format d'échange : 8 éléments, 6 relations (dont une relation vers une relation), 3 vues.</summary>
    public static string Model(string appName = "ITSM-SUITE", bool withLegacy = true, string extra = "") => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <model xmlns="http://www.opengroup.org/xsd/archimate/3.0/" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" identifier="id-demo">
          <name xml:lang="fr">Démo</name>
          <elements>
            <element identifier="id-cap" xsi:type="Capability"><name xml:lang="fr">Gérer les services numériques</name>
              <properties><property propertyDefinitionRef="propid-1"><value xml:lang="fr">2</value></property></properties></element>
            <element identifier="id-itsm" xsi:type="ApplicationComponent"><name xml:lang="fr">{appName}</name>
              <documentation xml:lang="fr">Suite ITSM cible.</documentation>
              <properties><property propertyDefinitionRef="propid-2"><value xml:lang="fr">Active</value></property></properties></element>
            {(withLegacy ? """<element identifier="id-legacy" xsi:type="ApplicationComponent"><name xml:lang="fr">PORTAIL-LEGACY</name></element>""" : "")}
            <element identifier="id-report" xsi:type="ApplicationComponent"><name xml:lang="fr">REPORTING-SERVICE</name></element>
            <element identifier="id-ticket" xsi:type="DataObject"><name xml:lang="fr">Ticket de support</name></element>
            <element identifier="id-srv" xsi:type="Node"><name xml:lang="fr">SRV-APP-01</name></element>
            <element identifier="id-prj1" xsi:type="WorkPackage"><name xml:lang="fr">PRJ-ITSM-001</name>
              <properties><property propertyDefinitionRef="propid-3"><value xml:lang="fr">En cours</value></property></properties></element>
            <element identifier="id-prj2" xsi:type="WorkPackage"><name xml:lang="fr">PRJ-BI-002</name></element>
          </elements>
          <relationships>
            <relationship identifier="id-r1" source="id-itsm" target="id-cap" xsi:type="Realization" />
            <relationship identifier="id-r2" source="id-itsm" target="id-ticket" xsi:type="Access" accessType="ReadWrite" />
            <relationship identifier="id-r3" source="id-itsm" target="id-report" xsi:type="Flow"><name xml:lang="fr">Tickets clos</name></relationship>
            <relationship identifier="id-r4" source="id-srv" target="id-itsm" xsi:type="Assignment" />
            <relationship identifier="id-r5" source="id-ticket" target="id-r3" xsi:type="Association" isDirected="true" />
            {(withLegacy ? """<relationship identifier="id-r6" source="id-legacy" target="id-cap" xsi:type="Realization" />""" : "")}
            {extra}
          </relationships>
          <organizations>
            <item><label xml:lang="fr">Views</label>
              <item><label xml:lang="fr">Entreprise</label>
                <item><label xml:lang="fr">Projets</label>
                  <item><label xml:lang="fr">PRJ-ITSM-001</label><item identifierRef="id-v-cible" /></item>
                  <item><label xml:lang="fr">PRJ-BI-002</label><item identifierRef="id-v-bi" /></item>
                </item>
                <item><label xml:lang="fr">POS</label><item identifierRef="id-v-pos" /></item>
              </item>
            </item>
          </organizations>
          <propertyDefinitions>
            <propertyDefinition identifier="propid-1" type="string"><name>pos.order</name></propertyDefinition>
            <propertyDefinition identifier="propid-2" type="string"><name>lifecycle.state</name></propertyDefinition>
            <propertyDefinition identifier="propid-3" type="string"><name>project.status</name></propertyDefinition>
          </propertyDefinitions>
          <views><diagrams>
            <view identifier="id-v-pos" xsi:type="Diagram"><name xml:lang="fr">POS simplifié</name>
              <node identifier="id-n1" elementRef="id-cap" xsi:type="Element" x="10" y="10" w="400" h="200">
                <style><fillColor r="245" g="222" b="170" a="100" /><lineColor r="92" g="92" b="92" a="100" /><font name="Segoe UI" size="9"><color r="0" g="0" b="0" /></font></style>
                <node identifier="id-n2" elementRef="id-itsm" xsi:type="Element" x="30" y="60" w="120" h="55">
                  <style><fillColor r="181" g="255" b="255" a="100" /></style></node>
              </node>
              <node identifier="id-n3" xsi:type="Label" x="10" y="230" w="200" h="40"><label xml:lang="fr">Légende</label></node>
            </view>
            <view identifier="id-v-cible" xsi:type="Diagram" viewpoint="Application Cooperation"><name xml:lang="fr">01 - Cible ITSM</name>
              <node identifier="id-n4" elementRef="id-itsm" xsi:type="Element" x="10" y="10" w="120" h="55" />
              <node identifier="id-n5" elementRef="id-report" xsi:type="Element" x="300" y="10" w="120" h="55" />
              <node identifier="id-n6" elementRef="id-cap" xsi:type="Element" x="10" y="200" w="120" h="55" />
              <connection identifier="id-c1" relationshipRef="id-r3" xsi:type="Relationship" source="id-n4" target="id-n5">
                <style lineWidth="2"><lineColor r="0" g="0" b="0" /></style><bendpoint x="200" y="80" /></connection>
            </view>
            <view identifier="id-v-bi" xsi:type="Diagram"><name xml:lang="fr">Cible BI</name>
              <node identifier="id-n7" elementRef="id-report" xsi:type="Element" x="10" y="10" w="120" h="55" />
              <node identifier="id-n8" elementRef="id-ticket" xsi:type="Element" x="200" y="10" w="120" h="55" />
            </view>
          </diagrams></views>
        </model>
        """;

    public static Stream Stream(string text) => new MemoryStream(Encoding.UTF8.GetBytes(text.TrimStart()));

    /// <summary>Fabrique un classeur .xlsx minimal : chaînes en ligne, une feuille par entrée.</summary>
    public static Stream Xlsx(params (string Name, string?[][] Rows)[] sheets)
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string path, string content)
            {
                using var w = new StreamWriter(zip.CreateEntry(path).Open(), new UTF8Encoding(false));
                w.Write(content);
            }
            const string main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var sheetTags = new StringBuilder();
            var rels = new StringBuilder();
            for (var i = 0; i < sheets.Length; i++)
            {
                sheetTags.Append($"<sheet name=\"{Escape(sheets[i].Name)}\" sheetId=\"{i + 1}\" r:id=\"rId{i + 1}\"/>");
                rels.Append($"<Relationship Id=\"rId{i + 1}\" Type=\"x\" Target=\"worksheets/sheet{i + 1}.xml\"/>");
                var data = new StringBuilder();
                for (var r = 0; r < sheets[i].Rows.Length; r++)
                {
                    data.Append($"<row r=\"{r + 1}\">");
                    for (var c = 0; c < sheets[i].Rows[r].Length; c++)
                    {
                        var v = sheets[i].Rows[r][c];
                        if (v is null) continue;
                        var reference = $"{(char)('A' + c)}{r + 1}";
                        data.Append(double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _)
                            ? $"<c r=\"{reference}\"><v>{v}</v></c>"
                            : $"<c r=\"{reference}\" t=\"inlineStr\"><is><t>{Escape(v)}</t></is></c>");
                    }
                    data.Append("</row>");
                }
                Add($"xl/worksheets/sheet{i + 1}.xml", $"<worksheet xmlns=\"{main}\"><sheetData>{data}</sheetData></worksheet>");
            }
            Add("xl/workbook.xml", $"<workbook xmlns=\"{main}\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets>{sheetTags}</sheets></workbook>");
            Add("xl/_rels/workbook.xml.rels", $"<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">{rels}</Relationships>");
        }
        ms.Position = 0;
        return ms;
    }

    private static string Escape(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace("\"", "&quot;");
}
