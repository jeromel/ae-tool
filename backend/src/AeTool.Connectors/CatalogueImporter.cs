using AeTool.Core;

namespace AeTool.Connectors;

public sealed record CatalogueCandidate(string Id, string Name, string? TypeLabel);

/// <summary>Une ligne du catalogue à rapprocher : application, objet de données, flux technique ou flux applicatif.</summary>
public sealed class CatalogueItem
{
    public const string Link = "lier";
    public const string Create = "créer";
    public const string Ignore = "ignorer";

    /// <summary>Code catalogue (APP-001, DO-003, FA-012…) : il sert de clé aux décisions.</summary>
    public string Code { get; set; } = "";
    /// <summary>application | donnee | flux-technique | flux</summary>
    public string Kind { get; set; } = "";
    public string Label { get; set; } = "";
    /// <summary>Pour un flux : « émetteur → récepteur ».</summary>
    public string? Detail { get; set; }
    public string Proposal { get; set; } = Create;
    public string? TargetId { get; set; }
    public string Reason { get; set; } = "";
    public List<CatalogueCandidate> Candidates { get; set; } = new();
}

public sealed class CatalogueAnalysis
{
    public string File { get; set; } = "";
    public Dictionary<string, int> Sheets { get; set; } = new();
    public List<CatalogueItem> Items { get; set; } = new();
    public List<Anomaly> Anomalies { get; set; } = new();
}

public sealed class CatalogueDecision
{
    public string Action { get; set; } = CatalogueItem.Create;
    public string? TargetId { get; set; }
}

/// <summary>
/// Import du catalogue des données et des flux (classeur Excel) : applications, objets de données et leurs attributs,
/// flux applicatifs, flux techniques et traçabilité entre les deux. En deux temps, comme un dessin draw.io : l'analyse
/// rapproche chaque ligne du référentiel (par code, puis par nom) sans rien écrire ; l'import applique les choix.
/// Rejouable : un code déjà connu met à jour l'objet ou la relation qui le porte.
/// </summary>
public static class CatalogueImporter
{
    public const string SourceTag = "catalogue";

    private const string SheetApps = "Applications";
    private const string SheetData = "Objets de données";
    private const string SheetAttributes = "Attributs";
    private const string SheetFlows = "Flux applicatifs";
    private const string SheetTechnical = "Flux techniques";
    private const string SheetTrace = "Traçabilité FA-FT";
    private const string SheetLists = "Listes";

    private sealed class Sheet
    {
        public string Name = "";
        public string[] Header = Array.Empty<string>();
        public List<string?[]> Rows = new();
        public int Col(string name) => Array.FindIndex(Header, h => string.Equals(h, name, StringComparison.OrdinalIgnoreCase));
        public string? Cell(string?[] row, string name) => Cell(row, Col(name));
        public static string? Cell(string?[] row, int col) => col >= 0 && col < row.Length && !string.IsNullOrWhiteSpace(row[col]) ? row[col]!.Trim() : null;
    }

    private sealed class Book
    {
        public readonly Dictionary<string, Sheet> Sheets = new();
        public Sheet? this[string name] => Sheets.GetValueOrDefault(name);
    }

    private static Book Read(Stream xlsx, List<Anomaly> anomalies)
    {
        var raw = XlsxReader.Read(xlsx);
        var book = new Book();
        (string Name, string Key)[] wanted =
        {
            (SheetApps, "Code"), (SheetData, "Code"), (SheetAttributes, "Code objet"), (SheetFlows, "Code"),
            (SheetTechnical, "Code"), (SheetTrace, "Flux applicatif"),
        };
        foreach (var (name, key) in wanted)
        {
            var sheetName = raw.Keys.FirstOrDefault(k => string.Equals(k.Trim(), name, StringComparison.OrdinalIgnoreCase));
            if (sheetName is null)
            {
                anomalies.Add(new Anomaly(name is SheetApps or SheetData or SheetFlows ? "erreur" : "avertissement", "feuille-absente",
                    $"Onglet « {name} » absent du classeur : non repris."));
                continue;
            }
            var rows = raw[sheetName];
            var headerIndex = rows.FindIndex(r => r.Length > 0 && string.Equals(r[0]?.Trim(), key, StringComparison.OrdinalIgnoreCase));
            if (headerIndex < 0) { anomalies.Add(new Anomaly("erreur", "entete-absente", $"Onglet « {name} » : ligne d'en-tête (première cellule « {key} ») introuvable.")); continue; }
            book.Sheets[name] = new Sheet
            {
                Name = name, Header = rows[headerIndex].Select(h => h?.Trim() ?? "").ToArray(),
                Rows = rows.Skip(headerIndex + 1).Where(r => r.Any(c => !string.IsNullOrWhiteSpace(c))).ToList(),
            };
        }
        if (raw.Keys.FirstOrDefault(k => k.Trim() == SheetLists) is { } lists)
            book.Sheets[SheetLists] = new Sheet { Name = SheetLists, Header = raw[lists].FirstOrDefault()?.Select(h => h?.Trim() ?? "").ToArray() ?? Array.Empty<string>(), Rows = raw[lists].Skip(1).ToList() };
        return book;
    }

