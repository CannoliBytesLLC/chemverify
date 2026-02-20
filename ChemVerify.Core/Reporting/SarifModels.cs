using System.Text.Json.Serialization;

namespace ChemVerify.Core.Reporting;

internal sealed class SarifLog
{
    [JsonPropertyName("$schema")]
    public string Schema { get; set; } = "https://json.schemastore.org/sarif-2.1.0.json";
    public string Version { get; set; } = "2.1.0";
    public List<SarifRun> Runs { get; set; } = [];
}

internal sealed class SarifRun
{
    public SarifTool Tool { get; set; } = new();
    public List<SarifResult> Results { get; set; } = [];
}

internal sealed class SarifTool
{
    public SarifDriver Driver { get; set; } = new();
}

internal sealed class SarifDriver
{
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string? InformationUri { get; set; }
    public List<SarifRule> Rules { get; set; } = [];
}

internal sealed class SarifRule
{
    public string Id { get; set; } = string.Empty;
    public string? Name { get; set; }
    public SarifMessage? ShortDescription { get; set; }
    public SarifRuleConfiguration? DefaultConfiguration { get; set; }
}

internal sealed class SarifRuleConfiguration
{
    public string Level { get; set; } = "warning";
}

internal sealed class SarifResult
{
    public string RuleId { get; set; } = string.Empty;
    public SarifMessage Message { get; set; } = new();
    public string Level { get; set; } = "warning";
    public List<SarifLocation>? Locations { get; set; }
}

internal sealed class SarifMessage
{
    public string Text { get; set; } = string.Empty;
}

internal sealed class SarifLocation
{
    public SarifPhysicalLocation PhysicalLocation { get; set; } = new();
}

internal sealed class SarifPhysicalLocation
{
    public SarifArtifactLocation ArtifactLocation { get; set; } = new();
    public SarifRegion? Region { get; set; }
}

internal sealed class SarifArtifactLocation
{
    public string Uri { get; set; } = string.Empty;
}

internal sealed class SarifRegion
{
    public int? StartLine { get; set; }
    public int? StartColumn { get; set; }
    public int? EndLine { get; set; }
    public int? EndColumn { get; set; }
    public int? CharOffset { get; set; }
    public int? CharLength { get; set; }
    public SarifSnippet? Snippet { get; set; }
}

internal sealed class SarifSnippet
{
    public string Text { get; set; } = string.Empty;
}
