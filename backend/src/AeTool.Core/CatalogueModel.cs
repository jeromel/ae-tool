namespace AeTool.Core;

/// <summary>
/// Vocabulaire du catalogue des données et des flux : sous-types précisés par une propriété dans Archi (partage de fichiers,
/// profil de poste, flux technique), propriétés des objets de données, des flux applicatifs et des flux techniques.
/// Les clés suivent la forme « domaine.nom » des propriétés Archi ; les libellés sont ceux que voit l'utilisateur.
/// </summary>
public static class CatalogueModel
{
    /// <summary>Propriété Archi qui précise le type d'un élément (Archi ne transmet pas ses spécialisations dans le format d'échange).</summary>
    public const string Subtype = "asset.subtype";
    /// <summary>Code catalogue : préfixe de nature (APP, DO, FA, FT, PP) et numéro séquentiel, jamais réutilisé.</summary>
    public const string Code = "asset.code";
    /// <summary>Statut de l'information : Constaté, À confirmer, À définir, Proposé…</summary>
    public const string InfoStatus = "info.status";

    // Objets de données.
    public const string DataCategory = "data.category";
    public const string DataOwner = "data.owner";
    public const string DataSteward = "data.steward";
    public const string DataSensitivity = "data.sensitivity";
    public const string DataPersonal = "data.personal";
    public const string DataPivot = "data.pivot";
    /// <summary>Sur une relation d'accès application → donnée : Maître, Relais ou Consommateur.</summary>
    public const string DataRole = "data.role";

    // Applications.
    public const string AppZone = "app.zone";
    public const string AppDataRole = "app.data-role";
    public const string BusinessOwner = "org.owner";
    public const string TechnicalOwner = "org.support";
    public const string Lifecycle = "lifecycle.state";

    // Flux applicatifs.
    public const string FlowKind = "flow.kind";
    public const string FlowFrequency = "flow.frequency";
    public const string FlowMode = "flow.mode";
    public const string FlowOrder = "flow.order";
    public const string FlowDependencies = "flow.dependencies";

    // Flux techniques et traçabilité.
    public const string TechStep = "tech.step";
    public const string TechFormat = "tech.format";
    public const string TechOrchestration = "tech.orchestration";
    public const string TechZone = "tech.zone";
    public const string TechAccount = "tech.account";
    public const string TechMonitoring = "tech.monitoring";
    /// <summary>Sur le lien flux technique → flux applicatif : ordre d'exécution de l'étape.</summary>
    public const string TraceOrder = "trace.order";

    public const string RoleMaster = "Maître";
    public const string RoleRelay = "Relais";
    public const string RoleConsumer = "Consommateur";

    public const string FileShareType = "partage-fichiers";
    public const string WorkstationType = "profil-poste";
    public const string TechnicalFlowType = "flux-technique";
    public const string FileShareSubtype = "Partage de fichiers";
    public const string WorkstationSubtype = "Profil de poste";
    public const string TechnicalFlowSubtype = "Flux technique";

    /// <summary>Statut de l'information qui vaut vérification ; les autres s'affichent en pointillé.</summary>
    public const string Verified = "Constaté";

    public static bool IsVerified(string? status) => string.IsNullOrWhiteSpace(status) || string.Equals(status.Trim(), Verified, StringComparison.OrdinalIgnoreCase);

    /// <summary>Préfixe de code attribué par l'outil, par rôle.</summary>
    public static readonly IReadOnlyDictionary<string, string> CodePrefixes = new Dictionary<string, string>
    {
        [Roles.Workstation] = "PP",
    };

    private static readonly (string Key, string Label, string? Description)[] Properties =
    {
        (Subtype, "Sous-type", "Précise le type d'un élément Archi (partage de fichiers, profil de poste, flux technique…)"),
        (Code, "Code catalogue", "Préfixe de nature et numéro séquentiel ; un code n'est jamais réutilisé"),
        (InfoStatus, "Statut de l'information", "Constaté : vérifié dans l'existant ; À confirmer : connu, non vérifié ; À définir : à concevoir"),
        (DataCategory, "Catégorie de donnée", "Référentiel, donnée opérationnelle, donnée de pilotage…"),
        (DataOwner, "Data owner", null), (DataSteward, "Data steward", null),
        (DataSensitivity, "Sensibilité", null), (DataPersonal, "Données personnelles", "Oui, Non ou À confirmer"),
        (DataPivot, "Identifiant pivot", null), (DataRole, "Rôle vis-à-vis de la donnée", "Maître, Relais ou Consommateur"),
        (AppZone, "Zone", "IT, OT ou IT/OT"), (AppDataRole, "Rôle data", null),
        (BusinessOwner, "Responsable métier", null), (TechnicalOwner, "Responsable technique", null), (Lifecycle, "Cycle de vie", null),
        (FlowKind, "Type de flux", null), (FlowFrequency, "Périodicité", null), (FlowMode, "Mode", null),
        (FlowOrder, "Ordre", null), (FlowDependencies, "Dépendances", null),
        (TechStep, "Étape", "Extraction, transformation, transport, chargement"), (TechFormat, "Format", null),
        (TechOrchestration, "Orchestration", null), (TechZone, "Zone", null), (TechAccount, "Compte de service", null),
        (TechMonitoring, "Supervision", null), (TraceOrder, "Ordre d'exécution", null),
    };

