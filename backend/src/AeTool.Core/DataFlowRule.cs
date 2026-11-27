namespace AeTool.Core;

/// <summary>
/// Règle unique « flux de données » (note d'étapes de Colbert §3, arbitrage PO du 27/11/2026). Est un flux de données toute
/// relation au rôle « flux » dont une extrémité au moins est d'un type transcodé en un concept applicatif d'ArchiMate. On
/// s'appuie sur le concept transcodé, pas sur la couche : une couche se renomme, un type peut en changer. Un flux entre deux
/// étapes, deux chaînes de valeur ou deux processus métier n'en est pas un. Seul un flux qui touche un composant, une
/// collaboration, une interface ou un service applicatif doit porter un protocole : un flux logique entre fonctions n'en a pas.
/// L'espace Flux, le parcours d'une donnée et les contrôles du catalogue lisent tous cette règle, jamais une copie.
/// </summary>
public sealed class DataFlowRule
{
    /// <summary>Concepts applicatifs d'ArchiMate : une extrémité de ce type fait d'un flux un flux de données.</summary>
    public static readonly IReadOnlySet<string> ApplicationConcepts = new HashSet<string>
    {
        "ApplicationComponent", "ApplicationCollaboration", "ApplicationInterface", "ApplicationFunction",
        "ApplicationInteraction", "ApplicationProcess", "ApplicationEvent", "ApplicationService",
    };

    /// <summary>Concepts dont un flux doit porter protocole et chiffrement.</summary>
    public static readonly IReadOnlySet<string> ProtocolConcepts = new HashSet<string>
    {
        "ApplicationComponent", "ApplicationCollaboration", "ApplicationInterface", "ApplicationService",
    };

    private readonly Repository _repo;
    private readonly HashSet<string> _flowTypes, _applicative, _protocol;

    public DataFlowRule(Repository repo)
    {
        _repo = repo;
        var m = repo.Metamodel;
        _flowTypes = m.RelTypeKeysWith(Roles.Flow);
        // Concept : la lecture unique du métamodèle (ConceptOf). Le rôle « application » l'emporte sur le concept, comme dans
        // la lecture de l'interface (natureOf) : un type au rôle application est toujours une extrémité applicative.
        var applications = m.TypeKeysWith(Roles.Application);
        _applicative = TranscodedTo(m, ApplicationConcepts);
        _applicative.UnionWith(applications);
        _protocol = TranscodedTo(m, ProtocolConcepts);
        _protocol.UnionWith(applications);
    }

    private static HashSet<string> TranscodedTo(Metamodel m, IReadOnlySet<string> concepts) =>
        m.ObjectTypes.Where(t => m.ConceptOf(t.Key) is { } c && concepts.Contains(c)).Select(t => t.Key).ToHashSet();

    private bool Is(string id, HashSet<string> types) => _repo.Objects.TryGetValue(id, out var o) && !o.Archived && types.Contains(o.TypeKey);
    private bool Active(string id) => _repo.Objects.TryGetValue(id, out var o) && !o.Archived;

    /// <summary>Vrai si l'objet est d'un type transcodé en un concept applicatif.</summary>
    public bool IsApplicative(string objectId) => Is(objectId, _applicative);

    /// <summary>Flux de données : rôle « flux », deux extrémités actives, dont une au moins applicative.</summary>
    public bool IsDataFlow(Relationship r) =>
        !r.Archived && _flowTypes.Contains(r.TypeKey) && Active(r.SourceId) && Active(r.TargetId) && (IsApplicative(r.SourceId) || IsApplicative(r.TargetId));

    /// <summary>Flux de données qui doit porter protocole et chiffrement.</summary>
    public bool ExpectsProtocol(Relationship r) => IsDataFlow(r) && (Is(r.SourceId, _protocol) || Is(r.TargetId, _protocol));

    /// <summary>Tous les flux de données actifs.</summary>
    public IEnumerable<Relationship> DataFlows => _repo.ActiveRelationships.Where(IsDataFlow);
}
