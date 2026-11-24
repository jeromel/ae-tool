using System.Text;

namespace AeTool.Core;

// Rapprochement du référentiel avec une source externe (CMDB, outil de projets, export quelconque).
// Le moteur ne connaît que des lignes « colonne → valeur » : chaque source a son adaptateur mince qui les produit.
// Principe : l'outil lit, compare et propose ; il n'écrit jamais dans la source.

/// <summary>Valeur de la colonne de la source → type du métamodèle.</summary>
public sealed class TypeMapping
{
    public string SourceValue { get; set; } = "";
    public string TypeKey { get; set; } = "";
}

/// <summary>Colonne de la source → propriété du référentiel.</summary>
public sealed class FieldMapping
{
    public string SourceField { get; set; } = "";
    public string PropertyKey { get; set; } = "";
    public string? Label { get; set; }
}

public sealed class SourceMapping
{
    /// <summary>Colonne portant l'identifiant stable chez la source.</summary>
    public string IdField { get; set; } = "";
    public string NameField { get; set; } = "";
    /// <summary>Colonne portant la nature de la ligne (catégorie de CI, type de projet…).</summary>
    public string? TypeField { get; set; }
    public List<TypeMapping> Types { get; set; } = new();
    /// <summary>Type retenu quand la colonne de nature est vide ou que sa valeur n'est pas listée.</summary>
    public string? DefaultTypeKey { get; set; }
    public List<FieldMapping> Fields { get; set; } = new();
}

/// <summary>Configuration d'une source externe. Ne contient jamais de secret : celui-ci est gardé à part, chiffré.</summary>
public sealed class SourceProfile
{
    public const string KindFile = "fichier";
    public const string KindApi = "api";

    /// <summary>Identifiant court : « cmdb », « projets »… Sert de clé dans ExternalRefs des objets.</summary>
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public string Kind { get; set; } = KindFile;
    /// <summary>Réglages de l'adaptateur (adresse, compte, ressource…).</summary>
    public Dictionary<string, string> Connection { get; set; } = new();
    public SourceMapping Mapping { get; set; } = new();
    /// <summary>La source fait foi : à l'application, le nom de l'objet lié est aligné sur elle.</summary>
    public bool SourceIsMaster { get; set; }
    /// <summary>Lignes de la source que l'utilisateur a choisi d'ignorer.</summary>
    public List<string> IgnoredIds { get; set; } = new();
    public DateTime? LastRunAt { get; set; }
    public string? LastRunBy { get; set; }
    /// <summary>Comptage des postes de travail par profil de poste (CMDB) : champ pivot et champs de répartition. Null = non utilisé.</summary>
    public WorkstationSettings? Workstations { get; set; }

    /// <summary>Profil de départ pour un export de fichier : colonnes génériques, aucun type choisi.</summary>
    public static SourceProfile FileTemplate() => new()
    {
        Key = "fichier", Label = "Export de fichier (CSV ou Excel)", Kind = KindFile,
        Mapping = new SourceMapping { IdField = "ID", NameField = "Nom" },
    };

    /// <summary>
    /// Profil de départ pour une CMDB lue par son API REST. Les réglages de pagination et les noms de champs sont des
    /// valeurs courantes, à ajuster d'après la documentation de l'API et l'aperçu. Aucun type n'est choisi : il dépend du métamodèle.
    /// </summary>
    public static SourceProfile ApiTemplate() => new()
    {
        Key = "cmdb", Label = "CMDB par API REST", Kind = KindApi, SourceIsMaster = false,
        Connection = new Dictionary<string, string>
        {
            ["url"] = "https://", ["query"] = "", ["pageSize"] = "100", ["pageSizeParam"] = "limit", ["offsetParam"] = "offset",
            ["itemsField"] = "", ["totalField"] = "total", ["nextField"] = "", ["idField"] = "id", ["authMode"] = "bearer", ["login"] = "",
        },
        Mapping = new SourceMapping
        {
            IdField = "@id", NameField = "name",
            Fields =
            {
                new FieldMapping { SourceField = "serial_number", PropertyKey = "cmdb:numero-serie", Label = "Numéro de série" },
                new FieldMapping { SourceField = "asset_tag", PropertyKey = "cmdb:etiquette", Label = "Étiquette d'inventaire" },
                new FieldMapping { SourceField = "install_date", PropertyKey = "cmdb:date-installation", Label = "Date d'installation" },
                new FieldMapping { SourceField = "location.name", PropertyKey = "cmdb:localisation", Label = "Localisation" },
            },
        },
    };
}

