using System.Text.Json;

namespace LocalAgenticCodingBenchmark.Core;

public sealed class ReviewService(IGitClient gitClient)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public async Task ReviewAsync(BenchmarkConfig config, string configPath, CancellationToken cancellationToken)
    {
        var configDirectory = Path.GetDirectoryName(configPath) ?? Directory.GetCurrentDirectory();
        var artifactsRoot = Path.GetFullPath(Path.Combine(configDirectory, config.Defaults.ArtifactsRoot));
        var runs = GetReviewRuns(artifactsRoot);
        var skippedRepos = new HashSet<string>(StringComparer.Ordinal);

        foreach (var run in runs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (skippedRepos.Contains(run.Record.RepoPath))
            {
                continue;
            }

            if (!gitClient.IsClean(run.Record.RepoPath))
            {
                skippedRepos.Add(run.Record.RepoPath);
                var status = gitClient.GetStatusPorcelain(run.Record.RepoPath);
                Console.Error.WriteLine($"Skipping repository '{run.Record.RepoPath}' because the working tree is dirty.");
                if (!string.IsNullOrWhiteSpace(status))
                {
                    Console.Error.WriteLine(status);
                }

                continue;
            }

            var patchPath = Path.Combine(run.Record.ArtifactDirectory, "code-diff.patch");
            var agentOutputPath = Path.Combine(run.Record.ArtifactDirectory, "agent-output.md");
            var patchContents = File.Exists(patchPath)
                ? await File.ReadAllTextAsync(patchPath, cancellationToken)
                : string.Empty;

            try
            {
                if (!string.IsNullOrWhiteSpace(patchContents))
                {
                    gitClient.EnsurePatchApplies(run.Record.RepoPath, patchPath);
                    gitClient.ApplyPatch(run.Record.RepoPath, patchPath);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Skipping run '{run.Record.RunId}' because patch could not be applied: {ex.Message}");
                gitClient.ResetHard(run.Record.RepoPath);
                continue;
            }

            try
            {
                Console.WriteLine($"[{run.Record.RunId}] {run.Record.Task} | {run.Record.Model} | {run.Record.Tool}");
                Console.WriteLine($"Repo: {run.Record.RepoPath}");
                if (string.IsNullOrWhiteSpace(patchContents))
                {
                    Console.WriteLine("Code Diff: <empty>");
                }

                Console.WriteLine("Agent Output:");
                Console.WriteLine(File.Exists(agentOutputPath)
                    ? await File.ReadAllTextAsync(agentOutputPath, cancellationToken)
                    : string.Empty);

                var rating = PromptForQualityRating();
                var updatedRecord = new StoredRunRecord
                {
                    RunId = run.Record.RunId,
                    Tool = run.Record.Tool,
                    Model = run.Record.Model,
                    Task = run.Record.Task,
                    RepoPath = run.Record.RepoPath,
                    Status = run.Record.Status,
                    ExitCode = run.Record.ExitCode,
                    TimedOut = run.Record.TimedOut,
                    ArtifactDirectory = run.Record.ArtifactDirectory,
                    StartedAtUtc = run.Record.StartedAtUtc,
                    FinishedAtUtc = run.Record.FinishedAtUtc,
                    DurationSeconds = run.Record.DurationSeconds,
                    QualityRating = rating,
                    FailureReason = run.Record.FailureReason
                };

                await PersistRecordAsync(run.Record.ArtifactDirectory, updatedRecord, cancellationToken);
            }
            finally
            {
                gitClient.ResetHard(run.Record.RepoPath);
            }
        }

        var reportService = new ReportService();
        await reportService.GenerateAsync(config, configDirectory, cancellationToken);
    }

    public static IReadOnlyList<ReviewRun> GetReviewRuns(string artifactsRoot)
    {
        return ReportService.LoadRecords(artifactsRoot)
            .Where(r => r.Status == RunStatus.Succeeded)
            .Select(r => new ReviewRun(r))
            .OrderBy(r => r.Record.Task, StringComparer.Ordinal)
            .ThenBy(r => r.Record.Model, StringComparer.Ordinal)
            .ThenBy(r => r.Record.Tool, StringComparer.Ordinal)
            .ThenBy(r => r.Record.StartedAtUtc)
            .ToArray();
    }

    private static string PromptForQualityRating()
    {
        while (true)
        {
            Console.Write("QualityRating (1-6, F=Failed): ");
            var input = Console.ReadLine()?.Trim();
            if (string.Equals(input, "f", StringComparison.OrdinalIgnoreCase))
            {
                return "Failed";
            }

            if (input is not null && input.Length == 1 && input[0] >= '1' && input[0] <= '6')
            {
                return input;
            }

            Console.WriteLine("Please enter a valid grade from 1 to 6, or F for Failed.");
        }
    }

    private static Task PersistRecordAsync(string artifactDirectory, StoredRunRecord record, CancellationToken cancellationToken)
    {
        return File.WriteAllTextAsync(
            Path.Combine(artifactDirectory, "run.json"),
            JsonSerializer.Serialize(record, JsonOptions),
            cancellationToken);
    }
}

public sealed record ReviewRun(StoredRunRecord Record);
