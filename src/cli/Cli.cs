using LocalAgenticCodingBenchmark.Core;

namespace LocalAgenticCodingBenchmark.Cli;

public static class Cli
{
    public static async Task<int> RunAsync(string[] args)
    {
        var command = args.FirstOrDefault()?.ToLowerInvariant() ?? "run";
        var remaining = command is "run" or "report"
            ? args.Skip(1).ToArray()
            : args;

        var configPath = remaining.FirstOrDefault() ?? "benchmark.yaml";

        try
        {
            return command switch
            {
                "run" => await RunBenchmarkAsync(configPath),
                "report" => await GenerateReportAsync(configPath),
                "help" or "--help" or "-h" => PrintHelp(),
                _ => PrintUnknownCommand(command)
            };
        }
        catch (BenchmarkConfigurationException ex)
        {
            Console.Error.WriteLine($"Configuration error: {ex.Message}");
            return 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static async Task<int> RunBenchmarkAsync(string configPath)
    {
        var config = BenchmarkConfigParser.Load(configPath);
        BenchmarkConfigValidator.Validate(config);

        var runnerFactory = new ToolRunnerFactory();
        var orchestrator = new BenchmarkOrchestrator(
            runnerFactory,
            new GitClient(),
            new SystemProcessRunner(),
            new Clock());

        var results = await orchestrator.RunAsync(config, Path.GetFullPath(configPath), CancellationToken.None);
        var summary = results.GroupBy(r => r.Status).OrderBy(g => g.Key).Select(g => $"{g.Key}: {g.Count()}");
        Console.WriteLine($"Completed {results.Count} run(s).");
        Console.WriteLine(string.Join(", ", summary));

        foreach (var result in results.Where(r => r.Status != RunStatus.Succeeded))
        {
            Console.Error.WriteLine(
                $"{result.Status}: tool={result.Tool}, model={result.Model}, task={result.Task}, artifacts={result.ArtifactDirectory}");

            if (!string.IsNullOrWhiteSpace(result.FailureReason))
            {
                Console.Error.WriteLine(result.FailureReason);
            }
        }

        if (results.Any(r => r.Status == RunStatus.Failed))
        {
            return 1;
        }

        if (results.Any(r => r.Status == RunStatus.SkippedDirtyRepo))
        {
            return 2;
        }

        return 0;
    }

    private static async Task<int> GenerateReportAsync(string configPath)
    {
        var config = BenchmarkConfigParser.Load(configPath);
        BenchmarkConfigValidator.Validate(config);

        var configDirectory = Path.GetDirectoryName(Path.GetFullPath(configPath)) ?? Directory.GetCurrentDirectory();
        var reportService = new ReportService();
        var reportPath = await reportService.GenerateAsync(config, configDirectory, CancellationToken.None);
        Console.WriteLine($"Report written to {reportPath}");
        return 0;
    }

    private static int PrintHelp()
    {
        var help = """
            Usage:
              benchmark-orchestrator run [benchmark.yaml]
              benchmark-orchestrator report [benchmark.yaml]

            Commands:
              run     Execute all enabled tool/model/task combinations.
              report  Rebuild the aggregated Markdown report from run artifacts.
            """;

        Console.WriteLine(help);
        return 0;
    }

    private static int PrintUnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command '{command}'. Use 'help' for usage.");
        return 2;
    }
}
