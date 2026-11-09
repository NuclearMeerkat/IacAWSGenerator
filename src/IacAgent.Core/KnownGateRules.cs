namespace IacAgent.Core;

/// <summary>Rule ids the host uses when choosing an exit code.</summary>
public static class KnownGateRules
{
    /// <summary>The Infracost estimate is above the monthly budget.</summary>
    public const string BudgetExceeded = "budget-exceeded";
}
