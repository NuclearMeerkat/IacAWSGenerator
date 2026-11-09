using IacAgent.Core;

namespace IacAgent.Host.Pipeline;

internal static class EscalationReport
{
    public static async Task WriteAsync(
        string directory,
        RunOutcome outcome,
        IReadOnlyList<GateResult> results,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        var lines = new List<string>
        {
            "# Escalation",
            string.Empty,
            $"Outcome: {outcome}",
            string.Empty,
            "## Remaining issues",
        };

        var issues = results.Where(result => !result.Passed).SelectMany(result => result.Issues.Select(issue => (result.GateName, issue)));
        var any = false;
        foreach (var (gate, issue) in issues)
        {
            any = true;
            var line = issue.Line?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-";
            lines.Add($"- {gate} {issue.File}:{line} [{issue.Severity}] {issue.RuleId}: {issue.Message}");
        }

        if (!any)
        {
            lines.Add("- None.");
        }

        var path = Path.Combine(directory, "escalation.md");
        await File.WriteAllLinesAsync(path, lines, cancellationToken).ConfigureAwait(false);
    }
}