    /// <summary>
    /// Ajoute ce qui manque au métamodèle : types et règles de transcodification. Ne modifie jamais un type
    /// ou une règle existants : un administrateur qui les a retouchés garde ses choix.
    /// </summary>
    public static void Ensure(Metamodel m)
    {
        void Type(string key, string label, string layer, string fill, string icon, string[] roles, string concept, string subtype)
        {
            if (m.Type(key) is null)
            {
                if (m.Layers.All(l => l.Key != layer)) layer = m.Layers.FirstOrDefault()?.Key ?? "autre";
                m.ObjectTypes.Add(new ObjectType
                {
                    Key = key, Label = label, LayerKey = layer, Roles = roles.ToList(),
                    Visual = new Visual { Shape = "rectangle", Fill = fill, Stroke = "#5c5c5c", TextColor = "#000000", Template = DrawingDefaults.FallbackTemplate, Icon = icon },
                });
            }
            if (!m.Transcoding.Elements.Any(r => r.TypeKey == key))
                m.Transcoding.Elements.Add(new ElementRule { TypeKey = key, ArchimateType = concept, WhenProperty = Subtype, WhenValue = subtype, Principal = true });
        }
        Type(FileShareType, "Partage de fichiers", "technique", "#c9e7b7", "noeud", new[] { Roles.Infrastructure, Roles.FileShare }, "Node", FileShareSubtype);
        Type(WorkstationType, "Profil de poste", "technique", "#c9e7b7", "equipement", new[] { Roles.Infrastructure, Roles.Workstation }, "Device", WorkstationSubtype);
        Type(TechnicalFlowType, "Flux technique", "technique", "#dcefd0", "reseau", new[] { Roles.TechnicalFlow }, "TechnologyProcess", TechnicalFlowSubtype);
    }

    /// <summary>
    /// Définitions des propriétés du catalogue, créées à son premier import. Une propriété qu'une autre définition reprend déjà sous
    /// son nom Archi n'est pas recréée.
    /// </summary>
    public static void EnsureProperties(Metamodel m)
    {
        foreach (var (key, label, description) in Properties)
        {
            if (m.Property(key) is null && m.PropertyDefinitions.Any(d => d.ArchiName == key)) continue;
            var def = m.EnsureProperty(key, label, "catalogue");
            if (string.IsNullOrEmpty(def.Description)) def.Description = description;
        }
    }

    private static readonly HashSet<string> Owned = Properties.Select(p => p.Key).ToHashSet();

    /// <summary>Propriété tenue par le catalogue : un réimport Archi qui ne la porte pas ne l'efface pas.</summary>
    public static bool IsOwned(string key) => Owned.Contains(key);

    /// <summary>Prochain code libre pour un préfixe : le plus grand numéro en usage, plus un (un code retiré n'est jamais réattribué).</summary>
    public static string NextCode(Repository repo, string prefix)
    {
        var max = 0;
        foreach (var o in repo.Objects.Values)
            if (o.Get(Code) is { } c && c.StartsWith(prefix + "-", StringComparison.Ordinal) && int.TryParse(c[(prefix.Length + 1)..], out var n) && n > max) max = n;
        return $"{prefix}-{max + 1:000}";
    }

    /// <summary>Attribue un code à un objet qui n'en a pas, si son type porte un rôle à code (profil de poste). Renvoie le code attribué.</summary>
    public static string? AssignCode(Repository repo, ArchObject o)
    {
        if (!string.IsNullOrWhiteSpace(o.Get(Code))) return null;
        foreach (var (role, prefix) in CodePrefixes)
            if (repo.Metamodel.IsA(o.TypeKey, role))
            {
                var code = NextCode(repo, prefix);
                o.Set(Code, code);
                return code;
            }
        return null;
    }
}
