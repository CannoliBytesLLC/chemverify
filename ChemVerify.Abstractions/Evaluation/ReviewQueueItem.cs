using ChemVerify.Abstractions.Enums;
using ChemVerify.Abstractions.Models;

namespace ChemVerify.Abstractions.Evaluation;

/// <summary>
/// A single item in the reviewer queue — a paragraph + its actual findings,
/// prioritized for manual inspection. Designed for JSON/CSV export.
/// </summary>
/// <param name="ParagraphText">The source text that was verified.</param>
/// <param name="Findings">Actual findings produced by the engine.</param>
/// <param name="Priority">Lower is higher priority (1 = most urgent).</param>
/// <param name="PriorityReason">Human-readable explanation of why this item was prioritized.</param>
/// <param name="ValidatorNames">Distinct validators that fired on this paragraph.</param>
/// <param name="MaxConfidence">Highest confidence among all findings.</param>
/// <param name="HasConflictingSignals">True when findings contain both Pass and Fail for overlapping context.</param>
/// <param name="UnverifiedCount">Number of findings with <see cref="ValidationStatus.Unverified"/>.</param>
/// <param name="SourceId">Optional trace-back to a gold-set or corpus item.</param>
/// <param name="ReviewerErrorCategory">
/// Populated after review — classifies the root cause if findings were wrong.
/// </param>
/// <param name="ReviewerNotes">Free-text notes added during review.</param>
public sealed record ReviewQueueItem(
    string ParagraphText,
    IReadOnlyList<ValidationFinding> Findings,
    int Priority,
    string PriorityReason,
    IReadOnlyList<string> ValidatorNames,
    double MaxConfidence,
    bool HasConflictingSignals,
    int UnverifiedCount,
    string? SourceId = null,
    ErrorCategory? ReviewerErrorCategory = null,
    string? ReviewerNotes = null);
