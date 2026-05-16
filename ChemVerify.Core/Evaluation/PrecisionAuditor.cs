using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ChemVerify.Core.Evaluation;

/// <summary>
/// Validator precision audit tool. Consumes the bulk-validation failures JSON
/// produced by the Pistachio harness and emits a structured, deterministic
/// audit suitable for human review and tuning.
/// </summary>
/// <remarks>
/// This is an observability layer: it does NOT change validator behaviour.
/// It groups failures by validator, samples up to N per validator (seeded for
/// reproducibility), heuristically pre-classifies each finding into a likely
/// false-positive class, and produces JSON, CSV, and summary projections.
/// </remarks>
public static class PrecisionAuditor
{
    /// <summary>Suspected false-positive class assigned by the pre-classifier.</summary>
    public enum ReviewLabel
    {
        Unclassified,
        TruePositive,
        FalsePositive,
        Ambiguous,
        ExtractionArtifact,
        ScopeLeakage,
        ReagentLifecycleIssue,
        LimitingReagentIssue,
        MediumRoleIssue,
        ProductPropertyIssue,
        SequentialOperationIssue,
        MissingContext
    }

    public sealed class AuditOptions
    {
        public int TopValidatorCount { get; init; } = 5;
        public int SamplesPerValidator { get; init; } = 50;
        public int Seed { get; init; } = 1337;
        public int TopPhraseCount { get; init; } = 25;
    }

    // ── Input schema (matches PistachioTest failure JSON) ───────────────

    public sealed class FailureRecord
    {
        public int Index { get; set; }
        public string SourceFile { get; set; } = "";
        public int TextLength { get; set; }
        public string FullText { get; set; } = "";
        public int ClaimCount { get; set; }
        public int FindingCount { get; set; }
        public int FailCount { get; set; }
        public List<FindingRecord> Findings { get; set; } = [];
    }

    public sealed class FindingRecord
    {
        public string Validator { get; set; } = "";
        public string Status { get; set; } = "";
        public string? Message { get; set; }
        public double Confidence { get; set; }
        public string? Kind { get; set; }
        public string? EvidenceSnippet { get; set; }
    }

    // ── Output schema ────────────────────────────────────────────────────

    public sealed class AuditedFinding
    {
        public string SourceFile { get; init; } = "";
        public int ParagraphIndex { get; init; }
        public string Validator { get; init; } = "";
        public string Status { get; init; } = "";
        public string? Kind { get; init; }
        public double Confidence { get; init; }
        public string? EvidenceSnippet { get; init; }
        public string FullText { get; init; } = "";
        public string SuspectedFailureClass { get; init; } = nameof(ReviewLabel.Unclassified);
        public string? OperationType { get; init; }
        public int? StepIndex { get; init; }
        public string? Severity { get; init; }
        public IReadOnlyList<string> SuggestedReviewLabels { get; init; } = [];
        public string? Message { get; init; }
    }

    public sealed class ValidatorSummary
    {
        public string Validator { get; init; } = "";
        public int FailCount { get; init; }
        public int SampledCount { get; init; }
        public Dictionary<string, int> ClassDistribution { get; init; } = new();
    }

    public sealed class ClassSummary
    {
        public string SuspectedFailureClass { get; init; } = "";
        public int Count { get; init; }
        public Dictionary<string, int> ByValidator { get; init; } = new();
    }

    public sealed class PhraseFrequency
    {
        public string Phrase { get; init; } = "";
        public int Occurrences { get; init; }
        public Dictionary<string, int> ByValidator { get; init; } = new();
    }

    public sealed class TuningRecommendation
    {
        public string Validator { get; init; } = "";
        public string Recommendation { get; init; } = "";
        public string Rationale { get; init; } = "";
        public int EstimatedImpact { get; init; }
    }