/// <summary>Anomalie de lecture d'une source : signalée, jamais corrigée en silence.</summary>
public sealed record SourceIssue(string Severity, string Code, string Message, string? Ref = null);

/// <summary>Ce que produit un adaptateur : des lignes brutes, leurs colonnes et les anomalies de lecture.</summary>
public sealed class SourceTable
{
    public List<string> Columns { get; set; } = new();
    public List<Dictionary<string, string?>> Rows { get; set; } = new();
    public List<SourceIssue> Issues { get; set; } = new();
    /// <summary>Nombre total de lignes chez la source quand elle l'annonce.</summary>
    public long? Total { get; set; }
    /// <summary>Vrai si la lecture s'est arrêtée avant la fin (limite atteinte) : les objets « disparus » ne sont alors pas fiables.</summary>
    public bool Truncated { get; set; }
}

public sealed record SourceRecord(string ExternalId, string Name, string? TypeValue, Dictionary<string, string?> Fields);

public sealed record SourceRecordSet(List<SourceRecord> Records, List<SourceIssue> Issues);

public static class SourceRecords
{
    /// <summary>Lignes brutes → enregistrements du mappage. Une ligne sans identifiant, sans nom ou en double est écartée et signalée.</summary>
    public static SourceRecordSet From(IReadOnlyList<Dictionary<string, string?>> rows, SourceMapping mapping)
    {
        var issues = new List<SourceIssue>();
        WarnMissingColumns(rows, mapping, issues);
        var records = new List<SourceRecord>();
        var seen = new HashSet<string>();
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var id = Cell(row, mapping.IdField);
            var name = Cell(row, mapping.NameField);
            var where = id ?? name ?? $"ligne {i + 1}";
            if (id is null) { issues.Add(new SourceIssue("erreur", "identifiant-absent", "Ligne sans identifiant ; ignorée.", where)); continue; }
            if (name is null) { issues.Add(new SourceIssue("erreur", "nom-absent", "Ligne sans nom ; ignorée.", id)); continue; }
            if (!seen.Add(id)) { issues.Add(new SourceIssue("erreur", "identifiant-duplique", "Identifiant présent sur plusieurs lignes ; seule la première est reprise.", id)); continue; }
            var fields = new Dictionary<string, string?>();
            foreach (var f in mapping.Fields.Where(f => row.ContainsKey(f.SourceField))) fields[f.SourceField] = Cell(row, f.SourceField);
            records.Add(new SourceRecord(id, name, string.IsNullOrWhiteSpace(mapping.TypeField) ? null : Cell(row, mapping.TypeField), fields));
        }
        return new SourceRecordSet(records, issues);
    }

    /// <summary>Colonnes du mappage absentes de toutes les lignes (hors celles que la source omet quand elles sont vides).</summary>
    public static List<string> MissingColumns(IReadOnlyList<Dictionary<string, string?>> rows, SourceMapping mapping)
    {
        var present = rows.SelectMany(r => r.Keys).ToHashSet();
        var wanted = new[] { mapping.IdField, mapping.NameField, mapping.TypeField }.Concat(mapping.Fields.Select(f => f.SourceField));
        return wanted.Where(c => !string.IsNullOrWhiteSpace(c) && !present.Contains(c!)).Select(c => c!).Distinct().ToList();
    }

    private static void WarnMissingColumns(IReadOnlyList<Dictionary<string, string?>> rows, SourceMapping mapping, List<SourceIssue> issues)
    {
        if (rows.Count == 0) return;
        foreach (var column in MissingColumns(rows, mapping))
        {
            var essential = column == mapping.IdField || column == mapping.NameField;
            issues.Add(new SourceIssue(essential ? "erreur" : "avertissement", "colonne-absente",
                $"Colonne « {column} » absente de la source" + (essential ? " : aucune ligne ne peut être reprise." : " : non reprise."), column));
        }
    }

    private static string? Cell(Dictionary<string, string?> row, string? column) =>
        column is not null && row.TryGetValue(column, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;
}

