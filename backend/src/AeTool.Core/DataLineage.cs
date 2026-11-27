namespace AeTool.Core;

// Parcours d'une donnée : où elle vit (applications, serveurs, partages), par quels flux applicatifs elle circule et par quelles
// étapes techniques chacun passe, qui y accède (profils de poste). Calculé à la demande depuis le graphe, rien n'est stocké.

public sealed record LineageFocus(string Id, string Name, string Kind, string TypeLabel, bool Supported, string? Message);
public sealed record LineageRole(string AppId, string AppName, string Role, bool Catalogue);
public sealed record LineageData(string Id, string Name, string TypeLabel, string? Code, string? Category, string? Sensitivity, string? Personal,
    string? Owner, string? Steward, string? Pivot, string? Status, string? Documentation, bool Derived, List<LineageRole> Roles, List<DataAttribute> Attributes);
public sealed record LineageApp(string Id, string Name, string? Code, string? Zone, string? Status, List<string> Roles, List<string> HostIds, List<string> ProfileIds, bool External);
public sealed record LineageStep(string TechnicalId, int? Order, string? Status);
public sealed record LineageFlow(string Id, string? Code, string Name, string SourceId, string TargetId, List<string> DataIds, string? Kind,
    string? Frequency, string? Mode, string? Lifecycle, string? Status, List<LineageStep> Steps);
public sealed record LineageTechnical(string Id, string Name, string? Code, string? Step, string? Protocol, string? Format, string? Orchestration,
    string? Zone, string? Account, string? Monitoring, string? Status, string? AppId, string? AppName, List<string> HostIds);
public sealed record LineageHost(string Id, string Name, string TypeLabel, string Kind, List<string> Details, string? Location, string Usage,
    List<string> AppIds, List<string> Files);
public sealed record LineageProfile(string Id, string Name, string? Code, List<string> AppIds, int? Workstations, Dictionary<string, int>? BySite);
public sealed record Lineage(LineageFocus Focus, List<LineageData> Data, List<LineageApp> Applications, List<LineageFlow> Flows,
    List<LineageTechnical> Technical, List<LineageHost> Hosts, List<LineageProfile> Profiles, List<string> Gaps, DateTime? CensusAt);

public sealed class LineageReader
{
    public const string Storage = "stockage";
    public const string Transit = "transit";
    public const string Files = "fichiers";

    private readonly Repository _repo;
    private readonly Metamodel _meta;
    private readonly DataFlowRule _rule;
    private readonly ILookup<string, Relationship> _from;
    private readonly ILookup<string, Relationship> _to;
    private readonly HashSet<string> _apps, _data, _technical, _infra, _shares, _profiles, _carriers, _flowTypes, _decomposition, _locations;

    public LineageReader(Repository repo)
    {
        _repo = repo;
        _meta = repo.Metamodel;
        _rule = new DataFlowRule(repo);
        var active = repo.ActiveRelationships.Where(r => Exists(r.SourceId) && Exists(r.TargetId)).ToList();
        _from = active.ToLookup(r => r.SourceId);
        _to = active.ToLookup(r => r.TargetId);
        _apps = _meta.TypeKeysWith(Roles.Application);
        _data = _meta.TypeKeysWith(Roles.Data);
        _technical = _meta.TypeKeysWith(Roles.TechnicalFlow);
        _shares = _meta.TypeKeysWith(Roles.FileShare);
        _profiles = _meta.TypeKeysWith(Roles.Workstation);
        _infra = _meta.TypeKeysWith(Roles.Infrastructure);
        _carriers = _meta.TypeKeysWith(Roles.DataCarrier);
        _flowTypes = _meta.RelTypeKeysWith(Roles.Flow);
        _decomposition = _meta.RelTypeKeysWith(Roles.Decomposition);
        _locations = _meta.Transcoding.Elements.Where(r => r.ArchimateType == "Location").Select(r => r.TypeKey).ToHashSet();
    }

    private bool Exists(string id) =>
        (_repo.Objects.TryGetValue(id, out var o) && !o.Archived) || (_repo.Relationships.TryGetValue(id, out var r) && !r.Archived);

