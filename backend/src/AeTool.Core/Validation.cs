namespace AeTool.Core;

/// <summary>Résultat d'une transition : réussie, ou refusée avec un message destiné à l'utilisateur.</summary>
public sealed record Transition(bool Ok, string? Error = null)
{
    public static readonly Transition Done = new(true);
    public static Transition Refused(string error) => new(false, error);
}

/// <summary>
/// Circuit de validation d'un objet (EF-51) : brouillon, proposé, validé. Les règles d'état sont ici, pures ; qui a le droit
/// d'appeler chaque transition est décidé par la table des habilitations (contributeur pour proposer, valideur pour le reste).
/// </summary>
public static class Validation
{
    public const string Draft = "Brouillon";
    public const string Proposed = "Proposé";
    public const string Validated = "Validé";

    public static readonly string[] All = { Draft, Proposed, Validated };

    public static bool IsStatus(string? status) => status is not null && All.Contains(status);

    /// <summary>État d'un objet qui entre dans le référentiel par un import Archi : le réglage du métamodèle, « Validé » s'il est absent ou inconnu.</summary>
    public static string ArchiImportStatus(Metamodel m) => IsStatus(m.Settings.ArchiImportStatus) ? m.Settings.ArchiImportStatus : Validated;

    /// <summary>Applique l'état d'entrée à un objet neuf, avec l'auteur et la date qui vont avec.</summary>
    public static void Enter(ArchObject o, string status, string user, DateTime now)
    {
        o.ValidationStatus = status;
        if (status == Proposed) (o.ProposedBy, o.ProposedAt) = (user, now);
        else if (status == Validated) (o.ValidatedBy, o.ValidatedAt) = (user, now);
    }

    /// <summary>Brouillon → Proposé.</summary>
    public static Transition Propose(ArchObject o, string user, DateTime now)
    {
        if (o.Archived) return Transition.Refused("Un objet archivé ne peut pas être proposé à la validation.");
        if (o.ValidationStatus == Proposed) return Transition.Refused("L'objet est déjà proposé à la validation.");
        if (o.ValidationStatus == Validated) return Transition.Refused("L'objet est déjà validé.");
        o.ValidationStatus = Proposed;
        (o.ProposedBy, o.ProposedAt) = (user, now);
        // Une nouvelle proposition répond au refus précédent : son commentaire ne doit pas survivre.
        o.ValidationComment = null;
        return Transition.Done;
    }

    /// <summary>Proposé → Validé, ou Brouillon → Validé : un valideur peut valider directement.</summary>
    public static Transition Validate(ArchObject o, string user, DateTime now)
    {
        if (o.Archived) return Transition.Refused("Un objet archivé ne peut pas être validé.");
        if (o.ValidationStatus == Validated) return Transition.Refused("L'objet est déjà validé.");
        o.ValidationStatus = Validated;
        (o.ValidatedBy, o.ValidatedAt) = (user, now);
        o.ValidationComment = null;
        return Transition.Done;
    }

    /// <summary>Proposé → Brouillon. Le commentaire est obligatoire : sans lui, l'auteur ignore ce qu'il doit corriger.</summary>
    public static Transition Reject(ArchObject o, string? comment, DateTime now)
    {
        if (o.ValidationStatus != Proposed) return Transition.Refused("Seul un objet proposé à la validation peut être refusé.");
        if (string.IsNullOrWhiteSpace(comment)) return Transition.Refused("Le commentaire du refus est obligatoire.");
        o.ValidationStatus = Draft;
        o.ValidationComment = comment.Trim();
        // Le refus ne laisse pas croire que l'objet est en attente d'un valideur.
        (o.ProposedBy, o.ProposedAt) = (null, null);
        return Transition.Done;
    }

    /// <summary>
    /// Effet d'une modification sur l'état. Un objet validé modifié sur le fond par un non-valideur repasse à « proposé » :
    /// il doit être revalidé. Un valideur qui modifie reste maître de l'état. Renvoie vrai si l'état a changé.
    /// </summary>
    public static bool AfterChange(ArchObject o, ObjectState before, bool byValidator, string user, DateTime now)
    {
        if (byValidator || o.ValidationStatus != Validated || !ChangeSet.IsSubstantive(before, ChangeSet.Capture(o))) return false;
        o.ValidationStatus = Proposed;
        (o.ProposedBy, o.ProposedAt) = (user, now);
        (o.ValidatedBy, o.ValidatedAt) = (null, null);
        return true;
    }
}