public static class LineStatus
{
    public const string Linked = "lié";
    public const string Proposed = "proposé";
    public const string Ambiguous = "ambigu";
    public const string New = "nouveau";
    public const string Ignored = "ignoré";
    public const string Invalid = "invalide";
    public static readonly string[] All = { Linked, Proposed, Ambiguous, New, Ignored, Invalid };
}

public static class DecisionAction
{
    public const string Link = "lier";
    public const string Create = "creer";
    public const string Ignore = "ignorer";
    public const string Unlink = "delier";
    public static readonly string[] All = { Link, Create, Ignore, Unlink };
}

public sealed record Candidate(string Id, string Name, string TypeKey);
public sealed record MissingObject(string Id, string Name, string TypeKey, string ExternalId);
/// <summary>Écart entre le référentiel et la source. Field vaut « name », « type » ou la clé de la propriété.</summary>
public sealed record Difference(string Field, string Label, string? RepositoryValue, string? SourceValue);

public sealed class ReconciliationLine
{
    public string ExternalId { get; set; } = "";
    public string Name { get; set; } = "";
    public string? TypeValue { get; set; }
    public string Status { get; set; } = "";
    /// <summary>Type du métamodèle résolu pour la ligne ; vide si introuvable.</summary>
    public string? TypeKey { get; set; }
    /// <summary>Objet lié (statut lié) ou proposé (statut proposé).</summary>
    public string? ObjectId { get; set; }
    public string? ObjectName { get; set; }
    public List<Candidate> Candidates { get; set; } = new();
    public List<Difference> Differences { get; set; } = new();
    public string? Message { get; set; }
}

public sealed class ReconciliationReport
{
    public string ProfileKey { get; set; } = "";
    public DateTime At { get; set; } = DateTime.UtcNow;
    public Dictionary<string, int> Counts { get; set; } = new();
    public List<ReconciliationLine> Lines { get; set; } = new();
    public List<MissingObject> Missing { get; set; } = new();
    public int UnlinkedCount { get; set; }
    public List<Candidate> Unlinked { get; set; } = new();
    public List<SourceIssue> Issues { get; set; } = new();
    public List<string> Notes { get; set; } = new();
}

public sealed class Decision
{
    public string ExternalId { get; set; } = "";
    /// <summary>lier | creer | ignorer | delier.</summary>
    public string Action { get; set; } = "";
    /// <summary>Pour « lier » : objet à relier ; à défaut, l'objet proposé par le rapprochement.</summary>
    public string? ObjectId { get; set; }
}

public sealed class ApplyRequest
{
    public List<Decision> Decisions { get; set; } = new();
    /// <summary>Lie toutes les lignes « proposé » qui n'ont pas reçu de décision.</summary>
    public bool AcceptProposals { get; set; }
    /// <summary>Recopie sur les objets liés les propriétés reprises (et le nom si la source fait foi).</summary>
    public bool SyncLinked { get; set; } = true;
}

public sealed class ApplyResult
{
    public int Linked { get; set; }
    public int Created { get; set; }
    public int Updated { get; set; }
    public int Ignored { get; set; }
    public int Unlinked { get; set; }
    /// <summary>Décisions non appliquées, avec la raison.</summary>
    public List<string> Messages { get; set; } = new();
    public bool Changed => Linked + Created + Updated + Ignored + Unlinked > 0;
}

