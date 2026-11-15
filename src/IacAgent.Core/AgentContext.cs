namespace IacAgent.Core;

/// <summary>Input for one agent invocation. Design notes are advisor text, separate from the requirements. On a repair, other files name resources whose content is not included.</summary>
public sealed record AgentContext(
    string Prompt,
    bool AutoApprove,
    string? RequirementsJson = null,
    Artifact? Artifact = null,
    IReadOnlyList<GateResult>? GateResults = null,
    IReadOnlyList<string>? OtherFiles = null,
    string? DesignNotes = null);
