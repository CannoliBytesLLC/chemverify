using ChemVerify.Abstractions.Enums;

namespace ChemVerify.Abstractions.Evaluation;

/// <summary>
/// A single manually-reviewed benchmark example used to measure validator quality.
/// Each item carries the source text, expected findings, and reviewer metadata.
/// Designed for serialization to/from JSON gold-set files.
/// </summary>
/// <param name="Id">Stable identifier for this gold-set example (e.g. "GS-0042").</param>
/// <param name="ParagraphText">The procedure or paragraph text to verify.</param>
/// <param name="ExpectedFindings">What a correct engine run should produce.</param>
/// <param name="ReviewerLabel">Who reviewed this example (initials or handle).</param>
/// <param name="ReviewedAtUtc">When the review was completed.</param>
/// <param name="ReviewerNotes">Free-text notes from the reviewer about the overall example.</param>
/// <param name="DomainTags">
/// Chemistry domain tags for stratified analysis
/// (e.g. "reduction", "esterification", "organometallic", "inert-atmosphere").
/// </param>
/// <param name="SourceDataset">
/// Origin of this example (e.g. "pistachio", "USPTO", "manual-authored").
/// </param>
/// <param name="ClaimsJson">
/// Optional serialized <see cref="Models.ExtractedClaim"/> list.
/// When provided, the audit runner can skip extraction and test validators in isolation.
/// </param>
public sealed record GoldSetItem(
    string Id,
    string ParagraphText,
    IReadOnlyList<ExpectedFinding> ExpectedFindings,
    string ReviewerLabel,
    DateTimeOffset ReviewedAtUtc,
    string? ReviewerNotes = null,
    IReadOnlyList<string>? DomainTags = null,
    string? SourceDataset = null,
    string? ClaimsJson = null);
