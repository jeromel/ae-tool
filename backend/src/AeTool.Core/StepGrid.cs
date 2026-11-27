namespace AeTool.Core;

/// <summary>Un objet sous une étape, dans une couche. <c>Via</c> : par où il y arrive, avec la nature de ce maillon.</summary>
public sealed record StepItem(string Id, string Name, string TypeLabel, string? Via = null, string? ViaKind = null, string? ViaId = null)
{
    /// <summary>Capacité de l'étape par laquelle l'objet y arrive : l'écran groupe une cellule par capacité, sur la couleur de son domaine.</summary>
    public string? Capability { get; init; }
}

/// <summary>Une colonne : une étape (chaîne ou processus), ou une capacité quand la chaîne n'a pas d'étapes.</summary>
public sealed record StepColumn(string Id, string Name, string Kind, List<StepItem> Capabilities, Dictionary<string, List<StepItem>> Layers);

/// <summary>Une chaîne de valeur : ses colonnes dans l'ordre du modèle, et la colonne « Toute la chaîne » s'il y en a une.</summary>
public sealed record ChainView(string Id, string Name, bool HasSteps, bool Ordered, List<StepColumn> Columns, StepColumn? Whole);

/// <summary>Un processus découpé en étapes (niveau 2) : ses étapes en colonnes, lignes Qui, Données (avec le sens) et Applications.</summary>
public sealed record ProcessView(string Id, string Name, bool Ordered, List<StepColumn> Columns);

/// <summary>Processus hors de toute chaîne ; <c>Nature</c> : celle de sa capacité de premier niveau (`capacite.nature`).</summary>
public sealed record OffChainProcess(string Id, string Name, string? Nature, string? TopCapability);

/// <summary>Une étape touchée par l'objet désigné, avec le chemin (les applications, serveurs ou capacités par où).</summary>
public sealed record TouchedStep(string ChainId, string ColumnId, List<StepItem> Via);

/// <summary>
/// Grille « étape × couche » d'EV-06 (besoin `feature-vue-chaine-valeur-processus`, maquette `maquette-chaines-couches-impact`,
/// revue de coût de Colbert, ratifiée le 27/11). Lecteur pur, branché sur les rôles, jamais sur la clé d'un type.
/// <list type="bullet">
/// <item>Étapes d'une chaîne : les objets de rôle chaîne de valeur qu'elle compose (rôle de relation décomposition) ; ordre par
/// le rôle enchaînement. Sans étapes, les colonnes sont les capacités qui servent la chaîne, sans ordre.</item>
/// <item>Capacités : celles reliées à l'étape. Une capacité reliée à la chaîne elle-même la sert entière (« Toute la chaîne »).</item>
/// <item>Processus : ceux reliés à une capacité de l'étape ou à une de ses sous-capacités (descente) ; « via » la sous-capacité.</item>
/// <item>Applications : reliées à une capacité de l'étape (ou sous-capacité), ou à un processus de l'étape (« via » le processus).</item>
/// <item>Serveurs : ceux qui hébergent une application de l'étape, par la règle unique `HostedApplications` (« via » l'application).</item>
/// <item>Projets : associés à une capacité de l'étape (impact qualifié ou relation), « via » la capacité.</item>
/// </list>
/// L'impact d'un objet est l'index inverse de cette grille : aucun second parcours du graphe (revue de Colbert §3).
/// </summary>
public sealed class StepGrid
{
    public const string Capacities = "capacites", Processes = "processus", Applications = "applications", Servers = "serveurs", Projects = "projets";
    public const string Who = "qui", Data = "donnees";
    public const string NatureProperty = "capacite.nature";
    public static readonly string[] LayerKeys = { Capacities, Processes, Applications, Servers, Projects };

    private readonly Repository _repo;
    private readonly Metamodel _m;
    private readonly ILookup<string, Relationship> _from, _to;
    private readonly HashSet<string> _chains, _processes, _capabilities, _apps, _projects, _who, _data, _decomposition, _sequence;
    private readonly Dictionary<string, List<string>> _hostsOfApp = new();
    private readonly Dictionary<string, string> _parentCapability = new();

