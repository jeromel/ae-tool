namespace AeTool.Core;

// Contrôles de cohérence du catalogue des données et des flux, et registre des données personnelles et sensibles.

public sealed record CatalogueFinding(string Code, string Severity, string Title, string Hint, List<CatalogueRef> Items);
public sealed record CatalogueRef(string Id, string Label, string? Detail);
public sealed record ReliabilityRow(string Kind, int Verified, int Total);
public sealed record CatalogueQualityReport(List<CatalogueFinding> Findings, List<ReliabilityRow> Reliability, bool HasCatalogue);

public sealed record RegisterFlow(string Id, string Label, string SourceName, string TargetName, bool CrossesItOt, string? Status);
public sealed record RegisterRow(string Id, string Name, string? Code, string? Sensitivity, string? Personal, string? Owner, string? Master,
    List<string> Applications, List<RegisterFlow> Flows, List<string> Shares, List<string> PersonalAttributes, bool CrossesItOt);
public sealed record RegisterShare(string Id, string Name, List<string> Data, List<string> Files);
public sealed record PersonalDataRegister(List<RegisterRow> Rows, List<RegisterShare> Shares);

public sealed class CatalogueQuality
{
    private readonly Repository _repo;
    private readonly Metamodel _meta;
    private readonly LineageHelpers _h;

    public CatalogueQuality(Repository repo)
    {
        _repo = repo;
        _meta = repo.Metamodel;
        _h = new LineageHelpers(repo);
    }

    /// <summary>Valeurs de sensibilité qui désignent une donnée à protéger, dans les deux vocabulaires rencontrés.</summary>
    private static readonly HashSet<string> Protected = new(StringComparer.OrdinalIgnoreCase) { "Confidentiel", "Sensible", "Secret", "C3", "C4" };

    public static bool IsProtected(string? sensitivity) => sensitivity is not null && Protected.Contains(sensitivity.Trim());

    /// <summary>
    /// Constats qui portent sur l'infrastructure (chaîne technique, hébergement, partages). Au périmètre « métier »
    /// (décision 012), elle est retirée avant la lecture : ces constats y seraient faux, toute application paraissant
    /// sans hébergement et tout flux sans chaîne. Ils sont donc retirés, pas comptés à zéro (recette de Haussman, question 4).
    /// </summary>
    private static readonly HashSet<string> InfrastructureFindings = new()
        { "flux-sans-chaine", "chaine-sans-arrivee", "flux-technique-inutilise", "hebergement-inconnu", "partage-sans-sous-type" };