    public sealed class AuditReport
    {
        public DateTimeOffset GeneratedUtc { get; init; }
        public int TotalFailureRecords { get; init; }
        public int TotalFailFindings { get; init; }
        public int Seed { get; init; }
        public int SamplesPerValidator { get; init; }
        public List<ValidatorSummary> ValidatorSummaries { get; init; } = [];
        public List<ClassSummary> ClassSummaries { get; init; } = [];
        public List<PhraseFrequency> TopPhrases { get; init; } = [];
        public List<TuningRecommendation> Recommendations { get; init; } = [];
        public List<AuditedFinding> SampledFindings { get; init; } = [];
    }

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>Loads a failure JSON file (top-level array of <see cref="FailureRecord"/>).</summary>
    public static IReadOnlyList<FailureRecord> LoadFailures(string failuresJsonPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failuresJsonPath);
        using FileStream fs = File.OpenRead(failuresJsonPath);
        return JsonSerializer.Deserialize<List<FailureRecord>>(fs, ReadOptions) ?? [];
    }

    /// <summary>
    /// Builds a precision audit from in-memory failure records. Sampling is
    /// deterministic for a given seed.
    /// </summary>
    public static AuditReport Audit(
        IReadOnlyList<FailureRecord> failures,
        AuditOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(failures);
        options ??= new AuditOptions();

        // Flatten all Fail findings, preserving paragraph context.
        List<(FailureRecord Para, FindingRecord Finding)> allFails = [];
        foreach (FailureRecord rec in failures)
        {
            foreach (FindingRecord f in rec.Findings)
            {
                if (string.Equals(f.Status, "Fail", StringComparison.OrdinalIgnoreCase))
                    allFails.Add((rec, f));
            }
        }

        // Rank validators by fail count and pick top N.
        Dictionary<string, int> failByValidator = allFails
            .GroupBy(t => t.Finding.Validator, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        string[] topValidators = failByValidator
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Take(Math.Max(0, options.TopValidatorCount))
            .Select(kv => kv.Key)
            .ToArray();

        // Deterministic sampling per validator (Fisher–Yates on an index list).
        Random rng = new(options.Seed);
        List<AuditedFinding> sampled = [];
        List<ValidatorSummary> validatorSummaries = [];

        foreach (string validator in topValidators)
        {
            (FailureRecord Para, FindingRecord Finding)[] pool = allFails
                .Where(t => t.Finding.Validator == validator)
                .OrderBy(t => t.Para.SourceFile, StringComparer.Ordinal)
                .ThenBy(t => t.Para.Index)
                .ThenBy(t => t.Finding.Kind, StringComparer.Ordinal)
                .ToArray();

            int take = Math.Min(options.SamplesPerValidator, pool.Length);
            int[] indices = SampleIndices(pool.Length, take, rng);

            Dictionary<string, int> classDist = new(StringComparer.Ordinal);
            foreach (int i in indices)
            {
                AuditedFinding af = Classify(pool[i].Para, pool[i].Finding);
                sampled.Add(af);
                classDist[af.SuspectedFailureClass] =
                    classDist.GetValueOrDefault(af.SuspectedFailureClass) + 1;
            }

            validatorSummaries.Add(new ValidatorSummary
            {
                Validator = validator,
                FailCount = pool.Length,
                SampledCount = take,
                ClassDistribution = classDist
            });
        }

        // Class summary across all sampled findings.
        Dictionary<string, ClassSummary> classMap = new(StringComparer.Ordinal);
        foreach (AuditedFinding af in sampled)
        {
            if (!classMap.TryGetValue(af.SuspectedFailureClass, out ClassSummary? cs))
            {
                cs = new ClassSummary { SuspectedFailureClass = af.SuspectedFailureClass };
                classMap[af.SuspectedFailureClass] = cs;
            }

            cs.ByValidator[af.Validator] = cs.ByValidator.GetValueOrDefault(af.Validator) + 1;
        }

        List<ClassSummary> classSummaries = classMap.Values
            .Select(c => new ClassSummary
            {
                SuspectedFailureClass = c.SuspectedFailureClass,
                Count = c.ByValidator.Values.Sum(),
                ByValidator = c.ByValidator
            })
            .OrderByDescending(c => c.Count)
            .ThenBy(c => c.SuspectedFailureClass, StringComparer.Ordinal)
            .ToList();

        List<PhraseFrequency> topPhrases = ExtractTopPhrases(sampled, options.TopPhraseCount);
        List<TuningRecommendation> recs = BuildRecommendations(validatorSummaries, classSummaries);

        return new AuditReport
        {
            GeneratedUtc = DateTimeOffset.UtcNow,
            TotalFailureRecords = failures.Count,
            TotalFailFindings = allFails.Count,
            Seed = options.Seed,
            SamplesPerValidator = options.SamplesPerValidator,
            ValidatorSummaries = validatorSummaries,
            ClassSummaries = classSummaries,
            TopPhrases = topPhrases,
            Recommendations = recs,
            SampledFindings = sampled
        };
    }

    /// <summary>Writes the audit report JSON to <paramref name="path"/>.</summary>
    public static void WriteJson(AuditReport report, string path)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        File.WriteAllText(path, JsonSerializer.Serialize(report, WriteOptions));
    }

    /// <summary>Writes the per-finding sampled CSV to <paramref name="path"/>.</summary>
    public static void WriteCsv(AuditReport report, string path)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        StringBuilder sb = new();
        sb.AppendLine(
            "SourceFile,ParagraphIndex,Validator,Status,Kind,Confidence," +
            "SuspectedFailureClass,OperationType,StepIndex,Severity," +
            "SuggestedReviewLabels,EvidenceSnippet,Message,FullText");

        foreach (AuditedFinding f in report.SampledFindings)
        {
            sb.Append(Csv(f.SourceFile)).Append(',')
              .Append(f.ParagraphIndex).Append(',')
              .Append(Csv(f.Validator)).Append(',')
              .Append(Csv(f.Status)).Append(',')
              .Append(Csv(f.Kind)).Append(',')
              .Append(f.Confidence.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
              .Append(Csv(f.SuspectedFailureClass)).Append(',')
              .Append(Csv(f.OperationType)).Append(',')
              .Append(f.StepIndex?.ToString(CultureInfo.InvariantCulture) ?? "").Append(',')
              .Append(Csv(f.Severity)).Append(',')
              .Append(Csv(string.Join("|", f.SuggestedReviewLabels))).Append(',')
              .Append(Csv(f.EvidenceSnippet)).Append(',')
              .Append(Csv(f.Message)).Append(',')
              .Append(Csv(f.FullText))
              .AppendLine();
        }

        File.WriteAllText(path, sb.ToString());
    }

    // ── Heuristic pre-classifier ────────────────────────────────────────

    private static readonly Regex ProductPropertyRegex = new(
        @"\b(?:bp|b\.?p\.?|mp|m\.?p\.?|melting\s+point|boiling\s+point|mmHg|torr|" +
        @"NMR|MS|HRMS|m/?z|m/?e|IR|UV|HPLC|GC|TLC\s*Rf)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SequentialCueRegex = new(
        @"\b(?:then|after|followed\s+by|subsequently|over\s+\d|additional|" +
        @"next|thereafter|finally|once)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex AqueousMediumRegex = new(
        @"\b(?:in\s+\d+\s*[MN]\s*(?:HCl|H2SO4|HNO3|NaOH|KOH)|in\s+aqueous|" +
        @"in\s+amine|in\s+pyridine|in\s+triethylamine|in\s+TEA|in\s+DBU|in\s+DIPEA|" +
        @"in\s+water|in\s+brine)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex WorkupBoundaryRegex = new(
        @"\b(?:quench(?:ed|ing)?|workup|worked\s+up|wash(?:ed|ing)?|" +
        @"extract(?:ed|ing|ion)?|partition(?:ed|ing)?|aqueous\s+phase|" +
        @"organic\s+phase|brine)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex AnalysisPhaseRegex = new(
        @"\b(?:NMR|MS|HRMS|IR|UV|elem(?:ental)?\s+anal|" +
        @"melting\s+point|boiling\s+point|distillation|recrystalli[sz]ed)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // catalyst / additive sized masses (mg, microgram) — used as a heuristic
    // signal for limiting-reagent confusion in YieldMassConsistency findings.
    private static readonly Regex SmallMassRegex = new(
        @"\b\d+(?:\.\d+)?\s*(?:mg|µg|ug|mcg)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CatalystAdditiveRegex = new(
        @"\b(?:cat(?:alyst|alytic)?|additive|loading|mol\s*%|equiv(?:alents?)?|" +
        @"Pd|Pt|Ni|Cu|Rh|Ir|Ru|TBAF|TBAI|DMAP|Et3N|TEA|K2CO3|NaHCO3)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static AuditedFinding Classify(FailureRecord para, FindingRecord f)
    {
        string evidence = f.EvidenceSnippet ?? "";
        string fullText = para.FullText ?? "";
        string combined = evidence + " " + fullText;

        ReviewLabel suspected = ReviewLabel.Unclassified;
        List<string> suggested = [];

        bool sequential = SequentialCueRegex.IsMatch(combined);
        bool productProperty = ProductPropertyRegex.IsMatch(evidence)
            || ProductPropertyRegex.IsMatch(fullText);
        bool aqueousMedium = AqueousMediumRegex.IsMatch(combined);
        bool aqueousAfterWorkup = AqueousAfterWorkup(fullText, evidence);
        bool afterAnalysis = EvidenceAfterAnalysis(fullText, evidence);

        switch (f.Validator)
        {
            case "YieldMassConsistencyValidator":
                if (SmallMassRegex.IsMatch(evidence) && CatalystAdditiveRegex.IsMatch(combined))
                {
                    suspected = ReviewLabel.LimitingReagentIssue;
                    suggested.Add(nameof(ReviewLabel.LimitingReagentIssue));
                }
                break;

            case "MissingSolventValidator":
                if (aqueousMedium)
                {
                    suspected = ReviewLabel.MediumRoleIssue;
                    suggested.Add(nameof(ReviewLabel.MediumRoleIssue));
                }
                break;

            case "IncompatibleReagentSolventValidator":
                if (aqueousAfterWorkup)
                {
                    suspected = ReviewLabel.ReagentLifecycleIssue;
                    suggested.Add(nameof(ReviewLabel.ReagentLifecycleIssue));
                }
                break;

            case "NumericContradictionValidator":
                if (sequential)
                {
                    suspected = ReviewLabel.SequentialOperationIssue;
                    suggested.Add(nameof(ReviewLabel.SequentialOperationIssue));
                }
                break;

            case "SolventTemperatureValidator":
                if (productProperty)
                {
                    suspected = ReviewLabel.ProductPropertyIssue;
                    suggested.Add(nameof(ReviewLabel.ProductPropertyIssue));
                }
                break;
        }

        if (afterAnalysis && suspected == ReviewLabel.Unclassified)
        {
            suspected = ReviewLabel.ScopeLeakage;
        }
        if (afterAnalysis && !suggested.Contains(nameof(ReviewLabel.ScopeLeakage)))
        {
            suggested.Add(nameof(ReviewLabel.ScopeLeakage));
        }

        // Always suggest the manual triage trio so reviewers have a baseline.
        suggested.Add(nameof(ReviewLabel.TruePositive));
        suggested.Add(nameof(ReviewLabel.FalsePositive));
        suggested.Add(nameof(ReviewLabel.Ambiguous));

        return new AuditedFinding
        {
            SourceFile = para.SourceFile,
            ParagraphIndex = para.Index,
            Validator = f.Validator,
            Status = f.Status,
            Kind = f.Kind,
            Confidence = f.Confidence,
            EvidenceSnippet = f.EvidenceSnippet,
            FullText = para.FullText,
            SuspectedFailureClass = suspected.ToString(),
            OperationType = null,   // reserved for future engine wiring
            StepIndex = null,       // reserved for future engine wiring
            Severity = null,        // reserved for future engine wiring
            SuggestedReviewLabels = suggested,
            Message = f.Message
        };
    }

    private static bool AqueousAfterWorkup(string fullText, string evidence)
    {
        if (string.IsNullOrEmpty(fullText) || string.IsNullOrEmpty(evidence)) return false;
        Match boundary = WorkupBoundaryRegex.Match(fullText);
        if (!boundary.Success) return false;
        int evidenceIdx = fullText.IndexOf(evidence, StringComparison.Ordinal);
        if (evidenceIdx < 0) return false;
        return evidenceIdx > boundary.Index;
    }

    private static bool EvidenceAfterAnalysis(string fullText, string evidence)
    {
        if (string.IsNullOrEmpty(fullText) || string.IsNullOrEmpty(evidence)) return false;
        Match analysis = AnalysisPhaseRegex.Match(fullText);
        if (!analysis.Success) return false;
        int evidenceIdx = fullText.IndexOf(evidence, StringComparison.Ordinal);
        if (evidenceIdx < 0) return false;
        return evidenceIdx > analysis.Index;
    }

    // ── Phrase mining ───────────────────────────────────────────────────

    private static readonly Regex TokenSplitRegex = new(@"[^A-Za-z0-9°%]+", RegexOptions.Compiled);
    private static readonly HashSet<string> Stopwords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "a", "an", "and", "or", "of", "to", "in", "on", "at", "for", "with",
        "was", "were", "is", "are", "be", "been", "by", "as", "from", "this", "that",
        "it", "its", "into", "over", "than", "then", "after", "before", "until"
    };

    private static List<PhraseFrequency> ExtractTopPhrases(IReadOnlyList<AuditedFinding> findings, int top)
    {
        // Bigram frequency on evidence snippets (cheap approximation of "repeated patterns").
        Dictionary<string, Dictionary<string, int>> counts = new(StringComparer.OrdinalIgnoreCase);

        foreach (AuditedFinding f in findings)
        {
            string snippet = f.EvidenceSnippet ?? "";
            if (snippet.Length == 0) continue;

            string[] tokens = TokenSplitRegex.Split(snippet)
                .Where(t => t.Length > 1 && !Stopwords.Contains(t))
                .Select(t => t.ToLowerInvariant())
                .ToArray();

            for (int i = 0; i + 1 < tokens.Length; i++)
            {
                string bigram = tokens[i] + " " + tokens[i + 1];
                if (!counts.TryGetValue(bigram, out Dictionary<string, int>? perValidator))
                {
                    perValidator = new Dictionary<string, int>(StringComparer.Ordinal);
                    counts[bigram] = perValidator;
                }
                perValidator[f.Validator] = perValidator.GetValueOrDefault(f.Validator) + 1;
            }
        }

        return counts
            .Select(kv => new PhraseFrequency
            {
                Phrase = kv.Key,
                Occurrences = kv.Value.Values.Sum(),
                ByValidator = kv.Value
            })
            .Where(p => p.Occurrences >= 2)
            .OrderByDescending(p => p.Occurrences)
            .ThenBy(p => p.Phrase, StringComparer.Ordinal)
            .Take(top)
            .ToList();
    }

    // ── Recommendation synthesizer ──────────────────────────────────────

    private static List<TuningRecommendation> BuildRecommendations(
        List<ValidatorSummary> validators,
        List<ClassSummary> classes)
    {
        List<TuningRecommendation> recs = [];
        foreach (ValidatorSummary v in validators)
        {
            if (v.SampledCount == 0) continue;

            foreach ((string cls, int count) in v.ClassDistribution
                .OrderByDescending(kv => kv.Value))
            {
                if (cls == nameof(ReviewLabel.Unclassified)) continue;

                double share = count / (double)v.SampledCount;
                if (share < 0.10) continue;

                int est = (int)Math.Round(v.FailCount * share);
                recs.Add(new TuningRecommendation
                {
                    Validator = v.Validator,
                    Recommendation = RecommendationFor(v.Validator, cls),
                    Rationale = $"{count}/{v.SampledCount} sampled findings ({share:P0}) " +
                                $"pre-classified as {cls}; extrapolated impact ~{est} fails.",
                    EstimatedImpact = est
                });
            }
        }

        return recs.OrderByDescending(r => r.EstimatedImpact).ToList();
    }

    private static string RecommendationFor(string validator, string cls) => cls switch
    {
        nameof(ReviewLabel.ProductPropertyIssue) =>
            $"Tighten {validator} to suppress findings whose evidence falls inside an Analysis/ProductProperty operation context.",
        nameof(ReviewLabel.SequentialOperationIssue) =>
            $"Restrict {validator} contradictions to claims within the same operation context; broaden sequential-cue gating.",
        nameof(ReviewLabel.ReagentLifecycleIssue) =>
            $"Extend reagent lifecycle termination in {validator} so post-workup aqueous mentions are not flagged.",
        nameof(ReviewLabel.MediumRoleIssue) =>
            $"Teach {validator} to recognize aqueous acid/base/amine media as solvent-equivalent.",
        nameof(ReviewLabel.LimitingReagentIssue) =>
            $"Have {validator} compare yield mass against a designated limiting reagent rather than catalyst/additive masses.",
        nameof(ReviewLabel.ScopeLeakage) =>
            $"Add operation-scope termination in {validator} for evidence appearing after analysis/product-property cues.",
        _ => $"Manually review {cls} cases in {validator} to determine the correct gating."
    };

    // ── Sampling + CSV helpers ──────────────────────────────────────────

    private static int[] SampleIndices(int populationSize, int take, Random rng)
    {
        if (take >= populationSize)
        {
            int[] all = new int[populationSize];
            for (int i = 0; i < populationSize; i++) all[i] = i;
            return all;
        }

        int[] pool = new int[populationSize];
        for (int i = 0; i < populationSize; i++) pool[i] = i;

        // Partial Fisher–Yates: only need the first `take` elements.
        for (int i = 0; i < take; i++)
        {
            int j = i + rng.Next(populationSize - i);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }

        int[] result = new int[take];
        Array.Copy(pool, result, take);
        Array.Sort(result);
        return result;
    }

    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        bool needsQuote = value.IndexOfAny([',', '"', '\n', '\r']) >= 0;
        string escaped = value.Replace("\"", "\"\"");
        return needsQuote ? "\"" + escaped + "\"" : escaped;
    }
}
