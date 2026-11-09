namespace IacAgent.Host.Configuration;

/// <summary>One generate invocation passed to the pipeline.</summary>
public sealed record GenerateRequest(
    string Prompt,
    string Profile,
    string OutputDirectory,
    string? ModelStrong,
    string? ModelCheap,
    string? Endpoint,
    int MaxRepairIterations,
    bool AutoApprove);