    public CatalogueQualityReport Check()
    {
        var findings = new List<CatalogueFinding>();
        var h = _h;
        var scoped = _repo.Scope is not null;
        var coded = h.Objects.Where(o => o.Get(CatalogueModel.Code) is { Length: > 0 }).ToList();
        var flows = h.Flows.ToList();
        var codedFlows = flows.Where(f => h.P(f, CatalogueModel.Code) is not null).ToList();
        void Add(string code, string severity, string title, string hint, IEnumerable<CatalogueRef> items)
        {
            if (scoped && InfrastructureFindings.Contains(code)) return;
            var list = items.ToList();
            if (list.Count > 0) findings.Add(new CatalogueFinding(code, severity, title, hint, list));
        }
        string FlowLabel(Relationship f) => $"{h.P(f, CatalogueModel.Code) ?? ""} {f.Name ?? ""}".Trim();
        string Ends(Relationship f) => $"{h.Name(f.SourceId)} → {h.Name(f.TargetId)}";

        // Émetteur qui n'est ni maître ni relais de la donnée transportée.
        Add("emetteur-non-maitre", "avertissement", "Flux émis par une application qui n'est ni maître ni relais de la donnée",
            "Si l'émetteur transmet légitimement la donnée, déclarez-le relais dans le catalogue ; sinon corrigez l'émetteur ou la donnée du flux.",
            codedFlows.SelectMany(f => h.Carried(f).Select(d => (f, d))).Where(x =>
            {
                var roles = h.RolesOn(x.d);
                if (!roles.Any(r => r.Role == CatalogueModel.RoleMaster)) return false;
                return !roles.Any(r => r.AppId == x.f.SourceId && r.Role is CatalogueModel.RoleMaster or CatalogueModel.RoleRelay);
            }).Select(x => new CatalogueRef(x.f.Id, FlowLabel(x.f),
                $"{Ends(x.f)} ; {h.Name(x.d)} a pour maître {string.Join(", ", h.RolesOn(x.d).Where(r => r.Role == CatalogueModel.RoleMaster).Select(r => r.AppName))}")));

        Add("donnee-sans-maitre", "erreur", "Objets de données sans système maître", "Désignez le système qui fait foi pour la donnée.",
            coded.Where(o => h.IsData(o.Id) && !h.RolesOn(o.Id).Any(r => r.Role == CatalogueModel.RoleMaster)).Select(o => Ref(o)));
        Add("donnee-plusieurs-maitres", "erreur", "Objets de données avec plusieurs systèmes maîtres", "Une donnée n'a qu'un maître ; les autres sont relais ou consommateurs.",
            coded.Where(o => h.IsData(o.Id) && h.RolesOn(o.Id).Count(r => r.Role == CatalogueModel.RoleMaster) > 1)
                .Select(o => Ref(o, string.Join(", ", h.RolesOn(o.Id).Where(r => r.Role == CatalogueModel.RoleMaster).Select(r => r.AppName)))));
        Add("donnee-sans-responsable", "avertissement", "Objets de données sans data owner ou sans data steward", "Le catalogue doit dire qui décide et qui tient la donnée.",
            coded.Where(o => h.IsData(o.Id) && (string.IsNullOrWhiteSpace(o.Get(CatalogueModel.DataOwner)) || string.IsNullOrWhiteSpace(o.Get(CatalogueModel.DataSteward))))
                .Select(o => Ref(o, string.IsNullOrWhiteSpace(o.Get(CatalogueModel.DataOwner)) ? "data owner manquant" : "data steward manquant")));

        // Sensibilité : valeurs hors de la liste de référence du catalogue, regroupées par valeur.
        var reference = _repo.CatalogueLists.FirstOrDefault(kv => kv.Key.Contains("ensibilit", StringComparison.OrdinalIgnoreCase)).Value;
        if (reference is { Count: > 0 })
        {
            var outside = coded.Where(o => h.IsData(o.Id) && o.Get(CatalogueModel.DataSensitivity) is { Length: > 0 } v && !reference.Contains(v.Trim(), StringComparer.OrdinalIgnoreCase))
                .GroupBy(o => o.Get(CatalogueModel.DataSensitivity)!.Trim()).ToList();
            if (outside.Count > 0)
                findings.Add(new CatalogueFinding("sensibilite-hors-liste", "avertissement",
                    $"Sensibilité hors de la liste de référence ({string.Join(", ", reference)})",
                    "Deux vocabulaires cohabitent : choisissez-en un et alignez la liste de référence ou les objets.",
                    outside.Select(g => new CatalogueRef(g.First().Id, $"« {g.Key} »", $"{g.Count()} objet(s) : {string.Join(", ", g.Select(o => o.Name).Take(6))}{(g.Count() > 6 ? "…" : "")}")).ToList()));
        }
        Add("donnees-personnelles-incoherentes", "avertissement", "Objets déclarés sans données personnelles mais dont un attribut en contient",
            "Mettez « Données personnelles » à Oui sur l'objet, ou corrigez l'attribut.",
            coded.Where(o => h.IsData(o.Id) && string.Equals(o.Get(CatalogueModel.DataPersonal)?.Trim(), "Non", StringComparison.OrdinalIgnoreCase)
                    && _repo.DataAttributes.GetValueOrDefault(o.Id)?.Any(a => a.IsPersonal) == true)
                .Select(o => Ref(o, string.Join(", ", _repo.DataAttributes[o.Id].Where(a => a.IsPersonal).Select(a => a.Name)))));

        // Chaînes techniques.
        Add("flux-sans-chaine", "avertissement", "Flux applicatifs sans chaîne technique", "Décrivez par quelles étapes techniques le flux passe (onglet Traçabilité).",
            codedFlows.Where(f => h.Steps(f).Count == 0).Select(f => new CatalogueRef(f.Id, FlowLabel(f), Ends(f))));
        Add("chaine-sans-arrivee", "information", "Chaînes techniques qui n'atteignent pas l'application cible",
            "Aucune étape ne concerne l'application cible : la chaîne est souvent décrite côté émetteur seulement. Ajoutez l'étape qui charge la donnée dans la cible, pour savoir par où elle y entre.",
            codedFlows.Select(f => (f, steps: h.Steps(f))).Where(x => x.steps.Count > 0 && h.IsApp(x.f.TargetId)
                    && !x.steps.Any(s => h.TechnicalApp(s.TechnicalId) == x.f.TargetId))
                .Select(x => new CatalogueRef(x.f.Id, FlowLabel(x.f), $"{Ends(x.f)} ; dernière étape : {_repo.Objects[x.steps[^1].TechnicalId].Name} ({_repo.Objects[x.steps[^1].TechnicalId].Get(CatalogueModel.TechStep) ?? "étape"})")));
        var usedTechnical = flows.SelectMany(h.Steps).Select(s => s.TechnicalId).ToHashSet();
        Add("flux-technique-inutilise", "information", "Flux techniques qui ne réalisent aucun flux applicatif", "À relier à un flux applicatif, ou à retirer s'il n'existe plus.",
            h.Objects.Where(o => h.IsTechnical(o.Id) && !usedTechnical.Contains(o.Id)).Select(o => Ref(o)));

        // Applications du catalogue.
        Add("application-sans-flux", "information", "Applications du catalogue sans aucun flux du catalogue",
            "Normal pour un composant technique (bus, plateforme d'échange) ; sinon, un flux manque au catalogue.",
            coded.Where(o => h.IsApp(o.Id) && !codedFlows.Any(f => f.SourceId == o.Id || f.TargetId == o.Id)).Select(o => Ref(o)));
        Add("hebergement-inconnu", "avertissement", "Applications du catalogue dont l'hébergement n'est pas renseigné",
            "Sans hébergement, le parcours d'une donnée ne peut pas dire sur quels serveurs elle se trouve.",
            coded.Where(o => h.IsApp(o.Id) && h.HostsOf(o.Id).Count == 0).Select(o => Ref(o)));
        Add("acces-hors-catalogue", "information", "Accès connus d'Archi mais absents du catalogue",
            "L'application accède à la donnée d'après Archi, mais le catalogue ne la cite ni comme maître, ni comme relais, ni comme consommateur.",
            coded.Where(o => h.IsData(o.Id)).SelectMany(o => h.RolesOn(o.Id).Where(r => !r.Catalogue).Select(r => new CatalogueRef(o.Id, o.Name, r.AppName))));

        // Partages modélisés par un nom « Partage… » sans sous-type.
        var nodeTypes = _meta.Transcoding.Elements.Where(r => r.ArchimateType == "Node" && !r.Conditional).Select(r => r.TypeKey).ToHashSet();
        Add("partage-sans-sous-type", "information", "Nœuds nommés « Partage… » sans sous-type",
            $"Reclassez-les en partages de fichiers : l'outil pose la propriété {CatalogueModel.Subtype} = « {CatalogueModel.FileShareSubtype} », que l'export vers Archi reprend.",
            h.Objects.Where(o => nodeTypes.Contains(o.TypeKey) && o.Name.TrimStart().StartsWith("Partage", StringComparison.OrdinalIgnoreCase)).Select(o => Ref(o)));

        // Fiabilité : part de ce qui est constaté.
        var reliability = new List<ReliabilityRow>();
        void Rate(string kind, IEnumerable<string?> statuses)
        {
            var list = statuses.ToList();
            if (list.Count > 0) reliability.Add(new ReliabilityRow(kind, list.Count(s => string.Equals(s?.Trim(), CatalogueModel.Verified, StringComparison.OrdinalIgnoreCase)), list.Count));
        }
        Rate("Applications", coded.Where(o => h.IsApp(o.Id)).Select(o => o.Get(CatalogueModel.InfoStatus)));
        Rate("Objets de données", coded.Where(o => h.IsData(o.Id)).Select(o => o.Get(CatalogueModel.InfoStatus)));
        Rate("Flux applicatifs", codedFlows.Select(f => h.P(f, CatalogueModel.InfoStatus)));
        Rate("Flux techniques", coded.Where(o => h.IsTechnical(o.Id)).Select(o => o.Get(CatalogueModel.InfoStatus)));
        Rate("Liens de traçabilité", flows.SelectMany(h.Steps).Select(s => s.Status));
        return new CatalogueQualityReport(findings, reliability, coded.Count > 0 || codedFlows.Count > 0);
    }