    public bool ServersAvailable { get; }
    public List<ChainView> Chains { get; } = new();
    public List<OffChainProcess> OffChain { get; } = new();
    public List<ProcessView> ProcessViews { get; } = new();
    /// <summary>Vrai si `capacite.nature` est posée sur au moins une capacité : le bloc hors chaîne se découpe alors.</summary>
    public bool NatureKnown { get; }

    public StepGrid(Repository repo)
    {
        _repo = repo;
        _m = repo.Metamodel;
        var active = repo.ActiveRelationships.Where(r => Active(r.SourceId) && Active(r.TargetId)).ToList();
        _from = active.ToLookup(r => r.SourceId);
        _to = active.ToLookup(r => r.TargetId);
        _chains = _m.TypeKeysWith(Roles.ValueStream);
        _processes = _m.TypeKeysWith(Roles.Process);
        _capabilities = _m.TypeKeysWith(Roles.Capability);
        _apps = _m.TypeKeysWith(Roles.Application);
        _projects = _m.TypeKeysWith(Roles.Project);
        _who = _m.TypeKeysWith(Roles.Who);
        _data = _m.TypeKeysWith(Roles.Data);
        _decomposition = _m.RelTypeKeysWith(Roles.Decomposition);
        _sequence = _m.RelTypeKeysWith(Roles.Sequence);
        // Périmètre « métier » (décision 012) : la couche serveurs n'existe pas, et rien ne la laisse deviner (C9).
        ServersAvailable = repo.Scope is null;
        if (ServersAvailable)
        {
            var hosting = new HostedApplications(repo);
            foreach (var host in repo.ActiveObjects.Where(o => hosting.IsHost(o.Id) && _m.ConceptOf(o.TypeKey) is "Node" or "Device"))
                foreach (var app in hosting.Of(host.Id))
                    (_hostsOfApp.TryGetValue(app, out var l) ? l : _hostsOfApp[app] = new List<string>()).Add(host.Id);
        }
        foreach (var r in active.Where(r => _decomposition.Contains(r.TypeKey) && Is(r.SourceId, _capabilities) && Is(r.TargetId, _capabilities)))
            _parentCapability.TryAdd(r.TargetId, r.SourceId);
        NatureKnown = repo.ActiveObjects.Any(o => Is(o.Id, _capabilities) && !string.IsNullOrWhiteSpace(o.Get(NatureProperty)));
        Build();
    }

    private bool Active(string id) =>
        (_repo.Objects.TryGetValue(id, out var o) && !o.Archived) || (_repo.Relationships.TryGetValue(id, out var r) && !r.Archived);
    private bool Is(string id, HashSet<string> types) => _repo.Objects.TryGetValue(id, out var o) && !o.Archived && types.Contains(o.TypeKey);
    private ArchObject Obj(string id) => _repo.Objects[id];
    private string Label(ArchObject o) => _m.Type(o.TypeKey)?.Label ?? o.TypeKey;
    private StepItem Item(string id, string? via = null, string? viaKind = null, string? viaId = null) =>
        new(id, Obj(id).Name, Label(Obj(id)), via, viaKind, viaId);

    /// <summary>Voisins reliés par une relation qui n'est ni une décomposition ni un enchaînement.</summary>
    private IEnumerable<string> Linked(string id) =>
        _from[id].Where(r => !_decomposition.Contains(r.TypeKey) && !_sequence.Contains(r.TypeKey)).Select(r => r.TargetId)
            .Concat(_to[id].Where(r => !_decomposition.Contains(r.TypeKey) && !_sequence.Contains(r.TypeKey)).Select(r => r.SourceId))
            .Where(x => _repo.Objects.ContainsKey(x)).Distinct();

    private IEnumerable<string> Children(string id, HashSet<string> types) =>
        _from[id].Where(r => _decomposition.Contains(r.TypeKey) && Is(r.TargetId, types)).Select(r => r.TargetId).Distinct();

    /// <summary>La capacité et ses sous-capacités, chacune avec la sous-capacité par laquelle on y arrive (null : la capacité elle-même).</summary>
    private IEnumerable<(string Id, string? Sub)> Descent(string capability)
    {
        var seen = new HashSet<string> { capability };
        var queue = new Queue<string>(new[] { capability });
        yield return (capability, null);
        while (queue.Count > 0)
            foreach (var c in Children(queue.Dequeue(), _capabilities))
                if (seen.Add(c)) { queue.Enqueue(c); yield return (c, c); }
    }

