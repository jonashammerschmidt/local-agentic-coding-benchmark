using LocalAgenticCodingBenchmark.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LocalAgenticCodingBenchmark.TestRunner;

[TestClass]
public sealed class BenchmarkRunnerTests
{
    [TestMethod]
    public void ParsesMinimalYamlConfig()
    {
        const string yaml = """
            version: 1

            defaults:
              artifactsRoot: .benchmarks/runs
              reportsRoot: .benchmarks/reports
              timeoutSeconds: 900

            tools:
              - id: codex
                enabled: true

            models:
              - id: qwen3-coder-next:q4_K_M
                provider: ollama
                enabled: true

            tasks:
              - id: run-tests
                repoPath: /tmp/repo
                prompt: "Führe die Tests aus."
            """;

        var config = BenchmarkConfigParser.Parse(yaml.Split('\n'));

        Assert.AreEqual(1, config.Version);
        Assert.AreEqual(".benchmarks/runs", config.Defaults.ArtifactsRoot);
        Assert.AreEqual(1, config.Tools.Count);
        Assert.AreEqual("codex", config.Tools[0].Id);
        Assert.AreEqual("ollama", config.Models[0].Provider);
        Assert.AreEqual("Führe die Tests aus.", config.Tasks[0].Prompt);
    }

