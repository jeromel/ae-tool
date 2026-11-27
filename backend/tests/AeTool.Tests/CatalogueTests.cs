using System.Text.Json.Nodes;
using AeTool.Connectors;
using AeTool.Core;
using static AeTool.Tests.Harness;

namespace AeTool.Tests;

/// <summary>Catalogue des données et des flux : métamodèle, codes, comptage des postes, import, parcours, contrôles, registre.</summary>
public static class CatalogueTests
{
    public static void Run()
    {
        Test("catalogue : le métamodèle par défaut porte partage de fichiers, profil de poste et flux technique, reconnus par asset.subtype", () =>
        {
            var m = Metamodel.Default();
            True(m.IsA(CatalogueModel.FileShareType, Roles.FileShare), "partage");
            True(m.IsA(CatalogueModel.WorkstationType, Roles.Workstation), "poste");
            True(m.IsA(CatalogueModel.TechnicalFlowType, Roles.TechnicalFlow), "flux technique");
            var t = new Transcoder(m);
            Equal(CatalogueModel.FileShareType, t.ElementType("Node", new[] { new ArchiProperty("asset.subtype", "Partage de fichiers") }).TypeKey, "nœud sous-typé");
            Equal("noeud", t.ElementType("Node", Array.Empty<ArchiProperty>()).TypeKey, "nœud ordinaire");
            Equal(CatalogueModel.WorkstationType, t.ElementType("Device", new[] { new ArchiProperty("asset.subtype", " profil de poste ") }).TypeKey, "casse et espaces ignorés");
            var export = t.ExportElement(new ArchObject { TypeKey = CatalogueModel.FileShareType });
            Equal("Node", export.Concept, "concept exporté");
            Equal("Partage de fichiers", export.Extra?.Value, "sous-type réécrit à l'export");
        });

        Test("catalogue : un référentiel en version 3 reçoit les nouveaux types sans perdre un type retouché", () =>
        {
            var m = Metamodel.Default();
            m.ObjectTypes.RemoveAll(x => x.Key is CatalogueModel.FileShareType or CatalogueModel.TechnicalFlowType);
            m.Transcoding.Elements.RemoveAll(r => r.TypeKey is CatalogueModel.FileShareType or CatalogueModel.TechnicalFlowType);
            m.Type(CatalogueModel.WorkstationType)!.Label = "Poste type";
            var json = System.Text.Json.JsonSerializer.SerializeToNode(new Repository { Metamodel = m })!.AsObject();
            json["SchemaVersion"] = 3;
            var repo = RepositoryUpgrader.Read(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json.ToJsonString())), out var upgraded);
            True(upgraded, "migré");
            Equal(Repository.CurrentSchemaVersion, repo.SchemaVersion, "version");
            True(repo.Metamodel.Type(CatalogueModel.FileShareType) is not null, "partage ajouté");
            True(repo.Metamodel.Transcoding.Elements.Any(r => r.TypeKey == CatalogueModel.TechnicalFlowType && r.WhenValue == "Flux technique"), "règle ajoutée");
            Equal("Poste type", repo.Metamodel.Type(CatalogueModel.WorkstationType)!.Label, "libellé retouché gardé");
        });

        Test("catalogue : un profil de poste reçoit le code suivant, jamais un code déjà pris", () =>
        {
            var repo = new Repository();
            var a = new ArchObject { Id = "a", TypeKey = CatalogueModel.WorkstationType, Name = "PC A" };
            a.Set(CatalogueModel.Code, "PP-007");
            var b = new ArchObject { Id = "b", TypeKey = CatalogueModel.WorkstationType, Name = "PC B" };
            var c = new ArchObject { Id = "c", TypeKey = "application", Name = "App" };
            repo.Objects["a"] = a; repo.Objects["b"] = b; repo.Objects["c"] = c;
            Equal("PP-008", CatalogueModel.AssignCode(repo, b), "code attribué");
            Equal<string?>(null, CatalogueModel.AssignCode(repo, b), "déjà codé");
            Equal<string?>(null, CatalogueModel.AssignCode(repo, c), "pas de code pour une application");
            a.Archived = true;
            Equal("PP-009", CatalogueModel.NextCode(repo, "PP"), "un code d'objet archivé n'est pas réattribué");
        });

        Test("catalogue : comptage des postes par code, nom de profil toléré, codes inconnus et propositions par entité", () =>
        {
            var repo = new Repository();
            ArchObject P(string id, string name, string code) { var o = new ArchObject { Id = id, TypeKey = CatalogueModel.WorkstationType, Name = name }; o.Set(CatalogueModel.Code, code); repo.Objects[id] = o; return o; }
            P("p1", "PC bureautique", "PP-001"); P("p2", "PC RH", "PP-002"); P("p3", "PC vide", "PP-003");
            Dictionary<string, string?> Row(string id, string? code, string entity, string cat = "Poste") => new() { ["ID"] = id, ["Nom"] = "N" + id, ["Code"] = code, ["Site"] = "Siège", ["Entité"] = entity, ["Modèle"] = "M", ["Cat"] = cat };
            var rows = new List<Dictionary<string, string?>>
            {
                Row("1", "PP-001", "DSI"), Row("2", "pp-001", "DSI"), Row("3", "PC RH", "DRH/Paie"), Row("4", "PP-002", "DRH/Paie"),
                Row("5", null, "DRH/Formation"), Row("6", "PP-404", "DSI"), Row("7", null, "DSI", "Imprimante"),
            };
            var settings = new WorkstationSettings { PivotField = "Code", SiteField = "Site", EntityField = "Entité", ModelField = "Modèle", FilterField = "Cat", FilterValue = "poste" };
            var c = WorkstationCounter.Count(repo, rows, settings, "cmdb", "ID", "Nom", "test");
            Equal(6, c.Read, "lus (imprimante écartée)");
            Equal(2, c.ByProfile["PP-001"].Total, "code sans casse");
            Equal(2, c.ByProfile["PP-002"].Total, "nom de profil reconnu");
            Equal(1, c.Unclassified.Total, "sans profil");
            Equal(1, c.UnknownCodes["PP-404"].Total, "code inconnu");
            Equal(2, c.ToAttach.Count, "postes à rattacher");
            Equal("PP-003", string.Join(",", c.EmptyProfiles), "profil sans poste");
            var s = c.Suggestions.Single(x => x.Entity == "DRH/Formation");
            Equal("PP-002", s.ProfileCode, "proposition tirée de l'entité parente");
        });
    }

    /// <summary>Sur le jeu Hydréane : import du catalogue, parcours des factures, contrôles, registre et comptage des postes.</summary>
    public static void RunOnExample(string dir)
    {
        var repo = new Repository();
        using (var s = File.OpenRead(Path.Combine(dir, "hydreane-referentiel.xml"))) ArchimateExchange.Import(s, repo);
        var path = Path.Combine(dir, "hydreane-catalogue-donnees-flux.xlsx");
        string Id(string name) => repo.ActiveObjects.First(o => o.Name == name).Id;

        Test("jeu Hydréane : le modèle Archi donne des partages, des profils de poste codés et un partage à reclasser", () =>
        {
            Equal(2, repo.ActiveObjects.Count(o => o.TypeKey == CatalogueModel.FileShareType), "partages sous-typés");
            var profiles = repo.ActiveObjects.Where(o => o.TypeKey == CatalogueModel.WorkstationType).ToDictionary(o => o.Name, o => o.Get(CatalogueModel.Code));
            Equal(4, profiles.Count, "profils");
            Equal("PP-004", profiles["PC métier exploitation"], "code attribué au profil qui n'en avait pas");
            Equal("noeud", repo.Objects[Id("Partage Exploitation")].TypeKey, "partage sans sous-type resté nœud");
        });

        CatalogueAnalysis a = new();
        Test("jeu Hydréane : l'analyse du catalogue rapproche par nom et propose un destinataire externe", () =>
        {
            using (var s = File.OpenRead(path)) a = CatalogueImporter.Analyse(s, "catalogue.xlsx", repo);
            Equal(23, a.Items.Count(i => i.Kind == "application" && i.Proposal == CatalogueItem.Link), "applications rapprochées");
            Equal(10, a.Items.Count(i => i.Kind == "donnee" && i.Proposal == CatalogueItem.Link), "objets de données de même nom");
            var factures = a.Items.Single(i => i.Code == "DO-003");
            Equal(CatalogueItem.Create, factures.Proposal, "Factures abonnés : pas de même nom");
            True(factures.Candidates.Any(c => c.Name == "Factures émises"), "Factures émises proposé comme nom proche");
            Equal(11, a.Items.Count(i => i.Kind == "flux-technique" && i.Proposal == CatalogueItem.Create), "flux techniques à créer");
            Equal("", string.Join(" | ", a.Anomalies.Select(x => $"{x.Code} {x.Ref} {x.Message}")), "anomalies d'analyse");
            var external = a.Items.Single(i => i.Kind == "externe");
            Equal("EXTERNE", external.Code, "destinataire hors de l'onglet Applications");
            True(external.Candidates.Any(c => c.Name == "Agence de bassin"), "acteurs proposés");
            var fa3 = a.Items.Single(i => i.Code == "FA-003");
            Equal(CatalogueItem.Link, fa3.Proposal, "FA-003 rapproché du flux Archi ONDINE → Mon Espace Eau");
        });

        ImportReport report = new();
        Test("jeu Hydréane : l'import applique les choix et se rejoue sans écart", () =>
        {
            var factures = a.Items.Single(i => i.Code == "DO-003").Candidates.Single(c => c.Name == "Factures émises").Id;
            var decisions = new Dictionary<string, CatalogueDecision> { ["DO-003"] = new() { Action = CatalogueItem.Link, TargetId = factures } };
            using (var s = File.OpenRead(path)) report = CatalogueImporter.Apply(s, "catalogue.xlsx", repo, "test", decisions);
            Equal("", string.Join(" | ", report.Anomalies.Where(x => x.Severity == "erreur").Select(x => $"{x.Code} {x.Ref} {x.Message}")), "erreurs");
            Equal(5, report.Anomalies.Count(x => x.Code == "application-inconnue"), "consommateurs hors applications signalés");
            Equal("DO-003", repo.Objects[factures].Get(CatalogueModel.Code), "code posé sur Factures émises");
            Equal(30, report.For("Flux applicatifs").Total, "flux applicatifs repris");
            Equal(1, report.For("Destinataires externes").Added, "destinataire externe créé");
            Equal(57, report.For("Traçabilité").Added, "liens de traçabilité");
            Equal(87, repo.DataAttributes.Values.Sum(l => l.Count), "attributs");
            Equal("CMDB", repo.DataAttributes.Values.First().First().TargetSystem, "système cible tiré de l'en-tête");
            ImportReport again;
            using (var s = File.OpenRead(path)) again = CatalogueImporter.Apply(s, "catalogue.xlsx", repo, "test", decisions);
            Equal(0, again.Counts.Values.Sum(c => c.Added + c.Updated), "écarts au rejeu");
        });

        Test("jeu Hydréane : parcours des factures (applications, chaîne technique, serveurs, partage, postes)", () =>
        {
            var l = new LineageReader(repo).Read(Id("Factures émises"), derived: false);
            Equal("donnee", l.Focus.Kind, "focus");
            var apps = l.Applications.Select(x => x.Name).ToHashSet();
            foreach (var name in new[] { "ONDINE", "Mon Espace Eau", "FINEO", "Archivage électronique" }) True(apps.Contains(name), $"application {name}");
            var withDerived = new LineageReader(repo).Read(Id("Factures émises"), derived: true);
            True(withDerived.Data.Any(d => d.Name == "Écritures comptables" && d.Derived), "écritures comptables en donnée dérivée");
            True(withDerived.Applications.Any(x => x.Name == "Entrepôt de données"), "entrepôt atteint par les écritures");
            var fa3 = l.Flows.Single(f => f.Code == "FA-003");
            Equal("API ONDINE|Bus d’intégration Hydréane|API Mon Espace Eau", string.Join("|", fa3.Steps.Select(s => l.Technical.Single(t => t.Id == s.TechnicalId).Name)), "chaîne FA-003 dans l'ordre");
            var hosts = l.Hosts.ToDictionary(h => h.Name);
            Equal(LineageReader.Storage, hosts["VM-FACT-01"].Usage, "VM-FACT-01 stocke");
            Equal(LineageReader.Transit, hosts["VM-ECH-01"].Usage, "VM-ECH-01 en transit (bus)");
            Equal(LineageReader.Files, hosts["Partage Clientèle"].Usage, "partage clientèle : fichiers");
            True(hosts["VM-FACT-01"].Details.Contains("Oracle Database 19c"), "logiciel système en détail du serveur");
            True(l.Profiles.Any(p => p.Name == "PC bureautique Direction clientèle"), "profil d'accès");
            True(l.Gaps.Any(g => g.Contains("Archivage électronique") && g.Contains("Hébergement")), "hébergement de l'archivage inconnu");
            True(l.Gaps.Any(g => g.StartsWith("FA-028") && g.Contains("n'atteint pas Archivage électronique")), "chaîne FA-028 incomplète");
            True(!l.Gaps.Any(g => g.StartsWith("FA-003") && g.Contains("n'atteint pas")), "FA-003 complète");
            True(l.Gaps.Any(g => g.Contains("RECOUVR")), "accès Archi hors catalogue signalé");
        });

        Test("jeu Hydréane : le parcours s'enregistre comme vue, exportable vers Archi, et se régénère sans doublon", () =>
        {
            var l = new LineageReader(repo).Read(Id("Factures émises"), derived: false);
            var d = LineageView.Save(repo, l, "test");
            Equal(l.Data.Count + l.Applications.Count + l.Technical.Count + l.Hosts.Count + l.Profiles.Count, d.Nodes.Count, "une case par élément");
            True(d.Connections.Count > 5, "relations tracées");
            LineageView.Save(repo, l, "test");
            Equal(1, repo.Diagrams.Values.Count(x => x.Source == LineageView.SourceTag), "une seule vue par parcours");
            var xml = ArchimateExchange.Export(repo, new[] { d.Id }).ToString();
            True(xml.Contains("Parcours : Factures émises"), "vue exportée");
        });

        Test("jeu Hydréane : parcours depuis le bus et depuis un serveur", () =>
        {
            var bus = new LineageReader(repo).Read(Id("Bus d’intégration Hydréane"), derived: false);
            True(bus.Flows.Count >= 5, "flux qui s'arrêtent si le bus tombe");
            var vm = new LineageReader(repo).Read(Id("VM-FACT-01"), derived: false);
            True(vm.Applications.Any(x => x.Name == "ONDINE"), "ONDINE hébergée");
            True(vm.Data.Any(d => d.Name == "Factures émises"), "factures sur le serveur");
            True(vm.Applications.All(x => x.Name is "ONDINE" or "RECOUVR" or "IMMO" or "Ancienne facturation (consultation)"
                || vm.Flows.Any(f => f.SourceId == x.Id || f.TargetId == x.Id)), "seulement les applications hébergées et leurs partenaires de flux");
            True(!vm.Applications.Any(x => x.Name == "PAIE RH"), "le logiciel système partagé n'élargit pas le périmètre");
        });

        // Règle du cœur extraite de DataLineage (revue de coût d'EV-06 de Colbert, §3) : le parcours et la grille la partagent.
        Test("jeu Hydréane : applications hébergées par un serveur, règle du cœur, et le parcours la suit", () =>
        {
            var hosting = new HostedApplications(repo);
            var vm = Id("VM-FACT-01");
            var apps = hosting.Of(vm).Select(a => repo.Objects[a].Name).ToHashSet();
            True(apps.Contains("ONDINE"), "ONDINE hébergée : " + string.Join(", ", apps));
            True(!apps.Contains("PAIE RH"), "le logiciel système partagé n'élargit pas le périmètre");
            True(hosting.Scope(vm).Contains(vm) && hosting.Scope(vm).All(hosting.IsHost), "le périmètre ne contient que de l'hébergement");
            var lineage = new LineageReader(repo).Read(vm, derived: false);
            True(apps.All(a => lineage.Applications.Any(x => x.Name == a)), "le parcours retrouve toutes les applications hébergées");
            Equal(0, hosting.Of(Id("ONDINE")).Count, "une application n'héberge rien");
        });

        Test("jeu Hydréane : contrôles de cohérence du catalogue", () =>
        {
            var q = new CatalogueQuality(repo).Check();
            var codes = q.Findings.ToDictionary(f => f.Code);
            True(codes["emetteur-non-maitre"].Items.Any(i => i.Label.StartsWith("FA-004")), "FA-004 : ONDINE n'est pas maître des écritures");
            var open = codes["chaine-sans-arrivee"].Items.Select(i => i.Label[..6]).ToHashSet();
            True(open.Contains("FA-028") && open.Contains("FA-005") && !open.Contains("FA-003"), "chaînes qui n'atteignent pas la cible : " + string.Join(",", open));
            True(codes["sensibilite-hors-liste"].Items.Any(i => i.Label == "« Interne »"), "vocabulaire de sensibilité");
            True(codes["hebergement-inconnu"].Items.Any(i => i.Label.Contains("Archivage électronique")), "hébergement");
            True(codes["application-sans-flux"].Items.Any(i => i.Label.Contains("RECOUVR")), "RECOUVR sans flux");
            Equal(1, codes["partage-sans-sous-type"].Items.Count, "partage à reclasser");
            var links = q.Reliability.Single(r => r.Kind == "Liens de traçabilité");
            Equal(35, links.Verified, "liens constatés");
            Equal(57, links.Total, "liens");
        });

        Test("jeu Hydréane : registre des données personnelles, partages compris", () =>
        {
            var r = new CatalogueQuality(repo).Register();
            var factures = r.Rows.Single(x => x.Code == "DO-003");
            Equal("Partage Clientèle", string.Join(",", factures.Shares), "partage hébergeant les factures");
            True(r.Shares.Any(s => s.Name == "Partage Finance" && s.Data.Contains("Écritures comptables")), "partage finance");
            True(r.Rows.All(x => x.Personal == "Oui" || CatalogueQuality.IsProtected(x.Sensitivity) || x.PersonalAttributes.Count > 0), "seulement ce qui est à protéger");
        });

        Test("jeu Hydréane : comptage des postes de la CMDB d'exemple", () =>
        {
            var table = TabularReader.Read(File.OpenRead(Path.Combine(dir, "hydreane-cmdb-postes.csv")), "postes.csv");
            var settings = new WorkstationSettings { PivotField = "Profil de poste", SiteField = "Site", EntityField = "Entité", ModelField = "Modèle", FilterField = "Catégorie", FilterValue = "Poste de travail" };
            var c = WorkstationCounter.Count(repo, table.Rows, settings, "cmdb", "ID", "Nom", "test");
            Equal(94, c.Read, "postes lus");
            Equal(36, c.ByProfile["PP-001"].Total, "PC bureautique");
            Equal(27, c.ByProfile["PP-002"].Total, "PC clientèle");
            Equal(13, c.ByProfile["PP-003"].Total, "PC finance (dont 2 par leur nom)");
            Equal(6, c.ByProfile["PP-004"].Total, "PC exploitation");
            Equal(9, c.Unclassified.Total, "sans profil");
            Equal(3, c.UnknownCodes["PP-099"].Total, "code inconnu");
            Equal("PP-002", c.Suggestions.Single(s => s.Entity == "Direction clientèle/Facturation").ProfileCode, "proposition");
            repo.Workstations = c;
            var l = new LineageReader(repo).Read(Id("Factures émises"), derived: false);
            Equal(27, l.Profiles.Single(p => p.Code == "PP-002").Workstations, "effectif affiché dans le parcours");
        });
    }
}
