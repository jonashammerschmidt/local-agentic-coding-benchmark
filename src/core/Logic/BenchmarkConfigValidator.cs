namespace LocalAgenticCodingBenchmark.Core;

public static class BenchmarkConfigValidator
{
    private static readonly HashSet<string> SupportedTools = ["codex", "opencode", "claude"];

    public static void Validate(BenchmarkConfig config)
    {
        if (config.Version != 1)
        {
            throw new BenchmarkConfigurationException($"Unsupported config version '{config.Version}'.");
        }

        if (config.Defaults.TimeoutSeconds <= 0)
        {
            throw new BenchmarkConfigurationException("defaults.timeoutSeconds must be greater than zero.");
        }

        EnsureUnique(config.Tools.Select(t => t.Id), "tools");
        EnsureUnique(config.Models.Select(m => m.Id), "models");
        EnsureUnique(config.Tasks.Select(t => t.Id), "tasks");

        foreach (var tool in config.Tools)
        {
            if (!SupportedTools.Contains(tool.Id))
            {
                throw new BenchmarkConfigurationException($"Unsupported tool id '{tool.Id}'. Supported values: codex, opencode, claude.");
            }
        }

        foreach (var task in config.Tasks)
        {
            var fullRepoPath = Path.GetFullPath(task.RepoPath);
            if (!Directory.Exists(fullRepoPath))
            {
                throw new BenchmarkConfigurationException($"Task '{task.Id}' references missing repoPath '{fullRepoPath}'.");
            }

            if (!Directory.Exists(Path.Combine(fullRepoPath, ".git")))
            {
                throw new BenchmarkConfigurationException($"Task '{task.Id}' repoPath '{fullRepoPath}' is not a Git repository.");
            }
        }
    }

    private static void EnsureUnique(IEnumerable<string> ids, string section)
    {
        var duplicates = ids
            .GroupBy(id => id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        if (duplicates.Length > 0)
        {
            throw new BenchmarkConfigurationException($"Duplicate ids in '{section}': {string.Join(", ", duplicates)}.");
        }
    }
}