    private CatalogueRef Ref(ArchObject o, string? detail = null) => new(o.Id, $"{o.Get(CatalogueModel.Code) ?? ""} {o.Name}".Trim(), detail);

    /// <summary>Données personnelles ou à protéger : où elles vivent, par où elles passent, et les partages qui en hébergent.</summary>
    public PersonalDataRegister Register()
    {
        var h = _h;
        var rows = new List<RegisterRow>();
        var flows = h.Flows.ToList();
        string Zone(string appId) => (_repo.Objects[appId].Get(CatalogueModel.AppZone) ?? (h.IsApplicative(appId) ? "IT" : "EXTERNE")).Replace(" ", "").ToUpperInvariant();
        // Traverse la frontière IT/OT : les deux zones diffèrent et l'une touche l'OT.
        bool Crosses(Relationship f) => Zone(f.SourceId) != Zone(f.TargetId) && (Zone(f.SourceId).Contains("OT") || Zone(f.TargetId).Contains("OT"));
        var sharesData = new Dictionary<string, (HashSet<string> Data, HashSet<string> Files)>();
        foreach (var o in h.Objects.Where(o => h.IsData(o.Id)))
        {
            var personal = string.Equals(o.Get(CatalogueModel.DataPersonal)?.Trim(), "Oui", StringComparison.OrdinalIgnoreCase);
            var attributes = _repo.DataAttributes.GetValueOrDefault(o.Id)?.Where(a => a.IsPersonal).Select(a => a.Name).ToList() ?? new List<string>();
            if (!personal && attributes.Count == 0 && !IsProtected(o.Get(CatalogueModel.DataSensitivity))) continue;
            var carrying = flows.Where(f => h.Carried(f).Contains(o.Id)).ToList();
            var roles = h.RolesOn(o.Id);
            var apps = roles.Select(r => r.AppName).Concat(carrying.SelectMany(f => new[] { h.Name(f.SourceId), h.Name(f.TargetId) })).Distinct().OrderBy(x => x).ToList();
            var shares = new List<string>();
            foreach (var (hostId, file) in h.FilesOf(o.Id))
            {
                if (!h.IsShare(hostId)) continue;
                shares.Add(h.Name(hostId));
                if (!sharesData.TryGetValue(hostId, out var entry)) sharesData[hostId] = entry = (new HashSet<string>(), new HashSet<string>());
                entry.Data.Add(o.Name); entry.Files.Add(file);
            }
            var registerFlows = carrying.Select(f => new RegisterFlow(f.Id, $"{h.P(f, CatalogueModel.Code) ?? ""} {f.Name ?? ""}".Trim(), h.Name(f.SourceId), h.Name(f.TargetId), Crosses(f), h.P(f, CatalogueModel.InfoStatus))).ToList();
            rows.Add(new RegisterRow(o.Id, o.Name, o.Get(CatalogueModel.Code), o.Get(CatalogueModel.DataSensitivity), personal ? "Oui" : o.Get(CatalogueModel.DataPersonal),
                o.Get(CatalogueModel.DataOwner), roles.FirstOrDefault(r => r.Role == CatalogueModel.RoleMaster)?.AppName,
                apps, registerFlows, shares.Distinct().ToList(), attributes, registerFlows.Any(f => f.CrossesItOt)));
        }
        return new PersonalDataRegister(
            rows.OrderByDescending(r => r.Personal == "Oui").ThenBy(r => r.Code ?? "~").ThenBy(r => r.Name).ToList(),
            sharesData.Select(kv => new RegisterShare(kv.Key, h.Name(kv.Key), kv.Value.Data.OrderBy(x => x).ToList(), kv.Value.Files.OrderBy(x => x).ToList())).OrderBy(s => s.Name).ToList());
    }
}

