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
                enabled: true
            """;

        var config = BenchmarkConfigParser.Parse(yaml.Split('\n'));

        Assert.AreEqual(1, config.Version);
        Assert.AreEqual(".benchmarks/runs", config.Defaults.ArtifactsRoot);
        Assert.AreEqual("Say Hello World!", config.Defaults.WarmupPrompt);
        Assert.AreEqual(1, config.Tools.Count);
        Assert.AreEqual("codex", config.Tools[0].Id);
        Assert.AreEqual("ollama", config.Models[0].Provider);
        Assert.AreEqual("Führe die Tests aus.", config.Tasks[0].Prompt);
        Assert.IsTrue(config.Tasks[0].Enabled);
    }

    [TestMethod]
    public void ParsesConfiguredWarmupPrompt()
    {
        const string yaml = """
            version: 1

            defaults:
              warmupPrompt: "Sag genau Hello World!"

            tools:
              - id: codex
                enabled: true

            models:
              - id: model-a
                provider: ollama
                enabled: true

            tasks:
              - id: run-tests
                repoPath: /tmp/repo
                prompt: "Führe die Tests aus."
                enabled: true
            """;

        var config = BenchmarkConfigParser.Parse(yaml.Split('\n'));

        Assert.AreEqual("Sag genau Hello World!", config.Defaults.WarmupPrompt);
    }

    [TestMethod]
    public void DefaultsSkipPermissionsPolicyToAbort()
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
                repoPath: /tmp/repo
                prompt: "Führe die Tests aus."
                enabled: true
            """;

        var config = BenchmarkConfigParser.Parse(yaml.Split('\n'));

        Assert.AreEqual(PermissionRequestPolicy.Abort, config.Defaults.SkipPermissions);
    }

    [TestMethod]
    public void ParsesConfiguredSkipPermissionsPolicy()
    {
        const string yaml = """
            version: 1

            defaults:
              skipPermissions: abort

            tools:
              - id: codex
                enabled: true

            models:
              - id: model-a
                provider: ollama
                enabled: true

            tasks:
              - id: run-tests
                repoPath: /tmp/repo
                prompt: "Führe die Tests aus."
                enabled: true
            """;

        var config = BenchmarkConfigParser.Parse(yaml.Split('\n'));

        Assert.AreEqual(PermissionRequestPolicy.Abort, config.Defaults.SkipPermissions);
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
                enabled: true
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
            Tasks = [new TaskConfig { Id = "t1", RepoPath = "/tmp/repo", Prompt = "p1", Enabled = true }, new TaskConfig { Id = "t2", RepoPath = "/tmp/repo", Prompt = "p2", Enabled = true }]
        };

        var runs = BenchmarkOrchestrator.PlanRuns(config, "/tmp/runs", DateTimeOffset.Parse("2026-03-23T10:00:00Z")).ToArray();
        Assert.AreEqual(8, runs.Length);
    }

    [TestMethod]
    public void PlanRunsSkipsDisabledTasks()
    {
        var config = new BenchmarkConfig
        {
            Version = 1,
            Defaults = new DefaultsConfig(),
            Tools = [new ToolConfig { Id = "codex", Enabled = true }],
            Models = [new ModelConfig { Id = "m1", Provider = "ollama", Enabled = true }],
            Tasks =
            [
                new TaskConfig { Id = "t1", RepoPath = "/tmp/repo", Prompt = "p1", Enabled = true },
                new TaskConfig { Id = "t2", RepoPath = "/tmp/repo", Prompt = "p2", Enabled = false }
            ]
        };

        var runs = BenchmarkOrchestrator.PlanRuns(config, "/tmp/runs", DateTimeOffset.Parse("2026-03-23T10:00:00Z")).ToArray();

        Assert.AreEqual(1, runs.Length);
        Assert.AreEqual("t1", runs[0].Task.Id);
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
            Tasks = [new TaskConfig { Id = "t1", RepoPath = repo, Prompt = "prompt", Enabled = true }]
        };

        Assert.ThrowsExactly<BenchmarkConfigurationException>(() => BenchmarkConfigValidator.Validate(config));
    }

    [TestMethod]
    public void RejectsPromptPermissionPolicyUntilImplemented()
    {
        var temp = CreateTemporaryDirectory();
        var repo = CreateGitRepo(temp);
        var config = new BenchmarkConfig
        {
            Version = 1,
            Defaults = new DefaultsConfig { SkipPermissions = PermissionRequestPolicy.Prompt },
            Tools = [new ToolConfig { Id = "claude", Enabled = true }],
            Models = [new ModelConfig { Id = "m1", Provider = "ollama", Enabled = true }],
            Tasks = [new TaskConfig { Id = "t1", RepoPath = repo, Prompt = "prompt", Enabled = true }]
        };

        var exception = Assert.ThrowsExactly<BenchmarkConfigurationException>(() => BenchmarkConfigValidator.Validate(config));
        StringAssert.Contains(exception.Message, "prompt");
    }

    [TestMethod]
    public void ClaudeRunnerBuildsBenchmarkForOllamaUsingAnthropicCompatibilityEnv()
    {
        var run = CreatePlannedRun("claude");
        var runner = new ClaudeToolRunner();

        var spec = runner.BuildBenchmark(run);

        Assert.AreEqual("ollama", run.Model.Provider);
        Assert.AreEqual("ollama", spec.EnvironmentVariables["ANTHROPIC_AUTH_TOKEN"]);
        Assert.AreEqual(string.Empty, spec.EnvironmentVariables["ANTHROPIC_API_KEY"]);
        Assert.AreEqual("http://localhost:11434", spec.EnvironmentVariables["ANTHROPIC_BASE_URL"]);
    }

    [TestMethod]
    public void ClaudeRunnerRejectsUnsupportedProvider()
    {
        var baseRun = CreatePlannedRun("claude");
        var run = new PlannedRun
        {
            RunId = baseRun.RunId,
            ArtifactDirectory = baseRun.ArtifactDirectory,
            TimeoutSeconds = baseRun.TimeoutSeconds,
            WarmupPrompt = baseRun.WarmupPrompt,
            SkipPermissions = baseRun.SkipPermissions,
            Tool = baseRun.Tool,
            Model = new ModelConfig { Id = "model-a", Provider = "unknown", Enabled = true },
            Task = baseRun.Task
        };
        var runner = new ClaudeToolRunner();

        var exception = Assert.ThrowsExactly<BenchmarkConfigurationException>(() => runner.BuildBenchmark(run));
        StringAssert.Contains(exception.Message, "Unsupported provider 'unknown' for tool 'claude'");
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
            Tasks = [new TaskConfig { Id = "task-a", RepoPath = repo, Prompt = "prompt", Enabled = true }]
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
            Tasks = [new TaskConfig { Id = "task-a", RepoPath = repo, Prompt = "prompt", Enabled = true }]
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
    public async Task WritesArtifactsForNewFilesInCodeDiffPatch()
    {
        var temp = CreateTemporaryDirectory();
        var repo = CreateGitRepo(temp);
        var addedFile = Path.Combine(repo, "new-file.txt");

        var processRunner = new FakeProcessRunner(_ =>
        {
            File.WriteAllText(addedFile, "new content");
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
            Tasks = [new TaskConfig { Id = "task-a", RepoPath = repo, Prompt = "prompt", Enabled = true }]
        };

        var orchestrator = new BenchmarkOrchestrator(new ToolRunnerFactory(), new GitClient(), processRunner, new Clock());
        var configPath = Path.Combine(temp, "benchmark.yaml");
        await File.WriteAllTextAsync(configPath, "version: 1");

        var results = await orchestrator.RunAsync(config, configPath, CancellationToken.None);
        var artifactDirectory = results[0].ArtifactDirectory;

        var diff = await File.ReadAllTextAsync(Path.Combine(artifactDirectory, "code-diff.patch"));
        StringAssert.Contains(diff, "new-file.txt");
        StringAssert.Contains(diff, "new content");
        Assert.IsFalse(File.Exists(addedFile));
    }

    [TestMethod]
    public async Task RunsWarmupOutsideMeasuredDuration()
    {
        var temp = CreateTemporaryDirectory();
        var repo = CreateGitRepo(temp);
        var startedAt = DateTimeOffset.Parse("2026-03-24T10:00:00Z");
        var finishedAt = startedAt.AddSeconds(2);
        var specs = new List<ProcessSpec>();

        var processRunner = new FakeProcessRunner(spec =>
        {
            specs.Add(spec);

            if (specs.Count == 1)
            {
                return new ProcessExecutionResult
                {
                    ExitCode = 0,
                    StandardOutput = "Hello World!",
                    StandardError = string.Empty,
                    StartedAtUtc = startedAt.AddSeconds(-5),
                    FinishedAtUtc = startedAt.AddSeconds(-4),
                    TimedOut = false
                };
            }

            return new ProcessExecutionResult
            {
                ExitCode = 0,
                StandardOutput = "timed output",
                StandardError = string.Empty,
                StartedAtUtc = startedAt,
                FinishedAtUtc = finishedAt,
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
                TimeoutSeconds = 5,
                WarmupPrompt = "Sag Hello World!"
            },
            Tools = [new ToolConfig { Id = "codex", Enabled = true }],
            Models = [new ModelConfig { Id = "model-a", Provider = "ollama", Enabled = true }],
            Tasks = [new TaskConfig { Id = "task-a", RepoPath = repo, Prompt = "prompt", Enabled = true }]
        };

        var orchestrator = new BenchmarkOrchestrator(new ToolRunnerFactory(), new GitClient(), processRunner, new Clock());
        var configPath = Path.Combine(temp, "benchmark.yaml");
        await File.WriteAllTextAsync(configPath, "version: 1");

        var results = await orchestrator.RunAsync(config, configPath, CancellationToken.None);

        Assert.AreEqual(2, specs.Count);
        CollectionAssert.Contains(specs[0].Arguments.ToArray(), "Sag Hello World!");
        CollectionAssert.Contains(specs[1].Arguments.ToArray(), "prompt");
        Assert.AreEqual(2d, results[0].DurationSeconds);
        Assert.AreEqual(startedAt, results[0].StartedAtUtc);
        Assert.AreEqual(finishedAt, results[0].FinishedAtUtc);
        Assert.AreEqual("Hello World!", await File.ReadAllTextAsync(Path.Combine(results[0].ArtifactDirectory, "warmup-stdout.log")));
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

        StringAssert.Contains(markdown, "| Run ID | Tool  | Model   | Task   | Duration | Status    | Quality Rating |");
        StringAssert.Contains(markdown, "| run-1  | codex | model-a | task-a | 1.25s    | Succeeded |                |");
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

    [TestMethod]
    public void OpenCodeRunnerBuildsBenchmarkLikeCodexIntegration()
    {
        var run = CreatePlannedRun("opencode");
        var runner = new OpenCodeToolRunner();

        var spec = runner.BuildBenchmark(run);

        Assert.AreEqual("opencode", spec.FileName);
        Assert.AreEqual(run.Task.RepoPath, spec.WorkingDirectory);
        Assert.AreEqual(Path.Combine(run.ArtifactDirectory, "agent-output.md"), spec.AgentOutputFilePath);
        CollectionAssert.AreEqual(
            new[] { "run", "--model", $"{run.Model.Provider}/{run.Model.Id}", run.Task.Prompt },
            spec.Arguments.ToArray());
        StringAssert.Contains(spec.EnvironmentVariables["OPENCODE_CONFIG_CONTENT"], run.Model.Id);
    }

    [TestMethod]
    public void OpenCodeRunnerBuildsWarmupWithProviderQualifiedModel()
    {
        var run = CreatePlannedRun("opencode");
        var runner = new OpenCodeToolRunner();

        var spec = runner.BuildWarmup(run, "Hello World!");

        Assert.AreEqual("opencode", spec.FileName);
        Assert.AreEqual(run.Task.RepoPath, spec.WorkingDirectory);
        CollectionAssert.AreEqual(
            new[] { "run", "--model", $"{run.Model.Provider}/{run.Model.Id}", "Hello World!" },
            spec.Arguments.ToArray());
        StringAssert.Contains(spec.EnvironmentVariables["OPENCODE_CONFIG_CONTENT"], run.Model.Id);
    }

    [TestMethod]
    public void ClaudeRunnerBuildsBenchmarkLikeCodexIntegration()
    {
        var run = CreatePlannedRun("claude");
        var runner = new ClaudeToolRunner();

        var spec = runner.BuildBenchmark(run);

        Assert.AreEqual("claude", spec.FileName);
        Assert.AreEqual(run.Task.RepoPath, spec.WorkingDirectory);
        Assert.AreEqual(Path.Combine(run.ArtifactDirectory, "agent-output.md"), spec.AgentOutputFilePath);
        CollectionAssert.AreEqual(
            new[] { "-p", run.Task.Prompt, "--model", run.Model.Id, "--output-format", "text", "--dangerously-skip-permissions" },
            spec.Arguments.ToArray());
        Assert.AreEqual("ollama", spec.EnvironmentVariables["ANTHROPIC_AUTH_TOKEN"]);
        Assert.AreEqual("http://localhost:11434", spec.EnvironmentVariables["ANTHROPIC_BASE_URL"]);
    }

    [TestMethod]
    public void ClaudeRunnerOmitsSkipPermissionsFlagWhenConfiguredToAbort()
    {
        var run = CreatePlannedRun("claude", PermissionRequestPolicy.Abort);
        var runner = new ClaudeToolRunner();

        var spec = runner.BuildBenchmark(run);

        CollectionAssert.AreEqual(
            new[] { "-p", run.Task.Prompt, "--model", run.Model.Id, "--output-format", "text" },
            spec.Arguments.ToArray());
    }

    [TestMethod]
    public async Task SystemProcessRunnerPersistsStdoutAsAgentOutputWhenRequested()
    {
        var temp = CreateTemporaryDirectory();
        var outputPath = Path.Combine(temp, "agent-output.md");
        var runner = new SystemProcessRunner();
        var spec = new ProcessSpec
        {
            FileName = "git",
            Arguments = ["--version"],
            WorkingDirectory = Directory.GetCurrentDirectory(),
            AgentOutputFilePath = outputPath
        };

        var result = await runner.ExecuteAsync(spec, TimeSpan.FromSeconds(10), CancellationToken.None);

        Assert.AreEqual(0, result.ExitCode);
        Assert.IsTrue(File.Exists(outputPath));
        StringAssert.Contains(await File.ReadAllTextAsync(outputPath), "git version");
    }

    [TestMethod]
    public void ReviewServiceOrdersRunsByTaskThenModelThenTool()
    {
        var temp = CreateTemporaryDirectory();
        var artifactsRoot = Path.Combine(temp, "runs");
        Directory.CreateDirectory(artifactsRoot);

        WriteStoredRun(
            Path.Combine(artifactsRoot, "run-1"),
            new StoredRunRecord
            {
                RunId = "run-1",
                Tool = "opencode",
                Model = "m2",
                Task = "t1",
                RepoPath = "/tmp/repo",
                Status = RunStatus.Succeeded,
                StartedAtUtc = DateTimeOffset.Parse("2026-03-24T10:00:02Z"),
                ArtifactDirectory = Path.Combine(artifactsRoot, "run-1")
            });
        WriteStoredRun(
            Path.Combine(artifactsRoot, "run-2"),
            new StoredRunRecord
            {
                RunId = "run-2",
                Tool = "codex",
                Model = "m1",
                Task = "t2",
                RepoPath = "/tmp/repo",
                Status = RunStatus.Succeeded,
                StartedAtUtc = DateTimeOffset.Parse("2026-03-24T10:00:01Z"),
                ArtifactDirectory = Path.Combine(artifactsRoot, "run-2")
            });
        WriteStoredRun(
            Path.Combine(artifactsRoot, "run-3"),
            new StoredRunRecord
            {
                RunId = "run-3",
                Tool = "codex",
                Model = "m1",
                Task = "t1",
                RepoPath = "/tmp/repo",
                Status = RunStatus.Succeeded,
                StartedAtUtc = DateTimeOffset.Parse("2026-03-24T10:00:00Z"),
                ArtifactDirectory = Path.Combine(artifactsRoot, "run-3")
            });
        WriteStoredRun(
            Path.Combine(artifactsRoot, "run-4"),
            new StoredRunRecord
            {
                RunId = "run-4",
                Tool = "codex",
                Model = "m1",
                Task = "t1",
                RepoPath = "/tmp/repo",
                Status = RunStatus.Failed,
                StartedAtUtc = DateTimeOffset.Parse("2026-03-24T09:00:00Z"),
                ArtifactDirectory = Path.Combine(artifactsRoot, "run-4")
            });

        var runs = ReviewService.GetReviewRuns(artifactsRoot);

        CollectionAssert.AreEqual(new[] { "run-3", "run-1", "run-2" }, runs.Select(r => r.Record.RunId).ToArray());
    }

    [TestMethod]
    public async Task ReviewServicePersistsRatingRegeneratesReportAndResetsRepo()
    {
        var temp = CreateTemporaryDirectory();
        var repo = CreateGitRepo(temp);
        var artifactsRoot = Path.Combine(temp, "runs");
        var reportsRoot = Path.Combine(temp, "reports");
        var artifactDirectory = Path.Combine(artifactsRoot, "run-1");
        Directory.CreateDirectory(artifactDirectory);

        await File.WriteAllTextAsync(Path.Combine(repo, "README.md"), "hello" + Environment.NewLine + "reviewed");
        var gitClient = new GitClient();
        var diff = gitClient.GetDiff(repo);
        gitClient.ResetHard(repo);
        await File.WriteAllTextAsync(Path.Combine(artifactDirectory, "code-diff.patch"), diff);
        await File.WriteAllTextAsync(Path.Combine(artifactDirectory, "agent-output.md"), "agent output");
        WriteStoredRun(
            artifactDirectory,
            new StoredRunRecord
            {
                RunId = "run-1",
                Tool = "codex",
                Model = "model-a",
                Task = "task-a",
                RepoPath = repo,
                Status = RunStatus.Succeeded,
                ExitCode = 0,
                TimedOut = false,
                ArtifactDirectory = artifactDirectory,
                StartedAtUtc = DateTimeOffset.Parse("2026-03-24T10:00:00Z"),
                FinishedAtUtc = DateTimeOffset.Parse("2026-03-24T10:00:03Z"),
                DurationSeconds = 3.0,
                QualityRating = string.Empty
            });

        var config = new BenchmarkConfig
        {
            Version = 1,
            Defaults = new DefaultsConfig { ArtifactsRoot = "runs", ReportsRoot = "reports" },
            Tools = [new ToolConfig { Id = "codex", Enabled = true }],
            Models = [new ModelConfig { Id = "model-a", Provider = "ollama", Enabled = true }],
            Tasks = [new TaskConfig { Id = "task-a", RepoPath = repo, Prompt = "prompt", Enabled = true }]
        };

        var originalIn = Console.In;
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var input = new StringReader("7\n2\n");
        using var output = new StringWriter();
        using var error = new StringWriter();
        Console.SetIn(input);
        Console.SetOut(output);
        Console.SetError(error);

        try
        {
            var service = new ReviewService(gitClient);
            await service.ReviewAsync(config, Path.Combine(temp, "benchmark.yaml"), CancellationToken.None);
        }
        finally
        {
            Console.SetIn(originalIn);
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }

        var stored = ReportService.LoadRecords(artifactsRoot).Single();
        Assert.AreEqual("2", stored.QualityRating);
        Assert.IsTrue(File.Exists(Path.Combine(reportsRoot, "results.md")));
        StringAssert.Contains(await File.ReadAllTextAsync(Path.Combine(reportsRoot, "results.md")), "2");
        StringAssert.Contains(output.ToString(), "agent output");
        StringAssert.Contains(output.ToString(), "Please enter a valid grade from 1 to 6, or F for Failed.");
        Assert.AreEqual("hello", File.ReadAllText(Path.Combine(repo, "README.md")));
        Assert.IsTrue(new GitClient().IsClean(repo));
    }

    [TestMethod]
    public async Task ReviewServiceSkipsEntireTaskWhenRepositoryIsDirty()
    {
        var temp = CreateTemporaryDirectory();
        var repo = CreateGitRepo(temp);
        await File.WriteAllTextAsync(Path.Combine(repo, "dirty.txt"), "dirty");
        var artifactsRoot = Path.Combine(temp, "runs");
        Directory.CreateDirectory(artifactsRoot);

        WriteStoredRun(
            Path.Combine(artifactsRoot, "run-1"),
            new StoredRunRecord
            {
                RunId = "run-1",
                Tool = "codex",
                Model = "model-a",
                Task = "task-a",
                RepoPath = repo,
                Status = RunStatus.Succeeded,
                StartedAtUtc = DateTimeOffset.Parse("2026-03-24T10:00:00Z"),
                ArtifactDirectory = Path.Combine(artifactsRoot, "run-1")
            });
        await File.WriteAllTextAsync(Path.Combine(artifactsRoot, "run-1", "code-diff.patch"), string.Empty);
        await File.WriteAllTextAsync(Path.Combine(artifactsRoot, "run-1", "agent-output.md"), "agent output");

        var config = new BenchmarkConfig
        {
            Version = 1,
            Defaults = new DefaultsConfig { ArtifactsRoot = "runs", ReportsRoot = "reports" },
            Tools = [new ToolConfig { Id = "codex", Enabled = true }],
            Models = [new ModelConfig { Id = "model-a", Provider = "ollama", Enabled = true }],
            Tasks = [new TaskConfig { Id = "task-a", RepoPath = repo, Prompt = "prompt", Enabled = true }]
        };

        var originalIn = Console.In;
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var input = new StringReader(string.Empty);
        using var output = new StringWriter();
        using var error = new StringWriter();
        Console.SetIn(input);
        Console.SetOut(output);
        Console.SetError(error);

        try
        {
            var service = new ReviewService(new GitClient());
            await service.ReviewAsync(config, Path.Combine(temp, "benchmark.yaml"), CancellationToken.None);
        }
        finally
        {
            Console.SetIn(originalIn);
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }

        var stored = ReportService.LoadRecords(artifactsRoot).Single();
        Assert.AreEqual(string.Empty, stored.QualityRating);
        StringAssert.Contains(error.ToString(), "Skipping repository");
    }

    [TestMethod]
    public async Task ReviewServiceAllowsEmptyPatchFiles()
    {
        var temp = CreateTemporaryDirectory();
        var repo = CreateGitRepo(temp);
        var artifactsRoot = Path.Combine(temp, "runs");
        var artifactDirectory = Path.Combine(artifactsRoot, "run-1");
        Directory.CreateDirectory(artifactDirectory);

        await File.WriteAllTextAsync(Path.Combine(artifactDirectory, "code-diff.patch"), string.Empty);
        await File.WriteAllTextAsync(Path.Combine(artifactDirectory, "agent-output.md"), "agent output");
        WriteStoredRun(
            artifactDirectory,
            new StoredRunRecord
            {
                RunId = "run-1",
                Tool = "codex",
                Model = "model-a",
                Task = "task-a",
                RepoPath = repo,
                Status = RunStatus.Succeeded,
                ExitCode = 0,
                TimedOut = false,
                ArtifactDirectory = artifactDirectory,
                StartedAtUtc = DateTimeOffset.Parse("2026-03-24T10:00:00Z"),
                FinishedAtUtc = DateTimeOffset.Parse("2026-03-24T10:00:03Z"),
                DurationSeconds = 3.0,
                QualityRating = string.Empty
            });

        var config = new BenchmarkConfig
        {
            Version = 1,
            Defaults = new DefaultsConfig { ArtifactsRoot = "runs", ReportsRoot = "reports" },
            Tools = [new ToolConfig { Id = "codex", Enabled = true }],
            Models = [new ModelConfig { Id = "model-a", Provider = "ollama", Enabled = true }],
            Tasks = [new TaskConfig { Id = "task-a", RepoPath = repo, Prompt = "prompt", Enabled = true }]
        };

        var originalIn = Console.In;
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var input = new StringReader("1\n");
        using var output = new StringWriter();
        using var error = new StringWriter();
        Console.SetIn(input);
        Console.SetOut(output);
        Console.SetError(error);

        try
        {
            var service = new ReviewService(new GitClient());
            await service.ReviewAsync(config, Path.Combine(temp, "benchmark.yaml"), CancellationToken.None);
        }
        finally
        {
            Console.SetIn(originalIn);
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }

        var stored = ReportService.LoadRecords(artifactsRoot).Single();
        Assert.AreEqual("1", stored.QualityRating);
        StringAssert.Contains(output.ToString(), "Code Diff: <empty>");
        Assert.IsTrue(new GitClient().IsClean(repo));
    }

    [TestMethod]
    public async Task ReviewCommandWorksWithArtifactsRootOnlyConfig()
    {
        var temp = CreateTemporaryDirectory();
        var repo = CreateGitRepo(temp);
        var artifactsRoot = Path.Combine(temp, "runs");
        var artifactDirectory = Path.Combine(artifactsRoot, "run-1");
        Directory.CreateDirectory(artifactDirectory);

        await File.WriteAllTextAsync(Path.Combine(artifactDirectory, "code-diff.patch"), string.Empty);
        await File.WriteAllTextAsync(Path.Combine(artifactDirectory, "agent-output.md"), "agent output");
        WriteStoredRun(
            artifactDirectory,
            new StoredRunRecord
            {
                RunId = "run-1",
                Tool = "codex",
                Model = "model-a",
                Task = "task-a",
                RepoPath = repo,
                Status = RunStatus.Succeeded,
                ArtifactDirectory = artifactDirectory,
                StartedAtUtc = DateTimeOffset.Parse("2026-03-24T10:00:00Z"),
                FinishedAtUtc = DateTimeOffset.Parse("2026-03-24T10:00:03Z")
            });

        var configPath = Path.Combine(temp, "benchmark.yaml");
        await File.WriteAllTextAsync(
            configPath,
            """
            version: 1

            defaults:
              artifactsRoot: runs
            """);

        var originalIn = Console.In;
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var input = new StringReader("3\n");
        using var output = new StringWriter();
        using var error = new StringWriter();
        Console.SetIn(input);
        Console.SetOut(output);
        Console.SetError(error);

        try
        {
            var exitCode = await LocalAgenticCodingBenchmark.Cli.Cli.RunAsync(["review", configPath]);
            Assert.AreEqual(0, exitCode);
        }
        finally
        {
            Console.SetIn(originalIn);
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }

        var stored = ReportService.LoadRecords(artifactsRoot).Single();
        Assert.AreEqual("3", stored.QualityRating);
    }

    [TestMethod]
    public async Task ReviewServiceAcceptsFailedShortcut()
    {
        var temp = CreateTemporaryDirectory();
        var repo = CreateGitRepo(temp);
        var artifactsRoot = Path.Combine(temp, "runs");
        var artifactDirectory = Path.Combine(artifactsRoot, "run-1");
        Directory.CreateDirectory(artifactDirectory);

        await File.WriteAllTextAsync(Path.Combine(artifactDirectory, "code-diff.patch"), string.Empty);
        await File.WriteAllTextAsync(Path.Combine(artifactDirectory, "agent-output.md"), "agent output");
        WriteStoredRun(
            artifactDirectory,
            new StoredRunRecord
            {
                RunId = "run-1",
                Tool = "codex",
                Model = "model-a",
                Task = "task-a",
                RepoPath = repo,
                Status = RunStatus.Succeeded,
                ArtifactDirectory = artifactDirectory,
                StartedAtUtc = DateTimeOffset.Parse("2026-03-24T10:00:00Z"),
                FinishedAtUtc = DateTimeOffset.Parse("2026-03-24T10:00:03Z")
            });

        var config = new BenchmarkConfig
        {
            Version = 1,
            Defaults = new DefaultsConfig { ArtifactsRoot = "runs", ReportsRoot = "reports" }
        };

        var originalIn = Console.In;
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var input = new StringReader("f\n");
        using var output = new StringWriter();
        using var error = new StringWriter();
        Console.SetIn(input);
        Console.SetOut(output);
        Console.SetError(error);

        try
        {
            var service = new ReviewService(new GitClient());
            await service.ReviewAsync(config, Path.Combine(temp, "benchmark.yaml"), CancellationToken.None);
        }
        finally
        {
            Console.SetIn(originalIn);
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }

        var stored = ReportService.LoadRecords(artifactsRoot).Single();
        Assert.AreEqual("Failed", stored.QualityRating);
        StringAssert.Contains(output.ToString(), "QualityRating (1-6, F=Failed): ");
    }

    private static PlannedRun CreatePlannedRun(string toolId, PermissionRequestPolicy skipPermissions = PermissionRequestPolicy.Skip) => new()
    {
        RunId = "run-1",
        ArtifactDirectory = "/tmp/artifacts/run-1",
        TimeoutSeconds = 900,
        WarmupPrompt = "Hello World!",
        SkipPermissions = skipPermissions,
        Tool = new ToolConfig { Id = toolId, Enabled = true },
        Model = new ModelConfig { Id = "model-a", Provider = "ollama", Enabled = true },
        Task = new TaskConfig { Id = "task-a", RepoPath = "/tmp/repo", Prompt = "Do the thing", Enabled = true }
    };

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

    private static void WriteStoredRun(string artifactDirectory, StoredRunRecord record)
    {
        Directory.CreateDirectory(artifactDirectory);
        File.WriteAllText(
            Path.Combine(artifactDirectory, "run.json"),
            System.Text.Json.JsonSerializer.Serialize(record, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }
}
