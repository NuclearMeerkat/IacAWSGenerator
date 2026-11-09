namespace IacAgent.Core;

/// <summary>One pass of agents and gates.</summary>
public sealed record IterationRecord(int Index, IReadOnlyList<GateSummary> Gates, TokenCount Tokens);
