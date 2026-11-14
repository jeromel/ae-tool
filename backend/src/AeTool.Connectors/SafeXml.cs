using System.Xml;
using System.Xml.Linq;

namespace AeTool.Connectors;

/// <summary>
/// Lecture du XML reçu de l'extérieur (export Archi, feuilles d'un classeur). Toute déclaration DOCTYPE est refusée :
/// ni le format d'échange ArchiMate ni le format xlsx n'en contiennent, et c'est par elle que passent les attaques
/// par entités (lecture de fichiers du serveur, expansion démesurée). Aucune ressource externe n'est résolue.
/// </summary>
public static class SafeXml
{
    private static readonly XmlReaderSettings Settings = new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };

    public static XDocument Load(Stream stream)
    {
        using var reader = XmlReader.Create(stream, Settings);
        return XDocument.Load(reader);
    }
}
