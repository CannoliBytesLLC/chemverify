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

        var config = new CommandLineConfiguration(root);
        if (output is not null) config.Output = output;
        if (error is not null) config.Error = error;
        return config;
    }
}