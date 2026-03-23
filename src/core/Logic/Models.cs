using System.Text.Json.Serialization;

namespace LocalAgenticCodingBenchmark.Core;

public sealed class BenchmarkConfig
{
    public int Version { get; init; }
    public DefaultsConfig Defaults { get; init; } = new();
    public List<ToolConfig> Tools { get; init; } = [];
    public List<ModelConfig> Models { get; init; } = [];
    public List<TaskConfig> Tasks { get; init; } = [];
}

public sealed class DefaultsConfig
{
    public string ArtifactsRoot { get; init; } = ".benchmarks/runs";
    public string ReportsRoot { get; init; } = ".benchmarks/reports";
    public int TimeoutSeconds { get; init; } = 900;
}

public sealed class ToolConfig
{
    public string Id { get; init; } = string.Empty;
    public bool Enabled { get; init; }
}

public sealed class ModelConfig
{
    public string Id { get; init; } = string.Empty;
    public string Provider { get; init; } = string.Empty;
    public bool Enabled { get; init; }
}

public sealed class TaskConfig
{
    public string Id { get; init; } = string.Empty;
    public string RepoPath { get; init; } = string.Empty;
    public string Prompt { get; init; } = string.Empty;
}

public sealed class PlannedRun
{
    public string RunId { get; init; } = string.Empty;
    public ToolConfig Tool { get; init; } = new();
    public ModelConfig Model { get; init; } = new();
    public TaskConfig Task { get; init; } = new();
    public string ArtifactDirectory { get; init; } = string.Empty;
    public int TimeoutSeconds { get; init; }
}

public sealed class ProcessSpec
{
    public string FileName { get; init; } = string.Empty;
    public IReadOnlyList<string> Arguments { get; init; } = [];
    public string WorkingDirectory { get; init; } = string.Empty;
    public string? AgentOutputFilePath { get; init; }
}

public sealed class ProcessExecutionResult
{
    public required int ExitCode { get; init; }
    public required string StandardOutput { get; init; }
    public required string StandardError { get; init; }
    public required DateTimeOffset StartedAtUtc { get; init; }
    public required DateTimeOffset FinishedAtUtc { get; init; }
    public bool TimedOut { get; init; }
    public TimeSpan Duration => FinishedAtUtc - StartedAtUtc;
}

public sealed class ToolRunResult
{
    public required ProcessExecutionResult Process { get; init; }
    public string? AgentOutputOverride { get; init; }
}

public enum RunStatus
{
    Succeeded,
    Failed,
    SkippedDirtyRepo
}

public sealed class RunRecord
{
    public string RunId { get; init; } = string.Empty;
    public string Tool { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public string Task { get; init; } = string.Empty;
    public string RepoPath { get; init; } = string.Empty;
    public RunStatus Status { get; init; }
    public int ExitCode { get; init; }
    public bool TimedOut { get; init; }
    public string ArtifactDirectory { get; init; } = string.Empty;
    public DateTimeOffset StartedAtUtc { get; init; }
    public DateTimeOffset FinishedAtUtc { get; init; }
    public double DurationSeconds { get; init; }
    public string QualityRating { get; init; } = string.Empty;
    public string? FailureReason { get; init; }
}

public sealed class StoredRunRecord
{
    public string RunId { get; init; } = string.Empty;
    public string Tool { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public string Task { get; init; } = string.Empty;
    public string RepoPath { get; init; } = string.Empty;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public RunStatus Status { get; init; }

    public int ExitCode { get; init; }
    public bool TimedOut { get; init; }
    public string ArtifactDirectory { get; init; } = string.Empty;
    public DateTimeOffset StartedAtUtc { get; init; }
    public DateTimeOffset FinishedAtUtc { get; init; }
    public double DurationSeconds { get; init; }
    public string QualityRating { get; init; } = string.Empty;
    public string? FailureReason { get; init; }
}
