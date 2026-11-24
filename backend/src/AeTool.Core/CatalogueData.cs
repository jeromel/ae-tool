namespace AeTool.Core;

/// <summary>Attribut métier d'un objet de données, tel que décrit par le catalogue. ArchiMate n'a pas d'équivalent : il n'est pas exporté vers Archi.</summary>
public sealed class DataAttribute
{
    public string Name { get; set; } = "";
    public string? Nature { get; set; }
    public string? SourceApplication { get; set; }
    public string? SourceTable { get; set; }
    public string? SourceField { get; set; }
    /// <summary>Champ correspondant dans un système cible (colonne « Champ … » du catalogue).</summary>
    public string? TargetField { get; set; }
    /// <summary>Nom du système cible, tiré de l'en-tête de la colonne (« Champ CMDB » → « CMDB »).</summary>
    public string? TargetSystem { get; set; }
    public string? Personal { get; set; }
    public string? Sensitivity { get; set; }
    public string? Status { get; set; }
    public string? Comment { get; set; }

    public bool IsPersonal => string.Equals(Personal?.Trim(), "Oui", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Effectif d'un groupe de postes (profil, code inconnu, postes non classés) : total, par site, par entité, par modèle.</summary>
public sealed class WorkstationCount
{
    public int Total { get; set; }
    public Dictionary<string, int> BySite { get; set; } = new();
    public Dictionary<string, int> ByEntity { get; set; } = new();
    public Dictionary<string, int> ByModel { get; set; } = new();

    public void Add(string? site, string? entity, string? model)
    {
        Total++;
        static void Inc(Dictionary<string, int> d, string? k) { k = string.IsNullOrWhiteSpace(k) ? "(non renseigné)" : k.Trim(); d[k] = d.GetValueOrDefault(k) + 1; }
        Inc(BySite, site); Inc(ByEntity, entity); Inc(ByModel, model);
    }
}

/// <summary>Poste à rattacher : son champ pivot est vide ou porte un code de profil inconnu.</summary>
public sealed record UnmatchedWorkstation(string Id, string Name, string? Code, string? Site, string? Entity, string? Model);

/// <summary>Profil proposé pour des postes non rattachés d'une même entité : celui que portent le plus souvent les postes rattachés de l'entité.</summary>
public sealed record WorkstationSuggestion(string Entity, int Count, string ProfileCode, string ProfileName, int Basis);

/// <summary>
/// Comptage des postes de travail lus dans la CMDB. Seuls des effectifs sont gardés, et la liste des postes à rattacher :
/// aucun nom d'utilisateur n'entre dans le référentiel.
/// </summary>
public sealed class WorkstationCensus
{
    public string SourceKey { get; set; } = "";
    public DateTime At { get; set; }
    public string? By { get; set; }
    public string PivotField { get; set; } = "";
    public int Read { get; set; }
    /// <summary>Code de profil (PP-nnn) → effectif.</summary>
    public Dictionary<string, WorkstationCount> ByProfile { get; set; } = new();
    public WorkstationCount Unclassified { get; set; } = new();
    /// <summary>Code présent dans la CMDB mais porté par aucun profil du référentiel → effectif.</summary>
    public Dictionary<string, WorkstationCount> UnknownCodes { get; set; } = new();
    /// <summary>Postes à rattacher (non classés ou code inconnu), dans la limite de <see cref="MaxListed"/>.</summary>
    public List<UnmatchedWorkstation> ToAttach { get; set; } = new();
    public List<WorkstationSuggestion> Suggestions { get; set; } = new();
    /// <summary>Profils du référentiel auxquels aucun poste n'est rattaché (codes).</summary>
    public List<string> EmptyProfiles { get; set; } = new();

    public const int MaxListed = 2000;
}

/// <summary>Réglages du comptage des postes sur une source : le champ pivot porte le code du profil de poste.</summary>
public sealed class WorkstationSettings
{
    public string PivotField { get; set; } = "";
    public string? SiteField { get; set; }
    public string? EntityField { get; set; }
    public string? ModelField { get; set; }
    /// <summary>Colonne et valeur qui désignent un poste de travail parmi les éléments lus (vide = toutes les lignes).</summary>
    public string? FilterField { get; set; }
    public string? FilterValue { get; set; }
}

public static class WorkstationCounter
{
    /// <summary>
    /// Compte les postes par profil d'après le champ pivot. Le code se compare sans casse ; un nom de profil dans le champ
    /// est aussi reconnu (transition avant que la CMDB ne porte les codes). Les postes sans profil reçoivent une proposition par entité.
    /// </summary>
    public static WorkstationCensus Count(Repository repo, IReadOnlyList<Dictionary<string, string?>> rows, WorkstationSettings s,
        string sourceKey, string idField, string nameField, string user)
    {
        var profiles = repo.ActiveObjects.Where(o => repo.Metamodel.IsA(o.TypeKey, Roles.Workstation)).ToList();
        var byCode = new Dictionary<string, ArchObject>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in profiles)
        {
            if (p.Get(CatalogueModel.Code) is { Length: > 0 } c) byCode.TryAdd(c.Trim(), p);
            byCode.TryAdd(p.Name.Trim(), p);
        }
        string? Cell(Dictionary<string, string?> row, string? field) =>
            string.IsNullOrWhiteSpace(field) || !row.TryGetValue(field, out var v) || string.IsNullOrWhiteSpace(v) ? null : v.Trim();

        var census = new WorkstationCensus { SourceKey = sourceKey, At = DateTime.UtcNow, By = user, PivotField = s.PivotField };
        var classifiedByEntity = new Dictionary<string, Dictionary<string, int>>();
        var unclassifiedByEntity = new Dictionary<string, int>();
        foreach (var row in rows)
        {
            if (!string.IsNullOrWhiteSpace(s.FilterField) && !string.Equals(Cell(row, s.FilterField), s.FilterValue?.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
            census.Read++;
            var code = Cell(row, s.PivotField);
            var site = Cell(row, s.SiteField);
            var entity = Cell(row, s.EntityField);
            var model = Cell(row, s.ModelField);
            if (code is not null && byCode.TryGetValue(code, out var profile))
            {
                var key = profile.Get(CatalogueModel.Code) ?? profile.Id;
                if (!census.ByProfile.TryGetValue(key, out var count)) census.ByProfile[key] = count = new WorkstationCount();
                count.Add(site, entity, model);
                if (entity is not null)
                {
                    if (!classifiedByEntity.TryGetValue(entity, out var perProfile)) classifiedByEntity[entity] = perProfile = new Dictionary<string, int>();
                    perProfile[key] = perProfile.GetValueOrDefault(key) + 1;
                }
                continue;
            }
            if (code is null) census.Unclassified.Add(site, entity, model);
            else
            {
                if (!census.UnknownCodes.TryGetValue(code, out var unknown)) census.UnknownCodes[code] = unknown = new WorkstationCount();
                unknown.Add(site, entity, model);
            }
            if (entity is not null) unclassifiedByEntity[entity] = unclassifiedByEntity.GetValueOrDefault(entity) + 1;
            if (census.ToAttach.Count < WorkstationCensus.MaxListed)
                census.ToAttach.Add(new UnmatchedWorkstation(Cell(row, idField) ?? Cell(row, nameField) ?? "?", Cell(row, nameField) ?? "", code, site, entity, model));
        }
        var nameOf = profiles.Where(p => p.Get(CatalogueModel.Code) is { Length: > 0 }).GroupBy(p => p.Get(CatalogueModel.Code)!).ToDictionary(g => g.Key, g => g.First().Name);
        foreach (var (entity, count) in unclassifiedByEntity.OrderByDescending(kv => kv.Value))
        {
            // L'entité exacte d'abord ; à défaut, l'entité parente la plus proche (« DRH/Service paie » → « DRH »).
            var basis = classifiedByEntity.GetValueOrDefault(entity) ?? Parent(entity, classifiedByEntity);
            if (basis is null || basis.Count == 0) continue;
            var best = basis.OrderByDescending(kv => kv.Value).First();
            census.Suggestions.Add(new WorkstationSuggestion(entity, count, best.Key, nameOf.GetValueOrDefault(best.Key, best.Key), best.Value));
        }
        census.EmptyProfiles = profiles.Select(p => p.Get(CatalogueModel.Code) ?? p.Id).Where(c => !census.ByProfile.ContainsKey(c)).OrderBy(c => c).ToList();
        return census;
    }

    private static Dictionary<string, int>? Parent(string entity, Dictionary<string, Dictionary<string, int>> known)
    {
        for (var cut = entity.LastIndexOf('/'); cut > 0; cut = entity.LastIndexOf('/', cut - 1))
        {
            var parent = entity[..cut];
            var merged = known.Where(kv => kv.Key == parent || kv.Key.StartsWith(parent + "/", StringComparison.Ordinal))
                .SelectMany(kv => kv.Value).GroupBy(kv => kv.Key).ToDictionary(g => g.Key, g => g.Sum(kv => kv.Value));
            if (merged.Count > 0) return merged;
        }
        return null;
    }
}
