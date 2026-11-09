using System.Text.Json;

namespace IacAgent.Spike;

/// <summary>Reads failed checks from Checkov JSON output.</summary>
internal static class CheckovOutputParser
{
    public static IReadOnlyList<ParsedIssue> Parse(string output)
    {
        using var document = JsonDocument.Parse(ExtractJson(output));
        var issues = new List<ParsedIssue>();
        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var report in document.RootElement.EnumerateArray())
            {
                ReadReport(report, issues);
            }
        }
        else
        {
            ReadReport(document.RootElement, issues);
        }

        return issues;
    }

    private static void ReadReport(JsonElement report, List<ParsedIssue> issues)
    {
        if (!report.TryGetProperty("results", out var results)
            || !results.TryGetProperty("failed_checks", out var failedChecks)
            || failedChecks.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var check in failedChecks.EnumerateArray())
        {
            var ruleId = ReadString(check, "check_id") ?? "checkov";
            var severity = ReadString(check, "severity") ?? "UNKNOWN";
            var message = ReadString(check, "check_name") ?? ruleId;
            var file = NormalizePath(ReadString(check, "file_path") ?? "");
            int? line = null;
            if (check.TryGetProperty("file_line_range", out var lineRange)
                && lineRange.ValueKind == JsonValueKind.Array
                && lineRange.GetArrayLength() > 0
                && lineRange[0].TryGetInt32(out var parsedLine))
            {
                line = parsedLine;
            }

            issues.Add(new ParsedIssue(file, line, ruleId, severity, message));
        }
    }

    private static string ExtractJson(string output)
    {
        var trimmed = output.Trim().TrimStart('\uFEFF');
        var objectStart = trimmed.IndexOf('{');
        var arrayStart = trimmed.IndexOf('[');
        var start = objectStart < 0 ? arrayStart : arrayStart < 0 ? objectStart : Math.Min(objectStart, arrayStart);
        if (start < 0)
        {
            throw new JsonException("Checkov output did not contain JSON.");
        }

        return trimmed[start..];
    }

    private static string NormalizePath(string path)
    {
        return path.TrimStart('/', '\\');
    }

    private static string? ReadString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return property.GetString();
    }
}
