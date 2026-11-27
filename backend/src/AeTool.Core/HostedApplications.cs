namespace AeTool.Core;

/// <summary>
/// « Applications hébergées par un serveur » : une seule règle du cœur (revue de coût d'EV-06 de Colbert, §3). Un serveur
/// compte avec ce qu'il agrège en propre (logiciels système, machines virtuelles d'une grappe). Un composant partagé par
/// plusieurs serveurs, comme un logiciel système commun, n'élargit pas le périmètre : sinon tomber un serveur ferait
/// tomber les applications de tous les autres. Le parcours d'une donnée et la grille d'EV-06 la lisent toutes deux.
/// </summary>
public sealed class HostedApplications
{
    private readonly Repository _repo;
    private readonly ILookup<string, Relationship> _from, _to;
    private readonly HashSet<string> _apps, _infra, _shares, _profiles, _technical, _decomposition;

    public HostedApplications(Repository repo)
    {
        _repo = repo;
        var m = repo.Metamodel;
        var active = repo.ActiveRelationships.Where(r => Exists(r.SourceId) && Exists(r.TargetId)).ToList();
        _from = active.ToLookup(r => r.SourceId);
        _to = active.ToLookup(r => r.TargetId);
        _apps = m.TypeKeysWith(Roles.Application);
        _infra = m.TypeKeysWith(Roles.Infrastructure);
        _shares = m.TypeKeysWith(Roles.FileShare);
        _profiles = m.TypeKeysWith(Roles.Workstation);
        _technical = m.TypeKeysWith(Roles.TechnicalFlow);
        _decomposition = m.RelTypeKeysWith(Roles.Decomposition);
    }

    private bool Exists(string id) =>
        (_repo.Objects.TryGetValue(id, out var o) && !o.Archived) || (_repo.Relationships.TryGetValue(id, out var r) && !r.Archived);
    private bool Is(string id, HashSet<string> types) => _repo.Objects.TryGetValue(id, out var o) && !o.Archived && types.Contains(o.TypeKey);
    private IEnumerable<string> Neighbours(string id) =>
        _from[id].Select(r => r.TargetId).Concat(_to[id].Select(r => r.SourceId)).Distinct();

    /// <summary>Élément d'hébergement : infrastructure, hors profil de poste et hors étape technique du catalogue.</summary>
    public bool IsHost(string id) => Is(id, _infra) && !Is(id, _profiles) && !Is(id, _technical);

    /// <summary>
    /// Le serveur et ce qu'il agrège en propre ; un élément agrégé par plusieurs serveurs n'y entre pas. Vide si l'objet
    /// n'est pas un élément d'hébergement : une application n'héberge rien.
    /// </summary>
    public HashSet<string> Scope(string hostId)
    {
        if (!IsHost(hostId) && !Is(hostId, _shares)) return new HashSet<string>();
        var scope = new HashSet<string> { hostId };
        foreach (var r in _from[hostId].Where(r => _decomposition.Contains(r.TypeKey) && IsHost(r.TargetId)
                                                   && _to[r.TargetId].Count(x => _decomposition.Contains(x.TypeKey) && IsHost(x.SourceId)) == 1))
            scope.Add(r.TargetId);
        return scope;
    }

    /// <summary>Applications reliées au serveur ou à ce qu'il agrège en propre.</summary>
    public HashSet<string> Of(string hostId) => Scope(hostId).SelectMany(Neighbours).Where(id => Is(id, _apps)).ToHashSet();
}
