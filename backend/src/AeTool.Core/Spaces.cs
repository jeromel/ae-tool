namespace AeTool.Core;

// Espaces de lecture par couche (capacités, applications, flux, données, infrastructure).
// Chaque espace est une projection du graphe, calculée à la demande : rien n'est stocké en plus.

public sealed record LinkedApplication(string Id, string Name, string Via);
public sealed record CapabilityNode(string Id, string Name, List<CapabilityNode> Children, List<LinkedApplication> Applications);
public sealed record ApplicationRow(string Id, string Name, string Source, int Relations, int Flows, int Technical, int Data,
    int Capabilities, int Projects, int? Cm, string? Decision);
public sealed record FlowRow(string Id, string? Name, string SourceId, string SourceName, string TargetId, string TargetName,
    string? Protocol, string? Encryption, List<string> Carried);
public sealed record DataRow(string Id, string Name, string TypeLabel, int Applications, int Artifacts);
public sealed record InfrastructureRow(string Id, string Name, string TypeLabel, int Applications, int Relations);

public sealed class SpaceReader
{
    private const string Related = "relation";
    private const string Nested = "vue";

    private readonly Repository _repo;
    private readonly Analyzer _an;
    private readonly HashSet<string> _appTypes;

    public SpaceReader(Repository repo, Analyzer analyzer)
    {
        _repo = repo;
        _an = analyzer;
        _appTypes = repo.Metamodel.Settings.ApplicationTypeKeys.ToHashSet();
    }

    private bool IsApplication(string id) => _repo.Objects.TryGetValue(id, out var o) && !o.Archived && _appTypes.Contains(o.TypeKey);
    private bool IsCapability(string id) => _repo.Objects.TryGetValue(id, out var o) && !o.Archived && o.TypeKey == _repo.Metamodel.Settings.CapabilityTypeKey;
    private string LayerOf(string id) => _repo.Metamodel.LayerOf(_repo.Objects[id].TypeKey);
    private IEnumerable<string> Neighbours(string id) =>
        _an.Outgoing(id).Select(r => r.TargetId).Concat(_an.Incoming(id).Select(r => r.SourceId)).Distinct();

    /// <summary>
    /// Applications rattachées à chaque capacité : par une relation directe, ou parce qu'une vue les y imbrique.
    /// Une application imbriquée dans une sous-capacité n'est pas répétée sur les capacités qui englobent celle-ci.
    /// </summary>
    public Dictionary<string, List<LinkedApplication>> ApplicationsByCapability()
    {
        var via = new Dictionary<(string Capability, string Application), string>();
        foreach (var capability in _repo.ActiveObjects.Where(o => IsCapability(o.Id)))
            foreach (var other in Neighbours(capability.Id).Where(IsApplication))
                via[(capability.Id, other)] = Related;

        var pairs = _an.NestingPairs().ToList();
        var capabilityInside = pairs.Where(p => IsCapability(p.Ancestor) && IsCapability(p.Descendant)).ToLookup(p => p.Ancestor, p => p.Descendant);
        var nestedApps = pairs.Where(p => IsCapability(p.Ancestor) && IsApplication(p.Descendant)).ToHashSet();
        foreach (var (capability, application) in nestedApps)
        {
            if (capabilityInside[capability].Any(inner => nestedApps.Contains((inner, application)))) continue;
            via[(capability, application)] = via.ContainsKey((capability, application)) ? $"{Related} et {Nested}" : Nested;
        }
        return via.GroupBy(kv => kv.Key.Capability).ToDictionary(g => g.Key,
            g => g.Select(kv => new LinkedApplication(kv.Key.Application, _repo.Objects[kv.Key.Application].Name, kv.Value)).OrderBy(a => a.Name).ToList());
    }

    /// <summary>Arbre des capacités : une capacité est fille de celle qui la compose ou l'agrège. Les racines n'ont pas de parent.</summary>
    public List<CapabilityNode> CapabilityMap()
    {
        var capabilities = _repo.ActiveObjects.Where(o => IsCapability(o.Id)).ToDictionary(o => o.Id);
        var children = new Dictionary<string, List<string>>();
        var hasParent = new HashSet<string>();
        foreach (var r in _repo.ActiveRelationships)
        {
            var type = _repo.Metamodel.RelType(r.TypeKey)?.ArchimateType;
            if (type is not ("Composition" or "Aggregation")) continue;
            if (!capabilities.ContainsKey(r.SourceId) || !capabilities.ContainsKey(r.TargetId) || r.SourceId == r.TargetId) continue;
            if (!hasParent.Add(r.TargetId)) continue; // une capacité déjà rattachée garde son premier parent
            if (!children.TryGetValue(r.SourceId, out var list)) children[r.SourceId] = list = new List<string>();
            list.Add(r.TargetId);
        }
        var apps = ApplicationsByCapability();
        var visited = new HashSet<string>();
        CapabilityNode Build(string id)
        {
            visited.Add(id);
            var kids = children.GetValueOrDefault(id, new List<string>()).Where(k => !visited.Contains(k)) // garde-fou contre un cycle
                .Select(Build).OrderBy(n => n.Name).ToList();
            return new CapabilityNode(id, capabilities[id].Name, kids, apps.GetValueOrDefault(id, new List<LinkedApplication>()));
        }
        return capabilities.Keys.Where(id => !hasParent.Contains(id)).Select(Build).OrderBy(n => n.Name).ToList();
    }

