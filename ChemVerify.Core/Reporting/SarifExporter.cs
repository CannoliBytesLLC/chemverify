using System.Text.Json;
using System.Text.Json.Serialization;
using ChemVerify.Abstractions;
using ChemVerify.Abstractions.Contracts;
using ChemVerify.Abstractions.Enums;
using ChemVerify.Abstractions.Interfaces;
using ChemVerify.Abstractions.Models;
using ChemVerify.Abstractions.Validation;

namespace ChemVerify.Core.Reporting;

public static class SarifExporter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string Build(
        ReportDto report,
        IReadOnlyList<ValidationFinding> findings,
        string inputPath,
        string? inputText = null,
        IEnumerable<IValidator>? validators = null)
    {
        List<ValidationFinding> nonPass = findings
            .Where(f => f.Status != ValidationStatus.Pass)
            .ToList();

        var metadataCatalog = RuleMetadataCatalog.Create(validators);
        SarifTextIndex? textIndex = inputText is null ? null : SarifTextIndex.Build(inputText);
        string uri = Path.GetFileName(inputPath);

        Dictionary<string, SarifRule> rulesMap = new(StringComparer.Ordinal);
        foreach (ValidationFinding finding in nonPass)
        {
            string ruleId = finding.RuleId ?? finding.ValidatorName;
            RuleMetadata? metadata = metadataCatalog.Resolve(ruleId, finding.ValidatorName);
            string ruleName = metadata?.Name ?? finding.ValidatorName ?? ruleId;
            string description = string.IsNullOrWhiteSpace(metadata?.Description)
                ? ruleName
                : metadata!.Description;
            string level = metadata?.Level ?? MapLevelFromStatus(finding.Status);

            rulesMap.TryAdd(ruleId, new SarifRule
            {
                Id = ruleId,
                Name = ruleName,
                ShortDescription = new SarifMessage { Text = description },
                DefaultConfiguration = new SarifRuleConfiguration { Level = level }
            });
        }

        List<SarifRule> rules = rulesMap.Values
            .OrderBy(r => r.Id, StringComparer.Ordinal)
            .ToList();

        List<SarifResult> results = nonPass
            .OrderBy(f => f.RuleId ?? f.ValidatorName, StringComparer.Ordinal)
            .ThenBy(f => f.EvidenceStartOffset ?? int.MaxValue)
            .ThenBy(f => f.Message, StringComparer.Ordinal)
            .Select(f => MapResult(f, uri, textIndex, metadataCatalog))
            .ToList();

        SarifLog log = new()
        {
            Runs =
            [
                new SarifRun
                {
                    Tool = new SarifTool
                    {
                        Driver = new SarifDriver
                        {
                            Name = "ChemVerify",
                            Version = report.EngineVersion,
                            InformationUri = "https://github.com/CannoliBytesLLC/chemverify",
                            Rules = rules
                        }
                    },
                    Results = results
                }
            ]
        };

        return JsonSerializer.Serialize(log, SerializerOptions);
    }

    private static SarifResult MapResult(
        ValidationFinding finding,
        string uri,
        SarifTextIndex? textIndex,
        RuleMetadataCatalog metadataCatalog)
    {
        string ruleId = finding.RuleId ?? finding.ValidatorName;
        RuleMetadata? metadata = metadataCatalog.Resolve(ruleId, finding.ValidatorName);

        SarifResult result = new()
        {
            RuleId = ruleId,
            Message = new SarifMessage { Text = finding.Message },
            Level = metadata?.Level ?? MapLevelFromStatus(finding.Status)
        };

        SarifRegion? region = BuildRegion(finding, textIndex);
        if (region is not null)
        {
            result.Locations =
            [
                new SarifLocation
                {
                    PhysicalLocation = new SarifPhysicalLocation
                    {
                        ArtifactLocation = new SarifArtifactLocation { Uri = uri },
                        Region = region
                    }
                }
            ];
        }

        return result;
    }

    private static SarifRegion? BuildRegion(ValidationFinding finding, SarifTextIndex? textIndex)
    {
        if (finding.EvidenceStartOffset is null || finding.EvidenceEndOffset is null)
        {
            return null;
        }

        int startOffset = finding.EvidenceStartOffset.Value;
        int endOffset = finding.EvidenceEndOffset.Value;
        if (endOffset < startOffset)
        {
            endOffset = startOffset;
        }

        SarifRegion region = new();
        if (textIndex is not null)
        {
            (int startLine, int startColumn) = textIndex.GetLineColumn(startOffset);
            (int endLine, int endColumn) = textIndex.GetLineColumn(endOffset);

            region.StartLine = startLine;
            region.StartColumn = startColumn;
            region.EndLine = endLine;
            region.EndColumn = endColumn;
        }
        else
        {
            region.CharOffset = startOffset;
            region.CharLength = Math.Max(0, endOffset - startOffset);
        }

        if (!string.IsNullOrWhiteSpace(finding.EvidenceSnippet))
        {
            region.Snippet = new SarifSnippet { Text = finding.EvidenceSnippet };
        }

        return region;
    }

    private static string MapLevelFromStatus(ValidationStatus status) => status switch
    {
        ValidationStatus.Fail => "error",
        ValidationStatus.Unverified => "warning",
        _ => "note"
    };

    private static string MapLevelFromSeverity(Severity severity) => severity switch
    {
        Severity.Info => "note",
        Severity.Low => "warning",
        Severity.Medium => "warning",
        Severity.High => "error",
        Severity.Critical => "error",
        _ => "warning"
    };

    private sealed record RuleMetadata(string Id, string Name, string Description, string Level);

    private sealed class RuleMetadataCatalog
    {
        private readonly Dictionary<string, RuleMetadata> _byId;
        private readonly Dictionary<string, RuleMetadata> _byName;

        private RuleMetadataCatalog(
            Dictionary<string, RuleMetadata> byId,
            Dictionary<string, RuleMetadata> byName)
        {
            _byId = byId;
            _byName = byName;
        }

        public static RuleMetadataCatalog Create(IEnumerable<IValidator>? validators)
        {
            Dictionary<string, RuleMetadata> byId = new(StringComparer.Ordinal);
            Dictionary<string, RuleMetadata> byName = new(StringComparer.Ordinal);

            if (validators is not null)
            {
                foreach (IValidator validator in validators)
                {
                    Type type = validator.GetType();
                    ValidatorMetadataAttribute? metadata = type.GetValidatorMetadata();
                    string id = metadata?.Id ?? type.Name;
                    string name = type.Name;
                    string description = metadata?.Description ?? string.Empty;
                    string level = MapLevelFromSeverity(metadata?.DefaultSeverity ?? Severity.Medium);

                    RuleMetadata ruleMetadata = new(id, name, description, level);
                    byId[id] = ruleMetadata;
                    byName[name] = ruleMetadata;
                }
            }

            return new RuleMetadataCatalog(byId, byName);
        }

        public RuleMetadata? Resolve(string ruleId, string validatorName)
        {
            if (_byId.TryGetValue(ruleId, out RuleMetadata? metadata))
            {
                return metadata;
            }

            if (_byName.TryGetValue(validatorName, out metadata))
            {
                return metadata;
            }

            return null;
        }
    }

    private sealed class SarifTextIndex
    {
        private readonly int[] _lineStarts;
        private readonly int _textLength;

        private SarifTextIndex(int[] lineStarts, int textLength)
        {
            _lineStarts = lineStarts;
            _textLength = textLength;
        }

        public static SarifTextIndex Build(string text)
        {
            List<int> lineStarts = [0];
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\n')
                {
                    lineStarts.Add(i + 1);
                }
            }

            return new SarifTextIndex(lineStarts.ToArray(), text.Length);
        }

        public (int Line, int Column) GetLineColumn(int offset)
        {
            int clamped = Math.Clamp(offset, 0, _textLength);
            int index = Array.BinarySearch(_lineStarts, clamped);
            if (index < 0)
            {
                index = ~index - 1;
            }

            int lineStart = _lineStarts[index];
            return (index + 1, clamped - lineStart + 1);
        }
    }
}