    // ------------------------------------------------------------ Rapprochement

    private static readonly HashSet<string> Stop = new() { "de", "des", "du", "la", "le", "les", "et", "d", "l", "en", "au", "aux", "a" };

    private static HashSet<string> Tokens(string s) => Text.Key(s).Split(' ').Where(t => t.Length > 1 && !Stop.Contains(t)).ToHashSet();

    private static double Similarity(string a, string b)
    {
        var x = Tokens(a); var y = Tokens(b);
        if (x.Count == 0 || y.Count == 0) return 0;
        return (double)x.Intersect(y).Count() / x.Union(y).Count();
    }

    private static void Match(CatalogueItem item, IEnumerable<ArchObject> pool, Metamodel meta, string what, string? preferredType = null)
    {
        // À nom égal, le type sous lequel l'objet serait créé passe devant (objet de données plutôt qu'objet métier).
        var list = pool.OrderByDescending(o => o.TypeKey == preferredType).ToList();
        CatalogueCandidate C(ArchObject o) => new(o.Id, o.Name, meta.Type(o.TypeKey)?.Label);
        var byCode = list.Where(o => string.Equals(o.Get(CatalogueModel.Code), item.Code, StringComparison.OrdinalIgnoreCase)).ToList();
        if (byCode.Count > 0)
        {
            item.Proposal = CatalogueItem.Link; item.TargetId = byCode[0].Id; item.Candidates = byCode.Select(C).ToList();
            item.Reason = byCode.Count == 1 ? "même code catalogue" : $"{byCode.Count} objets portent ce code : le premier est proposé";
            return;
        }
        var key = Text.Key(item.Label);
        var byName = list.Where(o => Text.Key(o.Name) == key).ToList();
        if (byName.Count > 0)
        {
            item.Proposal = CatalogueItem.Link; item.TargetId = byName[0].Id; item.Candidates = byName.Select(C).ToList();
            item.Reason = byName.Count == 1 ? "même nom" : $"{byName.Count} {what} portent ce nom : à choisir";
            return;
        }
        var close = list.Select(o => (o, s: Similarity(item.Label, o.Name))).Where(x => x.s >= 0.25).OrderByDescending(x => x.s).Take(6).Select(x => x.o).ToList();
        item.Proposal = CatalogueItem.Create;
        item.Candidates = close.Select(C).ToList();
        item.Reason = close.Count > 0 ? $"aucun objet de même nom ; noms proches à vérifier ({string.Join(", ", close.Take(3).Select(o => $"« {o.Name} »"))})"
            : "absent du référentiel";
    }

    private static IEnumerable<ArchObject> WithRole(Repository repo, string role)
    {
        var types = repo.Metamodel.TypeKeysWith(role);
        return repo.ActiveObjects.Where(o => types.Contains(o.TypeKey));
    }

    public static CatalogueAnalysis Analyse(Stream xlsx, string fileName, Repository repo)
    {
        var analysis = new CatalogueAnalysis { File = fileName };
        Book book;
        try { book = Read(xlsx, analysis.Anomalies); }
        catch (Exception e) when (e is InvalidDataException or System.Xml.XmlException or IOException)
        {
            throw new InvalidDataException($"Classeur illisible : {e.Message}");
        }
        Build(book, repo, analysis);
        return analysis;
    }

