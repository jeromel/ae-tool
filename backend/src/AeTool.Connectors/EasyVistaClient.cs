using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AeTool.Core;

namespace AeTool.Connectors;

/// <summary>Échec d'accès à une source, avec un message présentable à l'utilisateur : aucun détail technique, jamais de secret.</summary>
public sealed class SourceAccessException : Exception
{
    public SourceAccessException(string message) : base(message) { }
}

/// <summary>
/// Adaptateur API REST d'EasyVista (EV Service Manager), en lecture seule : seules des requêtes GET sont émises.
/// Adresse : https://{serveur}/api/v1/{compte}/{ressource}. Le client HTTP est injecté pour être testé avec des réponses simulées.
/// </summary>
public sealed partial class EasyVistaClient
{
    public const int MaxPages = 500;
    public const string DefaultResource = "configuration-items";
    public const string IdColumn = "@id";
    /// <summary>Réglages reconnus dans Connection ; tout autre réglage est ignoré à l'enregistrement.</summary>
    public static readonly string[] ConnectionKeys = { "baseUrl", "account", "resource", "fields", "search", "pageSize", "authMode", "login" };

    private readonly HttpClient _http;

    /// <summary>Délai d'attente par appel.</summary>
    public TimeSpan CallTimeout { get; set; } = TimeSpan.FromSeconds(30);
    /// <summary>Pause minimale entre deux pages : la plateforme limite à environ 100 appels par minute.</summary>
    public TimeSpan MinInterval { get; set; } = TimeSpan.FromMilliseconds(600);

    public EasyVistaClient(HttpClient http) => _http = http;

    [GeneratedRegex("^[A-Za-z0-9._-]+$")] private static partial Regex Segment();
    [GeneratedRegex("^[A-Za-z0-9._-]+(/[A-Za-z0-9._-]+)*$")] private static partial Regex ResourcePath();

    // ---------- Réglages ----------