public static class Reconciler
{
    private const int UnlinkedSample = 200;

    // ---------- Comparaison ----------

    public static ReconciliationReport Compare(Repository repo, SourceProfile profile, IReadOnlyList<SourceRecord> records)
    {
        var index = new Index(repo, profile);
        var lines = records.Select(index.Classify).ToList();
        FlagSharedTargets(lines, repo);
        var report = new ReconciliationReport { ProfileKey = profile.Key, Lines = lines };
        foreach (var status in LineStatus.All) report.Counts[status] = lines.Count(l => l.Status == status);
        var sourceIds = records.Select(r => r.ExternalId).ToHashSet();
        report.Missing = repo.ActiveObjects
            .Where(o => o.ExternalRefs.TryGetValue(profile.Key, out var id) && !sourceIds.Contains(id))
            .OrderBy(o => o.Name, StringComparer.OrdinalIgnoreCase)
            .Select(o => new MissingObject(o.Id, o.Name, o.TypeKey, o.ExternalRefs[profile.Key])).ToList();
        var unlinked = repo.ActiveObjects.Where(o => index.Targeted.Contains(o.TypeKey) && !o.ExternalRefs.ContainsKey(profile.Key))
            .OrderBy(o => o.Name, StringComparer.OrdinalIgnoreCase).ToList();
        report.UnlinkedCount = unlinked.Count;
        report.Unlinked = unlinked.Take(UnlinkedSample).Select(o => new Candidate(o.Id, o.Name, o.TypeKey)).ToList();
        if (index.Targeted.Count == 0)
            report.Notes.Add("Aucun type du métamodèle n'est visé par le profil : choisissez un type par défaut ou associez des valeurs de la source à des types.");
        return report;
    }

    /// <summary>Deux lignes qui visent le même objet ne peuvent pas être liées sans choix de l'utilisateur.</summary>
    private static void FlagSharedTargets(List<ReconciliationLine> lines, Repository repo)
    {
        foreach (var group in lines.Where(l => l.Status == LineStatus.Proposed).GroupBy(l => l.ObjectId!).Where(g => g.Count() > 1))
        {
            var target = repo.Objects[group.Key];
            foreach (var line in group)
            {
                line.Status = LineStatus.Ambiguous;
                line.Candidates = new List<Candidate> { new(target.Id, target.Name, target.TypeKey) };
                line.ObjectId = null; line.ObjectName = null; line.Differences = new();
                line.Message = $"{group.Count()} lignes de la source visent le même objet ; choisissez celle à lier.";
            }
        }
    }

    /// <summary>Recherches préparées une fois : objets liés, objets candidats par nom, types visés.</summary>
    private sealed class Index
    {
        private readonly Repository _repo;
        private readonly SourceProfile _profile;
        private readonly Dictionary<string, List<ArchObject>> _linked;
        private readonly Dictionary<string, List<ArchObject>> _byName;
        private readonly Dictionary<string, List<ArchObject>> _homonyms;
        private readonly HashSet<string> _ignored;

        public HashSet<string> Targeted { get; }

        public Index(Repository repo, SourceProfile profile)
        {
            _repo = repo; _profile = profile;
            Targeted = TargetedTypes(profile);
            _linked = repo.Objects.Values.Where(o => o.ExternalRefs.ContainsKey(profile.Key))
                .GroupBy(o => o.ExternalRefs[profile.Key]).ToDictionary(g => g.Key, g => g.ToList());
            _byName = repo.ActiveObjects.Where(o => Targeted.Contains(o.TypeKey) && !o.ExternalRefs.ContainsKey(profile.Key))
                .GroupBy(o => Text.Key(o.Name)).ToDictionary(g => g.Key, g => g.ToList());
            // Hors des types visés : ces objets ne sont jamais proposés d'office, mais un nom identique est signalé avant de créer.
            _homonyms = repo.ActiveObjects.Where(o => !Targeted.Contains(o.TypeKey) && !o.ExternalRefs.ContainsKey(profile.Key))
                .GroupBy(o => Text.Key(o.Name)).ToDictionary(g => g.Key, g => g.ToList());
            _ignored = profile.IgnoredIds.ToHashSet();
        }