    private ArchObject? Obj(string id) => _repo.Objects.TryGetValue(id, out var o) && !o.Archived ? o : null;
    private bool Is(string id, HashSet<string> types) => Obj(id) is { } o && types.Contains(o.TypeKey);
    private bool IsApp(string id) => Is(id, _apps);
    private bool IsData(string id) => Is(id, _data);
    private bool IsTechnical(string id) => Is(id, _technical);
    private bool IsProfile(string id) => Is(id, _profiles);
    private bool IsHost(string id) => Is(id, _infra) && !IsProfile(id) && !IsTechnical(id);
    /// <summary>Flux de données : la règle unique du cœur (<see cref="DataFlowRule"/>).</summary>
    private bool IsFlow(Relationship r) => _rule.IsDataFlow(r);

    private IEnumerable<Relationship> Links(string id) => _from[id].Concat(_to[id]);
    private static string Other(Relationship r, string id) => r.SourceId == id ? r.TargetId : r.SourceId;
    private IEnumerable<string> Neighbours(string id) => Links(id).Select(r => Other(r, id)).Distinct();
    private static string? P(Relationship r, string key) => r.Properties.FirstOrDefault(p => p.Key == key)?.Value is { Length: > 0 } v ? v : null;
    private static string? P(ArchObject o, string key) => o.Get(key) is { Length: > 0 } v ? v : null;
    private string Label(ArchObject o) => _meta.Type(o.TypeKey)?.Label ?? o.TypeKey;

    // ------------------------------------------------------------ Briques

    private IEnumerable<Relationship> AllFlows() => _repo.ActiveRelationships.Where(IsFlow);
    private List<string> Carried(Relationship flow) => Neighbours(flow.Id).Where(IsData).ToList();

    private List<LineageStep> Steps(Relationship flow) => Links(flow.Id).Where(r => IsTechnical(Other(r, flow.Id)))
        .Select(r => new LineageStep(Other(r, flow.Id), int.TryParse(P(r, CatalogueModel.TraceOrder), out var n) ? n : null, P(r, CatalogueModel.InfoStatus)))
        .OrderBy(s => s.Order ?? int.MaxValue).ToList();

    private string? TechnicalApp(string ftId) => Neighbours(ftId).FirstOrDefault(IsApp);

    /// <summary>Accès d'une application aux données : rôle du catalogue s'il est posé, sinon accès connu d'Archi.</summary>
    private IEnumerable<(string DataId, string Role, bool Catalogue)> Accesses(string appId) =>
        Links(appId).Where(r => IsData(Other(r, appId)) && !_flowTypes.Contains(r.TypeKey))
            .Select(r => (Other(r, appId), P(r, CatalogueModel.DataRole) ?? "Accès (hors catalogue)", P(r, CatalogueModel.DataRole) is not null));

    private List<LineageRole> RolesOn(string dataId) =>
        Links(dataId).Where(r => IsApp(Other(r, dataId)) && !_flowTypes.Contains(r.TypeKey))
            .Select(r => new LineageRole(Other(r, dataId), Obj(Other(r, dataId))!.Name, P(r, CatalogueModel.DataRole) ?? "Accès (hors catalogue)", P(r, CatalogueModel.DataRole) is not null))
            .GroupBy(x => x.AppId).Select(g => g.OrderBy(x => RoleRank(x.Role)).First())
            .OrderBy(x => RoleRank(x.Role)).ThenBy(x => x.AppName).ToList();

    private static int RoleRank(string role) => role switch { CatalogueModel.RoleMaster => 0, CatalogueModel.RoleRelay => 1, CatalogueModel.RoleConsumer => 2, _ => 3 };

    /// <summary>Hébergement direct d'une application : nœuds, équipements, logiciels système et partages qui lui sont reliés.</summary>
    private List<string> HostsOf(string appId) => Neighbours(appId).Where(IsHost).ToList();

    private List<string> ProfilesOf(string appId) => Neighbours(appId).Where(IsProfile).ToList();

    /// <summary>Supports (artefacts) d'une donnée et l'endroit où chacun est posé : partage ou serveur.</summary>
    private IEnumerable<(string HostId, string File)> FilesOf(string dataId) =>
        Neighbours(dataId).Where(id => Is(id, _carriers)).SelectMany(carrier => Neighbours(carrier).Where(IsHost).Select(h => (h, Obj(carrier)!.Name)));

