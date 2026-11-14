namespace AeTool.Core;

/// <summary>
/// Concepts standard d'ArchiMate 3, tels qu'écrits dans xsi:type du format d'échange.
/// Ces listes ne servent qu'à valider la matrice de transcodification et à proposer des choix à l'interface :
/// le reste de l'outil raisonne avec son propre métamodèle.
/// </summary>
public static class Archimate
{
    public static readonly IReadOnlyList<string> Elements = new[]
    {
        // Métier
        "BusinessActor", "BusinessRole", "BusinessCollaboration", "BusinessInterface", "BusinessProcess", "BusinessFunction",
        "BusinessInteraction", "BusinessEvent", "BusinessService", "BusinessObject", "Contract", "Representation", "Product",
        // Application
        "ApplicationComponent", "ApplicationCollaboration", "ApplicationInterface", "ApplicationFunction", "ApplicationInteraction",
        "ApplicationProcess", "ApplicationEvent", "ApplicationService", "DataObject",
        // Technologie
        "Node", "Device", "SystemSoftware", "TechnologyCollaboration", "TechnologyInterface", "Path", "CommunicationNetwork",
        "TechnologyFunction", "TechnologyProcess", "TechnologyInteraction", "TechnologyEvent", "TechnologyService", "Artifact",
        // Physique
        "Equipment", "Facility", "DistributionNetwork", "Material",
        // Motivation
        "Stakeholder", "Driver", "Assessment", "Goal", "Outcome", "Principle", "Requirement", "Constraint", "Meaning", "Value",
        // Stratégie
        "Resource", "Capability", "ValueStream", "CourseOfAction",
        // Mise en œuvre et migration
        "WorkPackage", "Deliverable", "ImplementationEvent", "Plateau", "Gap",
        // Composites et jonctions
        "Grouping", "Location", "AndJunction", "OrJunction",
    };

    public static readonly IReadOnlyList<string> Relationships = new[]
    {
        "Composition", "Aggregation", "Assignment", "Realization", "Serving", "Access", "Influence", "Triggering", "Flow",
        "Specialization", "Association",
    };

    public static bool IsElement(string? concept) => concept is not null && Elements.Contains(concept);
    public static bool IsRelationship(string? concept) => concept is not null && Relationships.Contains(concept);
}
