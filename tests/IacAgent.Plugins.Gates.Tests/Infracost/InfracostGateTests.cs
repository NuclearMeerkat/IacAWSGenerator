using IacAgent.Core;
using IacAgent.Plugins.Gates.Infracost;

namespace IacAgent.Plugins.Gates.Tests.Infracost;

public sealed class InfracostGateTests
{
    [Fact]
    public async Task CheckAsync_OverBudget_FailsWithReadableMessage()
    {
        const string json = """
            {
              "currency": "USD",
              "summary": { "total_monthly_cost": "42.50" },
              "projects": [
                {
                  "resources": [
                    { "name": "aws_nat_gateway.main", "cost_components": [ { "total_monthly_cost": "32.85" } ] },
                    { "name": "aws_lambda_function.hello", "cost_components": [ { "total_monthly_cost": "0.20" } ] }
                  ]
                }
              ]
            }
            """;
        var gate = new InfracostGate(new ScriptedProcessRunner(json), new BudgetLimit(10m));

        var result = await gate.CheckAsync(new Artifact(Path.GetTempPath(), []), CancellationToken.None);

        Assert.False(result.Passed);
        var issue = Assert.Single(result.Issues);
        Assert.Equal(KnownGateRules.BudgetExceeded, issue.RuleId);
        Assert.Contains("42.50 USD", issue.Message, StringComparison.Ordinal);
        Assert.Contains("10.00 USD", issue.Message, StringComparison.Ordinal);
        Assert.Contains("aws_nat_gateway.main", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckAsync_UnderBudget_Passes()
    {
        var gate = new InfracostGate(new ScriptedProcessRunner("""{"currency":"USD","totalMonthlyCost":"1.25","projects":[]}"""), new BudgetLimit(10m));

        var result = await gate.CheckAsync(new Artifact(Path.GetTempPath(), []), CancellationToken.None);

        Assert.True(result.Passed);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public async Task CheckAsync_NoBudget_DoesNotRunTheTool()
    {
        var gate = new InfracostGate(new ThrowingProcessRunner(), new BudgetLimit(null));

        var result = await gate.CheckAsync(new Artifact(Path.GetTempPath(), []), CancellationToken.None);

        Assert.True(result.Passed);
    }
}