    private static void Build(Book book, Repository repo, CatalogueAnalysis analysis)
    {
        var meta = repo.Metamodel;
        foreach (var s in book.Sheets.Values.Where(s => s.Name != SheetLists)) analysis.Sheets[s.Name] = s.Rows.Count;
        var apps = WithRole(repo, Roles.Application).ToList();
        void Entities(string sheetName, string nameColumn, string kind, IEnumerable<ArchObject> pool, string what, string? preferred = null)
        {
            if (book[sheetName] is not { } sheet) return;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in sheet.Rows)
            {
                var code = sheet.Cell(row, "Code");
                var name = sheet.Cell(row, nameColumn);
                if (code is null) { analysis.Anomalies.Add(new Anomaly("erreur", "code-absent", $"Onglet « {sheetName} » : ligne sans code ; ignorée.", name)); continue; }
                if (name is null) { analysis.Anomalies.Add(new Anomaly("erreur", "nom-absent", $"Ligne sans nom ; ignorée.", code)); continue; }
                if (!seen.Add(code)) { analysis.Anomalies.Add(new Anomaly("erreur", "code-duplique", $"Code présent sur plusieurs lignes ; seule la première est reprise.", code)); continue; }
                var item = new CatalogueItem { Code = code, Kind = kind, Label = name };
                Match(item, pool, meta, what, preferred);
                analysis.Items.Add(item);
            }
        }
        Entities(SheetApps, "Nom", "application", apps, "applications");
        Entities(SheetData, "Objet de données", "donnee", WithRole(repo, Roles.Data), "objets de données",
            meta.Type("objet-donnees") is { } dt && meta.IsA(dt.Key, Roles.Data) ? dt.Key : meta.FirstTypeWith(Roles.Data)?.Key);
        Entities(SheetTechnical, "Nom", "flux-technique", WithRole(repo, Roles.TechnicalFlow), "flux techniques");

        // Flux applicatifs : rapprochés d'une relation de flux existante entre les deux mêmes applications.
        if (book[SheetFlows] is not { } flows) return;
        var appItems = analysis.Items.Where(i => i.Kind == "application").ToDictionary(i => i.Code, StringComparer.OrdinalIgnoreCase);
        // Extrémité hors de l'onglet Applications (« EXTERNE ») : un destinataire externe, à lier à un acteur existant ou à créer.
        var actorTypes = meta.Transcoding.Elements.Where(r => r.ArchimateType == "BusinessActor" && r.Principal).Select(r => r.TypeKey).ToHashSet();
        foreach (var code in flows.Rows.SelectMany(r => new[] { flows.Cell(r, "Code app. source"), flows.Cell(r, "Code app. cible") })
                     .Where(c => c is not null && !appItems.ContainsKey(c)).Select(c => c!).Distinct(StringComparer.OrdinalIgnoreCase).ToList())
        {
            var item = new CatalogueItem { Code = code, Kind = "externe", Label = $"Destinataire externe ({code})" };
            var pool = repo.ActiveObjects.Where(o => actorTypes.Contains(o.TypeKey) || apps.Contains(o)).ToList();
            Match(item, pool, meta, "objets");
            if (item.Proposal == CatalogueItem.Create)
            {
                item.Candidates = repo.ActiveObjects.Where(o => actorTypes.Contains(o.TypeKey)).OrderBy(o => o.Name).Take(12)
                    .Select(o => new CatalogueCandidate(o.Id, o.Name, meta.Type(o.TypeKey)?.Label)).ToList();
                item.Reason = "code absent de l'onglet Applications : système ou partenaire hors du catalogue, à lier à un acteur ou à créer";
            }
            analysis.Items.Add(item);
            appItems[code] = item;
        }
        var flowTypes = meta.RelTypeKeysWith(Roles.Flow);
        var seenFlows = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new List<(CatalogueItem Item, List<Relationship>? Existing)>();
        foreach (var row in flows.Rows)
        {
            var code = flows.Cell(row, "Code");
            var name = flows.Cell(row, "Nom du flux") ?? code;
            if (code is null) continue;
            if (!seenFlows.Add(code)) { analysis.Anomalies.Add(new Anomaly("erreur", "code-duplique", "Code de flux présent sur plusieurs lignes ; seule la première est reprise.", code)); continue; }
            var from = flows.Cell(row, "Code app. source");
            var to = flows.Cell(row, "Code app. cible");
            if (from is null || to is null)
            {
                analysis.Anomalies.Add(new Anomaly("erreur", "flux-incomplet",
                    $"« {name} » : application {(from is null ? "source" : "cible")} non renseignée ; le flux ne peut pas être repris tant qu'elle manque.", code));
                continue;
            }
            if (!appItems.TryGetValue(from, out var src) || !appItems.TryGetValue(to, out var dst))
            {
                analysis.Anomalies.Add(new Anomaly("erreur", "application-inconnue", $"« {name} » : application {(appItems.ContainsKey(from) ? to : from)} absente de l'onglet Applications ; flux non repris.", code));
                continue;
            }
            var item = new CatalogueItem { Code = code, Kind = "flux", Label = name!, Detail = $"{src.Label} → {dst.Label}" };
            var existing = src.Proposal == CatalogueItem.Link && dst.Proposal == CatalogueItem.Link
                ? repo.ActiveRelationships.Where(r => flowTypes.Contains(r.TypeKey) && r.SourceId == src.TargetId && r.TargetId == dst.TargetId).ToList()
                : null;
            pending.Add((item, existing));
            analysis.Items.Add(item);
        }

