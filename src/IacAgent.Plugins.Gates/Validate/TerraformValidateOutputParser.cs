using System.Text.Json;

using IacAgent.Core;

namespace IacAgent.Plugins.Gates.Validate;

/// <summary>Reads diagnostics from <c>terraform validate -json</c>.</summary>
internal static class TerraformValidateOutputParser
{
    public static IReadOnlyList<GateIssue> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("diagnostics", out var diagnostics)
            || diagnostics.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var issues = new List<GateIssue>();
        foreach (var diagnostic in diagnostics.EnumerateArray())
        {
            var severity = ReadString(diagnostic, "severity") ?? "error";
            var summary = ReadString(diagnostic, "summary") ?? "Terraform validation error";
            var detail = ReadString(diagnostic, "detail");
            var message = string.IsNullOrWhiteSpace(detail) ? summary : $"{summary}. {detail}";
            var file = string.Empty;
            int? line = null;
            if (diagnostic.TryGetProperty("range", out var range))
            {
                file = Path.GetFileName(ReadString(range, "filename") ?? string.Empty);
                if (range.TryGetProperty("start", out var start)
                    && start.TryGetProperty("line", out var lineElement)
                    && lineElement.TryGetInt32(out var parsedLine))
                {
                    line = parsedLine;
                }
            }

            issues.Add(new GateIssue(file, line, summary, severity, message));
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