    /// <summary>Données dérivées : portées par un flux qui part d'une application tenant la donnée vers une application qui en est maître.</summary>
    private HashSet<string> Derived(string dataId)
    {
        var holders = RolesOn(dataId).Select(r => r.AppId).ToHashSet();
        var result = new HashSet<string>();
        foreach (var f in AllFlows().Where(f => holders.Contains(f.SourceId)))
            foreach (var x in Carried(f).Where(x => x != dataId))
                if (RolesOn(x).Any(r => r.AppId == f.TargetId && r.Role == CatalogueModel.RoleMaster)) result.Add(x);
        return result;
    }

    // ------------------------------------------------------------ Parcours

    public Lineage Read(string id, bool derived)
    {
        var dataSet = new HashSet<string>();
        var derivedSet = new HashSet<string>();
        var flowSet = new HashSet<string>();
        var appSet = new HashSet<string>();
        var storing = new HashSet<string>();   // applications qui tiennent la donnée (accès ou réception par flux)
        var hostFocus = new HashSet<string>(); // hôtes à montrer même sans application (partage, serveur regardé)
        LineageFocus focus;

        if (_repo.Relationships.TryGetValue(id, out var rel) && !rel.Archived)
        {
            if (!IsFlow(rel)) return Empty(new LineageFocus(id, rel.Name ?? "Relation", "relation", _meta.RelType(rel.TypeKey)?.Label ?? rel.TypeKey, false,
                "Le parcours se lit depuis une donnée, une application, un flux applicatif, un flux technique, un serveur, un partage ou un profil de poste."));
            focus = new LineageFocus(id, rel.Name ?? $"{Obj(rel.SourceId)!.Name} → {Obj(rel.TargetId)!.Name}", "flux", "Flux applicatif", true, null);
            flowSet.Add(id);
            dataSet.UnionWith(Carried(rel));
        }
        else if (Obj(id) is { } o)
        {
            var kind = IsData(id) ? "donnee" : IsApp(id) ? "application" : IsTechnical(id) ? "flux-technique" : IsProfile(id) ? "poste"
                : Is(id, _shares) ? "partage" : IsHost(id) ? "infrastructure" : "autre";
            focus = new LineageFocus(id, o.Name, kind, Label(o), kind != "autre",
                kind == "autre" ? "Le parcours se lit depuis une donnée, une application, un flux, un serveur, un partage ou un profil de poste." : null);
            switch (kind)
            {
                case "donnee":
                    dataSet.Add(id);
                    if (derived) { derivedSet = Derived(id); dataSet.UnionWith(derivedSet); }
                    foreach (var f in AllFlows()) if (Carried(f).Any(dataSet.Contains)) flowSet.Add(f.Id);
                    foreach (var d in dataSet) appSet.UnionWith(RolesOn(d).Select(r => r.AppId));
                    break;
                case "application":
                    appSet.Add(id);
                    dataSet.UnionWith(Accesses(id).Select(a => a.DataId));
                    foreach (var f in AllFlows().Where(f => f.SourceId == id || f.TargetId == id)) { flowSet.Add(f.Id); dataSet.UnionWith(Carried(f)); }
                    break;
                case "flux-technique":
                    foreach (var f in AllFlows()) if (Steps(f).Any(s => s.TechnicalId == id)) { flowSet.Add(f.Id); dataSet.UnionWith(Carried(f)); }
                    break;
                case "poste":
                    appSet.UnionWith(Neighbours(id).Where(IsApp));
                    foreach (var a in appSet.ToList()) dataSet.UnionWith(Accesses(a).Select(x => x.DataId));
                    break;
                case "partage":
                case "infrastructure":
                    hostFocus.Add(id);
                    var scope = new HashSet<string> { id };
                    // Un serveur et ce qu'il agrège (logiciels système, machines virtuelles d'une grappe).
                    // Un composant partagé par plusieurs serveurs (logiciel système commun) n'élargit pas le périmètre.
                    foreach (var r in _from[id].Where(r => _decomposition.Contains(r.TypeKey) && IsHost(r.TargetId)
                                                           && _to[r.TargetId].Count(x => _decomposition.Contains(x.TypeKey) && IsHost(x.SourceId)) == 1))
                        scope.Add(r.TargetId);
                    var hosted = scope.SelectMany(Neighbours).Where(IsApp).ToHashSet();
                    appSet.UnionWith(hosted);
                    foreach (var a in hosted) dataSet.UnionWith(Accesses(a).Select(x => x.DataId));
                    foreach (var carrier in scope.SelectMany(Neighbours).Where(x => Is(x, _carriers)))
                        dataSet.UnionWith(Neighbours(carrier).Where(IsData));
                    foreach (var f in AllFlows())
                        if (hosted.Contains(f.SourceId) || hosted.Contains(f.TargetId) || Steps(f).Any(s => TechnicalApp(s.TechnicalId) is { } ta && hosted.Contains(ta)))
                            flowSet.Add(f.Id);
                    break;
                default:
                    return Empty(focus);
            }
        }
        else return Empty(new LineageFocus(id, "?", "inconnu", "", false, "Objet introuvable."));

        // Applications : extrémités des flux retenus, plus celles qui tiennent les données.
        var flows = flowSet.Select(f => _repo.Relationships[f]).ToList();
        foreach (var f in flows) { appSet.Add(f.SourceId); appSet.Add(f.TargetId); }
        foreach (var a in appSet)
            if (Accesses(a).Any(x => dataSet.Contains(x.DataId)) || flows.Any(f => f.TargetId == a && Carried(f).Any(dataSet.Contains))) storing.Add(a);

        // Étapes techniques et leur application (bus, plateforme d'échange…), dont l'hébergement sert au transit.
        var technical = flows.SelectMany(Steps).Select(s => s.TechnicalId).Distinct().ToList();
        var hostUsage = new Dictionary<string, string>();
        var hostApps = new Dictionary<string, HashSet<string>>();
        var hostFiles = new Dictionary<string, List<string>>();
        void Host(string h, string usage, string? app)
        {
            hostUsage[h] = hostUsage.TryGetValue(h, out var u) ? Stronger(u, usage) : usage;
            if (!hostApps.TryGetValue(h, out var set)) hostApps[h] = set = new HashSet<string>();
            if (app is not null) set.Add(app);
        }
        foreach (var a in appSet) foreach (var h in HostsOf(a)) Host(h, storing.Contains(a) ? Storage : Transit, a);
        foreach (var t in technical) if (TechnicalApp(t) is { } ta) foreach (var h in HostsOf(ta)) Host(h, appSet.Contains(ta) && storing.Contains(ta) ? Storage : Transit, ta);
        foreach (var d in dataSet)
            foreach (var (h, file) in FilesOf(d))
            {
                Host(h, Files, null);
                if (!hostFiles.TryGetValue(h, out var list)) hostFiles[h] = list = new List<string>();
                if (!list.Contains(file)) list.Add(file);
            }
        foreach (var h in hostFocus) if (!hostUsage.ContainsKey(h)) Host(h, Storage, null);

        // Un logiciel système agrégé par un serveur affiché devient un détail de ce serveur.
        var details = new Dictionary<string, List<string>>();
        foreach (var h in hostUsage.Keys.ToList())
        {
            var parent = _to[h].FirstOrDefault(r => _decomposition.Contains(r.TypeKey) && hostUsage.ContainsKey(r.SourceId) && r.SourceId != h);
            if (parent is null || hostFocus.Contains(h)) continue;
            if (!details.TryGetValue(parent.SourceId, out var list)) details[parent.SourceId] = list = new List<string>();
            list.Add(Obj(h)!.Name);
            hostApps[parent.SourceId].UnionWith(hostApps[h]);
            hostUsage[parent.SourceId] = Stronger(hostUsage[parent.SourceId], hostUsage[h]);
            if (hostFiles.Remove(h, out var files)) { if (!hostFiles.TryGetValue(parent.SourceId, out var pf)) hostFiles[parent.SourceId] = pf = new(); pf.AddRange(files); }
            hostUsage.Remove(h);
        }

        var census = _repo.Workstations;
        var profileIds = storing.SelectMany(ProfilesOf).Concat(focus.Kind == "poste" ? new[] { id } : Array.Empty<string>()).Distinct().ToList();
        var result = new Lineage(
            focus,
            dataSet.Select(d => DataOf(d, derivedSet.Contains(d))).OrderBy(d => d.Derived).ThenBy(d => d.Name).ToList(),
            appSet.Select(a =>
            {
                var app = Obj(a)!;
                var roles = Accesses(a).Where(x => dataSet.Contains(x.DataId)).Select(x => x.Role).Distinct().OrderBy(RoleRank).ToList();
                return new LineageApp(a, app.Name, P(app, CatalogueModel.Code), P(app, CatalogueModel.AppZone), P(app, CatalogueModel.InfoStatus), roles,
                    HostsOf(a).Where(hostUsage.ContainsKey).Concat(HostsOf(a).SelectMany(h => _to[h].Where(r => _decomposition.Contains(r.TypeKey) && hostUsage.ContainsKey(r.SourceId)).Select(r => r.SourceId))).Distinct().ToList(),
                    ProfilesOf(a), !_rule.IsApplicative(a));
            }).OrderBy(a => a.Name).ToList(),
            flows.Select(f => new LineageFlow(f.Id, P(f, CatalogueModel.Code), f.Name ?? $"{Obj(f.SourceId)!.Name} → {Obj(f.TargetId)!.Name}", f.SourceId, f.TargetId,
                Carried(f), P(f, CatalogueModel.FlowKind), P(f, CatalogueModel.FlowFrequency), P(f, CatalogueModel.FlowMode), P(f, CatalogueModel.Lifecycle),
                P(f, CatalogueModel.InfoStatus), Steps(f))).OrderBy(f => f.Code ?? "~").ThenBy(f => f.Name).ToList(),
            technical.Select(t =>
            {
                var o = Obj(t)!;
                var app = TechnicalApp(t);
                return new LineageTechnical(t, o.Name, P(o, CatalogueModel.Code), P(o, CatalogueModel.TechStep), P(o, _meta.Settings.FlowProtocolKey), P(o, CatalogueModel.TechFormat),
                    P(o, CatalogueModel.TechOrchestration), P(o, CatalogueModel.TechZone), P(o, CatalogueModel.TechAccount), P(o, CatalogueModel.TechMonitoring),
                    P(o, CatalogueModel.InfoStatus), app, app is null ? null : Obj(app)!.Name,
                    app is null ? new List<string>() : HostsOf(app).Select(h => hostUsage.ContainsKey(h) ? h : _to[h].FirstOrDefault(r => _decomposition.Contains(r.TypeKey) && hostUsage.ContainsKey(r.SourceId))?.SourceId)
                        .Where(h => h is not null).Select(h => h!).Distinct().ToList());
            }).ToList(),
            hostUsage.Select(kv =>
            {
                var h = Obj(kv.Key)!;
                var location = Neighbours(kv.Key).Select(Obj).FirstOrDefault(x => x is not null && _locations.Contains(x.TypeKey))?.Name;
                return new LineageHost(kv.Key, h.Name, Label(h), Is(kv.Key, _shares) ? "partage" : "serveur", details.GetValueOrDefault(kv.Key, new List<string>()),
                    location, kv.Value, hostApps[kv.Key].OrderBy(a => Obj(a)!.Name).ToList(), hostFiles.GetValueOrDefault(kv.Key, new List<string>()));
            }).OrderBy(h => UsageRank(h.Usage)).ThenBy(h => h.Name).ToList(),
            profileIds.Select(p =>
            {
                var o = Obj(p)!;
                var code = P(o, CatalogueModel.Code);
                var count = code is not null ? census?.ByProfile.GetValueOrDefault(code) : null;
                return new LineageProfile(p, o.Name, code, Neighbours(p).Where(appSet.Contains).ToList(), census is null ? null : count?.Total ?? 0, count?.BySite);
            }).OrderBy(p => p.Name).ToList(),
            new List<string>(), census?.At);
        Gaps(result, storing, focus);
        return result;
    }

