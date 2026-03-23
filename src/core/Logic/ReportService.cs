using System.Globalization;
using System.Text;
using System.Text.Json;

namespace LocalAgenticCodingBenchmark.Core;

public sealed class ReportService
{
    public async Task<string> GenerateAsync(BenchmarkConfig config, string configDirectory, CancellationToken cancellationToken)
    {
        var artifactsRoot = Path.GetFullPath(Path.Combine(configDirectory, config.Defaults.ArtifactsRoot));
        var reportsRoot = Path.GetFullPath(Path.Combine(configDirectory, config.Defaults.ReportsRoot));
        Directory.CreateDirectory(reportsRoot);

        var records = LoadRecords(artifactsRoot)
            .OrderBy(r => r.StartedAtUtc)
            .ThenBy(r => r.Tool, StringComparer.Ordinal)
            .ThenBy(r => r.Model, StringComparer.Ordinal)
            .ToArray();

        var markdown = BuildMarkdown(records);
        var reportPath = Path.Combine(reportsRoot, "results.md");
        await File.WriteAllTextAsync(reportPath, markdown, cancellationToken);
        return reportPath;
    }

    public static IReadOnlyList<StoredRunRecord> LoadRecords(string artifactsRoot)
    {
        if (!Directory.Exists(artifactsRoot))
        {
            return [];
        }

        var files = Directory.EnumerateFiles(artifactsRoot, "run.json", SearchOption.AllDirectories);
        var records = new List<StoredRunRecord>();
        foreach (var file in files)
        {
            var json = File.ReadAllText(file);
            var record = JsonSerializer.Deserialize<StoredRunRecord>(json);
            if (record is not null)
            {
                records.Add(record);
            }
        }

        return records;
    }

    public static string BuildMarkdown(IEnumerable<StoredRunRecord> records)
    {
        var builder = new StringBuilder();
        builder.AppendLine("| Run ID | Tool | Model | Task | Duration | Status | Quality Rating |");
        builder.AppendLine("| --- | --- | --- | --- | --- | --- | --- |");

        foreach (var record in records)
        {
            builder.Append("| ")
                .Append(record.RunId).Append(" | ")
                .Append(record.Tool).Append(" | ")
                .Append(record.Model).Append(" | ")
                .Append(record.Task).Append(" | ")
                .Append(record.DurationSeconds.ToString("0.###", CultureInfo.InvariantCulture)).Append("s | ")
                .Append(record.Status).Append(" | ")
                .Append(record.QualityRating ?? string.Empty).AppendLine(" |");
        }

        return builder.ToString();
    }
}
