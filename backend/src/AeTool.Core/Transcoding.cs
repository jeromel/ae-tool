namespace AeTool.Core;

// Matrice de transcodification entre le métamodèle de l'outil et ArchiMate, utilisée par l'import et l'export Archi.

public sealed class ElementRule
{
    /// <summary>Type du métamodèle.</summary>
    public string TypeKey { get; set; } = "";
    /// <summary>Concept ArchiMate, tel qu'écrit dans xsi:type du format d'échange.</summary>
    public string ArchimateType { get; set; } = "";
    /// <summary>Discriminant facultatif : nom de propriété Archi…</summary>
    public string? WhenProperty { get; set; }
    /// <summary>…et valeur attendue (comparaison sans casse, espaces de bord ignorés).</summary>
    public string? WhenValue { get; set; }
    /// <summary>Règle utilisée à l'export pour ce type du métamodèle.</summary>
    public bool Principal { get; set; }

    public bool Conditional => !string.IsNullOrWhiteSpace(WhenProperty);
}

public sealed class RelationRule
{
    public string TypeKey { get; set; } = "";
    public string ArchimateType { get; set; } = "";
    /// <summary>Type du métamodèle exigé à l'extrémité source ; vide = tout accepter.</summary>
    public string? SourceTypeKey { get; set; }
    public string? TargetTypeKey { get; set; }
    public bool Principal { get; set; }

    public int Constraints => (string.IsNullOrEmpty(SourceTypeKey) ? 0 : 1) + (string.IsNullOrEmpty(TargetTypeKey) ? 0 : 1);
}

public sealed class Transcoding
{
    public List<ElementRule> Elements { get; set; } = new();
    public List<RelationRule> Relations { get; set; } = new();
    /// <summary>Concept d'export d'un type sans règle.</summary>
    public string FallbackElementType { get; set; } = "Grouping";
    public string FallbackRelationType { get; set; } = "Association";
    /// <summary>Propriété écrite à l'export pour retrouver le type au réimport.</summary>
    public string TypeMarkerProperty { get; set; } = "ae:type";
    /// <summary>Type recevant un concept ArchiMate sans règle.</summary>
    public string UnmappedTypeKey { get; set; } = "autre";
    public string UnmappedRelationTypeKey { get; set; } = "association";
}

/// <summary>Propriété telle qu'écrite dans Archi (nom Archi, pas clé du métamodèle).</summary>
public readonly record struct ArchiProperty(string Name, string? Value);
/// <summary>Type retenu à l'import ; <see cref="Mapped"/> est faux quand aucune règle ne s'applique.</summary>
public sealed record TypeChoice(string TypeKey, bool Mapped);
/// <summary>Concept à écrire à l'export, et propriété éventuelle à ajouter (discriminant ou marqueur).</summary>
public sealed record ExportChoice(string Concept, ArchiProperty? Extra);
public sealed record Finding(string Severity, string Code, string Message, string? Key = null);
public sealed record RuleUsage(string Kind, string TypeKey, string ArchimateType, string? Condition, int Count);
public sealed record TranscodingReport(List<Finding> Findings, List<RuleUsage> Rules);
public sealed record Move(string Kind, string From, string To, int Count);
public sealed record Reclassification(int Changes, List<Move> Moves, bool DryRun);

public sealed class Transcoder
{
    private readonly Metamodel _m;
    private readonly Dictionary<string, string> _keyByArchiName = new();
    private readonly Dictionary<string, PropertyDefinition> _definitionByArchiName = new();

    public Transcoder(Metamodel metamodel)
    {
        _m = metamodel;
        foreach (var p in metamodel.PropertyDefinitions) Register(p);
    }

    private Transcoding Rules => _m.Transcoding;

    /// <summary>Nom de la propriété marqueur de type.</summary>
    public string Marker => Rules.TypeMarkerProperty;

    private void Register(PropertyDefinition p)
    {
        _keyByArchiName.TryAdd(p.ArchiPropertyName, p.Key);
        _definitionByArchiName.TryAdd(p.ArchiPropertyName, p);
    }

    // ------------------------------------------------------------ Propriétés

    public string KeyOf(string archiName) => _keyByArchiName.GetValueOrDefault(archiName, archiName);
    public string ArchiNameOf(string key) => _m.Property(key)?.ArchiPropertyName ?? key;

    public static string FromArchiDataType(string? archiType) => archiType switch
    {
        "number" => PropertyTypes.Number, "date" => PropertyTypes.Date, "boolean" => PropertyTypes.Boolean, _ => PropertyTypes.Text,
    };

