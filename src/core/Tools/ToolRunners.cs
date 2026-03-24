namespace LocalAgenticCodingBenchmark.Core;

public interface IToolRunner
{
    string ToolId { get; }
    ProcessSpec BuildWarmup(PlannedRun run, string warmupPrompt);
    ProcessSpec BuildBenchmark(PlannedRun run);
}

public sealed class ToolRunnerFactory
{
    public IToolRunner Create(string toolId) => toolId switch
    {
        "codex" => new CodexToolRunner(),
        "opencode" => new OpenCodeToolRunner(),
        "claude" => new ClaudeToolRunner(),
        _ => throw new BenchmarkConfigurationException($"Unsupported tool id '{toolId}'.")
    };
}

public sealed class CodexToolRunner : IToolRunner
{
    public string ToolId => "codex";

    public ProcessSpec BuildWarmup(PlannedRun run, string warmupPrompt) => new()
    {
        FileName = "codex",
        WorkingDirectory = run.Task.RepoPath,
        Arguments =
        [
            "exec",
            "--skip-git-repo-check",
            "--full-auto",
            "--model", run.Model.Id,
            "--cd", run.Task.RepoPath,
            warmupPrompt
        ]
    };

    public ProcessSpec BuildBenchmark(PlannedRun run) => new()
    {
        FileName = "codex",
        WorkingDirectory = run.Task.RepoPath,
        AgentOutputFilePath = Path.Combine(run.ArtifactDirectory, "agent-output.md"),
        Arguments =
        [
            "exec",
            "--skip-git-repo-check",
            "--full-auto",
            "--model", run.Model.Id,
            "--cd", run.Task.RepoPath,
            "--output-last-message", Path.Combine(run.ArtifactDirectory, "agent-output.md"),
            run.Task.Prompt
        ]
    };
}

public sealed class OpenCodeToolRunner : IToolRunner
{
    public string ToolId => "opencode";

    public ProcessSpec BuildWarmup(PlannedRun run, string warmupPrompt) => new()
    {
        FileName = "opencode",
        WorkingDirectory = run.Task.RepoPath,
        Arguments =
        [
            "run",
            "--model", run.Model.Id,
            warmupPrompt
        ]
    };

    public ProcessSpec BuildBenchmark(PlannedRun run) => new()
    {
        FileName = "opencode",
        WorkingDirectory = run.Task.RepoPath,
        Arguments =
        [
            "run",
            "--model", run.Model.Id,
            run.Task.Prompt
        ]
    };
}

public sealed class ClaudeToolRunner : IToolRunner
{
    public string ToolId => "claude";

    public ProcessSpec BuildWarmup(PlannedRun run, string warmupPrompt) => new()
    {
        FileName = "claude",
        WorkingDirectory = run.Task.RepoPath,
        Arguments =
        [
            "-p",
            warmupPrompt,
            "--model", run.Model.Id,
            "--cwd", run.Task.RepoPath
        ]
    };

    public ProcessSpec BuildBenchmark(PlannedRun run) => new()
    {
        FileName = "claude",
        WorkingDirectory = run.Task.RepoPath,
        Arguments =
        [
            "-p",
            run.Task.Prompt,
            "--model", run.Model.Id,
            "--cwd", run.Task.RepoPath
        ]
    };
}
