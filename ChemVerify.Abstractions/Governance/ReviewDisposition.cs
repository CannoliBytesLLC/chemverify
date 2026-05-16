namespace ChemVerify.Abstractions.Governance;

/// <summary>
/// Triage state for a <see cref="Models.ValidationFinding"/> as it moves through
/// human review. Used by the governance review workflow to track validator
/// precision and false-positive rates over time.
/// </summary>
public enum ReviewDisposition
{
    /// <summary>Awaiting human review.</summary>
    PendingReview = 0,

    /// <summary>Reviewer confirmed the finding represents a real issue.</summary>
    ConfirmedIssue,

    /// <summary>Reviewer marked the finding as a false positive.</summary>
    FalsePositive,

    /// <summary>Reviewer judged the finding to be a benign chemistry/procedural variation.</summary>
    BenignVariation,

    /// <summary>Reviewer escalated the finding to a domain expert.</summary>
    RequiresDomainExpert,

    /// <summary>Reviewer deferred decision pending additional evidence.</summary>
    Deferred
}