    private static int UsageRank(string usage) => usage switch { Storage => 0, Files => 1, _ => 2 };
    private static string Stronger(string a, string b) => UsageRank(a) <= UsageRank(b) ? a : b;

    private LineageData DataOf(string id, bool derived)
    {
        var o = Obj(id)!;
        return new LineageData(id, o.Name, Label(o), P(o, CatalogueModel.Code), P(o, CatalogueModel.DataCategory), P(o, CatalogueModel.DataSensitivity),
            P(o, CatalogueModel.DataPersonal), P(o, CatalogueModel.DataOwner), P(o, CatalogueModel.DataSteward), P(o, CatalogueModel.DataPivot),
            P(o, CatalogueModel.InfoStatus), o.Documentation, derived, RolesOn(id), _repo.DataAttributes.GetValueOrDefault(id, new List<DataAttribute>()));
    }

    /// <summary>
    /// Ce que le parcours ne sait pas dire : signalé tel quel, jamais comblé. Au périmètre « métier » (décision 012), serveurs,
    /// partages, profils de poste et étapes techniques sont retirés avant la lecture : leur absence ne tient qu'à l'accès, et
    /// l'annoncer comme un manque du référentiel serait faux. Seules restent les lacunes applicatives.
    /// </summary>
    private void Gaps(Lineage l, HashSet<string> storing, LineageFocus focus)
    {
        var scoped = _repo.Scope is not null;
        var tech = l.Technical.ToDictionary(t => t.Id);
        var unverified = new Dictionary<string, List<string>>();
        if (!scoped)
        {
            // Seul un composant s'héberge : un service ou une fonction applicative n'a pas de serveur à renseigner.
            foreach (var a in l.Applications.Where(a => a.HostIds.Count == 0 && IsApp(a.Id)))
                l.Gaps.Add($"Hébergement de {a.Name} non renseigné.");
            var noProfile = l.Applications.Where(a => storing.Contains(a.Id) && a.ProfileIds.Count == 0).Select(a => a.Name).ToList();
            if (noProfile.Count > 0 && focus.Kind is "donnee" or "application")
                l.Gaps.Add($"Accès utilisateurs non renseigné (aucun profil de poste relié) : {string.Join(", ", noProfile)}.");
        }
        foreach (var f in l.Flows.Where(_ => !scoped))
        {
            var label = f.Code ?? f.Name;
            // Un flux hors catalogue (sans code) n'a pas à décrire sa chaîne : seul le catalogue en porte.
            if (f.Steps.Count == 0) { if (f.Code is not null) l.Gaps.Add($"{label} : chaîne technique non décrite."); continue; }
            var target = l.Applications.FirstOrDefault(a => a.Id == f.TargetId);
            if (target is not null && IsApp(target.Id) && !f.Steps.Any(s => tech.GetValueOrDefault(s.TechnicalId)?.AppId == f.TargetId))
            {
                var last = tech.GetValueOrDefault(f.Steps[^1].TechnicalId);
                l.Gaps.Add($"{label} : la chaîne technique n'atteint pas {target.Name} (dernière étape : {last?.Name ?? "?"}{(last?.Step is { } st ? $", {st.ToLowerInvariant()}" : "")}).");
            }
            foreach (var s in f.Steps.Where(s => !CatalogueModel.IsVerified(s.Status)))
            {
                var key = $"{tech.GetValueOrDefault(s.TechnicalId)?.Name ?? "une étape"} ({s.Status})";
                if (!unverified.TryGetValue(key, out var flows)) unverified[key] = flows = new List<string>();
                flows.Add(label);
            }
        }
        // Un passage non vérifié est regroupé par étape : « Passage non vérifié par le bus (Proposé) : FA-001, FA-003… ».
        foreach (var (step, flows) in unverified)
            l.Gaps.Add($"Passage non vérifié par {step} : {string.Join(", ", flows)}.");
        foreach (var d in l.Data.Where(d => !d.Derived && d.Code is not null))
        {
            var outside = d.Roles.Where(r => !r.Catalogue).Select(r => r.AppName).ToList();
            if (outside.Count > 0) l.Gaps.Add($"{d.Name} : accès connus d'Archi mais absents du catalogue ({string.Join(", ", outside)}).");
        }
    }

    private static Lineage Empty(LineageFocus focus) => new(focus, new(), new(), new(), new(), new(), new(), new(), null);
}