    /// <summary>Contrôle des réglages ; renvoie le message d'erreur, ou null s'ils sont utilisables.</summary>
    public static string? Validate(IReadOnlyDictionary<string, string> connection)
    {
        var baseUrl = Setting(connection, "baseUrl");
        if (baseUrl is null) return "L'adresse du serveur EasyVista est obligatoire.";
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host) || uri.UserInfo.Length > 0)
            return "L'adresse du serveur EasyVista n'est pas valide.";
        if (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
            return "L'adresse du serveur EasyVista doit être en https (http n'est accepté que pour localhost).";
        var account = Setting(connection, "account");
        if (account is null || !Segment().IsMatch(account)) return "Le compte EasyVista est obligatoire (lettres, chiffres, points, tirets).";
        if (!ResourcePath().IsMatch(Setting(connection, "resource") ?? DefaultResource) || (Setting(connection, "resource") ?? "").Contains(".."))
            return "La ressource EasyVista n'est pas valide.";
        if (Setting(connection, "pageSize") is { } size && !int.TryParse(size, NumberStyles.None, CultureInfo.InvariantCulture, out _))
            return "La taille de page doit être un nombre entier.";
        var mode = AuthMode(connection);
        if (mode is null) return "Le mode d'authentification doit être « basic » ou « bearer ».";
        if (mode == "basic" && Setting(connection, "login") is null) return "L'identifiant de connexion est obligatoire en mode basic.";
        return null;
    }

    private static string? Setting(IReadOnlyDictionary<string, string> connection, string key) =>
        connection.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;

    private static string? AuthMode(IReadOnlyDictionary<string, string> connection) =>
        (Setting(connection, "authMode") ?? "basic").ToLowerInvariant() is var m && (m == "basic" || m == "bearer") ? m : null;

    private static int PageSize(IReadOnlyDictionary<string, string> connection) =>
        Math.Clamp(int.TryParse(Setting(connection, "pageSize"), out var n) ? n : 100, 1, 1000);

    private static Uri FirstUrl(IReadOnlyDictionary<string, string> c, int maxRows, int? offset = null)
    {
        var root = Setting(c, "baseUrl")!.TrimEnd('/');
        var query = new StringBuilder($"max_rows={maxRows}");
        if (Setting(c, "fields") is { } fields) query.Append("&fields=").Append(Uri.EscapeDataString(fields));
        if (Setting(c, "search") is { } search) query.Append("&search=").Append(Uri.EscapeDataString(search));
        if (offset is { } o) query.Append("&offset=").Append(o);
        return new Uri($"{root}/api/v1/{Setting(c, "account")}/{Setting(c, "resource") ?? DefaultResource}?{query}");
    }

    // ---------- Appels ----------

    /// <summary>Un seul appel, une seule ligne demandée : sert à vérifier l'adresse, le compte et les identifiants. Renvoie le total annoncé.</summary>
    public async Task<long?> ProbeAsync(IReadOnlyDictionary<string, string> connection, string? secret, CancellationToken ct)
    {
        Check(connection, secret);
        var page = await GetPageAsync(FirstUrl(connection, 1), connection, secret, ct);
        return page.Total;
    }

    /// <summary>
    /// Lit les enregistrements de la ressource, page après page, et les aplatit en lignes.
    /// S'arrête à maxRecords ou à MaxPages ; la table est alors marquée tronquée.
    /// </summary>
    public async Task<SourceTable> FetchAsync(IReadOnlyDictionary<string, string> connection, string? secret, int maxRecords, CancellationToken ct)
    {
        Check(connection, secret);
        var size = PageSize(connection);
        var table = new SourceTable();
        var columns = new List<string>();
        var seenColumns = new HashSet<string>();
        var url = FirstUrl(connection, size);
        string? previousFirstId = null;
        var fetched = 0;

        for (var pageNumber = 1; ; pageNumber++)
        {
            if (pageNumber > 1 && MinInterval > TimeSpan.Zero) await Task.Delay(MinInterval, ct);
            var page = await GetPageAsync(url, connection, secret, ct);
            table.Total = page.Total ?? table.Total;
            foreach (var row in page.Rows)
            {
                if (table.Rows.Count >= maxRecords) break;
                table.Rows.Add(row);
                foreach (var key in row.Keys) if (seenColumns.Add(key)) columns.Add(key);
            }
            fetched += page.Rows.Count;
            if (page.Rows.Count == 0) break;
            var firstId = page.Rows[0].GetValueOrDefault(IdColumn);

            var more = page.NextOffset is not null || (page.Total is { } t && fetched < t);
            if (table.Rows.Count >= maxRecords)
            {
                table.Truncated = more || table.Rows.Count < fetched;
                break;
            }
            if (!more) break;
            // Version sans « @next » : on retente avec offset ; si la plateforme l'ignore, la première ligne se répète.
            if (page.NextOffset is null && firstId is not null && firstId == previousFirstId)
            {
                table.Truncated = true;
                table.Issues.Add(new SourceIssue("avertissement", "pagination-non-prise-en-charge",
                    $"Cette version d'EasyVista ne pagine pas : seules les {fetched - page.Rows.Count} premières lignes ont été lues."));
                table.Rows.RemoveRange(table.Rows.Count - page.Rows.Count, page.Rows.Count);
                break;
            }
            if (pageNumber >= MaxPages)
            {
                table.Truncated = true;
                table.Issues.Add(new SourceIssue("avertissement", "limite-pages", $"Lecture arrêtée après {MaxPages} pages."));
                break;
            }
            previousFirstId = firstId;
            url = FirstUrl(connection, size, page.NextOffset ?? fetched);
        }
        table.Columns = columns;
        return table;
    }

    private static void Check(IReadOnlyDictionary<string, string> connection, string? secret)
    {
        if (Validate(connection) is { } problem) throw new SourceAccessException(problem);
        if (string.IsNullOrEmpty(secret)) throw new SourceAccessException("Aucun mot de passe ou jeton n'est enregistré pour cette source.");
    }

    private sealed record Page(List<Dictionary<string, string?>> Rows, long? Total, int? NextOffset);

    private async Task<Page> GetPageAsync(Uri url, IReadOnlyDictionary<string, string> connection, string? secret, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Authorization = AuthMode(connection) == "bearer"
            ? new AuthenticationHeaderValue("Bearer", secret)
            : new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Setting(connection, "login")}:{secret}")));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(CallTimeout);
        try
        {
            using var response = await _http.SendAsync(request, timeout.Token);
            ThrowIfRefused(response.StatusCode);
            var json = await response.Content.ReadAsStringAsync(timeout.Token);
            return Parse(json);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new SourceAccessException("EasyVista n'a pas répondu dans le délai imparti.");
        }
        catch (HttpRequestException)
        {
            throw new SourceAccessException("EasyVista est injoignable : vérifiez l'adresse du serveur et le réseau.");
        }
    }

    private static void ThrowIfRefused(HttpStatusCode status)
    {
        if ((int)status is >= 200 and < 300) return;
        throw new SourceAccessException(status switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "Identifiants refusés par EasyVista.",
            HttpStatusCode.NotFound => "Compte ou ressource introuvable.",
            HttpStatusCode.TooManyRequests => "Limite d'appels d'EasyVista atteinte : réessayez dans une minute.",
            _ => "EasyVista a répondu par une erreur.",
        });
    }

    // ---------- Lecture de la réponse ----------

    private static Page Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("records", out var records) || records.ValueKind != JsonValueKind.Array)
                throw new SourceAccessException("Réponse d'EasyVista illisible.");
            var rows = records.EnumerateArray().Where(r => r.ValueKind == JsonValueKind.Object).Select(Flatten).ToList();
            return new Page(rows, Counter(root, "total_record_count"), NextOffset(root));
        }
        catch (JsonException)
        {
            throw new SourceAccessException("Réponse d'EasyVista illisible.");
        }
    }

    /// <summary>Les compteurs sont des chaînes dans la réponse ; un nombre JSON est aussi accepté.</summary>
    private static long? Counter(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var v)) return null;
        var text = v.ValueKind == JsonValueKind.Number ? v.GetRawText() : v.GetString();
        return long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : null;
    }

    /// <summary>
    /// Décalage de la page suivante, lu dans le lien « @next ». On ne suit pas le lien lui-même : il peut porter un nom de serveur interne,
    /// et surtout y envoyer les identifiants à un hôte non configuré serait une fuite.
    /// </summary>
    private static int? NextOffset(JsonElement root)
    {
        if (!root.TryGetProperty("@next", out var next) || next.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(next.GetString())) return null;
        var match = OffsetParameter().Match(next.GetString()!);
        if (match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var offset)) return offset;
        throw new SourceAccessException("Réponse d'EasyVista illisible : lien de pagination non reconnu.");
    }

    [GeneratedRegex(@"[?&]offset=(\d+)")] private static partial Regex OffsetParameter();

    /// <summary>Enregistrement → colonnes : objets imbriqués en colonnes pointées, HREF imbriqués ignorés, « @id » tiré du HREF de l'enregistrement.</summary>
    private static Dictionary<string, string?> Flatten(JsonElement record)
    {
        var row = new Dictionary<string, string?>();
        if (record.TryGetProperty("HREF", out var href) && href.ValueKind == JsonValueKind.String) row[IdColumn] = LastSegment(href.GetString()!);
        AddProperties(record, "", row);
        return row;
    }

    private static void AddProperties(JsonElement element, string prefix, Dictionary<string, string?> row)
    {
        foreach (var p in element.EnumerateObject())
        {
            if (p.Name == "HREF") continue;
            var name = prefix + p.Name;
            row[name] = p.Value.ValueKind switch
            {
                JsonValueKind.Object => null,
                JsonValueKind.String => p.Value.GetString(),
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => p.Value.GetRawText(),
            };
            if (p.Value.ValueKind == JsonValueKind.Object) { row.Remove(name); AddProperties(p.Value, name + ".", row); }
        }
    }

    private static string? LastSegment(string href)
    {
        var path = href.Split('?', '#')[0].TrimEnd('/');
        var segment = path[(path.LastIndexOf('/') + 1)..];
        return segment.Length > 0 ? segment : null;
    }
}
