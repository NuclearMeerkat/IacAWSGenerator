using IacAgent.Plugins.Gates.Infracost;

namespace IacAgent.Plugins.Gates.Tests.Infracost;

public sealed class InfracostOutputParserTests
{
    [Fact]
    public void Parse_SavedScan_ReadsTheMonthlyTotalAndLargestItems()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Samples", "infracost-scan.json"));

        var estimate = InfracostOutputParser.Parse(json);

        Assert.Equal(42.50m, estimate.MonthlyCost);
        Assert.Equal("USD", estimate.Currency);
        Assert.Equal("aws_nat_gateway.main", estimate.Largest[0].Name);
        Assert.Equal(32.85m, estimate.Largest[0].MonthlyCost);
    }
}
