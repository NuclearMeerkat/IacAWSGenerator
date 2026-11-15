namespace IacAgent.Core;

/// <summary>A gate outcome stored in run.json, without the issue text.</summary>
public sealed record GateSummary(string Name, bool Passed, int IssueCount, int BlockingCount = 0, long DurationMs = 0);
