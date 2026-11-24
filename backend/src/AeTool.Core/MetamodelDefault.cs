namespace AeTool.Core;

/// <summary>
/// Métamodèle par défaut : types propres à l'outil, rôles, matrice de transcodification ArchiMate, couleurs par couche.
/// Une ligne de la table = un type d'objet et sa règle principale. Les noms de concepts ArchiMate n'apparaissent
/// qu'ici et dans la matrice : le reste du code passe par les rôles.
/// </summary>
public static class MetamodelDefault
{
    public static Metamodel Create()
    {
        var m = new Metamodel();
        string[][] layers =
        {
            new[] { "strategie", "Stratégie et capacités" }, new[] { "metier", "Métier" },
            new[] { "fonctionnelle", "Fonctionnelle" }, new[] { "applicative", "Applicative" },
            new[] { "donnees", "Données" }, new[] { "technique", "Technique et infrastructure" },
            new[] { "transformation", "Transformation" }, new[] { "motivation", "Motivation" },
            new[] { "autre", "Autre" },
        };
        for (var i = 0; i < layers.Length; i++) m.Layers.Add(new Layer { Key = layers[i][0], Label = layers[i][1], Order = i });

        // Format d'une ligne : clé | libellé | concept ArchiMate (vide = aucune règle) | rôles séparés par des virgules.
        void Add(string layer, string fill, string shape, params string[] rows)
        {
            foreach (var row in rows)
            {
                var p = row.Split('|');
                m.ObjectTypes.Add(new ObjectType
                {
                    Key = p[0], Label = p[1], LayerKey = layer,
                    Roles = p[3] == "" ? new List<string>() : p[3].Split(',').ToList(),
                    Visual = new Visual { Shape = shape, Fill = fill, Stroke = "#5c5c5c", TextColor = "#000000", Template = TemplateOf(p[0]), Icon = GlyphOf(p[0]) },
                });
                if (p[2] != "") m.Transcoding.Elements.Add(new ElementRule { TypeKey = p[0], ArchimateType = p[2], Principal = true });
            }
        }
        Add("strategie", "#f5deaa", "arrondi", "capacite|Capacité|Capability|capacite,fond-de-carte", "chaine-valeur|Chaîne de valeur|ValueStream|",
            "ressource|Ressource|Resource|", "plan-action|Plan d'action|CourseOfAction|");
        Add("motivation", "#ccccff", "rectangle", "partie-prenante|Partie prenante|Stakeholder|", "moteur|Moteur|Driver|", "constat|Constat|Assessment|",
            "objectif|Objectif|Goal|", "resultat|Résultat|Outcome|", "principe|Principe|Principle|", "exigence|Exigence|Requirement|",
            "contrainte|Contrainte|Constraint|", "signification|Signification|Meaning|", "valeur|Valeur|Value|");
        Add("metier", "#ffffb5", "rectangle", "acteur|Acteur|BusinessActor|", "role|Rôle|BusinessRole|",
            "collaboration-metier|Collaboration métier|BusinessCollaboration|", "interface-metier|Interface métier|BusinessInterface|",
            "processus|Processus|BusinessProcess|", "fonction-metier|Fonction métier|BusinessFunction|",
            "interaction-metier|Interaction métier|BusinessInteraction|", "evenement-metier|Événement métier|BusinessEvent|",
            "service-metier|Service métier|BusinessService|", "contrat|Contrat|Contract|", "representation|Représentation|Representation|",
            "produit|Produit|Product|");
        Add("fonctionnelle", "#d9e7f5", "rectangle", "zone|Zone / quartier|Grouping|fond-de-carte", "bloc-fonctionnel|Bloc fonctionnel|ApplicationFunction|");
        Add("applicative", "#b5ffff", "rectangle", "application|Application|ApplicationComponent|application",
            "collaboration-applicative|Collaboration applicative|ApplicationCollaboration|",
            "interface-applicative|Interface applicative|ApplicationInterface|", "interaction-applicative|Interaction applicative|ApplicationInteraction|",
            "traitement-applicatif|Traitement applicatif|ApplicationProcess|", "evenement-applicatif|Événement applicatif|ApplicationEvent|",
            "service-applicatif|Service applicatif|ApplicationService|");
        Add("donnees", "#ffe9b5", "rectangle", "objet-metier|Objet métier|BusinessObject|donnee", "objet-donnees|Objet de données|DataObject|donnee");
        Add("technique", "#c9e7b7", "rectangle", "noeud|Nœud|Node|infrastructure", "equipement|Équipement informatique|Device|infrastructure",
            "logiciel-systeme|Logiciel système|SystemSoftware|infrastructure",
            "collaboration-technique|Collaboration technique|TechnologyCollaboration|infrastructure",
            "interface-technique|Interface technique|TechnologyInterface|infrastructure", "chemin|Chemin|Path|infrastructure",
            "reseau|Réseau|CommunicationNetwork|infrastructure", "fonction-technique|Fonction technique|TechnologyFunction|infrastructure",
            "traitement-technique|Traitement technique|TechnologyProcess|infrastructure",
            "interaction-technique|Interaction technique|TechnologyInteraction|infrastructure",
            "evenement-technique|Événement technique|TechnologyEvent|infrastructure",
            "service-technique|Service technique|TechnologyService|infrastructure", "artefact|Artefact|Artifact|support-donnee",
            "equipement-industriel|Équipement industriel (OT)|Equipment|infrastructure", "installation|Installation|Facility|infrastructure",
            "reseau-distribution|Réseau de distribution|DistributionNetwork|infrastructure", "matiere|Matière|Material|");
        Add("transformation", "#ffe0e0", "rectangle", "projet|Projet|WorkPackage|projet", "livrable|Livrable|Deliverable|",
            "jalon|Jalon|ImplementationEvent|", "plateau|Plateau|Plateau|", "ecart|Écart|Gap|");
        Add("autre", "#f0f0f0", "rectangle", "lieu|Lieu|Location|fond-de-carte", "jonction-et|Jonction ET|AndJunction|fond-de-carte",
            "jonction-ou|Jonction OU|OrJunction|fond-de-carte", "autre|Autre (non classé)||");

        // Clé | libellé | lecture inverse | concept | trait | trait soigné | tête | rôles.
        string[][] rels =
        {
            new[] { "composition", "Composition", "Fait partie de", "Composition", "plein", "plein", "losange", "decomposition" },
            new[] { "agregation", "Agrégation", "Est regroupé dans", "Aggregation", "plein", "plein", "losange-creux", "decomposition" },
            new[] { "affectation", "Affectation", "Reçoit l'affectation de", "Assignment", "plein", "plein", "point", "" },
            new[] { "realisation", "Réalisation", "Est réalisé par", "Realization", "pointillés", "tirets", "triangle", "" },
            new[] { "service", "Sert", "Est servi par", "Serving", "plein", "plein", "ouverte", "" },
            new[] { "acces", "Accès", "Est accédé par", "Access", "pointillés", "pointillés", "aucune", "" },
            new[] { "influence", "Influence", "Est influencé par", "Influence", "tirets", "tirets", "ouverte", "" },
            new[] { "declenchement", "Déclenche", "Est déclenché par", "Triggering", "plein", "plein", "fleche", "" },
            new[] { "flux", "Flux", "Reçoit un flux de", "Flow", "tirets", "plein", "fleche", "flux" },
            new[] { "specialisation", "Spécialisation", "Est spécialisé par", "Specialization", "plein", "plein", "triangle", "" },
            new[] { "association", "Association", "Est associé à", "Association", "plein", "plein", "aucune", "" },
        };
        foreach (var r in rels)
        {
            m.RelationshipTypes.Add(new RelationshipType
            {
                Key = r[0], Label = r[1], ReverseLabel = r[2], LineStyle = r[4], SoftLine = r[5], Head = r[6],
                Roles = r[7] == "" ? new List<string>() : new List<string> { r[7] },
            });
            m.Transcoding.Relations.Add(new RelationRule { TypeKey = r[0], ArchimateType = r[3], Principal = true });
        }
        CatalogueModel.Ensure(m);
        return m;
    }