        public ReconciliationLine Classify(SourceRecord r)
        {
            var line = new ReconciliationLine
            {
                ExternalId = r.ExternalId, Name = r.Name, TypeValue = r.TypeValue, TypeKey = ResolveType(_repo.Metamodel, _profile.Mapping, r.TypeValue),
            };
            if (_linked.TryGetValue(r.ExternalId, out var linked)) return ClassifyLinked(line, r, linked);
            if (_ignored.Contains(r.ExternalId)) { line.Status = LineStatus.Ignored; return line; }
            var found = FindByName(r.Name, line.TypeKey, out var otherType);
            if (found.Count == 1)
            {
                line.Status = LineStatus.Proposed;
                line.ObjectId = found[0].Id; line.ObjectName = found[0].Name;
                line.Differences = Differences(found[0], r, _profile.Mapping);
                if (otherType) line.Differences.Insert(0, new Difference("type", "Type", found[0].TypeKey, line.TypeKey));
            }
            else if (found.Count > 1)
            {
                line.Status = LineStatus.Ambiguous;
                line.Candidates = found.Select(o => new Candidate(o.Id, o.Name, o.TypeKey)).ToList();
                line.Message = $"{found.Count} objets du référentiel portent ce nom.";
            }
            else if (line.TypeKey is null)
            {
                line.Status = LineStatus.Invalid;
                line.Message = string.IsNullOrWhiteSpace(r.TypeValue)
                    ? "Aucun type par défaut dans le profil pour une ligne sans nature."
                    : $"La nature « {r.TypeValue} » n'est associée à aucun type du métamodèle.";
            }
            else
            {
                line.Status = LineStatus.New;
                WarnOfHomonyms(line);
            }
            return line;
        }

        /// <summary>
        /// Garde-fou contre les doublons : un objet de même nom existe ailleurs dans le référentiel, sous un type que la source
        /// ne vise pas. La ligne reste « nouvelle » (rien n'est décidé à la place de l'utilisateur), mais elle le dit et
        /// présente ces objets, pour qu'il puisse lier plutôt que créer.
        /// </summary>
        private void WarnOfHomonyms(ReconciliationLine line)
        {
            if (!_homonyms.TryGetValue(Text.Key(line.Name), out var same)) return;
            line.Candidates = same.Select(o => new Candidate(o.Id, o.Name, o.TypeKey)).ToList();
            line.Message = same.Count == 1
                ? "Un objet du même nom existe déjà sous un autre type : le lier évite un doublon."
                : $"{same.Count} objets du même nom existent déjà sous d'autres types : en lier un évite un doublon.";
        }

        private ReconciliationLine ClassifyLinked(ReconciliationLine line, SourceRecord r, List<ArchObject> linked)
        {
            if (linked.Count > 1)
            {
                line.Status = LineStatus.Ambiguous;
                line.Candidates = linked.Select(o => new Candidate(o.Id, o.Name, o.TypeKey)).ToList();
                line.Message = "Plusieurs objets portent déjà ce lien ; supprimez les liens en trop.";
                return line;
            }
            line.Status = LineStatus.Linked;
            line.ObjectId = linked[0].Id; line.ObjectName = linked[0].Name;
            line.Differences = Differences(linked[0], r, _profile.Mapping);
            if (linked[0].Archived) line.Message = "L'objet lié est archivé dans le référentiel.";
            return line;
        }

