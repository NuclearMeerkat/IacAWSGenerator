namespace IacAgent.Plugins.Gates.Infracost;

internal sealed record InfracostEstimate(decimal? MonthlyCost, string Currency, IReadOnlyList<InfracostResourceCost> Largest);

internal sealed record InfracostResourceCost(string Name, decimal MonthlyCost);
