namespace LocalAgenticCodingBenchmark.Core;

public sealed class GitClient
{
    public bool IsClean(string repoPath)
    {
        return string.IsNullOrWhiteSpace(GetStatusPorcelain(repoPath));
    }

    public string GetStatusPorcelain(string repoPath)
    {
        var output = RunGit(repoPath, "status", "--porcelain");
        return output.StandardOutput.Trim();
    }

    public string GetDiff(string repoPath)
    {
        RunGit(repoPath, "add", "--intent-to-add", "--all");
        var output = RunGit(repoPath, "diff", "--binary", "--full-index");
        return output.StandardOutput;
    }

    public void ResetHard(string repoPath)
    {
        RunGit(repoPath, "reset", "--hard", "HEAD");
        RunGit(repoPath, "clean", "-fd");
    }

    private static GitCommandResult RunGit(string repoPath, params string[] args)
    {
        var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = repoPath,
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
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Git command failed: git {string.Join(" ", args)}{Environment.NewLine}{stderr}");
        }

        return new GitCommandResult(stdout, stderr);
    }

    private sealed record GitCommandResult(string StandardOutput, string StandardError);
}