    [TestMethod]
    public void ExpandsHomeDirectoryInRepoPath()
    {
        const string yaml = """
            version: 1

            tools:
              - id: codex
                enabled: true

            models:
              - id: model-a
                provider: ollama
                enabled: true

            tasks:
              - id: run-tests
                repoPath: ~/repo
                prompt: "Führe die Tests aus."
            """;

        var config = BenchmarkConfigParser.Parse(yaml.Split('\n'));

        Assert.AreEqual(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "repo"),
            config.Tasks[0].RepoPath);
    }

    [TestMethod]
    public void PlansFullCartesianProduct()
    {
        var config = new BenchmarkConfig
        {
            Version = 1,
            Defaults = new DefaultsConfig(),
            Tools = [new ToolConfig { Id = "codex", Enabled = true }, new ToolConfig { Id = "claude", Enabled = true }],
            Models = [new ModelConfig { Id = "m1", Provider = "ollama", Enabled = true }, new ModelConfig { Id = "m2", Provider = "ollama", Enabled = true }],
            Tasks = [new TaskConfig { Id = "t1", RepoPath = "/tmp/repo", Prompt = "p1" }, new TaskConfig { Id = "t2", RepoPath = "/tmp/repo", Prompt = "p2" }]
        };

        var runs = BenchmarkOrchestrator.PlanRuns(config, "/tmp/runs", DateTimeOffset.Parse("2026-03-23T10:00:00Z")).ToArray();
        Assert.AreEqual(8, runs.Length);
    }

    [TestMethod]
    public void RejectsDuplicateIds()
    {
        var temp = CreateTemporaryDirectory();
        var repo = CreateGitRepo(temp);
        var config = new BenchmarkConfig
        {
            Version = 1,
            Defaults = new DefaultsConfig(),
            Tools = [new ToolConfig { Id = "codex", Enabled = true }, new ToolConfig { Id = "codex", Enabled = true }],
            Models = [new ModelConfig { Id = "m1", Provider = "ollama", Enabled = true }],
            Tasks = [new TaskConfig { Id = "t1", RepoPath = repo, Prompt = "prompt" }]
        };

        Assert.ThrowsExactly<BenchmarkConfigurationException>(() => BenchmarkConfigValidator.Validate(config));
    }

    [TestMethod]
    public async Task SkipsDirtyRepositoriesCleanly()
    {
        var temp = CreateTemporaryDirectory();
        var repo = CreateGitRepo(temp);
        await File.WriteAllTextAsync(Path.Combine(repo, "dirty.txt"), "dirty");

        var config = new BenchmarkConfig
        {
            Version = 1,
            Defaults = new DefaultsConfig
            {
                ArtifactsRoot = "runs",
                ReportsRoot = "reports",
                TimeoutSeconds = 5
            },
            Tools = [new ToolConfig { Id = "codex", Enabled = true }],
            Models = [new ModelConfig { Id = "model-a", Provider = "ollama", Enabled = true }],
            Tasks = [new TaskConfig { Id = "task-a", RepoPath = repo, Prompt = "prompt" }]
        };

        var orchestrator = new BenchmarkOrchestrator(new ToolRunnerFactory(), new GitClient(), new FakeProcessRunner(), new Clock());
        var configPath = Path.Combine(temp, "benchmark.yaml");
        await File.WriteAllTextAsync(configPath, "version: 1");

        var results = await orchestrator.RunAsync(config, configPath, CancellationToken.None);
        Assert.AreEqual(1, results.Count);
        Assert.AreEqual(RunStatus.SkippedDirtyRepo, results[0].Status);

        var runJsonPath = Path.Combine(results[0].ArtifactDirectory, "run.json");
        Assert.IsTrue(File.Exists(runJsonPath));
    }

    [TestMethod]
    public async Task WritesArtifactsForFailedRuns()
    {
        var temp = CreateTemporaryDirectory();
        var repo = CreateGitRepo(temp);
        var targetFile = Path.Combine(repo, "README.md");

        var processRunner = new FakeProcessRunner(_ =>
        {
            File.AppendAllText(targetFile, Environment.NewLine + "change");
            return new ProcessExecutionResult
            {
                ExitCode = 3,
                StandardOutput = "agent output",
                StandardError = "tool failed",
                StartedAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1),
                FinishedAtUtc = DateTimeOffset.UtcNow,
                TimedOut = false
            };
        });

        var config = new BenchmarkConfig
        {
            Version = 1,
            Defaults = new DefaultsConfig
            {
                ArtifactsRoot = "runs",
                ReportsRoot = "reports",
                TimeoutSeconds = 5
            },
            Tools = [new ToolConfig { Id = "opencode", Enabled = true }],
            Models = [new ModelConfig { Id = "model-a", Provider = "ollama", Enabled = true }],
            Tasks = [new TaskConfig { Id = "task-a", RepoPath = repo, Prompt = "prompt" }]
        };

        var orchestrator = new BenchmarkOrchestrator(new ToolRunnerFactory(), new GitClient(), processRunner, new Clock());
        var configPath = Path.Combine(temp, "benchmark.yaml");
        await File.WriteAllTextAsync(configPath, "version: 1");

        var results = await orchestrator.RunAsync(config, configPath, CancellationToken.None);
        var artifactDirectory = results[0].ArtifactDirectory;

        Assert.AreEqual(RunStatus.Failed, results[0].Status);
        Assert.IsTrue(File.Exists(Path.Combine(artifactDirectory, "agent-output.md")));
        Assert.IsTrue(File.Exists(Path.Combine(artifactDirectory, "stdout.log")));
        Assert.IsTrue(File.Exists(Path.Combine(artifactDirectory, "stderr.log")));
        Assert.IsTrue(File.Exists(Path.Combine(artifactDirectory, "code-diff.patch")));

        var diff = await File.ReadAllTextAsync(Path.Combine(artifactDirectory, "code-diff.patch"));
        StringAssert.Contains(diff, "README.md");
        Assert.IsFalse(File.ReadAllText(targetFile).Contains("change", StringComparison.Ordinal));
    }

    [TestMethod]
    public void BuildsMarkdownReport()
    {
        var markdown = ReportService.BuildMarkdown(
        [
            new StoredRunRecord
            {
                RunId = "run-1",
                Tool = "codex",
                Model = "model-a",
                Task = "task-a",
                Status = RunStatus.Succeeded,
                DurationSeconds = 1.25,
                QualityRating = string.Empty
            }
        ]);

        StringAssert.Contains(markdown, "| Run ID | Tool | Model | Task | Duration | Status | Quality Rating |");
        StringAssert.Contains(markdown, "| run-1 | codex | model-a | task-a | 1.25s | Succeeded |  |");
    }

    [TestMethod]
    public async Task SystemProcessRunnerExecutesRealProcess()
    {
        var runner = new SystemProcessRunner();
        var spec = new ProcessSpec
        {
            FileName = "git",
            Arguments = ["--version"],
            WorkingDirectory = Directory.GetCurrentDirectory()
        };

        var result = await runner.ExecuteAsync(spec, TimeSpan.FromSeconds(10), CancellationToken.None);

        Assert.AreEqual(0, result.ExitCode);
        Assert.IsFalse(result.TimedOut);
        StringAssert.Contains(result.StandardOutput, "git version");
    }

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "benchmark-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string CreateGitRepo(string root)
    {
        var repo = Path.Combine(root, "repo");
        Directory.CreateDirectory(repo);
        Run(repo, "git", "init");
        Run(repo, "git", "config", "user.email", "tests@example.com");
        Run(repo, "git", "config", "user.name", "Tests");
        File.WriteAllText(Path.Combine(repo, "README.md"), "hello");
        Run(repo, "git", "add", "README.md");
        Run(repo, "git", "commit", "-m", "init");
        return repo;
    }

    private static void Run(string workdir, string fileName, params string[] args)
    {
        var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = fileName,
                WorkingDirectory = workdir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };

        foreach (var arg in args)
        {
            process.StartInfo.ArgumentList.Add(arg);
        }

        process.Start();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(stderr);
        }
    }

    private sealed class FakeProcessRunner(Func<ProcessSpec, ProcessExecutionResult>? resultFactory = null) : IProcessRunner
    {
        public Task<ProcessExecutionResult> ExecuteAsync(ProcessSpec spec, TimeSpan timeout, CancellationToken cancellationToken)
        {
            var result = resultFactory?.Invoke(spec) ?? new ProcessExecutionResult
            {
                ExitCode = 0,
                StandardOutput = "ok",
                StandardError = string.Empty,
                StartedAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1),
                FinishedAtUtc = DateTimeOffset.UtcNow,
                TimedOut = false
            };

            return Task.FromResult(result);
        }
    }
}
