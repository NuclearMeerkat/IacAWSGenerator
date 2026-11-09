using System.Text;

using IacAgent.Core;

namespace IacAgent.Plugins.Agents.Trio;

/// <summary>Summarizes low-severity Checkov findings. It cannot change the gate outcome.</summary>
internal sealed class SecurityReviewerAgent(IModelRouter router, IEnumerable<IRedactionRule> rules) : IAgent
{
    private readonly string _prompt = ChatText.ReadPrompt("SecurityReviewer.md");

    /// <inheritdoc />
    public string Name => "security-reviewer";

    /// <inheritdoc />
    public AgentRole Role => AgentRole.Reviewer;

    /// <inheritdoc />
    public async Task<AgentResult> RunAsync(AgentContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var findings = (context.GateResults ?? [])
            .SelectMany(result => result.Issues)
            .Where(IsReviewFinding)
            .ToList();
        if (findings.Count == 0)
        {
            return new AgentResult([]);
        }

        var builder = new StringBuilder();
        foreach (var issue in findings)
        {
            builder.Append(issue.File).Append(':').Append(issue.Line).Append(' ').Append(issue.RuleId).Append(": ").AppendLine(issue.Message);
        }

        var client = router.GetClient(ModelTier.Cheap);
        var (text, tokens) = await ChatText.CompleteAsync(client, _prompt, builder.ToString(), rules, cancellationToken).ConfigureAwait(false);
        return new AgentResult([], Review: text, Tokens: tokens);
    }

    private static bool IsReviewFinding(GateIssue issue)
    {
        return issue.Severity.Equals("LOW", StringComparison.OrdinalIgnoreCase)
            || issue.Severity.Equals("INFO", StringComparison.OrdinalIgnoreCase)
            || issue.Severity.Equals("UNKNOWN", StringComparison.OrdinalIgnoreCase);
    }
}
