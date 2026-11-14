using System.Text;
using System.Text.Json;
using AeTool.Core;

namespace AeTool.Api.Storage;

/// <summary>
/// Journal des modifications : un fichier <c>journal.jsonl</c>, une entrée JSON par ligne, en ajout seul. Une écriture ajoute des
/// lignes à la fin sans réécrire le reste : l'historique ne peut pas être perdu par une réécriture interrompue.
/// </summary>
public sealed class JournalFile
{
    public const string FileName = "journal.jsonl";
    private readonly string _path;
    private bool _needsLeadingNewline;

    public JournalFile(string path) => _path = path;

    /// <summary>
    /// Lit toutes les entrées. Une ligne illisible (écriture coupée net par un arrêt brutal) est ignorée ; si la dernière ligne n'est
    /// pas terminée, le prochain ajout commence sur une nouvelle ligne pour ne pas s'y coller.
    /// </summary>
    public List<JournalEntry> Read()
    {
        var entries = new List<JournalEntry>();
        _needsLeadingNewline = false;
        if (!File.Exists(_path)) return entries;
        var bytes = File.ReadAllBytes(_path);
        _needsLeadingNewline = bytes.Length > 0 && bytes[^1] != (byte)'\n';
        foreach (var line in Encoding.UTF8.GetString(bytes).Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try { if (JsonSerializer.Deserialize<JournalEntry>(line) is { } entry) entries.Add(entry); }
            catch (JsonException) { /* ligne tronquée : ignorée */ }
        }
        return entries;
    }

    /// <summary>Ajoute des entrées à la fin du fichier, vidées sur le disque avant le retour.</summary>
    public void Append(IEnumerable<JournalEntry> entries)
    {
        var text = new StringBuilder();
        if (_needsLeadingNewline) text.Append('\n');
        var any = false;
        foreach (var e in entries) { text.Append(JsonSerializer.Serialize(e)).Append('\n'); any = true; }
        if (!any) return;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        using (var s = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read))
        {
            s.Write(Encoding.UTF8.GetBytes(text.ToString()));
            s.Flush(true);
        }
        _needsLeadingNewline = false;
    }

    /// <summary>Réécrit tout le fichier (transfert depuis un ancien référentiel, restauration). Écriture sûre.</summary>
    public void Rewrite(IEnumerable<JournalEntry> entries)
    {
        AtomicFile.Write(_path, s =>
        {
            foreach (var e in entries) s.Write(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(e) + "\n"));
        });
        _needsLeadingNewline = false;
    }

    /// <summary>
    /// Réunit deux journaux sans doublon : toutes les entrées de <paramref name="primary"/>, dans leur ordre, puis celles de
    /// <paramref name="extra"/> qui n'y figurent pas. Sert au transfert depuis un ancien fichier et à la restauration.
    /// </summary>
    public static List<JournalEntry> Union(IEnumerable<JournalEntry> primary, IEnumerable<JournalEntry> extra)
    {
        var result = primary.ToList();
        var known = result.Select(Key).ToHashSet();
        foreach (var e in extra) if (known.Add(Key(e))) result.Add(e);
        return result;
    }

    private static string Key(JournalEntry e) => string.Join('\u0001', e.At.Ticks, e.User, e.Action, e.TargetId, e.TargetKind, e.Detail);
}