    public List<ApplicationRow> Applications()
    {
        var capabilityCount = ApplicationsByCapability().SelectMany(kv => kv.Value).GroupBy(a => a.Id).ToDictionary(g => g.Key, g => g.Count());
        var projectCount = _an.Projects().SelectMany(p => _an.ImpactsOf(p.Id)).GroupBy(i => i.ObjectId).ToDictionary(g => g.Key, g => g.Count());
        return _repo.ActiveObjects.Where(o => _appTypes.Contains(o.TypeKey)).OrderBy(o => o.Name).Select(o =>
        {
            var relations = _an.Outgoing(o.Id).Concat(_an.Incoming(o.Id)).ToList();
            var neighbours = Neighbours(o.Id).ToList();
            _repo.Assessments.TryGetValue(o.Id, out var a);
            var decision = a is null ? null : a.RetainedDecision ?? ScoringEngine.Evaluate(_repo.Metamodel.Scoring, a, _an.IsRetired(o)).ComputedDecision;
            return new ApplicationRow(o.Id, o.Name, o.Source, relations.Count,
                relations.Count(r => _repo.Metamodel.RelType(r.TypeKey)?.ArchimateType == "Flow"),
                neighbours.Count(n => LayerOf(n) == "technique" && _repo.Objects[n].TypeKey != "Artifact"),
                neighbours.Count(n => LayerOf(n) == "donnees" || _repo.Objects[n].TypeKey == "Artifact"),
                capabilityCount.GetValueOrDefault(o.Id), projectCount.GetValueOrDefault(o.Id), a?.Cm, decision);
        }).ToList();
    }

    /// <summary>Flux entre objets, avec protocole, chiffrement et objets rattachés au flux (les données qu'il transporte).</summary>
    public List<FlowRow> Flows()
    {
        var settings = _repo.Metamodel.Settings;
        var flows = _repo.ActiveRelationships
            .Where(r => _repo.Metamodel.RelType(r.TypeKey)?.ArchimateType == "Flow" && IsActive(r.SourceId) && IsActive(r.TargetId)).ToList();
        var ids = flows.Select(f => f.Id).ToHashSet();
        var carried = new Dictionary<string, List<string>>();
        foreach (var r in _repo.ActiveRelationships)
        {
            var (flowId, objectId) = ids.Contains(r.TargetId) ? (r.TargetId, r.SourceId) : ids.Contains(r.SourceId) ? (r.SourceId, r.TargetId) : (null, null);
            if (flowId is null || !IsActive(objectId!)) continue;
            if (!carried.TryGetValue(flowId, out var list)) carried[flowId] = list = new List<string>();
            list.Add(_repo.Objects[objectId!].Name);
        }
        string? Property(Relationship r, string key) => r.Properties.FirstOrDefault(p => p.Key == key)?.Value is { Length: > 0 } v ? v : null;
        return flows.Select(f => new FlowRow(f.Id, f.Name, f.SourceId, _repo.Objects[f.SourceId].Name, f.TargetId, _repo.Objects[f.TargetId].Name,
                Property(f, settings.FlowProtocolKey), Property(f, settings.FlowEncryptionKey), carried.GetValueOrDefault(f.Id, new List<string>())))
            .OrderBy(f => f.SourceName).ThenBy(f => f.TargetName).ToList();
    }

    private bool IsActive(string id) => _repo.Objects.TryGetValue(id, out var o) && !o.Archived;

    /// <summary>Objets de la couche données, avec les applications qui y accèdent directement ou par un artefact qui les réalise.</summary>
    public List<DataRow> Data() =>
        _repo.ActiveObjects.Where(o => _repo.Metamodel.LayerOf(o.TypeKey) == "donnees").OrderBy(o => o.Name).Select(o =>
        {
            var neighbours = Neighbours(o.Id).ToList();
            var artifacts = neighbours.Where(n => _repo.Objects[n].TypeKey == "Artifact").ToList();
            var applications = neighbours.Concat(artifacts.SelectMany(Neighbours)).Where(IsApplication).Distinct().Count();
            return new DataRow(o.Id, o.Name, _repo.Metamodel.Type(o.TypeKey)?.Label ?? o.TypeKey, applications, artifacts.Count);
        }).ToList();

    /// <summary>Objets techniques, avec le nombre d'applications qu'ils portent en aval, jusqu'à trois relations de distance.</summary>
    public List<InfrastructureRow> Infrastructure() =>
        _repo.ActiveObjects.Where(o => _repo.Metamodel.LayerOf(o.TypeKey) == "technique" && o.TypeKey != "Artifact").OrderBy(o => o.Name).Select(o =>
            new InfrastructureRow(o.Id, o.Name, _repo.Metamodel.Type(o.TypeKey)?.Label ?? o.TypeKey,
                _an.Impact(o.Id, 3, "aval").Count(r => _appTypes.Contains(r.TypeKey)),
                _an.Outgoing(o.Id).Count() + _an.Incoming(o.Id).Count())).ToList();
}
