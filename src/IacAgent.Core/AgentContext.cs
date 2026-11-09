namespace IacAgent.Core;

/// <summary>Input for one agent invocation.</summary>
public sealed record AgentContext(
    string Prompt,
    bool AutoApprove,
    string? RequirementsJson = null,
    Artifact? Artifact = null,
    IReadOnlyList<GateResult>? GateResults = null);