        // Une relation existante ne reçoit qu'un flux du catalogue : même code d'abord, puis même nom, puis seule relation restante.
        var taken = new HashSet<string>();
        void Take(CatalogueItem item, Relationship r, string reason) { item.Proposal = CatalogueItem.Link; item.TargetId = r.Id; item.Reason = reason; taken.Add(r.Id); }
        foreach (var (item, existing) in pending.Where(p => p.Existing is not null))
            if (existing!.FirstOrDefault(r => string.Equals(Prop(r, CatalogueModel.Code), item.Code, StringComparison.OrdinalIgnoreCase)) is { } same) Take(item, same, "même code catalogue");
        foreach (var (item, existing) in pending.Where(p => p.Existing is not null && p.Item.TargetId is null))
            if (existing!.FirstOrDefault(r => !taken.Contains(r.Id) && Prop(r, CatalogueModel.Code) is null && Text.Key(r.Name) == Text.Key(item.Label)) is { } named) Take(item, named, "même nom");
        // Plusieurs flux du catalogue pour une seule relation existante : elle va au nom le plus proche, les autres seront créés.
        foreach (var group in pending.Where(p => p.Existing is not null && p.Item.TargetId is null).GroupBy(p => p.Item.Detail))
        {
            var items = group.ToList();
            if (items.Count < 2) continue;
            foreach (var r in items[0].Existing!.Where(r => Prop(r, CatalogueModel.Code) is null && !taken.Contains(r.Id)))
            {
                var best = items.Where(p => p.Item.TargetId is null).OrderByDescending(p => Similarity(p.Item.Label, r.Name ?? "")).FirstOrDefault();
                if (best.Item is not null) Take(best.Item, r, "nom le plus proche parmi les flux du catalogue entre ces deux applications");
            }
        }
        foreach (var (item, existing) in pending)
        {
            if (existing is null) { item.Reason = "une des deux applications sera créée : le flux aussi"; continue; }
            var free = existing.Where(r => Prop(r, CatalogueModel.Code) is null && !taken.Contains(r.Id)).ToList();
            item.Candidates = existing.Where(r => Prop(r, CatalogueModel.Code) is null || r.Id == item.TargetId)
                .Select(r => new CatalogueCandidate(r.Id, r.Name ?? item.Detail ?? "", null)).ToList();
            if (item.TargetId is not null) continue;
            var siblings = pending.Count(p => p.Existing is not null && p.Item.TargetId is null && p.Item.Detail == item.Detail);
            if (free.Count == 1 && siblings == 1) Take(item, free[0], "seul flux entre ces deux applications");
            else if (free.Count > 0)
                item.Reason = $"{free.Count} flux existant(s) et {siblings} flux du catalogue entre ces deux applications : à lier ou à créer";
            else if (existing.Count == 0) item.Reason = "aucun flux entre ces deux applications";
            else
            {
                var others = pending.Where(p => p.Item != item && p.Item.TargetId is { } t && existing.Any(r => r.Id == t)).Select(p => p.Item.Code).ToList();
                item.Reason = others.Count > 0
                    ? $"le flux existant entre ces applications est rapproché de {string.Join(", ", others)} : celui-ci sera créé à part"
                    : "les flux existants entre ces applications portent déjà un autre code : créé à part";
            }
        }
    }

    private static string? Prop(Relationship r, string key) => r.Properties.FirstOrDefault(p => p.Key == key)?.Value is { Length: > 0 } v ? v : null;

    private static void Set(Relationship r, string key, string? value)
    {
        // Une cellule vide ne fait rien : une valeur venue d'Archi ou saisie dans l'outil n'est pas effacée.
        if (value is null) return;
        var p = r.Properties.FirstOrDefault(x => x.Key == key);
        if (p is null) r.Properties.Add(new PropertyValue { Key = key, Value = value });
        else p.Value = value;
    }

    // ------------------------------------------------------------ Application

    public static ImportReport Apply(Stream xlsx, string fileName, Repository repo, string user, IReadOnlyDictionary<string, CatalogueDecision>? decisions)
    {
        var report = new ImportReport { Source = $"Catalogue des données et des flux ({fileName})" };
        var analysis = new CatalogueAnalysis { File = fileName };
        Book book;
        try { book = Read(xlsx, analysis.Anomalies); }
        catch (Exception e) { report.Error("xlsx-invalide", $"Classeur illisible : {e.Message}"); return report; }
        CatalogueModel.Ensure(repo.Metamodel);
        CatalogueModel.EnsureProperties(repo.Metamodel);
        Build(book, repo, analysis);
        report.Anomalies.AddRange(analysis.Anomalies);

        var meta = repo.Metamodel;
        var now = DateTime.UtcNow;
        CatalogueDecision Choice(CatalogueItem i) =>
            decisions is not null && decisions.TryGetValue(i.Code, out var d) ? d : new CatalogueDecision { Action = i.Proposal, TargetId = i.TargetId };
        var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // code → identifiant d'objet ou de relation
        var appByName = new Dictionary<string, string>();                               // nom catalogue (clé) → objet

        string? TypeFor(string kind) => kind switch
        {
            "application" => meta.FirstTypeWith(Roles.Application)?.Key,
            "donnee" => (meta.Type("objet-donnees") is { } t && meta.IsA(t.Key, Roles.Data) ? t : meta.FirstTypeWith(Roles.Data))?.Key,
            "flux-technique" => meta.FirstTypeWith(Roles.TechnicalFlow)?.Key,
            "externe" => meta.Transcoding.Elements.FirstOrDefault(r => r.ArchimateType == "BusinessActor" && r.Principal && meta.Type(r.TypeKey) is { Archived: false })?.TypeKey
                         ?? meta.FirstTypeWith(Roles.Application)?.Key,
            _ => null,
        };
        string Category(string kind) => kind switch
        {
            "application" => "Applications", "donnee" => "Objets de données", "externe" => "Destinataires externes", _ => "Flux techniques",
        };

        // 1. Objets : applications, objets de données, flux techniques.
        foreach (var item in analysis.Items.Where(i => i.Kind != "flux"))
        {
            var tally = report.For(Category(item.Kind));
            var choice = Choice(item);
            if (choice.Action == CatalogueItem.Ignore) { report.Notes.Add($"{item.Code} « {item.Label} » : ignoré à votre demande."); continue; }
            ArchObject? obj = null;
            if (choice.Action == CatalogueItem.Link)
            {
                if (choice.TargetId is not null && repo.Objects.TryGetValue(choice.TargetId, out var found) && !found.Archived) obj = found;
                else { report.Error("objet-introuvable", $"« {item.Label} » : l'objet choisi n'existe pas ; ligne non reprise.", item.Code); continue; }
            }
            var added = obj is null;
            if (obj is null)
            {
                var typeKey = TypeFor(item.Kind);
                if (typeKey is null) { report.Error("type-absent", $"Aucun type du métamodèle ne convient pour « {item.Label} » ; ligne non reprise.", item.Code); continue; }
                obj = new ArchObject
                {
                    Id = "id-" + Guid.NewGuid().ToString("N"), TypeKey = typeKey, Name = item.Label, Source = SourceTag,
                    ValidationStatus = Validation.Proposed, ProposedBy = user, ProposedAt = now, UpdatedAt = now, UpdatedBy = user,
                };
                if (item.Kind == "flux-technique") obj.Set(CatalogueModel.Subtype, CatalogueModel.TechnicalFlowSubtype);
                repo.Objects[obj.Id] = obj;
                repo.Log(user, "Objet créé depuis le catalogue", JournalKinds.Object, obj.Id, null, $"{item.Code} « {obj.Name} », proposé à la validation");
            }
            var before = System.Text.Json.JsonSerializer.Serialize(obj.Properties) + obj.Documentation;
            resolved[item.Code] = obj.Id;
            if (item.Kind is "application") appByName[Text.Key(item.Label)] = obj.Id;
            Fill(book, item, obj, meta);
            var changed = before != System.Text.Json.JsonSerializer.Serialize(obj.Properties) + obj.Documentation;
            if (changed && !added) { obj.UpdatedAt = now; obj.UpdatedBy = user; }
            if (added) tally.Added++; else if (changed) tally.Updated++; else tally.Unchanged++;
        }

        string? AppId(string? name)
        {
            if (name is null) return null;
            if (appByName.TryGetValue(Text.Key(name), out var id)) return id;
            var existing = WithRole(repo, Roles.Application).FirstOrDefault(o => Text.Key(o.Name) == Text.Key(name));
            return existing?.Id;
        }
        var relTally = report.For("Relations");
        Relationship Ensure(string typeKey, string from, string to, Func<Relationship, bool>? any = null)
        {
            var found = repo.ActiveRelationships.FirstOrDefault(r => any is not null ? any(r) : r.TypeKey == typeKey && r.SourceId == from && r.TargetId == to);
            if (found is not null) { relTally.Unchanged++; return found; }
            var r = new Relationship { Id = "id-" + Guid.NewGuid().ToString("N"), TypeKey = typeKey, SourceId = from, TargetId = to, Source = SourceTag };
            repo.Relationships[r.Id] = r;
            relTally.Added++;
            return r;
        }
        var access = RelTypeOf(meta, "Access") ?? "acces";
        var association = RelTypeOf(meta, "Association") ?? meta.Transcoding.UnmappedRelationTypeKey;
        var flowType = meta.RelationshipTypes.FirstOrDefault(t => t.Roles.Contains(Roles.Flow) && !t.Archived)?.Key ?? "flux";
        var connected = new HashSet<string>(); // couples objet|objet déjà reliés, quel que soit le sens
        bool Linked(string a, string b) => repo.ActiveRelationships.Any(r => (r.SourceId == a && r.TargetId == b) || (r.SourceId == b && r.TargetId == a));

        // 2. Rôles vis-à-vis des données : maître, relais, consommateurs → relations d'accès portant le rôle.
        if (book[SheetData] is { } data)
            foreach (var row in data.Rows)
            {
                var code = data.Cell(row, "Code");
                if (code is null || !resolved.TryGetValue(code, out var dataId)) continue;
                var roles = new Dictionary<string, string>();
                void Note(string? names, string role)
                {
                    foreach (var n in (names ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        var app = AppId(n);
                        if (app is null) { report.Warn("application-inconnue", $"« {n} » ({role.ToLowerInvariant()}) n'est pas une application du catalogue ni du référentiel ; rôle non repris.", code); continue; }
                        roles.TryAdd(app, role); // l'ordre d'appel donne la priorité : maître, puis relais, puis consommateur
                    }
                }
                Note(data.Cell(row, "Système maître"), CatalogueModel.RoleMaster);
                Note(data.Cell(row, "Systèmes relais"), CatalogueModel.RoleRelay);
                Note(data.Cell(row, "Consommateurs"), CatalogueModel.RoleConsumer);
                foreach (var (app, role) in roles)
                {
                    var r = Ensure(access, app, dataId, r => r.TypeKey == access && r.SourceId == app && r.TargetId == dataId);
                    if (r.Source == SourceTag && r.AccessType is null) r.AccessType = role == CatalogueModel.RoleConsumer ? "read" : "write";
                    Set(r, CatalogueModel.DataRole, role);
                }
            }

        // 3. Flux techniques : application concernée.
        if (book[SheetTechnical] is { } technical)
            foreach (var row in technical.Rows)
            {
                var code = technical.Cell(row, "Code");
                if (code is null || !resolved.TryGetValue(code, out var ftId)) continue;
                var appName = technical.Cell(row, "Application concernée");
                var app = AppId(appName);
                if (appName is not null && app is null) { report.Warn("application-inconnue", $"« {appName} » (application concernée) n'est pas une application du catalogue ni du référentiel ; hébergement de l'étape inconnu.", code); continue; }
                if (app is not null && !Linked(ftId, app)) Ensure(association, ftId, app);
            }

        // 4. Flux applicatifs.
        var flowTally = report.For("Flux applicatifs");
        if (book[SheetFlows] is { } flows)
        {
            var rows = flows.Rows.Where(r => flows.Cell(r, "Code") is not null).GroupBy(r => flows.Cell(r, "Code")!, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            foreach (var item in analysis.Items.Where(i => i.Kind == "flux"))
            {
                var row = rows[item.Code];
                var choice = Choice(item);
                if (choice.Action == CatalogueItem.Ignore) continue;
                var from = resolved.GetValueOrDefault(flows.Cell(row, "Code app. source")!);
                var to = resolved.GetValueOrDefault(flows.Cell(row, "Code app. cible")!);
                if (from is null || to is null) { report.Warn("extremite-ignoree", $"« {item.Label} » : une des applications n'a pas été reprise ; flux non repris.", item.Code); continue; }
                Relationship? flow = null;
                if (choice.Action == CatalogueItem.Link && choice.TargetId is not null && repo.Relationships.TryGetValue(choice.TargetId, out var existing) && !existing.Archived) flow = existing;
                var added = flow is null;
                if (flow is null)
                {
                    flow = new Relationship { Id = "id-" + Guid.NewGuid().ToString("N"), TypeKey = flowType, SourceId = from, TargetId = to, Name = item.Label, Source = SourceTag };
                    repo.Relationships[flow.Id] = flow;
                }
                var before = System.Text.Json.JsonSerializer.Serialize(flow.Properties) + flow.Name;
                if (string.IsNullOrWhiteSpace(flow.Name)) flow.Name = item.Label;
                Set(flow, CatalogueModel.Code, item.Code);
                Set(flow, CatalogueModel.FlowKind, flows.Cell(row, "Type"));
                Set(flow, CatalogueModel.FlowFrequency, flows.Cell(row, "Périodicité"));
                Set(flow, CatalogueModel.FlowMode, flows.Cell(row, "Mode"));
                Set(flow, CatalogueModel.FlowOrder, flows.Cell(row, "Ordre"));
                Set(flow, CatalogueModel.FlowDependencies, flows.Cell(row, "Dépendances"));
                Set(flow, CatalogueModel.BusinessOwner, flows.Cell(row, "Responsable métier"));
                Set(flow, CatalogueModel.TechnicalOwner, flows.Cell(row, "Responsable technique"));
                Set(flow, CatalogueModel.Lifecycle, flows.Cell(row, "Cycle de vie"));
                Set(flow, CatalogueModel.InfoStatus, flows.Cell(row, "Statut info"));
                resolved[item.Code] = flow.Id;
                if (added) flowTally.Added++;
                else if (before != System.Text.Json.JsonSerializer.Serialize(flow.Properties) + flow.Name) flowTally.Updated++;
                else flowTally.Unchanged++;
                // Donnée transportée : une relation entre l'objet de données et le flux, comme Archi l'autorise.
                if (flows.Cell(row, "Objet de données") is { } dataCode)
                {
                    if (resolved.TryGetValue(dataCode, out var dataId)) { if (!Linked(dataId, flow.Id)) Ensure(association, dataId, flow.Id); }
                    else report.Warn("donnee-inconnue", $"« {item.Label} » : objet de données {dataCode} non repris ; donnée transportée non reliée.", item.Code);
                }
            }
        }

        // 5. Traçabilité : chaque étape technique réalise un flux applicatif, dans l'ordre d'exécution.
        if (book[SheetTrace] is { } trace)
        {
            var traceTally = report.For("Traçabilité");
            foreach (var row in trace.Rows)
            {
                var fa = trace.Cell(row, "Flux applicatif");
                var ft = trace.Cell(row, "Flux technique");
                if (fa is null || ft is null) continue;
                if (!resolved.TryGetValue(fa, out var flowId) || !resolved.TryGetValue(ft, out var ftId) || !repo.Relationships.ContainsKey(flowId))
                {
                    report.Warn("trace-incomplete", $"Lien {fa} → {ft} : un des deux flux n'a pas été repris.", $"{fa}/{ft}");
                    continue;
                }
                var link = repo.ActiveRelationships.FirstOrDefault(r => r.SourceId == ftId && r.TargetId == flowId);
                var added = link is null;
                link ??= Ensure(association, ftId, flowId);
                var before = System.Text.Json.JsonSerializer.Serialize(link.Properties);
                Set(link, CatalogueModel.TraceOrder, trace.Cell(row, "Ordre d'exécution"));
                Set(link, CatalogueModel.InfoStatus, trace.Cell(row, "Nature du lien"));
                if (added) traceTally.Added++;
                else if (before != System.Text.Json.JsonSerializer.Serialize(link.Properties)) traceTally.Updated++;
                else traceTally.Unchanged++;
            }
        }

        // 6. Attributs des objets de données : remplacés en bloc pour chaque objet présent dans l'onglet.
        if (book[SheetAttributes] is { } attrs)
        {
            var targetCol = Array.FindIndex(attrs.Header, h => h.StartsWith("Champ ", StringComparison.OrdinalIgnoreCase) && !h.Equals("Champ source", StringComparison.OrdinalIgnoreCase));
            var targetSystem = targetCol >= 0 ? attrs.Header[targetCol]["Champ ".Length..].Trim() : null;
            var byObject = new Dictionary<string, List<DataAttribute>>();
            foreach (var row in attrs.Rows)
            {
                var code = attrs.Cell(row, "Code objet");
                var name = attrs.Cell(row, "Attribut métier");
                if (code is null || name is null) continue;
                if (!resolved.TryGetValue(code, out var dataId)) { report.Warn("donnee-inconnue", $"Attribut « {name} » : objet de données {code} non repris.", code); continue; }
                if (!byObject.TryGetValue(dataId, out var list)) byObject[dataId] = list = new List<DataAttribute>();
                list.Add(new DataAttribute
                {
                    Name = name, Nature = attrs.Cell(row, "Nature"), SourceApplication = attrs.Cell(row, "Application source"),
                    SourceTable = attrs.Cell(row, "Table / objet source"), SourceField = attrs.Cell(row, "Champ source"),
                    TargetField = Sheet.Cell(row, targetCol), TargetSystem = targetSystem,
                    Personal = attrs.Cell(row, "Donnée personnelle"), Sensitivity = attrs.Cell(row, "Sensibilité"),
                    Status = attrs.Cell(row, "Statut"), Comment = attrs.Cell(row, "Commentaire"),
                });
            }
            var tally = report.For("Attributs");
            foreach (var (id, list) in byObject)
            {
                var previous = repo.DataAttributes.GetValueOrDefault(id);
                var same = previous is not null && System.Text.Json.JsonSerializer.Serialize(previous) == System.Text.Json.JsonSerializer.Serialize(list);
                if (previous is null) tally.Added += list.Count; else if (same) tally.Unchanged += list.Count; else tally.Updated += list.Count;
                repo.DataAttributes[id] = list;
            }
        }

        // 7. Listes de référence (sensibilité, statuts…) : servent aux contrôles de cohérence.
        if (book[SheetLists] is { } lists)
            for (var c = 0; c < lists.Header.Length; c++)
            {
                if (string.IsNullOrWhiteSpace(lists.Header[c])) continue;
                var values = lists.Rows.Select(r => Sheet.Cell(r, c)).Where(v => v is not null).Select(v => v!).Distinct().ToList();
                if (values.Count > 0) repo.CatalogueLists[lists.Header[c]] = values;
            }

        repo.Log(user, "Import catalogue des données et des flux", null,
            $"{fileName} : " + string.Join(" ; ", report.Counts.Select(c => $"{c.Key} +{c.Value.Added} ~{c.Value.Updated} ={c.Value.Unchanged}")));
        return report;
    }

    /// <summary>Type de relation du métamodèle dont la règle principale est le concept ArchiMate donné.</summary>
    private static string? RelTypeOf(Metamodel meta, string concept) =>
        meta.Transcoding.Relations.FirstOrDefault(r => r.ArchimateType == concept && r.Principal && meta.RelType(r.TypeKey) is { Archived: false })?.TypeKey;

    /// <summary>Recopie les colonnes descriptives d'une ligne sur l'objet. Une cellule vide laisse la valeur en place.</summary>
    private static void Fill(Book book, CatalogueItem item, ArchObject o, Metamodel meta)
    {
        o.Set(CatalogueModel.Code, item.Code);
        switch (item.Kind)
        {
            case "application" when book[SheetApps] is { } s && Row(s, item.Code) is { } row:
                if (string.IsNullOrWhiteSpace(o.Documentation)) o.Documentation = s.Cell(row, "Description");
                Put(o, CatalogueModel.AppZone, s.Cell(row, "Zone"));
                Put(o, CatalogueModel.BusinessOwner, s.Cell(row, "Propriétaire métier"));
                Put(o, CatalogueModel.TechnicalOwner, s.Cell(row, "Responsable technique"));
                Put(o, CatalogueModel.AppDataRole, s.Cell(row, "Rôle data"));
                Put(o, CatalogueModel.InfoStatus, s.Cell(row, "Statut"));
                break;
            case "donnee" when book[SheetData] is { } s && Row(s, item.Code) is { } row:
                if (string.IsNullOrWhiteSpace(o.Documentation)) o.Documentation = s.Cell(row, "Définition");
                Put(o, CatalogueModel.DataCategory, s.Cell(row, "Catégorie"));
                Put(o, CatalogueModel.DataPivot, s.Cell(row, "Identifiant pivot"));
                Put(o, CatalogueModel.DataOwner, s.Cell(row, "Data owner"));
                Put(o, CatalogueModel.DataSteward, s.Cell(row, "Data steward"));
                Put(o, CatalogueModel.DataSensitivity, s.Cell(row, "Sensibilité"));
                Put(o, CatalogueModel.DataPersonal, s.Cell(row, "Données personnelles"));
                Put(o, CatalogueModel.InfoStatus, s.Cell(row, "Statut"));
                break;
            case "flux-technique" when book[SheetTechnical] is { } s && Row(s, item.Code) is { } row:
                Put(o, CatalogueModel.TechStep, s.Cell(row, "Étape"));
                Put(o, meta.Settings.FlowProtocolKey, s.Cell(row, "Technologie / protocole"));
                Put(o, CatalogueModel.TechFormat, s.Cell(row, "Format"));
                Put(o, CatalogueModel.TechOrchestration, s.Cell(row, "Orchestration"));
                Put(o, CatalogueModel.TechZone, s.Cell(row, "Zone"));
                Put(o, CatalogueModel.TechAccount, s.Cell(row, "Compte de service"));
                Put(o, CatalogueModel.TechMonitoring, s.Cell(row, "Supervision / alerte"));
                Put(o, CatalogueModel.Lifecycle, s.Cell(row, "Cycle de vie"));
                Put(o, CatalogueModel.InfoStatus, s.Cell(row, "Statut info"));
                break;
        }
    }

    private static string?[]? Row(Sheet s, string code) => s.Rows.FirstOrDefault(r => string.Equals(s.Cell(r, "Code"), code, StringComparison.OrdinalIgnoreCase));

    private static void Put(ArchObject o, string key, string? value)
    {
        if (value is not null) o.Set(key, value);
    }
}
