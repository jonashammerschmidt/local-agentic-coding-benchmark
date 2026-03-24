namespace LocalAgenticCodingBenchmark.Core;

public static class BenchmarkConfigParser
{
    public static BenchmarkConfig Load(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new BenchmarkConfigurationException($"Configuration file '{fullPath}' was not found.");
        }

        var lines = File.ReadAllLines(fullPath);
        return Parse(lines);
    }

    public static BenchmarkConfig Parse(IEnumerable<string> lines)
    {
        var version = 0;
        var defaults = new Dictionary<string, string>(StringComparer.Ordinal);
        var tools = new List<Dictionary<string, string>>();
        var models = new List<Dictionary<string, string>>();
        var tasks = new List<Dictionary<string, string>>();

        string? section = null;
        Dictionary<string, string>? currentItem = null;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Replace("\t", "  ");
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var trimmed = line.Trim();
            if (trimmed.StartsWith('#'))
            {
                continue;
            }

            var indent = line.Length - line.TrimStart().Length;

            if (indent == 0)
            {
                currentItem = null;
                section = ParseTopLevel(trimmed, ref version);
                continue;
            }

            if (section is null)
            {
                throw new BenchmarkConfigurationException($"Encountered indented YAML content outside a section: '{trimmed}'.");
            }

            switch (section)
            {
                case "defaults":
                    if (indent != 2)
                    {
                        throw new BenchmarkConfigurationException($"Invalid indentation in defaults section: '{trimmed}'.");
                    }

                    var (defaultsKey, defaultsValue) = SplitKeyValue(trimmed);
                    defaults[defaultsKey] = Unquote(defaultsValue);
                    break;

                case "tools":
                case "models":
                case "tasks":
                    if (indent == 2 && trimmed.StartsWith("- "))
                    {
                        currentItem = new Dictionary<string, string>(StringComparer.Ordinal);
                        AddItem(section, currentItem, tools, models, tasks);
                        var itemBody = trimmed[2..].Trim();
                        if (!string.IsNullOrEmpty(itemBody))
                        {
                            var (itemKey, itemValue) = SplitKeyValue(itemBody);
                            currentItem[itemKey] = Unquote(itemValue);
                        }
                    }
                    else if (indent == 4 && currentItem is not null)
                    {
                        var (itemKey, itemValue) = SplitKeyValue(trimmed);
                        currentItem[itemKey] = Unquote(itemValue);
                    }
                    else
                    {
                        throw new BenchmarkConfigurationException($"Invalid list item format in section '{section}': '{trimmed}'.");
                    }

                    break;
            }
        }

        return new BenchmarkConfig
        {
            Version = version,
            Defaults = new DefaultsConfig
            {
                ArtifactsRoot = defaults.TryGetValue("artifactsRoot", out var artifactsRoot) ? artifactsRoot : ".benchmarks/runs",
                ReportsRoot = defaults.TryGetValue("reportsRoot", out var reportsRoot) ? reportsRoot : ".benchmarks/reports",
                TimeoutSeconds = defaults.TryGetValue("timeoutSeconds", out var timeoutSeconds) && int.TryParse(timeoutSeconds, out var parsedTimeout)
                    ? parsedTimeout
                    : 900,
                WarmupPrompt = defaults.TryGetValue("warmupPrompt", out var warmupPrompt) && !string.IsNullOrWhiteSpace(warmupPrompt)
                    ? warmupPrompt
                    : "Say Hello World!",
                SkipPermissions = GetPermissionRequestPolicy(defaults, "skipPermissions")
            },
            Tools = tools.Select(map => new ToolConfig
            {
                Id = GetRequired(map, "id", "tools"),
                Enabled = GetBool(map, "enabled")
            }).ToList(),
            Models = models.Select(map => new ModelConfig
            {
                Id = GetRequired(map, "id", "models"),
                Provider = GetRequired(map, "provider", "models"),
                Enabled = GetBool(map, "enabled")
            }).ToList(),
            Tasks = tasks.Select(map => new TaskConfig
            {
                Id = GetRequired(map, "id", "tasks"),
                RepoPath = ExpandHomeDirectory(GetRequired(map, "repoPath", "tasks")),
                Prompt = GetRequired(map, "prompt", "tasks")
            }).ToList()
        };
    }

    private static string? ParseTopLevel(string trimmed, ref int version)
    {
        if (trimmed.EndsWith(':'))
        {
            return trimmed[..^1];
        }

        var (key, value) = SplitKeyValue(trimmed);
        if (key == "version" && int.TryParse(Unquote(value), out var parsedVersion))
        {
            version = parsedVersion;
            return null;
        }

        throw new BenchmarkConfigurationException($"Unsupported top-level key '{key}'.");
    }

    private static void AddItem(
        string section,
        Dictionary<string, string> item,
        List<Dictionary<string, string>> tools,
        List<Dictionary<string, string>> models,
        List<Dictionary<string, string>> tasks)
    {
        switch (section)
        {
            case "tools":
                tools.Add(item);
                break;
            case "models":
                models.Add(item);
                break;
            case "tasks":
                tasks.Add(item);
                break;
        }
    }

    private static (string Key, string Value) SplitKeyValue(string input)
    {
        var index = input.IndexOf(':');
        if (index <= 0)
        {
            throw new BenchmarkConfigurationException($"Expected key/value pair but found '{input}'.");
        }

        var key = input[..index].Trim();
        var value = input[(index + 1)..].Trim();
        return (key, value);
    }

    private static string GetRequired(Dictionary<string, string> map, string key, string section)
    {
        if (map.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        throw new BenchmarkConfigurationException($"Missing required key '{key}' in section '{section}'.");
    }

    private static bool GetBool(Dictionary<string, string> map, string key)
    {
        if (!map.TryGetValue(key, out var value))
        {
            return false;
        }

        return value.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    private static PermissionRequestPolicy GetPermissionRequestPolicy(Dictionary<string, string> map, string key)
    {
        if (!map.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return PermissionRequestPolicy.Abort;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "skip" => PermissionRequestPolicy.Skip,
            "abort" => PermissionRequestPolicy.Abort,
            "prompt" => PermissionRequestPolicy.Prompt,
            _ => throw new BenchmarkConfigurationException(
                $"Unsupported value '{value}' for defaults.{key}. Supported values: skip, abort. Reserved for future use: prompt.")
        };
    }

    private static string Unquote(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length >= 2 &&
            ((trimmed.StartsWith('"') && trimmed.EndsWith('"')) || (trimmed.StartsWith('\'') && trimmed.EndsWith('\''))))
        {
            return trimmed[1..^1];
        }

        return trimmed;
    }

    private static string ExpandHomeDirectory(string path)
    {
        if (path == "~")
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        if (path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith("~\\", StringComparison.Ordinal))
        {
            var homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(homeDirectory, path[2..]);
        }

        return path;
    }
}
