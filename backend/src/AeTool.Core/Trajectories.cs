using System.Text.Json;
using System.Text.Json.Serialization;

namespace AeTool.Core;

/// <summary>Statuts d'un scénario. L'incrément 1 n'utilise que brouillon et proposé ; la décision du comité vient ensuite.</summary>
public static class ScenarioStatus
{
    public const string Draft = "Brouillon";
    public const string Proposed = "Proposé";
    public const string InReview = "En instruction";
    public const string Retained = "Retenu";
    public const string Rejected = "Écarté";
    public const string Done = "Réalisé";
    public static readonly string[] All = { Draft, Proposed, InReview, Retained, Rejected, Done };
}

public static class ChangeOps
{
    public const string Add = "ajout";
    public const string Remove = "retrait";
    public const string Modify = "modification";
    public const string Replace = "remplacement";
    public static readonly string[] All = { Add, Remove, Modify, Replace };
}

public static class ProjectionMarks
{
    public const string Added = "ajouté";
    public const string Modified = "modifié";
    public const string Removed = "retiré";
}

/// <summary>
/// Proposition de trajectoire pour un projet, non validée tant que le comité ne l'a pas retenue. Elle part de l'existant
/// ou d'une étape d'un autre scénario, et décrit des étapes successives par leurs changements.
/// </summary>
public sealed class Scenario
{
    public string Id { get; set; } = "";
    /// <summary>Objet « projet » du référentiel (rôle projet).</summary>
    public string ProjectId { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string Status { get; set; } = ScenarioStatus.Draft;
    /// <summary>Point de départ : une étape d'un autre scénario (variante), ou l'existant si vide.</summary>
    public string? StartScenarioId { get; set; }
    public string? StartStepId { get; set; }
    public List<TrajectoryStep> Steps { get; set; } = new();
    /// <summary>Objets qui n'existent que dans ce scénario (une offre à l'étude). Ils n'entrent au référentiel qu'à la bascule.</summary>
    public Dictionary<string, ArchObject> Envisioned { get; set; } = new();
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>Un état du SI à atteindre, décrit par ses changements par rapport à l'étape précédente.</summary>
public sealed class TrajectoryStep
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int Order { get; set; }
    /// <summary>Date prévue : facultative (étape conceptuelle sans date).</summary>
    public DateOnly? Date { get; set; }
    public string? Description { get; set; }
    /// <summary>Prévue, Livrée (ses changements sont dans l'existant) ou Annulée.</summary>
    public string Status { get; set; } = "Prévue";
    public List<TrajectoryChange> Changes { get; set; } = new();
}

/// <summary>
/// Un changement porté par une étape.
/// ajout : un objet envisagé (<see cref="ElementId"/>) ou une relation (<see cref="Relation"/>) ;
/// retrait : un objet ou une relation existants ;
/// modification : nouveau nom et propriétés d'un objet ou d'une relation, avec les valeurs d'avant pour détecter un existant qui a bougé ;
/// remplacement : <see cref="ElementId"/> remplacé par <see cref="ReplacementId"/>, ses relations reportées sur le remplaçant.
/// </summary>
public sealed class TrajectoryChange
{
    public string Id { get; set; } = "";
    public string Op { get; set; } = ChangeOps.Add;
    /// <summary>« objet » ou « relation ».</summary>
    public string Target { get; set; } = "objet";
    public string? ElementId { get; set; }
    public string? ReplacementId { get; set; }
    public PlannedRelation? Relation { get; set; }
    public string? NewName { get; set; }
    public List<PropertyValue>? Set { get; set; }
    public string? BeforeName { get; set; }
    public List<PropertyValue>? Before { get; set; }
    public string? Note { get; set; }
}

public sealed class PlannedRelation
{
    public string TypeKey { get; set; } = "";
    public string SourceId { get; set; } = "";
    public string TargetId { get; set; } = "";
    public string? Name { get; set; }
    public List<PropertyValue> Properties { get; set; } = new();
}

public sealed record ProjectionIssue(string Severity, string Code, string Message, string? StepId, string? ChangeId);

/// <summary>Le SI à une étape : un référentiel projeté (lecture seule), ce qui a changé, et ce qui n'a pas pu s'appliquer.</summary>
public sealed class Projected
{
    public required Repository Repo { get; init; }
    public required Dictionary<string, string> Marks { get; init; }
    public required List<ProjectionIssue> Issues { get; init; }
    public required string Label { get; init; }
    public required Scenario Scenario { get; init; }
    public required TrajectoryStep Step { get; init; }
}

/// <summary>
/// Calcul du SI à une étape : l'existant, puis les changements des étapes non livrées de la chaîne (étapes du scénario de
/// départ jusqu'au point de branchement, puis celles du scénario jusqu'à l'étape choisie).
/// Le référentiel projeté partage les objets inchangés avec l'existant et clone ceux qu'il touche : l'existant n'est jamais
/// modifié. Un objet ou une relation retirés y restent, archivés, pour que les vues puissent encore les dessiner, barrés.
/// </summary>
public static class Projection
{
    private static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public static Projected Build(Repository repo, string scenarioId, string stepId)
    {
        if (!repo.Scenarios.TryGetValue(scenarioId, out var scenario)) throw new KeyNotFoundException("Scénario introuvable.");
        var step = scenario.Steps.FirstOrDefault(s => s.Id == stepId) ?? throw new KeyNotFoundException("Étape introuvable.");
        var chain = Chain(repo, scenario, step);
        var envisioned = new Dictionary<string, ArchObject>();
        foreach (var s in ScenariosOf(repo, scenario)) foreach (var (id, o) in s.Envisioned) envisioned.TryAdd(id, o);

        var p = new Repository
        {
            ModelId = repo.ModelId, ModelName = repo.ModelName, Language = repo.Language, SchemaLocation = repo.SchemaLocation,
            Metamodel = repo.Metamodel,
            Objects = new Dictionary<string, ArchObject>(repo.Objects),
            Relationships = new Dictionary<string, Relationship>(repo.Relationships),
            Diagrams = repo.Diagrams, Folders = repo.Folders, Impacts = repo.Impacts, Assessments = repo.Assessments,
            Scenarios = repo.Scenarios,
        };
        var marks = new Dictionary<string, string>();
        var issues = new List<ProjectionIssue>();
        p.Replacements = new Dictionary<string, string>();
        var applier = new Applier(repo, p, marks, issues, envisioned);
        foreach (var s in chain)
            foreach (var c in s.Changes) applier.Apply(s, c);
        p.Projection = marks;
        var project = repo.Objects.TryGetValue(scenario.ProjectId, out var po) ? po.Name : scenario.ProjectId;
        return new Projected { Repo = p, Marks = marks, Issues = issues, Scenario = scenario, Step = step, Label = $"{project} › {scenario.Name} › {step.Name}" };
    }

