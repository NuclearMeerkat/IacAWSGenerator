namespace IacAgent.Spike;

internal static class ToolReport
{
    public static void Print(string title, IReadOnlyList<ParsedIssue> issues)
    {
        Console.WriteLine($"{title}: {issues.Count} issue(s)");
        foreach (var issue in issues)
        {
            var line = issue.Line?.ToString() ?? "-";
            Console.WriteLine($"  {issue.File}:{line} [{issue.Severity}] {issue.RuleId}: {issue.Message}");
        }
    }
}
