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

    public const string Flow = "flux";
    public const string Decomposition = "decomposition";

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
    };

    public static readonly IReadOnlyList<RoleInfo> ForRelationships = new RoleInfo[]
    {
        new(Flow, "Flux", "Espace Flux, pastille sur les vues, matrice émetteurs × récepteurs"),
        new(Decomposition, "Décomposition", "Arbre des capacités (le parent est la source)"),
    };

    public static bool IsObjectRole(string key) => ForObjects.Any(r => r.Key == key);
    public static bool IsRelationshipRole(string key) => ForRelationships.Any(r => r.Key == key);

    /// <summary>Rôle visé par une valeur de la forme « role:application » ; null si c'est une clé de type.</summary>
    public static string? FromSpec(string spec) => spec.StartsWith(Prefix, StringComparison.Ordinal) ? spec[Prefix.Length..] : null;
}
