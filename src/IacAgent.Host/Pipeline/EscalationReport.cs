using IacAgent.Core;
using IacAgent.Host.Redaction;

namespace IacAgent.Host.Pipeline;

internal static class EscalationReport
{
    public static async Task WriteAsync(
        string directory,
        RunOutcome outcome,
        IReadOnlyList<GateResult> results,
        IReadOnlyList<GeneratedFile> lastAttempt,
        IEnumerable<IRedactionRule> rules,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        var redactor = new Redactor(rules);
        var lines = new List<string>
        {
            "# Escalation",
            string.Empty,
            $"Outcome: {outcome}",
            string.Empty,
            "## Remaining issues",
        };

        var blocking = new List<(string Gate, GateIssue Issue)>();
        var advisory = 0;
        foreach (var result in results)
        {
            foreach (var issue in result.Issues)
            {
                if (issue.IsBlocking && !result.Passed)
                {
                    blocking.Add((result.GateName, issue));
                }
                else if (!issue.IsBlocking)
                {
                    advisory++;
                }
            }
        }

        if (blocking.Count == 0)
        {
            lines.Add("- None.");
        }

        foreach (var (gate, issue) in blocking)
        {
            var line = issue.Line?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-";
            lines.Add($"- {gate} {issue.File}:{line} [{issue.Severity}] {issue.RuleId}: {redactor.Redact(issue.Message)}");
        }

        lines.Add($"Advisory findings: {advisory}.");

        lines.Add(string.Empty);
        lines.Add("## Last attempt");
        if (lastAttempt.Count == 0)
        {
            lines.Add("None.");
        }

        foreach (var file in lastAttempt)
        {
            lines.Add(string.Empty);
            lines.Add($"### {file.RelativePath}");
            lines.Add(redactor.Redact(file.Content));
        }

        var path = Path.Combine(directory, "escalation.md");
        await File.WriteAllLinesAsync(path, lines, cancellationToken).ConfigureAwait(false);
    }
}
