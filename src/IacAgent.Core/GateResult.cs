namespace IacAgent.Core;

/// <summary>The outcome of one gate.</summary>
public sealed record GateResult(string GateName, bool Passed, IReadOnlyList<GateIssue> Issues);
