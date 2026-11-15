namespace IacAgent.Plugins.Gates.Checkov;

/// <summary>How Checkov findings affect the gate.</summary>
internal enum SecurityLevel
{
    Baseline,
    Strict,
}
