namespace IacAgent.Core;

/// <summary>One structured finding from a gate.</summary>
public sealed record GateIssue(string File, int? Line, string RuleId, string Severity, string Message);