    /// <summary>Étapes à appliquer, dans l'ordre : celles des scénarios de départ, puis celles du scénario jusqu'à l'étape choisie.</summary>
    public static List<TrajectoryStep> Chain(Repository repo, Scenario scenario, TrajectoryStep upTo)
    {
        var parts = new List<List<TrajectoryStep>>();
        var seen = new HashSet<string>();
        var current = scenario;
        var limit = upTo;
        while (current is not null && seen.Add(current.Id))
        {
            parts.Add(current.Steps.Where(s => s.Order <= limit.Order).OrderBy(s => s.Order).ToList());
            if (current.StartScenarioId is null || !repo.Scenarios.TryGetValue(current.StartScenarioId, out var parent)) break;
            var start = parent.Steps.FirstOrDefault(s => s.Id == current.StartStepId);
            if (start is null) break;
            current = parent; limit = start;
        }
        parts.Reverse();
        return parts.SelectMany(x => x).Where(s => s.Status is not ("Livrée" or "Annulée")).ToList();
    }

    /// <summary>Le scénario et ceux dont il part, du plus proche au plus lointain.</summary>
    public static IEnumerable<Scenario> ScenariosOf(Repository repo, Scenario scenario)
    {
        var seen = new HashSet<string>();
        for (var s = scenario; s is not null && seen.Add(s.Id);
             s = s.StartScenarioId is not null && repo.Scenarios.TryGetValue(s.StartScenarioId, out var parent) ? parent : null)
            yield return s;
    }

    /// <summary>Copie d'un scénario avec de nouveaux identifiants (scénario, étapes, changements, objets envisagés).</summary>
    public static Scenario CloneScenario(Scenario s, Func<string, string> newId)
    {
        var copy = Clone(s);
        copy.Id = newId("scn");
        var ids = copy.Envisioned.Keys.ToDictionary(k => k, _ => newId("env"));
        string? Map(string? id) => id is not null && ids.TryGetValue(id, out var n) ? n : id;
        copy.Envisioned = copy.Envisioned.ToDictionary(kv => ids[kv.Key], kv => { kv.Value.Id = ids[kv.Key]; return kv.Value; });
        foreach (var step in copy.Steps)
        {
            step.Id = newId("etp");
            foreach (var c in step.Changes)
            {
                c.Id = newId("chg");
                c.ElementId = Map(c.ElementId); c.ReplacementId = Map(c.ReplacementId);
                if (c.Relation is not null) { c.Relation.SourceId = Map(c.Relation.SourceId)!; c.Relation.TargetId = Map(c.Relation.TargetId)!; }
            }
        }
        return copy;
    }