    private void Build()
    {
        var reached = new HashSet<string>();
        foreach (var chain in _repo.ActiveObjects.Where(o => Is(o.Id, _chains)).ToList())
        {
            // Une étape d'une autre chaîne n'est pas une chaîne à part entière.
            if (_to[chain.Id].Any(r => _decomposition.Contains(r.TypeKey) && Is(r.SourceId, _chains))) continue;
            var steps = Children(chain.Id, _chains).ToList();
            var whole = Linked(chain.Id).Where(id => Is(id, _capabilities)).ToList();
            List<StepColumn> columns;
            bool ordered;
            if (steps.Count > 0)
            {
                (steps, ordered) = Order(steps);
                columns = steps.Select(s => Column(s, Obj(s).Name, "etape", Linked(s).Where(id => Is(id, _capabilities)).ToList(), reached)).ToList();
            }
            else
            {
                // Sans étapes : les capacités qui servent la chaîne, côte à côte, sans ordre (C2).
                ordered = false;
                columns = whole.OrderBy(c => Obj(c).Name, StringComparer.CurrentCulture)
                    .Select(c => Column(c, Obj(c).Name, "capacite", new List<string> { c }, reached)).ToList();
                whole = new List<string>();
            }
            var wholeColumn = whole.Count > 0 ? Column(chain.Id + ":toute", "Toute la chaîne", "toute", whole, reached) : null;
            Chains.Add(new ChainView(chain.Id, chain.Name, steps.Count > 0, ordered, columns, wholeColumn));
        }
        // Les chaînes renseignées d'abord : ce sont elles que le comité lit ; puis par nom.
        Chains.Sort((a, b) => a.HasSteps != b.HasSteps ? (a.HasSteps ? -1 : 1) : string.Compare(a.Name, b.Name, StringComparison.CurrentCulture));

        // Niveau 2 : les processus découpés en étapes (sous-processus composés, ordre par déclenchement).
        foreach (var p in _repo.ActiveObjects.Where(o => Is(o.Id, _processes)).OrderBy(o => o.Name, StringComparer.CurrentCulture))
        {
            var steps = Children(p.Id, _processes).ToList();
            if (steps.Count == 0) continue;
            var (ordered, isOrdered) = Order(steps);
            ProcessViews.Add(new ProcessView(p.Id, p.Name, isOrdered, ordered.Select(ProcessColumn).ToList()));
        }

        // Processus hors de toute chaîne : ni atteint par une chaîne, ni étape d'un autre processus.
        foreach (var p in _repo.ActiveObjects.Where(o => Is(o.Id, _processes) && !reached.Contains(o.Id)).OrderBy(o => o.Name, StringComparer.CurrentCulture))
        {
            if (_to[p.Id].Any(r => _decomposition.Contains(r.TypeKey) && Is(r.SourceId, _processes))) continue;
            var top = Linked(p.Id).Where(id => Is(id, _capabilities)).Select(Top).FirstOrDefault();
            var nature = top is null ? null : Obj(top).Get(NatureProperty);
            OffChain.Add(new OffChainProcess(p.Id, p.Name, string.IsNullOrWhiteSpace(nature) ? null : nature.Trim(), top is null ? null : Obj(top).Name));
        }
    }

    /// <summary>Capacité de premier niveau (racine de l'arbre des capacités) : le domaine, pour la couleur (charte §9).</summary>
    public string Top(string capability)
    {
        var seen = new HashSet<string>();
        while (_parentCapability.TryGetValue(capability, out var parent) && seen.Add(capability)) capability = parent;
        return capability;
    }

