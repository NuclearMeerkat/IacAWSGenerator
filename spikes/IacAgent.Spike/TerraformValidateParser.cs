using System.Text.Json;

namespace IacAgent.Spike;

/// <summary>Reads diagnostics from <c>terraform validate -json</c>.</summary>
internal static class TerraformValidateParser
{
    public static IReadOnlyList<ParsedIssue> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("diagnostics", out var diagnostics)
            || diagnostics.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var issues = new List<ParsedIssue>();
        foreach (var diagnostic in diagnostics.EnumerateArray())
        {
            var severity = ReadString(diagnostic, "severity") ?? "error";
            var summary = ReadString(diagnostic, "summary") ?? "Terraform validation error";
            var detail = ReadString(diagnostic, "detail");
            var message = string.IsNullOrWhiteSpace(detail) ? summary : $"{summary}. {detail}";
            var file = "";
            int? line = null;
            if (diagnostic.TryGetProperty("range", out var range))
            {
                file = ReadString(range, "filename") ?? "";
                if (range.TryGetProperty("start", out var start)
                    && start.TryGetProperty("line", out var lineElement)
                    && lineElement.TryGetInt32(out var parsedLine))
                {
                    line = parsedLine;
                }
            }

            issues.Add(new ParsedIssue(file, line, summary, severity, message));
        }

        return issues;
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
