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
/// Adaptateur « API REST » générique, en lecture seule : seules des requêtes GET sont émises. Il lit une ressource JSON
/// paginée, comme en exposent la plupart des CMDB et outils de gestion de projet. Tout ce qui varie d'une API à l'autre
/// se règle : adresse, paramètres de pagination, champ qui porte les éléments, le total, le lien de la page suivante et
/// l'identifiant. Le client HTTP est injecté pour être testé avec des réponses simulées.
/// </summary>
public sealed partial class RestSourceClient
{
    public const int MaxPages = 500;
    public const string IdColumn = "@id";
    /// <summary>Réglages reconnus dans Connection ; tout autre réglage est ignoré à l'enregistrement.</summary>
    public static readonly string[] ConnectionKeys =
        { "url", "query", "pageSize", "pageSizeParam", "offsetParam", "itemsField", "totalField", "nextField", "idField", "authMode", "login" };

    private readonly HttpClient _http;

    /// <summary>Délai d'attente par appel.</summary>
    public TimeSpan CallTimeout { get; set; } = TimeSpan.FromSeconds(30);
    /// <summary>Pause minimale entre deux pages, pour ménager les API qui limitent le nombre d'appels par minute.</summary>
    public TimeSpan MinInterval { get; set; } = TimeSpan.FromMilliseconds(600);

    public RestSourceClient(HttpClient http) => _http = http;

    [GeneratedRegex("^[A-Za-z0-9._@-]+$")] private static partial Regex Name();

    // ---------- Réglages ----------

    /// <summary>Contrôle des réglages ; renvoie le message d'erreur, ou null s'ils sont utilisables.</summary>
    public static string? Validate(IReadOnlyDictionary<string, string> connection)
    {
        var url = Setting(connection, "url");
        if (url is null) return "L'adresse de la ressource est obligatoire.";
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host) || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            return "L'adresse de la ressource n'est pas valide (sans identifiants ni paramètres : ceux-ci se règlent à part).";
        if (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
            return "L'adresse de la ressource doit être en https (http n'est accepté que pour localhost).";
        if (Setting(connection, "query") is { } query && (query.Contains('#') || query.Length > 1000 || query.StartsWith('?')))
            return "Les paramètres de requête ne sont pas valides (forme attendue : champ=valeur&autre=valeur).";
        foreach (var key in new[] { "pageSizeParam", "offsetParam", "itemsField", "totalField", "nextField", "idField" })
            if (Setting(connection, key) is { } name && !Name().IsMatch(name)) return $"Le réglage « {key} » ne doit contenir que des lettres, chiffres, points, tirets ou @.";
        if (Setting(connection, "pageSize") is { } size && !int.TryParse(size, NumberStyles.None, CultureInfo.InvariantCulture, out _))
            return "La taille de page doit être un nombre entier.";
        var mode = AuthMode(connection);
        if (mode is null) return "Le mode d'authentification doit être « basic » ou « bearer ».";
        if (mode == "basic" && Setting(connection, "login") is null) return "L'identifiant de connexion est obligatoire en mode basic.";
        return null;
    }

    private static string? Setting(IReadOnlyDictionary<string, string> connection, string key) =>
        connection.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;

    private static string AuthModeOrDefault(IReadOnlyDictionary<string, string> connection) => AuthMode(connection) ?? "basic";

    private static string? AuthMode(IReadOnlyDictionary<string, string> connection) =>
        (Setting(connection, "authMode") ?? "basic").ToLowerInvariant() is var m && (m == "basic" || m == "bearer") ? m : null;

    private static int PageSize(IReadOnlyDictionary<string, string> connection) =>
        Math.Clamp(int.TryParse(Setting(connection, "pageSize"), out var n) ? n : 100, 1, 1000);

    private static string OffsetParam(IReadOnlyDictionary<string, string> c) => Setting(c, "offsetParam") ?? "offset";

    private static Uri PageUrl(IReadOnlyDictionary<string, string> c, int pageSize, int? offset = null)
    {
        var query = new StringBuilder();
        if (Setting(c, "query") is { } extra) query.Append(extra).Append('&');
        query.Append(Uri.EscapeDataString(Setting(c, "pageSizeParam") ?? "limit")).Append('=').Append(pageSize);
        if (offset is { } o) query.Append('&').Append(Uri.EscapeDataString(OffsetParam(c))).Append('=').Append(o);
        return new Uri($"{Setting(c, "url")!.TrimEnd('/')}?{query}");
    }

    // ---------- Appels ----------