    /// <summary>
    /// Tri topologique par l'enchaînement. L'ordre n'est complet que si chaque étape enchaîne sur la suivante : une boucle,
    /// une étape sans lien d'ordre ou une bifurcation donnent « ordre incomplet » (revue de Colbert §1).
    /// </summary>
    private (List<string> Steps, bool Ordered) Order(List<string> steps)
    {
        var set = steps.ToHashSet();
        var next = steps.ToDictionary(s => s, s => _from[s].Where(r => _sequence.Contains(r.TypeKey) && set.Contains(r.TargetId)).Select(r => r.TargetId).Distinct().ToList());
        var indegree = steps.ToDictionary(s => s, _ => 0);
        foreach (var t in next.Values.SelectMany(x => x)) indegree[t]++;
        var byName = Comparer<string>.Create((a, b) => string.Compare(Obj(a).Name, Obj(b).Name, StringComparison.CurrentCulture));
        var ready = new SortedSet<string>(steps.Where(s => indegree[s] == 0), byName);
        var result = new List<string>();
        while (ready.Count > 0)
        {
            var s = ready.Min!;
            ready.Remove(s);
            result.Add(s);
            foreach (var t in next[s]) if (--indegree[t] == 0) ready.Add(t);
        }
        result.AddRange(steps.Where(s => !result.Contains(s)).OrderBy(s => Obj(s).Name, StringComparer.CurrentCulture)); // boucle
        var ordered = result.Zip(result.Skip(1)).All(p => next[p.First].Contains(p.Second));
        return (result, ordered);
    }

    private StepColumn Column(string id, string name, string kind, List<string> capabilities, HashSet<string> reached)
    {
        var caps = capabilities.Distinct().ToList();
        var layers = LayerKeys.Where(k => k != Servers || ServersAvailable).ToDictionary(k => k, _ => new List<StepItem>());
        layers[Capacities].AddRange(caps.Select(c => Item(c) with { Capability = c }));

        var processes = new Dictionary<string, StepItem>();
        var apps = new Dictionary<string, StepItem>();
        var projects = new Dictionary<string, StepItem>();
        foreach (var cap in caps)
            foreach (var (c, sub) in Descent(cap))
            {
                foreach (var n in Linked(c))
                {
                    if (Is(n, _processes)) processes.TryAdd(n, Item(n, sub is null ? null : Obj(sub).Name, sub is null ? null : "capacite", sub) with { Capability = cap });
                    else if (Is(n, _apps)) apps.TryAdd(n, Item(n, sub is null ? null : Obj(sub).Name, sub is null ? null : "capacite", sub) with { Capability = cap });
                    else if (Is(n, _projects) && sub is null) projects.TryAdd(n, Item(n, Obj(c).Name, "capacite", c) with { Capability = cap });
                }
                if (sub is null)
                    foreach (var i in _repo.Impacts.Where(i => i.ObjectId == c && i.Nature != "Hors périmètre" && Is(i.ProjectId, _projects)))
                        projects.TryAdd(i.ProjectId, Item(i.ProjectId, Obj(c).Name, "capacite", c) with { Capability = cap });
            }
        foreach (var p in processes.Keys.ToList())
        {
            reached.Add(p);
            foreach (var a in Linked(p).Where(x => Is(x, _apps)))
                apps.TryAdd(a, Item(a, Obj(p).Name, "processus", p) with { Capability = processes[p].Capability });
        }
        layers[Processes].AddRange(Sorted(processes.Values));
        layers[Applications].AddRange(Sorted(apps.Values));
        layers[Projects].AddRange(Sorted(projects.Values));
        if (ServersAvailable)
        {
            var servers = new List<StepItem>();
            foreach (var a in apps.Keys)
                foreach (var h in _hostsOfApp.GetValueOrDefault(a) ?? new List<string>())
                    servers.Add(Item(h, Obj(a).Name, "application", a) with { Capability = apps[a].Capability });
            layers[Servers].AddRange(servers.OrderBy(s => s.Name, StringComparer.CurrentCulture).ThenBy(s => s.Via, StringComparer.CurrentCulture));
        }
        return new StepColumn(id, name, kind, layers[Capacities], layers);
    }