    internal static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Json), Json)!;

    private sealed class Applier(Repository baseRepo, Repository p, Dictionary<string, string> marks, List<ProjectionIssue> issues,
                                 Dictionary<string, ArchObject> envisioned)
    {
        private void Issue(string severity, string code, string message, TrajectoryStep s, TrajectoryChange c) =>
            issues.Add(new ProjectionIssue(severity, code, message, s.Id, c.Id));

        private void Mark(string id, string mark)
        {
            // Ajouté puis modifié reste « ajouté » ; tout ce qui est retiré l'emporte.
            if (mark == ProjectionMarks.Modified && marks.TryGetValue(id, out var m) && m == ProjectionMarks.Added) return;
            marks[id] = mark;
        }

        private string NameOf(string id) =>
            p.Objects.TryGetValue(id, out var o) ? o.Name : envisioned.TryGetValue(id, out var e) ? e.Name
            : p.Relationships.TryGetValue(id, out var r) ? r.Name ?? id : id;

        private ArchObject? EditableObject(string id)
        {
            if (!p.Objects.TryGetValue(id, out var o)) return null;
            if (ReferenceEquals(o, baseRepo.Objects.GetValueOrDefault(id))) p.Objects[id] = o = Clone(o);
            return o;
        }

        private Relationship? EditableRelation(string id)
        {
            if (!p.Relationships.TryGetValue(id, out var r)) return null;
            if (ReferenceEquals(r, baseRepo.Relationships.GetValueOrDefault(id))) p.Relationships[id] = r = Clone(r);
            return r;
        }

        private bool Alive(string id) =>
            p.Objects.TryGetValue(id, out var o) ? !o.Archived : p.Relationships.TryGetValue(id, out var r) && !r.Archived;

        /// <summary>Fait entrer un objet envisagé dans l'état projeté (au premier changement qui le cite).</summary>
        private bool Bring(string id)
        {
            if (p.Objects.TryGetValue(id, out var existing)) return !existing.Archived;
            if (!envisioned.TryGetValue(id, out var e)) return false;
            var o = Clone(e);
            o.Archived = false;
            p.Objects[id] = o;
            Mark(id, ProjectionMarks.Added);
            return true;
        }

        public void Apply(TrajectoryStep s, TrajectoryChange c)
        {
            switch (c.Op)
            {
                case ChangeOps.Add when c.Target == "relation": AddRelation(s, c); break;
                case ChangeOps.Add: AddObject(s, c); break;
                case ChangeOps.Remove: Remove(s, c); break;
                case ChangeOps.Modify: Modify(s, c); break;
                case ChangeOps.Replace: Replace(s, c); break;
                default: Issue("erreur", "operation-inconnue", $"Opération « {c.Op} » inconnue.", s, c); break;
            }
        }

        private void AddObject(TrajectoryStep s, TrajectoryChange c)
        {
            if (c.ElementId is null) { Issue("erreur", "ajout-vide", "Ajout sans objet.", s, c); return; }
            if (p.Objects.TryGetValue(c.ElementId, out var o) && !o.Archived && !envisioned.ContainsKey(c.ElementId))
            {
                Issue("avertissement", "sans-effet", $"« {o.Name} » existe déjà : l'ajout est sans effet.", s, c);
                return;
            }
            if (!Bring(c.ElementId)) Issue("erreur", "objet-introuvable", "L'objet à ajouter n'existe plus dans le scénario.", s, c);
        }

        private void AddRelation(TrajectoryStep s, TrajectoryChange c)
        {
            var spec = c.Relation;
            if (spec is null) { Issue("erreur", "ajout-vide", "Ajout de relation sans définition.", s, c); return; }
            Bring(spec.SourceId); Bring(spec.TargetId);
            if (!Alive(spec.SourceId) || !Alive(spec.TargetId))
            {
                Issue("erreur", "extremite-absente", $"La relation « {NameOf(spec.SourceId)} → {NameOf(spec.TargetId)} » relie un élément absent à cette étape.", s, c);
                return;
            }
            p.Relationships[c.Id] = new Relationship
            {
                Id = c.Id, TypeKey = spec.TypeKey, SourceId = spec.SourceId, TargetId = spec.TargetId, Name = spec.Name,
                Properties = spec.Properties.Select(x => new PropertyValue { Key = x.Key, Value = x.Value }).ToList(), Source = "scénario",
            };
            Mark(c.Id, ProjectionMarks.Added);
        }

        private void Remove(TrajectoryStep s, TrajectoryChange c)
        {
            if (c.ElementId is null || !Alive(c.ElementId))
            {
                Issue("avertissement", "sans-effet", $"« {NameOf(c.ElementId ?? "")} » est déjà absent : le retrait est sans effet.", s, c);
                return;
            }
            Retire(c.ElementId);
        }

        /// <summary>Retire un objet ou une relation, et les relations qui n'ont plus d'extrémité.</summary>
        private void Retire(string id)
        {
            if (p.Objects.ContainsKey(id)) EditableObject(id)!.Archived = true;
            else EditableRelation(id)!.Archived = true;
            Mark(id, ProjectionMarks.Removed);
            foreach (var r in p.Relationships.Values.Where(r => !r.Archived && (r.SourceId == id || r.TargetId == id)).Select(r => r.Id).ToList())
                Retire(r);
        }

        private void Modify(TrajectoryStep s, TrajectoryChange c)
        {
            if (c.ElementId is null || !Alive(c.ElementId))
            {
                Issue("avertissement", "sans-effet", $"« {NameOf(c.ElementId ?? "")} » est absent à cette étape : la modification est sans effet.", s, c);
                return;
            }
            var isObject = p.Objects.ContainsKey(c.ElementId);
            var props = isObject ? EditableObject(c.ElementId)!.Properties : EditableRelation(c.ElementId)!.Properties;
            var name = isObject ? p.Objects[c.ElementId].Name : p.Relationships[c.ElementId].Name;
            // L'existant a-t-il bougé depuis la saisie du changement ?
            var drift = (c.BeforeName is not null && c.BeforeName != name)
                        || (c.Before ?? new()).Any(b => (props.FirstOrDefault(x => x.Key == b.Key)?.Value ?? "") != (b.Value ?? ""));
            if (drift) Issue("avertissement", "a-revoir", $"« {name} » a changé depuis la saisie de cette modification : à revoir.", s, c);
            if (c.NewName is { Length: > 0 })
            {
                if (isObject) p.Objects[c.ElementId].Name = c.NewName; else p.Relationships[c.ElementId].Name = c.NewName;
            }
            foreach (var v in c.Set ?? new())
            {
                props.RemoveAll(x => x.Key == v.Key);
                if (!string.IsNullOrEmpty(v.Value)) props.Add(new PropertyValue { Key = v.Key, Value = v.Value });
            }
            Mark(c.ElementId, ProjectionMarks.Modified);
        }

        private void Replace(TrajectoryStep s, TrajectoryChange c)
        {
            if (c.ElementId is null || c.ReplacementId is null) { Issue("erreur", "remplacement-incomplet", "Remplacement sans objet remplacé ou remplaçant.", s, c); return; }
            if (!Alive(c.ElementId)) { Issue("avertissement", "sans-effet", $"« {NameOf(c.ElementId)} » est déjà absent : le remplacement est sans effet.", s, c); return; }
            if (!Bring(c.ReplacementId)) { Issue("erreur", "objet-introuvable", "Le remplaçant n'existe pas à cette étape.", s, c); return; }
            var from = c.ElementId; var to = c.ReplacementId;
            // Les relations de l'objet remplacé sont reportées sur le remplaçant, sauf celles qu'il porte déjà.
            foreach (var r in p.Relationships.Values.Where(r => !r.Archived && (r.SourceId == from || r.TargetId == from)).Select(r => r.Id).ToList())
            {
                var rel = p.Relationships[r];
                var src = rel.SourceId == from ? to : rel.SourceId;
                var tgt = rel.TargetId == from ? to : rel.TargetId;
                if (src == tgt) continue;
                var twin = p.Relationships.Values.Any(x => !x.Archived && x.Id != r && x.TypeKey == rel.TypeKey && x.SourceId == src && x.TargetId == tgt);
                if (twin) { Retire(r); continue; }
                var e = EditableRelation(r)!;
                e.SourceId = src; e.TargetId = tgt;
                Mark(r, ProjectionMarks.Modified);
            }
            Retire(from);
            p.Replacements![from] = to;
        }
    }
}
