namespace AeTool.Connectors;

/// <summary>Anomalie d'import : signalée, jamais corrigée en silence.</summary>
public sealed record Anomaly(string Severity, string Code, string Message, string? Ref = null);

public sealed class Tally
{
    public int Added { get; set; }
    public int Updated { get; set; }
    public int Unchanged { get; set; }
    public int Archived { get; set; }
    public int Total => Added + Updated + Unchanged;
}

public sealed class ImportReport
{
    public string Source { get; set; } = "";
    public DateTime At { get; set; } = DateTime.UtcNow;
    public Dictionary<string, Tally> Counts { get; set; } = new();
    public List<Anomaly> Anomalies { get; set; } = new();
    public List<string> Notes { get; set; } = new();
    /// <summary>Vues créées ou mises à jour par l'import, pour y mener directement (import draw.io).</summary>
    public List<string>? Views { get; set; }

    public Tally For(string category)
    {
        if (!Counts.TryGetValue(category, out var t)) Counts[category] = t = new Tally();
        return t;
    }

    public void Warn(string code, string message, string? reference = null) =>
        Anomalies.Add(new Anomaly("avertissement", code, message, reference));

    public void Error(string code, string message, string? reference = null) =>
        Anomalies.Add(new Anomaly("erreur", code, message, reference));
}