    /// <summary>
    /// Colonne d'une étape de processus (grille ratifiée du 26/11, C7) : qui la porte, quelles données elle lit ou écrit (le
    /// sens toujours dit), quelles applications la servent (directement, ou par un service applicatif, « via » le service).
    /// </summary>
    private StepColumn ProcessColumn(string step)
    {
        var who = Linked(step).Where(x => Is(x, _who)).Select(x => Item(x));
        var data = new List<StepItem>();
        foreach (var r in _from[step].Concat(_to[step]).Where(r => r.AccessType is not null || IsAccess(r)))
        {
            var other = r.SourceId == step ? r.TargetId : r.SourceId;
            if (Is(other, _data)) data.Add(Item(other, AccessWord(r.AccessType), "acces"));
        }
        var apps = new Dictionary<string, StepItem>();
        foreach (var n in Linked(step))
        {
            if (Is(n, _apps)) apps.TryAdd(n, Item(n));
            else if (_repo.Objects.TryGetValue(n, out var svc) && _m.ConceptOf(svc.TypeKey) == "ApplicationService")
                foreach (var a in Linked(n).Where(x => Is(x, _apps))) apps.TryAdd(a, Item(a, svc.Name, "service", n));
        }
        var layers = new Dictionary<string, List<StepItem>>
        {
            [Who] = who.OrderBy(i => i.Name, StringComparer.CurrentCulture).ToList(),
            [Data] = data.DistinctBy(d => d.Id + d.Via).OrderBy(d => d.Name, StringComparer.CurrentCulture).ToList(),
            [Applications] = Sorted(apps.Values).ToList(),
        };
        return new StepColumn(step, Obj(step).Name, "etape", new List<StepItem>(), layers);
    }

    private bool IsAccess(Relationship r) => _m.Transcoding.Relations.Any(t => t.TypeKey == r.TypeKey && t.ArchimateType == "Access");

    /// <summary>Le sens d'un accès, en mots : écrit, lu, lu et écrit, ou utilisé quand le modèle ne le dit pas.</summary>
    public static string AccessWord(string? accessType) => accessType switch
    {
        "Write" => "écrit", "Read" => "lu", "ReadWrite" => "lu et écrit", _ => "utilisé",
    };

    /// <summary>Directs d'abord, puis « via », par ordre alphabétique dans chaque groupe (maquette §2).</summary>
    private static IEnumerable<StepItem> Sorted(IEnumerable<StepItem> items) =>
        items.OrderBy(i => i.Via is null ? 0 : 1).ThenBy(i => i.Name, StringComparer.CurrentCulture);

    /// <summary>Couche où se lit l'impact d'un objet : serveur, application ou projet ; null pour un autre objet (maquette §7).</summary>
    public string? ImpactLayer(string objectId)
    {
        if (Is(objectId, _apps)) return Applications;
        if (Is(objectId, _projects)) return Projects;
        if (ServersAvailable && _hostsOfApp.Values.Any(l => l.Contains(objectId))) return Servers;
        if (ServersAvailable && _repo.Objects.TryGetValue(objectId, out var o) && _m.ConceptOf(o.TypeKey) is "Node" or "Device") return Servers;
        return null;
    }

    /// <summary>
    /// Index inverse : les étapes (et la colonne « Toute la chaîne ») où figure l'objet, avec le chemin. Pour un serveur, les
    /// applications par lesquelles il touche l'étape ; pour un projet, les capacités ; pour une application, le processus ou
    /// la sous-capacité quand elle n'est pas directe.
    /// </summary>
    public List<TouchedStep> Touched(string objectId)
    {
        var layer = ImpactLayer(objectId);
        var result = new List<TouchedStep>();
        if (layer is null) return result;
        foreach (var chain in Chains)
            foreach (var column in chain.Columns.Concat(chain.Whole is null ? Array.Empty<StepColumn>() : new[] { chain.Whole }))
            {
                var hits = column.Layers[layer].Where(i => i.Id == objectId).ToList();
                if (hits.Count == 0) continue;
                var via = layer == Applications
                    ? hits.Where(h => h.ViaId is not null).Select(h => Item(h.ViaId!)).ToList()
                    : hits.Where(h => h.ViaId is not null).Select(h => Item(h.ViaId!)).DistinctBy(i => i.Id).OrderBy(i => i.Name, StringComparer.CurrentCulture).ToList();
                result.Add(new TouchedStep(chain.Id, column.Id, via));
            }
        return result;
    }

    /// <summary>Processus hors chaîne touchés par l'objet : une application qui le sert, ou un serveur d'une telle application.</summary>
    public List<string> TouchedOffChain(string objectId)
    {
        var layer = ImpactLayer(objectId);
        if (layer is not (Applications or Servers)) return new List<string>();
        var apps = layer == Applications ? new HashSet<string> { objectId }
            : _hostsOfApp.Where(kv => kv.Value.Contains(objectId)).Select(kv => kv.Key).ToHashSet();
        return OffChain.Where(p => Linked(p.Id).Any(apps.Contains)).Select(p => p.Id).ToList();
    }
}