        /// <summary>Recherche dans le type résolu d'abord, puis dans tous les types visés (otherType signale ce repli).</summary>
        private List<ArchObject> FindByName(string name, string? typeKey, out bool otherType)
        {
            otherType = false;
            if (!_byName.TryGetValue(Text.Key(name), out var all)) return new();
            if (typeKey is null) return all;
            var sameType = all.Where(o => o.TypeKey == typeKey).ToList();
            if (sameType.Count > 0) return sameType;
            otherType = true;
            return all;
        }
    }

    internal static HashSet<string> TargetedTypes(SourceProfile profile) =>
        profile.Mapping.Types.Select(t => t.TypeKey).Append(profile.Mapping.DefaultTypeKey ?? "").Where(k => k != "").ToHashSet();

    /// <summary>Type du métamodèle pour une valeur de la colonne de nature ; null s'il est absent ou inconnu du métamodèle.</summary>
    public static string? ResolveType(Metamodel metamodel, SourceMapping mapping, string? typeValue)
    {
        var key = string.IsNullOrWhiteSpace(typeValue) ? null
            : mapping.Types.FirstOrDefault(t => Text.Key(t.SourceValue) == Text.Key(typeValue))?.TypeKey;
        key ??= mapping.DefaultTypeKey;
        return key is not null && metamodel.Type(key) is { Archived: false } ? key : null;
    }

    private static List<Difference> Differences(ArchObject obj, SourceRecord record, SourceMapping mapping)
    {
        var list = new List<Difference>();
        if (!string.Equals(obj.Name.Trim(), record.Name, StringComparison.Ordinal))
            list.Add(new Difference("name", "Nom", obj.Name, record.Name));
        foreach (var f in mapping.Fields)
        {
            if (!record.Fields.TryGetValue(f.SourceField, out var incoming)) continue;
            var current = obj.Get(f.PropertyKey);
            if (Clean(current) != Clean(incoming)) list.Add(new Difference(f.PropertyKey, f.Label ?? f.PropertyKey, current, incoming));
        }
        return list;
    }

    private static string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    // ---------- Application ----------

    /// <summary>
    /// Applique les décisions de l'utilisateur au référentiel. Rien n'est supprimé : un objet disparu de la source reste, signalé.
    /// Le profil est modifié (lignes ignorées) : l'appelant doit le sauvegarder.
    /// </summary>
    public static ApplyResult Apply(Repository repo, SourceProfile profile, ReconciliationReport report,
        IReadOnlyList<SourceRecord> records, ApplyRequest request, string user)
    {
        var run = new ApplyRun(repo, profile, report, records, user);
        // Copie de l'état avant : l'historique de chaque objet touché se déduit de la comparaison à la fin.
        var before = repo.Objects.ToDictionary(kv => kv.Key, kv => ChangeSet.Capture(kv.Value));
        run.EnsureProperties();
        foreach (var d in request.Decisions) run.Decide(d);
        if (request.AcceptProposals)
            foreach (var line in report.Lines.Where(l => l.Status == LineStatus.Proposed && !run.Decided.Contains(l.ExternalId)))
                run.Link(line.ExternalId, line.ObjectId);
        if (request.SyncLinked) run.Sync();
        var r = run.Result;
        LogObjectChanges(repo, profile, before, user);
        if (r.Changed)
            repo.Log(user, "Rapprochement avec une source externe", null,
                $"{profile.Label} ({profile.Key}) : {r.Linked} lié(s), {r.Created} créé(s), {r.Updated} mis à jour, {r.Ignored} ignoré(s), {r.Unlinked} délié(s)"
                + (r.Messages.Count > 0 ? $", {r.Messages.Count} décision(s) refusée(s)" : ""));
        return r;
    }

