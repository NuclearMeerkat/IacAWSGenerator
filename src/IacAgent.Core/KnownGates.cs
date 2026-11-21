namespace IacAgent.Core;

/// <summary>Names of the synthetic gates the host records when an agent stops the run.</summary>
public static class KnownGates
{
    /// <summary>The orchestrator stopped the run before any Terraform was written.</summary>
    public const string Orchestrator = "orchestrator";

    /// <summary>The developer returned nothing the real gates can check.</summary>
    public const string Developer = "developer";
}
