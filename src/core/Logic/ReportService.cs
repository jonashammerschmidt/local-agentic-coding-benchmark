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
        var headers = new[]
        {
            "Run ID",
            "Tool",
            "Model",
            "Task",
            "Duration",
            "Status",
            "Quality Rating"
        };

        var rows = records
            .Select(record => new[]
            {
                record.RunId,
                record.Tool,
                record.Model,
                record.Task,
                $"{record.DurationSeconds.ToString("0.###", CultureInfo.InvariantCulture)}s",
                record.Status.ToString(),
                record.QualityRating ?? string.Empty
            })
            .ToArray();

        var widths = headers
            .Select((header, index) => Math.Max(header.Length, rows.Select(row => row[index].Length).DefaultIfEmpty(0).Max()))
            .ToArray();

        var builder = new StringBuilder();
        AppendRow(builder, headers, widths);
        AppendSeparator(builder, widths);

        foreach (var row in rows)
        {
            AppendRow(builder, row, widths);
        }

        return builder.ToString();
    }

    private static void AppendRow(StringBuilder builder, IReadOnlyList<string> cells, IReadOnlyList<int> widths)
    {
        builder.Append('|');
        for (var i = 0; i < cells.Count; i++)
        {
            builder.Append(' ')
                .Append(cells[i].PadRight(widths[i]))
                .Append(" |");
        }

        builder.AppendLine();
    }

    private static void AppendSeparator(StringBuilder builder, IReadOnlyList<int> widths)
    {
        builder.Append('|');
        for (var i = 0; i < widths.Count; i++)
        {
            builder.Append(' ')
                .Append(new string('-', widths[i]))
                .Append(" |");
        }

        builder.AppendLine();
    }
}
