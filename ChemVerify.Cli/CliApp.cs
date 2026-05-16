using System.CommandLine;

namespace ChemVerify.Cli;

/// <summary>
/// Builds the System.CommandLine root command for the ChemVerify CLI.
/// </summary>
public static class CliApp
{
    public static Task<int> RunAsync(string[] args)
    {
        CommandLineConfiguration config = BuildConfiguration();
        return config.InvokeAsync(args);
    }

    /// <summary>
    /// Constructs the <see cref="CommandLineConfiguration"/> for the CLI.
    /// Exposed for test invocation with custom Output / Error writers.
    /// </summary>
    public static CommandLineConfiguration BuildConfiguration(
        TextWriter? output = null,
        TextWriter? error = null)
    {
        var root = new RootCommand("ChemVerify \u2014 scientific text verification engine");

        var pathArg = new Argument<string>("path")
        {
            Description = "Path to the input text file to analyze"
        };

        var profileOption = new Option<string>("--profile")
        {
            Description = "Policy profile name",
            DefaultValueFactory = _ => "Default"
        };

        var formatOption = new Option<string>("--format")
        {
            Description = "Output format: json or sarif",
            DefaultValueFactory = _ => "json"
        };

        var outOption = new Option<string?>("--out")
        {
            Description = "Output file path (stdout if omitted)"
        };

        var maxInputCharsOption = new Option<int>("--max-input-chars")
        {
            Description = "Maximum allowed input character count",
            DefaultValueFactory = _ => 500_000
        };

        var analyzeCommand = new Command("analyze", "Analyze a scientific text file for verification")
        {
            pathArg,
            profileOption,
            formatOption,
            outOption,
            maxInputCharsOption
        };

        analyzeCommand.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            string path = parseResult.GetRequiredValue(pathArg);
            string profile = parseResult.GetValue(profileOption)!;
            string format = parseResult.GetValue(formatOption)!;
            string? outPath = parseResult.GetValue(outOption);
            int maxChars = parseResult.GetValue(maxInputCharsOption);

            return await AnalyzeCommandHandler.ExecuteAsync(
                path, profile, format, outPath, maxChars,
                parseResult.Configuration.Output,
                parseResult.Configuration.Error,
                ct);
        });

        root.Add(analyzeCommand);
        root.Add(BuildAuditCommand());

        var config = new CommandLineConfiguration(root);
        if (output is not null) config.Output = output;
        if (error is not null) config.Error = error;
        return config;
    }

    private static Command BuildAuditCommand()
    {
        var inputArg = new Argument<string>("input")
        {
            Description = "Path to a Pistachio failure JSON file"
        };
        var outDirOption = new Option<string?>("--out-dir")
        {
            Description = "Output directory for audit JSON/CSV (defaults to input file directory)"
        };
        var topValidatorsOption = new Option<int>("--top-validators")
        {
            Description = "Number of top validators (by fail count) to sample",
            DefaultValueFactory = _ => 5
        };
        var samplesOption = new Option<int>("--samples")
        {
            Description = "Number of findings to sample per validator",
            DefaultValueFactory = _ => 50
        };
        var seedOption = new Option<int>("--seed")
        {
            Description = "Sampling seed for deterministic output",
            DefaultValueFactory = _ => 1337
        };

        var auditCommand = new Command("audit", "Run a precision audit over a Pistachio failure JSON file")
        {
            inputArg,
            outDirOption,
            topValidatorsOption,
            samplesOption,
            seedOption
        };

        auditCommand.SetAction((ParseResult parseResult, CancellationToken _) =>
        {
            string input = parseResult.GetRequiredValue(inputArg);
            string? outDir = parseResult.GetValue(outDirOption);
            int topV = parseResult.GetValue(topValidatorsOption);
            int samples = parseResult.GetValue(samplesOption);
            int seed = parseResult.GetValue(seedOption);

            return Task.FromResult(AuditCommandHandler.Execute(
                input, outDir, topV, samples, seed,
                parseResult.Configuration.Output,
                parseResult.Configuration.Error));
        });

        return auditCommand;
    }
}