namespace IacAgent.Host.Configuration;

/// <summary>The settings for one invocation after command flags, environment variables, and iacagent.json are combined.</summary>
public sealed record RunSettings
{
    /// <summary>Gets the profile name.</summary>
    public required string Profile { get; init; }

    /// <summary>Gets the output directory.</summary>
    public required string OutputDirectory { get; init; }

    /// <summary>Gets the strong-tier model id.</summary>
    public string? ModelStrong { get; init; }

    /// <summary>Gets the cheap-tier model id.</summary>
    public string? ModelCheap { get; init; }

    /// <summary>Gets the local model endpoint. Empty means OpenRouter.</summary>
    public string? Endpoint { get; init; }

    /// <summary>Gets the repair limit.</summary>
    public int MaxRepairIterations { get; init; } = 3;

    /// <summary>Gets whether interactive questions are skipped.</summary>
    public bool AutoApprove { get; init; }

    /// <summary>Gets whether debug logging is enabled.</summary>
    public bool Verbose { get; init; }

    /// <summary>Gets the winning budget text before it is parsed.</summary>
    public string? BudgetText { get; init; }

    /// <summary>Gets the monthly budget in USD. Null means no budget gate.</summary>
    public decimal? MonthlyBudgetUsd { get; init; }
}