/// <summary>Lectures du graphe partagées par les contrôles et le registre.</summary>
public sealed class LineageHelpers
{
    private readonly Repository _repo;
    private readonly ILookup<string, Relationship> _from, _to;
    private readonly HashSet<string> _apps, _data, _technical, _infra, _shares, _profiles, _carriers, _flowTypes;

    public LineageHelpers(Repository repo)
    {
        _repo = repo;
        var m = repo.Metamodel;
        bool Exists(string id) => (repo.Objects.TryGetValue(id, out var o) && !o.Archived) || (repo.Relationships.TryGetValue(id, out var r) && !r.Archived);
        var active = repo.ActiveRelationships.Where(r => Exists(r.SourceId) && Exists(r.TargetId)).ToList();
        _from = active.ToLookup(r => r.SourceId);
        _to = active.ToLookup(r => r.TargetId);
        _apps = m.TypeKeysWith(Roles.Application); _data = m.TypeKeysWith(Roles.Data); _technical = m.TypeKeysWith(Roles.TechnicalFlow);
        _infra = m.TypeKeysWith(Roles.Infrastructure); _shares = m.TypeKeysWith(Roles.FileShare); _profiles = m.TypeKeysWith(Roles.Workstation);
        _carriers = m.TypeKeysWith(Roles.DataCarrier); _flowTypes = m.RelTypeKeysWith(Roles.Flow);
        _rule = new DataFlowRule(repo);
    }

