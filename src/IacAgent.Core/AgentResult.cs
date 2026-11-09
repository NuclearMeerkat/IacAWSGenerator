namespace IacAgent.Core;

/// <summary>What an agent decided during one invocation.</summary>
public sealed record AgentResult(
    IReadOnlyList<string> Assumptions,
    string? RequirementsJson = null,
    IReadOnlyList<GeneratedFile>? Files = null,
    string? Review = null,
    TokenCount? Tokens = null,
    bool Failed = false,
    string? FailureMessage = null,
    bool NeedsHuman = false);