    /// <summary>Gabarit de rendu d'un type, d'après la clé du type par défaut.</summary>
    private static string TemplateOf(string key) => key switch
    {
        "acteur" or "role" or "partie-prenante" or "objet-metier" or "objet-donnees" or "artefact" or "contrat" or "representation" or "livrable" => "rond",
        "zone" or "lieu" => "zone",
        _ => DrawingDefaults.FallbackTemplate,
    };

    /// <summary>Pictogramme d'un type, d'après la clé du type par défaut.</summary>
    private static string GlyphOf(string key) => key switch
    {
        "application" or "collaboration-applicative" => "application",
        "acteur" or "partie-prenante" => "acteur",
        "role" => "role",
        "objet-metier" or "contrat" or "representation" or "produit" => "objet",
        "objet-donnees" => "donnee",
        "artefact" or "livrable" => "artefact",
        "noeud" or "installation" => "noeud",
        "equipement" or "equipement-industriel" => "equipement",
        "logiciel-systeme" => "logiciel",
        "reseau" or "chemin" or "reseau-distribution" => "reseau",
        "capacite" or "ressource" => "capacite",
        "lieu" => "lieu",
        "projet" or "plateau" or "ecart" or "plan-action" => "projet",
        "objectif" or "resultat" or "moteur" or "principe" or "valeur" or "signification" or "constat" => "objectif",
        "exigence" or "contrainte" => "exigence",
        "zone" => "zone",
        _ when key.StartsWith("service-") => "service",
        _ when key.StartsWith("fonction-") || key == "bloc-fonctionnel" => "fonction",
        _ when key.StartsWith("interface-") => "interface",
        _ when key.StartsWith("evenement-") || key == "jalon" => "evenement",
        _ when key.StartsWith("traitement-") || key.StartsWith("interaction-") || key == "processus" || key == "chaine-valeur" => "processus",
        _ => DrawingDefaults.FallbackGlyph,
    };
}
