namespace IacAgent.Core;

/// <summary>Which configured model a call should use.</summary>
public enum ModelTier
{
    /// <summary>Orchestrator and Developer.</summary>
    Strong,

    /// <summary>Review and summarization.</summary>
    Cheap,
}
