using System.Text;
using System.Text.Json;

namespace LocalAgenticCodingBenchmark.Core;

public sealed class BenchmarkOrchestrator(
    ToolRunnerFactory runnerFactory,
    GitClient gitClient,
    IProcessRunner processRunner,
    Clock clock)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public async Task<IReadOnlyList<RunRecord>> RunAsync(BenchmarkConfig config, string configPath, CancellationToken cancellationToken)
    {
        var configDirectory = Path.GetDirectoryName(configPath) ?? Directory.GetCurrentDirectory();
        var artifactsRoot = Path.GetFullPath(Path.Combine(configDirectory, config.Defaults.ArtifactsRoot));
        Directory.CreateDirectory(artifactsRoot);

        var runs = PlanRuns(config, artifactsRoot, clock.UtcNow()).ToArray();
        var results = new List<RunRecord>(runs.Length);

        foreach (var run in runs)
        {
            Console.WriteLine($"[{run.RunId}] {run.Tool.Id} | {run.Model.Id} | {run.Task.Id}");
            Directory.CreateDirectory(run.ArtifactDirectory);

            RunRecord record;
            if (!gitClient.IsClean(run.Task.RepoPath))
            {
                var statusSummary = gitClient.GetStatusPorcelain(run.Task.RepoPath);
                record = new RunRecord
                {
                    RunId = run.RunId,
                    Tool = run.Tool.Id,
                    Model = run.Model.Id,
                    Task = run.Task.Id,
                    RepoPath = run.Task.RepoPath,
                    Status = RunStatus.SkippedDirtyRepo,
                    ExitCode = -1,
                    TimedOut = false,
                    ArtifactDirectory = run.ArtifactDirectory,
                    StartedAtUtc = clock.UtcNow(),
                    FinishedAtUtc = clock.UtcNow(),
                    DurationSeconds = 0,
                    FailureReason = string.IsNullOrWhiteSpace(statusSummary)
                        ? "Repository working tree is not clean."
                        : $"Repository working tree is not clean:{Environment.NewLine}{statusSummary}",
                    QualityRating = string.Empty
                };

                await WriteArtifactsAsync(record, string.Empty, string.Empty, string.Empty, cancellationToken);
                results.Add(record);
                Console.Error.WriteLine($"  skipped: repository '{run.Task.RepoPath}' is dirty");
                if (!string.IsNullOrWhiteSpace(statusSummary))
                {
                    Console.Error.WriteLine(statusSummary);
                }
                continue;
            }

            record = await ExecuteRunAsync(run, cancellationToken);
            results.Add(record);
        }

        var reportService = new ReportService();
        await reportService.GenerateAsync(config, configDirectory, cancellationToken);
        return results;
    }

    public static IEnumerable<PlannedRun> PlanRuns(BenchmarkConfig config, string artifactsRoot, DateTimeOffset nowUtc)
    {
        var enabledTools = config.Tools.Where(t => t.Enabled).ToArray();
        var enabledModels = config.Models.Where(m => m.Enabled).ToArray();
        var tasks = config.Tasks;

        foreach (var tool in enabledTools)
        {
            foreach (var model in enabledModels)
            {
                foreach (var task in tasks)
                {
                    var runId = BuildRunId(tool.Id, model.Id, task.Id, nowUtc);
                    var directoryName = $"{tool.Id}-{SanitizeForPath(model.Id)}-{runId}";
                    yield return new PlannedRun
                    {
                        RunId = runId,
                        Tool = tool,
                        Model = model,
                        Task = task,
                        ArtifactDirectory = Path.Combine(artifactsRoot, directoryName),
                        TimeoutSeconds = config.Defaults.TimeoutSeconds
                    };
                }
            }
        }
    }

    private async Task<RunRecord> ExecuteRunAsync(PlannedRun run, CancellationToken cancellationToken)
    {
        var runner = runnerFactory.Create(run.Tool.Id);
        var processSpec = runner.Build(run);
        var processResult = await processRunner.ExecuteAsync(processSpec, TimeSpan.FromSeconds(run.TimeoutSeconds), cancellationToken);
        var diff = gitClient.GetDiff(run.Task.RepoPath);

        string agentOutput;
        if (!string.IsNullOrWhiteSpace(processSpec.AgentOutputFilePath) && File.Exists(processSpec.AgentOutputFilePath))
        {
            agentOutput = await File.ReadAllTextAsync(processSpec.AgentOutputFilePath, cancellationToken);
        }
        else
        {
            agentOutput = processResult.StandardOutput;
        }

        try
        {
            gitClient.ResetHard(run.Task.RepoPath);
        }
        catch (Exception ex)
        {
            processResult = new ProcessExecutionResult
            {
                ExitCode = processResult.ExitCode,
                StandardOutput = processResult.StandardOutput,
                StandardError = string.Join(Environment.NewLine, processResult.StandardError, $"Failed to reset repository: {ex.Message}"),
                StartedAtUtc = processResult.StartedAtUtc,
                FinishedAtUtc = processResult.FinishedAtUtc,
                TimedOut = processResult.TimedOut
            };
        }

        var status = processResult.ExitCode == 0 && !processResult.TimedOut ? RunStatus.Succeeded : RunStatus.Failed;
        var record = new RunRecord
        {
            RunId = run.RunId,
            Tool = run.Tool.Id,
            Model = run.Model.Id,
            Task = run.Task.Id,
            RepoPath = run.Task.RepoPath,
            Status = status,
            ExitCode = processResult.ExitCode,
            TimedOut = processResult.TimedOut,
            ArtifactDirectory = run.ArtifactDirectory,
            StartedAtUtc = processResult.StartedAtUtc,
            FinishedAtUtc = processResult.FinishedAtUtc,
            DurationSeconds = Math.Round(processResult.Duration.TotalSeconds, 3),
            FailureReason = status == RunStatus.Failed
                ? BuildFailureReason(processResult)
                : null,
            QualityRating = string.Empty
        };

        await WriteArtifactsAsync(record, agentOutput, processResult.StandardOutput, processResult.StandardError, cancellationToken, diff);
        return record;
    }

    private async Task WriteArtifactsAsync(
        RunRecord record,
        string agentOutput,
        string stdout,
        string stderr,
        CancellationToken cancellationToken,
        string diff = "")
    {
        Directory.CreateDirectory(record.ArtifactDirectory);

        await File.WriteAllTextAsync(Path.Combine(record.ArtifactDirectory, "agent-output.md"), agentOutput, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(record.ArtifactDirectory, "stdout.log"), stdout, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(record.ArtifactDirectory, "stderr.log"), stderr, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(record.ArtifactDirectory, "code-diff.patch"), diff, cancellationToken);

        var stored = new StoredRunRecord
        {
            RunId = record.RunId,
            Tool = record.Tool,
            Model = record.Model,
            Task = record.Task,
            RepoPath = record.RepoPath,
            Status = record.Status,
            ExitCode = record.ExitCode,
            TimedOut = record.TimedOut,
            ArtifactDirectory = record.ArtifactDirectory,
            StartedAtUtc = record.StartedAtUtc,
            FinishedAtUtc = record.FinishedAtUtc,
            DurationSeconds = record.DurationSeconds,
            QualityRating = record.QualityRating,
            FailureReason = record.FailureReason
        };

        await File.WriteAllTextAsync(
            Path.Combine(record.ArtifactDirectory, "run.json"),
            JsonSerializer.Serialize(stored, JsonOptions),
            cancellationToken);
    }

    private static string BuildFailureReason(ProcessExecutionResult processResult)
    {
        if (processResult.TimedOut)
        {
            return "Process timed out.";
        }

        if (!string.IsNullOrWhiteSpace(processResult.StandardError))
        {
            return processResult.StandardError.Trim();
        }

        return $"Process exited with code {processResult.ExitCode}.";
    }

    private static string BuildRunId(string toolId, string modelId, string taskId, DateTimeOffset nowUtc)
    {
        var hashInput = $"{toolId}|{modelId}|{taskId}|{Guid.NewGuid():N}";
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(hashInput)))[..8].ToLowerInvariant();
        return $"{nowUtc:yyyyMMddHHmmss}-{hash}";
    }

    public static string SanitizeForPath(string value)
    {
        var chars = value
            .Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-')
            .ToArray();
        return new string(chars);
    }
}
