using System.Globalization;
using System.Text.Json;

using IacAgent.Core;

namespace IacAgent.Plugins.Gates.Infracost;

/// <summary>Compares an Infracost monthly estimate with the configured budget.</summary>
internal sealed class InfracostGate(IProcessRunner processRunner, BudgetLimit budget) : IGate
{
    /// <inheritdoc />
    public string Name => "infracost";

    /// <inheritdoc />
    public int Order => 30;

    /// <inheritdoc />
    public async Task<GateResult> CheckAsync(Artifact artifact, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        if (budget.UsdPerMonth is null)
        {
            return new GateResult(Name, true, []);
        }

        ProcessResult result;
        try
        {
            result = await processRunner.RunAsync(
                "infracost",
                ["scan", ".", "--json", "--currency", "USD", "--no-color"],
                artifact.Directory,
                cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException exception)
        {
            return Failed(exception.Message);
        }

        InfracostEstimate estimate;
        try
        {
            estimate = InfracostOutputParser.Parse(result.StandardOutput);
        }
        catch (JsonException)
        {
            return Failed("Infracost did not return JSON. Run infracost auth login, then try again.");
        }

        if (estimate.MonthlyCost is null)
        {
            return Failed("Infracost could not price this project.");
        }

        if (!estimate.Currency.Equals("USD", StringComparison.OrdinalIgnoreCase))
        {
            return Failed($"Infracost reported {estimate.Currency}, and --budget is USD per month.");
        }

        if (estimate.MonthlyCost.Value <= budget.UsdPerMonth.Value)
        {
            return new GateResult(Name, true, []);
        }

        var message = new System.Text.StringBuilder();
        message.Append(CultureInfo.InvariantCulture, $"Estimated monthly cost is {estimate.MonthlyCost.Value:0.00} USD. Budget is {budget.UsdPerMonth.Value:0.00} USD.");
        if (estimate.Largest.Count > 0)
        {
            message.Append(" Largest items: ");
            message.Append(string.Join(", ", estimate.Largest.Select(item => $"{item.Name} {item.MonthlyCost.ToString("0.00", CultureInfo.InvariantCulture)} USD")));
            message.Append('.');
        }

        return new GateResult(Name, false, [new GateIssue(string.Empty, null, KnownGateRules.BudgetExceeded, "error", message.ToString())]);
    }

    private GateResult Failed(string message)
    {
        return new GateResult(Name, false, [new GateIssue(string.Empty, null, "infracost", "error", message)]);
    }
}
