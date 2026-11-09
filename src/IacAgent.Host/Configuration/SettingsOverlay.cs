namespace IacAgent.Host.Configuration;

/// <summary>Values supplied by one configuration layer. Null means the layer did not set the value.</summary>
public sealed record SettingsOverlay
{
    /// <summary>Gets the profile name.</summary>
    public string? Profile { get; init; }

    /// <summary>Gets the output directory.</summary>
    public string? OutputDirectory { get; init; }

    /// <summary>Gets the strong-tier model id.</summary>
    public string? ModelStrong { get; init; }

    /// <summary>Gets the cheap-tier model id.</summary>
    public string? ModelCheap { get; init; }

    /// <summary>Gets the local model endpoint. When set, the run does not use OpenRouter.</summary>
    public string? Endpoint { get; init; }

    /// <summary>Gets the repair limit.</summary>
    public int? MaxRepairIterations { get; init; }

    /// <summary>Gets whether interactive questions are skipped.</summary>
    public bool? AutoApprove { get; init; }

    /// <summary>Gets whether debug logging is enabled.</summary>
    public bool? Verbose { get; init; }

    /// <summary>Gets the raw monthly budget text, in USD.</summary>
    public string? Budget { get; init; }
}
