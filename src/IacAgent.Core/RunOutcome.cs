namespace IacAgent.Core;

/// <summary>How a generate run ended.</summary>
public enum RunOutcome
{
    Succeeded,
    GateFailed,
    BudgetExceeded,
    NeedsHuman,
    ConfigError,
}
