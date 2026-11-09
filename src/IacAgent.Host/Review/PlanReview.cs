using IacAgent.Core;

namespace IacAgent.Host.Review;

/// <summary>Checkov findings and the model's explanation of a plan.</summary>
public sealed record PlanReview(IReadOnlyList<GateIssue> Findings, string Explanation);