    private readonly DataFlowRule _rule;
    /// <summary>Extrémité applicative d'un flux de données (composant, service, fonction…).</summary>
    public bool IsApplicative(string id) => _rule.IsApplicative(id);

    public IEnumerable<ArchObject> Objects => _repo.ActiveObjects;
    private bool Is(string id, HashSet<string> t) => _repo.Objects.TryGetValue(id, out var o) && !o.Archived && t.Contains(o.TypeKey);
    public bool IsApp(string id) => Is(id, _apps);
    public bool IsData(string id) => Is(id, _data);
    public bool IsTechnical(string id) => Is(id, _technical);
    public bool IsShare(string id) => Is(id, _shares);
    public bool IsHost(string id) => Is(id, _infra) && !Is(id, _profiles) && !IsTechnical(id);
    public string Name(string id) => _repo.Objects.TryGetValue(id, out var o) ? o.Name : id;
    public string? P(Relationship r, string key) => r.Properties.FirstOrDefault(p => p.Key == key)?.Value is { Length: > 0 } v ? v : null;
    /// <summary>Flux de données : la règle unique du cœur (<see cref="DataFlowRule"/>).</summary>
    public IEnumerable<Relationship> Flows => _rule.DataFlows;
    private bool Active(string id) => _repo.Objects.TryGetValue(id, out var o) && !o.Archived;
    private IEnumerable<Relationship> Links(string id) => _from[id].Concat(_to[id]);
    private static string Other(Relationship r, string id) => r.SourceId == id ? r.TargetId : r.SourceId;
    private IEnumerable<string> Neighbours(string id) => Links(id).Select(r => Other(r, id)).Distinct();
    public List<string> Carried(Relationship f) => Neighbours(f.Id).Where(IsData).ToList();
    public List<LineageStep> Steps(Relationship f) => Links(f.Id).Where(r => IsTechnical(Other(r, f.Id)))
        .Select(r => new LineageStep(Other(r, f.Id), int.TryParse(P(r, CatalogueModel.TraceOrder), out var n) ? n : null, P(r, CatalogueModel.InfoStatus)))
        .OrderBy(s => s.Order ?? int.MaxValue).ToList();
    public List<LineageRole> RolesOn(string dataId) =>
        Links(dataId).Where(r => IsApp(Other(r, dataId)) && !_flowTypes.Contains(r.TypeKey))
            .Select(r => new LineageRole(Other(r, dataId), Name(Other(r, dataId)), P(r, CatalogueModel.DataRole) ?? "Accès (hors catalogue)", P(r, CatalogueModel.DataRole) is not null))
            .GroupBy(x => x.AppId).Select(g => g.OrderBy(x => x.Catalogue ? 0 : 1).First()).ToList();
    public List<string> HostsOf(string appId) => Neighbours(appId).Where(IsHost).ToList();
    public string? TechnicalApp(string ftId) => Neighbours(ftId).FirstOrDefault(IsApp);
    public IEnumerable<(string HostId, string File)> FilesOf(string dataId) =>
        Neighbours(dataId).Where(id => Is(id, _carriers)).SelectMany(c => Neighbours(c).Where(IsHost).Select(h => (h, Name(c))));
}
