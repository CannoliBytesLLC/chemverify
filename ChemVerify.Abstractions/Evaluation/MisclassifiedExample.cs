using ChemVerify.Abstractions.Enums;
using ChemVerify.Abstractions.Models;

namespace ChemVerify.Abstractions.Evaluation;

/// <summary>
/// A single misclassified example (false positive or false negative) captured
/// during audit for debugging and rule improvement.
/// </summary>
/// <param name="GoldSetItemId">The gold-set example that was misclassified.</param>
/// <param name="ParagraphSnippet">Truncated paragraph text for quick identification.</param>
/// <param name="ActualStatus">What the validator produced.</param>
/// <param name="ExpectedStatus">What the gold-set says the correct answer is.</param>
/// <param name="Confidence">Validator's confidence for its (incorrect) finding.</param>
/// <param name="FindingMessage">The message from the actual finding.</param>
/// <param name="FindingKind">The kind from the actual finding.</param>
public sealed record MisclassifiedExample(
    string GoldSetItemId,
    string ParagraphSnippet,
    ValidationStatus ActualStatus,
    ValidationStatus ExpectedStatus,
    double Confidence,
    string? FindingMessage = null,
    string? FindingKind = null);
