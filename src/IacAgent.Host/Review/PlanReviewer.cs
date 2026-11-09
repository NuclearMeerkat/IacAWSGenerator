using System.Text;
using System.Text.Json;

using IacAgent.Core;

using Microsoft.Extensions.AI;

namespace IacAgent.Host.Review;

/// <summary>Reads a Terraform plan, redacts secrets, runs Checkov, and asks the cheap model to explain the findings.</summary>
public sealed class PlanReviewer(IEnumerable<IGate> gates, IModelRouter router, IEnumerable<IRedactionRule> rules)
{
    private const string SystemPrompt = """
        You explain Checkov findings on an existing Terraform plan and propose a fix for each one.
        Do not repeat secret values. Leave [redacted] as it is.
        """;

    /// <summary>Reviews one plan file against the Terraform source directory.</summary>
    public async Task<PlanReview> ReviewAsync(string planPath, string sourceDirectory, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(planPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        var plan = await File.ReadAllTextAsync(planPath, cancellationToken).ConfigureAwait(false);
        var redactedPlan = Redact(plan);
        _ = JsonDocument.Parse(redactedPlan);

        var checkov = gates.Single(gate => gate.Name == "checkov");
        var result = await checkov.CheckAsync(new Artifact(sourceDirectory, []), cancellationToken).ConfigureAwait(false);
        var findings = result.Issues
            .Select(issue => issue with { Message = Redact(issue.Message) })
            .ToList();
        var explanation = await ExplainAsync(redactedPlan, findings, cancellationToken).ConfigureAwait(false);
        return new PlanReview(findings, Redact(explanation));
    }

    private async Task<string> ExplainAsync(string redactedPlan, IReadOnlyList<GateIssue> findings, CancellationToken cancellationToken)
    {
        var user = new StringBuilder();
        user.AppendLine("Plan:");
        user.AppendLine(Truncate(redactedPlan, 4000));
        user.AppendLine();
        user.AppendLine("Findings:");
        if (findings.Count == 0)
        {
            user.AppendLine("None.");
        }

        foreach (var finding in findings)
        {
            user.Append(finding.File).Append(':').Append(finding.Line).Append(' ');
            user.Append(finding.RuleId).Append(": ").AppendLine(finding.Message);
        }

        var client = router.GetClient(ModelTier.Cheap);
        var response = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.System, SystemPrompt), new ChatMessage(ChatRole.User, user.ToString())],
            options: null,
            cancellationToken).ConfigureAwait(false);
        return response.Text ?? string.Empty;
    }

    private string Redact(string text)
    {
        foreach (var rule in rules)
        {
            text = rule.Redact(text);
        }

        return text;
    }

    private static string Truncate(string text, int maxLength)
    {
        return text.Length <= maxLength ? text : text[..maxLength];
    }
}