    /// <summary>Un seul appel, une seule ligne demandée : sert à vérifier l'adresse, le compte et les identifiants. Renvoie le total annoncé.</summary>
    public async Task<long?> ProbeAsync(IReadOnlyDictionary<string, string> connection, string? secret, CancellationToken ct)
    {
        Check(connection, secret);
        var page = await GetPageAsync(PageUrl(connection, 1), connection, secret, ct);
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
        var url = PageUrl(connection, size);
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
            // API sans lien de page suivante : on retente avec le décalage ; si elle l'ignore, la première ligne se répète.
            if (page.NextOffset is null && firstId is not null && firstId == previousFirstId)
            {
                table.Truncated = true;
                table.Issues.Add(new SourceIssue("avertissement", "pagination-non-prise-en-charge",
                    $"Cette API ne tient pas compte du décalage : seules les {fetched - page.Rows.Count} premières lignes ont été lues."));
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
            url = PageUrl(connection, size, page.NextOffset ?? fetched);
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
        request.Headers.Authorization = AuthModeOrDefault(connection) == "bearer"
            ? new AuthenticationHeaderValue("Bearer", secret)
            : new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Setting(connection, "login")}:{secret}")));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(CallTimeout);
        try
        {
            using var response = await _http.SendAsync(request, timeout.Token);
            ThrowIfRefused(response.StatusCode);
            var json = await response.Content.ReadAsStringAsync(timeout.Token);
            return Parse(json, connection);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new SourceAccessException("La source n'a pas répondu dans le délai imparti.");
        }
        catch (HttpRequestException)
        {
            throw new SourceAccessException("La source est injoignable : vérifiez l'adresse et le réseau.");
        }
    }

    private static void ThrowIfRefused(HttpStatusCode status)
    {
        if ((int)status is >= 200 and < 300) return;
        throw new SourceAccessException(status switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "Identifiants refusés par la source.",
            HttpStatusCode.NotFound => "Ressource introuvable : vérifiez l'adresse.",
            HttpStatusCode.TooManyRequests => "Limite d'appels de la source atteinte : réessayez dans une minute.",
            _ => "La source a répondu par une erreur.",
        });
    }

    // ---------- Lecture de la réponse ----------

    private static Page Parse(string json, IReadOnlyDictionary<string, string> c)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var items = Items(root, Setting(c, "itemsField")) ?? throw new SourceAccessException("Réponse de la source illisible : liste d'éléments introuvable.");
            var idField = Setting(c, "idField");
            var rows = items.EnumerateArray().Where(r => r.ValueKind == JsonValueKind.Object).Select(r => Flatten(r, idField)).ToList();
            var total = root.ValueKind == JsonValueKind.Object && Setting(c, "totalField") is { } tf ? Counter(root, tf) : null;
            var next = root.ValueKind == JsonValueKind.Object && Setting(c, "nextField") is { } nf ? NextOffset(root, nf, OffsetParam(c)) : null;
            return new Page(rows, total, next);
        }
        catch (JsonException)
        {
            throw new SourceAccessException("Réponse de la source illisible.");
        }
    }

    /// <summary>Les éléments : la racine si c'est une liste, sinon le champ indiqué, sinon le premier champ de la racine qui est une liste.</summary>
    private static JsonElement? Items(JsonElement root, string? field)
    {
        if (root.ValueKind == JsonValueKind.Array) return root;
        if (root.ValueKind != JsonValueKind.Object) return null;
        if (field is not null) return root.TryGetProperty(field, out var v) && v.ValueKind == JsonValueKind.Array ? v : null;
        foreach (var p in root.EnumerateObject()) if (p.Value.ValueKind == JsonValueKind.Array) return p.Value;
        return null;
    }

    /// <summary>Les compteurs sont souvent des chaînes dans les réponses ; un nombre JSON est aussi accepté.</summary>
    private static long? Counter(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var v)) return null;
        var text = v.ValueKind == JsonValueKind.Number ? v.GetRawText() : v.GetString();
        return long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : null;
    }

    /// <summary>
    /// Décalage de la page suivante, lu dans le lien de page suivante. On ne suit pas le lien lui-même : il peut porter un
    /// nom de serveur interne, et surtout y envoyer les identifiants à un hôte non configuré serait une fuite.
    /// </summary>
    private static int? NextOffset(JsonElement root, string field, string offsetParam)
    {
        if (!root.TryGetProperty(field, out var next) || next.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(next.GetString())) return null;
        var match = Regex.Match(next.GetString()!, $@"[?&]{Regex.Escape(offsetParam)}=(\d+)");
        if (match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var offset)) return offset;
        throw new SourceAccessException("Réponse de la source illisible : lien de page suivante non reconnu.");
    }

    /// <summary>
    /// Élément → colonnes : objets imbriqués en colonnes pointées. Le champ identifiant donne la colonne « @id » (dernier
    /// segment quand c'est un lien) ; les champs du même nom dans les objets imbriqués, des liens, sont ignorés.
    /// </summary>
    private static Dictionary<string, string?> Flatten(JsonElement record, string? idField)
    {
        var row = new Dictionary<string, string?>();
        if (idField is not null && record.TryGetProperty(idField, out var id) && id.ValueKind is JsonValueKind.String or JsonValueKind.Number)
            row[IdColumn] = LastSegment(id.ValueKind == JsonValueKind.Number ? id.GetRawText() : id.GetString()!);
        AddProperties(record, "", row, idField);
        return row;
    }

    private static void AddProperties(JsonElement element, string prefix, Dictionary<string, string?> row, string? idField)
    {
        foreach (var p in element.EnumerateObject())
        {
            if (p.Name == idField) continue;
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
            if (p.Value.ValueKind == JsonValueKind.Object) { row.Remove(name); AddProperties(p.Value, name + ".", row, idField); }
        }
    }

    private static string? LastSegment(string value)
    {
        var path = value.Split('?', '#')[0].TrimEnd('/');
        var segment = path[(path.LastIndexOf('/') + 1)..];
        return segment.Length > 0 ? segment : null;
    }
}
