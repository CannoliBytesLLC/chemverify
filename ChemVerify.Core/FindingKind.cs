namespace ChemVerify.Core;

/// <summary>
/// Well-known finding kind values used across validators and the risk scorer.
/// Stored as strings (not an enum) to keep ValidationFinding flexible,
/// but centralised here to prevent typo drift.
/// </summary>
public static class FindingKind
{
    /// <summary>Only one claim exists for this context+unit; nothing to compare against.</summary>
    public const string NotCheckable = "NotCheckable";

    /// <summary>Evidence is missing or could not be retrieved (e.g. DOI not resolved).</summary>
    public const string MissingEvidence = "MissingEvidence";

    /// <summary>Claims differ but strong scenario language suggests they describe different conditions.</summary>
    public const string MultiScenario = "MultiScenario";

    /// <summary>Claims differ beyond the contradiction threshold with no mitigating context.</summary>
    public const string Contradiction = "Contradiction";
}

