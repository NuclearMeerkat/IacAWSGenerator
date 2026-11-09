namespace IacAgent.Plugins.Gates.Infracost;

/// <summary>Monthly cost ceiling in USD. Null means the budget gate is off.</summary>
public sealed record BudgetLimit(decimal? UsdPerMonth);
