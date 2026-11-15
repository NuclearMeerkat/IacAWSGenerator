using IacAgent.Plugins.Gates.Infracost;

namespace IacAgent.Plugins.Gates.Tests.Infracost;

public sealed class InfracostOutputParserTests
{
    [Fact]
    public void Parse_SavedScan_ReadsTheMonthlyTotalAndLargestItems()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Samples", "infracost-scan.json"));

        var estimate = InfracostOutputParser.Parse(json);

        Assert.Equal(0m, estimate.MonthlyCost);
        Assert.Equal("USD", estimate.Currency);
        Assert.Empty(estimate.Largest);
    }
}
