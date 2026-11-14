namespace AeTool.Api.Storage;

/// <summary>Écriture sûre : fichier temporaire, vidage sur le disque, puis remplacement. Une coupure ne laisse jamais un fichier tronqué.</summary>
public static class AtomicFile
{
    public static void Write(string path, Action<Stream> write)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var tmp = path + ".tmp";
        using (var s = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            write(s);
            // Sans ce vidage, le remplacement pourrait précéder l'arrivée des données sur le disque.
            s.Flush(true);
        }
        File.Move(tmp, path, overwrite: true);
    }
}