    /// <summary>Une entrée de journal par objet créé ou modifié par le rapprochement, avec l'avant/après.</summary>
    private static void LogObjectChanges(Repository repo, SourceProfile profile, Dictionary<string, ObjectState> before, string user)
    {
        foreach (var (id, o) in repo.Objects)
        {
            var existed = before.TryGetValue(id, out var old);
            var changes = ChangeSet.Between(repo.Metamodel, old, ChangeSet.Capture(o));
            if (changes.Count == 0) continue;
            repo.Log(user, existed ? "Objet modifié par rapprochement" : "Objet créé par rapprochement", JournalKinds.Object, id, changes, $"{o.Name} (source {profile.Key})");
        }
    }

    private sealed class ApplyRun
    {
        private readonly Repository _repo;
        private readonly SourceProfile _profile;
        private readonly ReconciliationReport _report;
        private readonly Dictionary<string, SourceRecord> _records;
        private readonly Dictionary<string, ArchObject> _linkedByExternalId;
        private readonly string _user;

        public ApplyResult Result { get; } = new();
        public HashSet<string> Decided { get; } = new();

        public ApplyRun(Repository repo, SourceProfile profile, ReconciliationReport report, IReadOnlyList<SourceRecord> records, string user)
        {
            _repo = repo; _profile = profile; _report = report; _user = user;
            _records = records.ToDictionary(r => r.ExternalId);
            // Un identifiant porté par plusieurs objets est signalé ambigu au rapprochement : on garde le premier ici.
            _linkedByExternalId = repo.Objects.Values.Where(o => o.ExternalRefs.ContainsKey(profile.Key))
                .GroupBy(o => o.ExternalRefs[profile.Key]).ToDictionary(g => g.Key, g => g.First());
        }

        public void EnsureProperties()
        {
            foreach (var f in _profile.Mapping.Fields) _repo.Metamodel.EnsureProperty(f.PropertyKey, f.Label ?? f.PropertyKey, _profile.Key);
        }

        public void Decide(Decision d)
        {
            Decided.Add(d.ExternalId);
            if (!_records.ContainsKey(d.ExternalId) && d.Action != DecisionAction.Unlink)
            { Refuse(d, "identifiant absent de la dernière lecture de la source"); return; }
            switch (d.Action)
            {
                case DecisionAction.Link: Link(d.ExternalId, d.ObjectId ?? Proposed(d.ExternalId)); break;
                case DecisionAction.Create: Create(d.ExternalId); break;
                case DecisionAction.Ignore: Ignore(d.ExternalId); break;
                case DecisionAction.Unlink: Unlink(d.ExternalId); break;
                default: Refuse(d, $"action « {d.Action} » inconnue"); break;
            }
        }

        private string? Proposed(string externalId) =>
            _report.Lines.FirstOrDefault(l => l.ExternalId == externalId && l.Status == LineStatus.Proposed)?.ObjectId;

        private void Refuse(Decision d, string reason) => Result.Messages.Add($"{d.ExternalId} ({d.Action}) : {reason}.");
        private void Refuse(string externalId, string action, string reason) => Result.Messages.Add($"{externalId} ({action}) : {reason}.");

        public void Link(string externalId, string? objectId)
        {
            if (objectId is null || !_repo.Objects.TryGetValue(objectId, out var obj) || obj.Archived)
            { Refuse(externalId, DecisionAction.Link, "aucun objet actif à lier"); return; }
            if (obj.ExternalRefs.TryGetValue(_profile.Key, out var current))
            {
                if (current != externalId) Refuse(externalId, DecisionAction.Link, $"l'objet « {obj.Name} » est déjà lié à un autre identifiant de cette source");
                return;
            }
            if (_linkedByExternalId.ContainsKey(externalId))
            { Refuse(externalId, DecisionAction.Link, "cet identifiant est déjà lié à un autre objet"); return; }
            obj.ExternalRefs[_profile.Key] = externalId;
            _linkedByExternalId[externalId] = obj;
            _profile.IgnoredIds.Remove(externalId);
            Touch(obj);
            Result.Linked++;
        }

