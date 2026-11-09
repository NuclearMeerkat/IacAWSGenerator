using IacAgent.Core;

namespace IacAgent.Host.Pipeline;

internal static class IssueSignature
{
    public static string Create(IReadOnlyList<GateResult> results)
    {
        var lines = results
            .Where(result => !result.Passed)
            .SelectMany(result => result.Issues.Select(issue =>
                $"{result.GateName}|{issue.File}|{issue.Line}|{issue.RuleId}|{issue.Message}"))
            .Order(StringComparer.Ordinal);
        return string.Join('\n', lines);
    }
}
