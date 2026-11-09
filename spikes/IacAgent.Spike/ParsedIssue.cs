namespace IacAgent.Spike;

/// <summary>One diagnostic taken from a Terraform or Checkov report.</summary>
internal sealed record ParsedIssue(string File, int? Line, string RuleId, string Severity, string Message);