        private void Create(string externalId)
        {
            if (_linkedByExternalId.ContainsKey(externalId)) { Refuse(externalId, DecisionAction.Create, "déjà lié à un objet"); return; }
            var record = _records[externalId];
            var typeKey = ResolveType(_repo.Metamodel, _profile.Mapping, record.TypeValue);
            if (typeKey is null) { Refuse(externalId, DecisionAction.Create, "aucun type du métamodèle pour cette ligne"); return; }
            var obj = new ArchObject
            {
                Id = FreeId(externalId), TypeKey = typeKey, Name = record.Name, Source = _profile.Key, ValidationStatus = "Proposé",
                ProposedBy = _user, ProposedAt = DateTime.UtcNow,
            };
            obj.ExternalRefs[_profile.Key] = externalId;
            foreach (var f in _profile.Mapping.Fields)
                if (record.Fields.TryGetValue(f.SourceField, out var v) && Clean(v) is { } value) obj.Set(f.PropertyKey, value);
            Touch(obj);
            _repo.Objects[obj.Id] = obj;
            _linkedByExternalId[externalId] = obj;
            _profile.IgnoredIds.Remove(externalId);
            Result.Created++;
        }

        private string FreeId(string externalId)
        {
            var baseId = $"id-{_profile.Key}-{Sanitize(externalId)}";
            var id = baseId;
            for (var n = 2; _repo.Objects.ContainsKey(id); n++) id = $"{baseId}-{n}";
            return id;
        }

        private void Ignore(string externalId)
        {
            if (_linkedByExternalId.ContainsKey(externalId)) { Refuse(externalId, DecisionAction.Ignore, "déjà lié à un objet : déliez-le d'abord"); return; }
            if (_profile.IgnoredIds.Contains(externalId)) return;
            _profile.IgnoredIds.Add(externalId);
            Result.Ignored++;
        }

        private void Unlink(string externalId)
        {
            if (_linkedByExternalId.Remove(externalId, out var obj))
            {
                obj.ExternalRefs.Remove(_profile.Key);
                Touch(obj);
                Result.Unlinked++;
            }
            // Délier une ligne ignorée la remet en jeu au prochain rapprochement.
            else if (!_profile.IgnoredIds.Remove(externalId)) Refuse(externalId, DecisionAction.Unlink, "aucun lien à supprimer");
        }

        /// <summary>Recopie la source sur chaque objet lié. Sans écart, l'objet n'est pas touché : le rejeu ne modifie rien.</summary>
        public void Sync()
        {
            foreach (var (externalId, obj) in _linkedByExternalId)
            {
                if (obj.Archived || !_records.TryGetValue(externalId, out var record)) continue;
                var changed = false;
                if (_profile.SourceIsMaster && obj.Name != record.Name) { obj.Name = record.Name; changed = true; }
                foreach (var f in _profile.Mapping.Fields)
                    if (record.Fields.TryGetValue(f.SourceField, out var incoming)) changed |= SetProperty(obj, f.PropertyKey, Clean(incoming));
                if (!changed) continue;
                Touch(obj);
                Result.Updated++;
            }
        }

        private static bool SetProperty(ArchObject obj, string key, string? value)
        {
            if (Clean(obj.Get(key)) == value) return false;
            if (value is null) obj.Properties.RemoveAll(p => p.Key == key);
            else obj.Set(key, value);
            return true;
        }

        private void Touch(ArchObject obj) { obj.UpdatedAt = DateTime.UtcNow; obj.UpdatedBy = _user; }
    }

    /// <summary>Identifiant externe réduit à des caractères sûrs pour un identifiant d'objet.</summary>
    public static string Sanitize(string externalId)
    {
        var sb = new StringBuilder();
        foreach (var ch in externalId.ToLowerInvariant())
            sb.Append(ch is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '.' or '_' ? ch : '-');
        var s = System.Text.RegularExpressions.Regex.Replace(sb.ToString(), "-{2,}", "-").Trim('-');
        return s.Length == 0 ? "x" : s.Length > 80 ? s[..80] : s;
    }
}