    public static string ToArchiDataType(string? dataType) => dataType switch
    {
        PropertyTypes.Number => "number", PropertyTypes.Date => "date", PropertyTypes.Boolean => "boolean", _ => "string",
    };

    /// <summary>Type de la définition Archi d'une propriété, d'après son nom Archi.</summary>
    public string ArchiDataType(string archiName) =>
        ToArchiDataType(_definitionByArchiName.GetValueOrDefault(archiName)?.DataType);

    /// <summary>Crée la définition d'une propriété Archi inconnue ; renvoie vrai si elle a été créée.</summary>
    public bool EnsureProperty(string archiName, string? archiType, string source)
    {
        if (_keyByArchiName.ContainsKey(archiName)) return false;
        var def = new PropertyDefinition { Key = archiName, Label = archiName, DataType = FromArchiDataType(archiType), Source = source };
        _m.PropertyDefinitions.Add(def);
        Register(def);
        return true;
    }

    /// <summary>Propriétés Archi converties en valeurs du métamodèle ; le marqueur de type n'est jamais conservé.</summary>
    public List<PropertyValue> Values(IEnumerable<ArchiProperty> raw) =>
        raw.Where(p => p.Name != Rules.TypeMarkerProperty).Select(p => new PropertyValue { Key = KeyOf(p.Name), Value = p.Value }).ToList();

    /// <summary>Valeurs du métamodèle converties en propriétés Archi, avec éventuellement une propriété ajoutée ou corrigée.</summary>
    public List<ArchiProperty> ArchiProperties(IEnumerable<PropertyValue> values, ArchiProperty? extra = null)
    {
        var list = values.Select(p => new ArchiProperty(ArchiNameOf(p.Key), p.Value)).ToList();
        if (extra is { } e)
        {
            var index = list.FindIndex(p => p.Name == e.Name);
            if (index < 0) list.Add(e);
            else if (!Same(list[index].Value, e.Value)) list[index] = e;
        }
        return list;
    }

