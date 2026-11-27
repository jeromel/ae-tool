namespace AeTool.Core;

/// <summary>Un rôle fonctionnel : ce que l'outil fait d'un type qui le porte.</summary>
public sealed record RoleInfo(string Key, string Label, string Effect);

/// <summary>
/// Rôles fonctionnels. Les écrans et les analyses se branchent sur un rôle, jamais sur la clé d'un type :
/// on peut ainsi renommer, ajouter ou fusionner des types sans casser une fonction.
/// </summary>
public static class Roles
{
    public const string Application = "application";
    public const string Capability = "capacite";
    public const string Project = "projet";
    public const string Data = "donnee";
    public const string DataCarrier = "support-donnee";
    public const string Infrastructure = "infrastructure";
    public const string Backdrop = "fond-de-carte";
    public const string TechnicalFlow = "flux-technique";
    public const string FileShare = "partage";
    public const string Workstation = "poste";
    public const string ValueStream = "chaine-valeur";
    public const string Process = "processus";
    public const string Who = "qui";

    public const string Flow = "flux";
    public const string Decomposition = "decomposition";
    public const string Sequence = "enchainement";

    /// <summary>Préfixe qui désigne un rôle plutôt qu'une clé de type (matrices).</summary>
    public const string Prefix = "role:";

    public static readonly IReadOnlyList<RoleInfo> ForObjects = new RoleInfo[]
    {
        new(Application, "Application", "Espace Applications, portefeuille, matrices, reprise du classeur d'urbanisation"),
        new(Capability, "Capacité", "Carte des capacités, rattachement des applications"),
        new(Project, "Projet", "Liste des projets, impacts et adhérences"),
        new(Data, "Donnée", "Espace Données"),
        new(DataCarrier, "Support de donnée", "Artefact (fichier, base, message) qui matérialise une donnée"),
        new(Infrastructure, "Infrastructure", "Espace Infrastructure, socle technique d'une application"),
        new(Backdrop, "Fond de carte", "Présent dans une vue projet, il ne vaut pas impact"),
        new(TechnicalFlow, "Flux technique", "Étape technique (extraction, transport…) qui réalise un flux applicatif ; parcours d'une donnée"),
        new(FileShare, "Partage de fichiers", "Lieu de stockage de fichiers ; parcours d'une donnée, registre des données personnelles"),
        new(Workstation, "Profil de poste", "Poste type d'une population ; accès utilisateurs, comptage des postes de la CMDB"),
        new(ValueStream, "Chaîne de valeur", "Vue par chaîne de valeur ; ses étapes sont les chaînes qu'elle compose"),
        new(Process, "Processus", "Vue par processus ; ses étapes sont les processus qu'il compose"),
        new(Who, "Qui", "Ligne « Qui » d'un processus : acteur ou rôle métier"),
    };

    public static readonly IReadOnlyList<RoleInfo> ForRelationships = new RoleInfo[]
    {
        new(Flow, "Flux", "Espace Flux, pastille sur les vues, matrice émetteurs × récepteurs"),
        new(Decomposition, "Décomposition", "Arbre des capacités, étapes d'une chaîne ou d'un processus (le parent est la source)"),
        new(Sequence, "Enchaînement", "Ordre des étapes d'une chaîne ou d'un processus (entre deux objets de rôle chaîne de valeur ou processus)"),
    };

    /// <summary>
    /// Rôles de la vue par chaîne de valeur et par processus (EV-06, revue de coût de Colbert §1), posés d'après le concept
    /// transcodé et non d'après la clé du type : un métamodèle aux clés renommées les reçoit aussi. Un rôle déjà porté par un
    /// type n'est pas redistribué : un réglage fait à la main est respecté. Renvoie le nombre de rôles posés.
    /// </summary>
    public static int EnsureProcessRoles(Metamodel m)
    {
        var added = 0;
        void Objects(string role, params string[] concepts)
        {
            if (m.ObjectTypes.Any(t => t.Roles.Contains(role))) return;
            var keys = m.Transcoding.Elements.Where(r => concepts.Contains(r.ArchimateType) && !r.Conditional).Select(r => r.TypeKey).ToHashSet();
            foreach (var t in m.ObjectTypes.Where(t => keys.Contains(t.Key))) { t.Roles.Add(role); added++; }
        }
        Objects(ValueStream, "ValueStream");
        Objects(Process, "BusinessProcess");
        Objects(Who, "BusinessActor", "BusinessRole");
        if (!m.RelationshipTypes.Any(t => t.Roles.Contains(Sequence)))
        {
            var keys = m.Transcoding.Relations.Where(r => r.ArchimateType is "Flow" or "Triggering").Select(r => r.TypeKey).ToHashSet();
            foreach (var t in m.RelationshipTypes.Where(t => keys.Contains(t.Key))) { t.Roles.Add(Sequence); added++; }
        }
        return added;
    }

    public static bool IsObjectRole(string key) => ForObjects.Any(r => r.Key == key);
    public static bool IsRelationshipRole(string key) => ForRelationships.Any(r => r.Key == key);

    /// <summary>Rôle visé par une valeur de la forme « role:application » ; null si c'est une clé de type.</summary>
    public static string? FromSpec(string spec) => spec.StartsWith(Prefix, StringComparison.Ordinal) ? spec[Prefix.Length..] : null;
}