    private static bool Same(string? a, string? b) =>
        string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);

    // ------------------------------------------------------------ Import

    private static string? Find(IReadOnlyList<ArchiProperty> props, string name) =>
        props.Where(p => p.Name == name).Select(p => p.Value).FirstOrDefault();

    private bool Matches(ElementRule rule, IReadOnlyList<ArchiProperty> props) =>
        !rule.Conditional || props.Any(p => p.Name == rule.WhenProperty && Same(p.Value, rule.WhenValue));

    /// <summary>Type du métamodèle pour un élément Archi : marqueur, règle conditionnelle, règle simple, sinon type « non classé ».</summary>
    public TypeChoice ElementType(string archimateType, IReadOnlyList<ArchiProperty> props)
    {
        if (Find(props, Rules.TypeMarkerProperty) is { } marked && _m.Type(marked) is not null) return new TypeChoice(marked, true);
        var candidates = Rules.Elements.Where(r => r.ArchimateType == archimateType).ToList();
        var rule = candidates.FirstOrDefault(r => r.Conditional && Matches(r, props)) ?? candidates.FirstOrDefault(r => !r.Conditional);
        return rule is null ? new TypeChoice(Rules.UnmappedTypeKey, false) : new TypeChoice(rule.TypeKey, true);
    }

    /// <summary>Type du métamodèle pour une relation Archi ; les extrémités sont null quand ce sont des relations.</summary>
    public TypeChoice RelationType(string archimateType, IReadOnlyList<ArchiProperty> props, string? sourceType, string? targetType)
    {
        if (Find(props, Rules.TypeMarkerProperty) is { } marked && _m.RelType(marked) is not null) return new TypeChoice(marked, true);
        var rule = Rules.Relations
            .Where(r => r.ArchimateType == archimateType
                        && (string.IsNullOrEmpty(r.SourceTypeKey) || r.SourceTypeKey == sourceType)
                        && (string.IsNullOrEmpty(r.TargetTypeKey) || r.TargetTypeKey == targetType))
            .OrderByDescending(r => r.Constraints).FirstOrDefault();
        return rule is null ? new TypeChoice(Rules.UnmappedRelationTypeKey, false) : new TypeChoice(rule.TypeKey, true);
    }

    /// <summary>Crée au besoin le type recevant les concepts sans règle, dans la couche « autre ».</summary>
    public string EnsureUnmappedType()
    {
        var key = Rules.UnmappedTypeKey;
        if (_m.Type(key) is not null) return key;
        if (_m.Layers.All(l => l.Key != "autre")) _m.Layers.Add(new Layer { Key = "autre", Label = "Autre", Order = _m.Layers.Count });
        _m.ObjectTypes.Add(new ObjectType
        {
            Key = key, Label = "Autre (non classé)", LayerKey = "autre",
            Visual = new Visual { Shape = "rectangle", Fill = "#f0f0f0", Stroke = "#5c5c5c", TextColor = "#000000",
                Template = DrawingDefaults.FallbackTemplate, Icon = DrawingDefaults.FallbackGlyph },
        });
        return key;
    }

    public string EnsureUnmappedRelationType()
    {
        var key = Rules.UnmappedRelationTypeKey;
        if (_m.RelType(key) is null)
            _m.RelationshipTypes.Add(new RelationshipType { Key = key, Label = "Association", ReverseLabel = "Est associé à" });
        return key;
    }

    // ------------------------------------------------------------ Export

    /// <summary>Concept d'export d'un objet : son concept d'origine si une règle le permet, sinon la règle principale, sinon le repli.</summary>
    public ExportChoice ExportElement(ArchObject o)
    {
        var rules = Rules.Elements.Where(r => r.TypeKey == o.TypeKey).ToList();
        var rule = string.IsNullOrEmpty(o.ArchimateType) ? null : rules.FirstOrDefault(r => r.ArchimateType == o.ArchimateType);
        // Un objet « non classé » garde son concept standard d'origine : sans cela l'aller-retour le perdrait.
        if (rule is null && o.TypeKey == Rules.UnmappedTypeKey && rules.Count == 0 && Archimate.IsElement(o.ArchimateType))
            return new ExportChoice(o.ArchimateType!, null);
        rule ??= rules.FirstOrDefault(r => r.Principal) ?? rules.FirstOrDefault();
        if (rule is null) return new ExportChoice(Rules.FallbackElementType, new ArchiProperty(Rules.TypeMarkerProperty, o.TypeKey));
        return new ExportChoice(rule.ArchimateType, rule.Conditional ? new ArchiProperty(rule.WhenProperty!, rule.WhenValue) : null);
    }

    public ExportChoice ExportRelation(Relationship r)
    {
        var rules = Rules.Relations.Where(x => x.TypeKey == r.TypeKey).ToList();
        var rule = string.IsNullOrEmpty(r.ArchimateType) ? null : rules.FirstOrDefault(x => x.ArchimateType == r.ArchimateType);
        if (rule is null && r.TypeKey == Rules.UnmappedRelationTypeKey && rules.Count == 0 && Archimate.IsRelationship(r.ArchimateType))
            return new ExportChoice(r.ArchimateType!, null);
        rule ??= rules.FirstOrDefault(x => x.Principal) ?? rules.FirstOrDefault();
        return rule is null
            ? new ExportChoice(Rules.FallbackRelationType, new ArchiProperty(Rules.TypeMarkerProperty, r.TypeKey))
            : new ExportChoice(rule.ArchimateType, null);
    }

    // ------------------------------------------------------------ Réapplication

    /// <summary>
    /// Recalcule le type de chaque objet et de chaque relation qui a un concept d'origine, selon les règles courantes.
    /// Un objet de type sans règle exporté avec le concept de repli (il a été retrouvé grâce au marqueur) est laissé tel quel :
    /// son type vient de l'outil, pas de la matrice.
    /// </summary>
    public Reclassification Reclassify(Repository repo, bool dryRun, string user = "système")
    {
        var ruled = Rules.Elements.Select(r => r.TypeKey).ToHashSet();
        var objectType = repo.Objects.Values.ToDictionary(o => o.Id, o => o.TypeKey);
        var moves = new Dictionary<(string Kind, string From, string To), int>();
        var objectChanges = new List<(ArchObject Item, string To)>();
        foreach (var o in repo.Objects.Values.Where(o => !string.IsNullOrEmpty(o.ArchimateType)))
        {
            if (!ruled.Contains(o.TypeKey) && o.TypeKey != Rules.UnmappedTypeKey && o.ArchimateType == Rules.FallbackElementType) continue;
            var to = ElementType(o.ArchimateType!, ArchiProperties(o.Properties)).TypeKey;
            if (to == o.TypeKey) continue;
            objectChanges.Add((o, to));
            objectType[o.Id] = to;
            Count(moves, "objet", o.TypeKey, to);
        }
        var relationChanges = new List<(Relationship Item, string To)>();
        var relRuled = Rules.Relations.Select(r => r.TypeKey).ToHashSet();
        foreach (var r in repo.Relationships.Values.Where(r => !string.IsNullOrEmpty(r.ArchimateType)))
        {
            if (!relRuled.Contains(r.TypeKey) && r.TypeKey != Rules.UnmappedRelationTypeKey && r.ArchimateType == Rules.FallbackRelationType) continue;
            var to = RelationType(r.ArchimateType!, ArchiProperties(r.Properties), objectType.GetValueOrDefault(r.SourceId), objectType.GetValueOrDefault(r.TargetId)).TypeKey;
            if (to == r.TypeKey) continue;
            relationChanges.Add((r, to));
            Count(moves, "relation", r.TypeKey, to);
        }
        var result = new Reclassification(objectChanges.Count + relationChanges.Count,
            moves.Select(kv => new Move(kv.Key.Kind, kv.Key.From, kv.Key.To, kv.Value)).OrderBy(x => x.Kind).ThenByDescending(x => x.Count).ToList(), dryRun);
        if (dryRun || result.Changes == 0) return result;
        foreach (var (item, to) in objectChanges)
        {
            item.TypeKey = to == Rules.UnmappedTypeKey ? EnsureUnmappedType() : to;
            item.UpdatedAt = DateTime.UtcNow;
        }
        foreach (var (item, to) in relationChanges) item.TypeKey = to == Rules.UnmappedRelationTypeKey ? EnsureUnmappedRelationType() : to;
        repo.Log(user, "Métamodèle : matrice réappliquée", null,
            $"{objectChanges.Count} objet(s) et {relationChanges.Count} relation(s) retypé(s)");
        return result;
    }

    private static void Count(Dictionary<(string, string, string), int> moves, string kind, string from, string to) =>
        moves[(kind, from, to)] = moves.GetValueOrDefault((kind, from, to)) + 1;

    // ------------------------------------------------------------ Contrôles

    /// <summary>Erreurs de structure d'une matrice : règles visant un type inconnu ou un concept hors liste standard.</summary>
    public static List<Finding> Errors(Metamodel m, Transcoding t)
    {
        var errors = new List<Finding>();
        void Error(string code, string message, string? key) => errors.Add(new Finding("erreur", code, message, key));
        foreach (var r in t.Elements)
        {
            if (m.Type(r.TypeKey) is null) Error("regle-type-inconnu", $"Règle d'élément vers un type inconnu : « {r.TypeKey} ».", r.TypeKey);
            if (!Archimate.IsElement(r.ArchimateType)) Error("concept-hors-standard", $"« {r.ArchimateType} » n'est pas un concept d'élément ArchiMate standard.", r.ArchimateType);
            if (r.Conditional && r.WhenValue is null) Error("regle-condition-incomplete", $"La règle {r.TypeKey} → {r.ArchimateType} a une propriété discriminante sans valeur.", r.TypeKey);
        }
        foreach (var r in t.Relations)
        {
            if (m.RelType(r.TypeKey) is null) Error("regle-type-inconnu", $"Règle de relation vers un type inconnu : « {r.TypeKey} ».", r.TypeKey);
            if (!Archimate.IsRelationship(r.ArchimateType)) Error("concept-hors-standard", $"« {r.ArchimateType} » n'est pas un concept de relation ArchiMate standard.", r.ArchimateType);
            foreach (var end in new[] { r.SourceTypeKey, r.TargetTypeKey }.Where(e => !string.IsNullOrEmpty(e)))
                if (m.Type(end!) is null) Error("regle-type-inconnu", $"Contrainte d'extrémité vers un type inconnu : « {end} ».", end);
        }
        if (!Archimate.IsElement(t.FallbackElementType)) Error("concept-hors-standard", $"Concept d'élément de repli invalide : « {t.FallbackElementType} ».", t.FallbackElementType);
        if (!Archimate.IsRelationship(t.FallbackRelationType)) Error("concept-hors-standard", $"Concept de relation de repli invalide : « {t.FallbackRelationType} ».", t.FallbackRelationType);
        if (string.IsNullOrWhiteSpace(t.TypeMarkerProperty)) Error("marqueur-absent", "Le nom de la propriété marqueur est obligatoire.", "TypeMarkerProperty");
        if (m.Type(t.UnmappedTypeKey) is null && m.ObjectTypes.Count > 0 && t.UnmappedTypeKey != "autre")
            Error("regle-type-inconnu", $"Type des concepts sans règle inconnu : « {t.UnmappedTypeKey} ».", t.UnmappedTypeKey);
        if (m.RelType(t.UnmappedRelationTypeKey) is null && m.RelationshipTypes.Count > 0 && t.UnmappedRelationTypeKey != "association")
            Error("regle-type-inconnu", $"Type de relation des concepts sans règle inconnu : « {t.UnmappedRelationTypeKey} ».", t.UnmappedRelationTypeKey);
        return errors;
    }

    /// <summary>Constats sur la matrice courante et, par règle, le nombre d'objets ou de relations concernés.</summary>
    public TranscodingReport Check(Repository repo)
    {
        var findings = Errors(_m, Rules);
        void Add(string severity, string code, string message, string key) => findings.Add(new Finding(severity, code, message, key));

        foreach (var g in Rules.Elements.Where(r => !r.Conditional).GroupBy(r => r.ArchimateType).Where(g => g.Count() > 1))
            Add("avertissement", "regle-doublon", $"Plusieurs règles sans condition pour « {g.Key} » : la première l'emporte.", g.Key);
        foreach (var g in Rules.Relations.Where(r => r.Constraints == 0).GroupBy(r => r.ArchimateType).Where(g => g.Count() > 1))
            Add("avertissement", "regle-doublon", $"Plusieurs règles de relation sans contrainte pour « {g.Key} » : la première l'emporte.", g.Key);

        var ruledTypes = Rules.Elements.Select(r => r.TypeKey).ToHashSet();
        foreach (var t in _m.ObjectTypes.Where(t => !ruledTypes.Contains(t.Key) && t.Key != Rules.UnmappedTypeKey))
            Add("information", "type-sans-regle", $"Le type « {t.Label} » n'a pas de règle : exporté avec le concept de repli et un marqueur.", t.Key);
        var relRuled = Rules.Relations.Select(r => r.TypeKey).ToHashSet();
        foreach (var t in _m.RelationshipTypes.Where(t => !relRuled.Contains(t.Key) && t.Key != Rules.UnmappedRelationTypeKey))
            Add("information", "type-sans-regle", $"Le type de relation « {t.Label} » n'a pas de règle : exporté avec le concept de repli et un marqueur.", t.Key);

        var knownConcepts = Rules.Elements.Select(r => r.ArchimateType).ToHashSet();
        foreach (var g in repo.Objects.Values.Where(o => !string.IsNullOrEmpty(o.ArchimateType) && !knownConcepts.Contains(o.ArchimateType!)).GroupBy(o => o.ArchimateType!))
            Add("avertissement", "concept-sans-regle", $"{g.Count()} objet(s) de concept « {g.Key} » sans règle de transcodification.", g.Key);
        var knownRelations = Rules.Relations.Select(r => r.ArchimateType).ToHashSet();
        foreach (var g in repo.Relationships.Values.Where(r => !string.IsNullOrEmpty(r.ArchimateType) && !knownRelations.Contains(r.ArchimateType!)).GroupBy(r => r.ArchimateType!))
            Add("avertissement", "concept-sans-regle", $"{g.Count()} relation(s) de concept « {g.Key} » sans règle de transcodification.", g.Key);

        var objectCounts = repo.Objects.Values.GroupBy(o => (o.TypeKey, o.ArchimateType)).ToDictionary(g => g.Key, g => g.Count());
        var relationCounts = repo.Relationships.Values.GroupBy(r => (r.TypeKey, r.ArchimateType)).ToDictionary(g => g.Key, g => g.Count());
        var usage = Rules.Elements.Select(r => new RuleUsage("element", r.TypeKey, r.ArchimateType,
                r.Conditional ? $"{r.WhenProperty} = {r.WhenValue}" : null, objectCounts.GetValueOrDefault((r.TypeKey, (string?)r.ArchimateType))))
            .Concat(Rules.Relations.Select(r => new RuleUsage("relation", r.TypeKey, r.ArchimateType,
                r.Constraints == 0 ? null : $"{r.SourceTypeKey ?? "*"} > {r.TargetTypeKey ?? "*"}", relationCounts.GetValueOrDefault((r.TypeKey, (string?)r.ArchimateType)))))
            .ToList();
        return new TranscodingReport(findings, usage);
    }
}
