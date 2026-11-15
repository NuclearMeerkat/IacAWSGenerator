using IacAgent.Plugins.Models;

using Microsoft.Extensions.Configuration;

namespace IacAgent.Host.Tests.Models;

public sealed class ModelRouterTests
{
    [Fact]
    public void CreateChatOptions_Temperature_ReachesTheOptions()
    {
        var router = new ModelRouter(new ModelConnection("strong", null, null, 0.2f), EmptyPrices());

        Assert.Equal(0.2f, router.CreateChatOptions().Temperature);
    }

    [Fact]
    public void CreateChatOptions_None_SendsNullTemperature()
    {
        var router = new ModelRouter(new ModelConnection("strong", null, null, null), EmptyPrices());

        Assert.Null(router.CreateChatOptions().Temperature);
    }

    [Fact]
    public void EstimateCostUsd_KnownModel_UsesPerMillionRates()
    {
        var directory = Directory.CreateTempSubdirectory("iacagent-price-").FullName;
        try
        {
            File.WriteAllText(
                Path.Combine(directory, "iacagent.json"),
                """
                {
                  "pricing": {
                    "org/model.v1": { "inputPerMillion": 2, "outputPerMillion": 4 }
                  }
                }
                """);
            var configuration = new ConfigurationBuilder().SetBasePath(directory).AddJsonFile("iacagent.json").Build();
            var router = new ModelRouter(new ModelConnection("org/model.v1", null, null), PriceTable.Bind(configuration));

            Assert.Equal(3m, router.EstimateCostUsd("org/model.v1", 1_000_000, 250_000));
            Assert.Null(router.EstimateCostUsd("other", 1_000_000, 250_000));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Bind_NegativeRate_Throws()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Pricing:org/model:inputPerMillion"] = "-1",
            ["Pricing:org/model:outputPerMillion"] = "1",
        }).Build();

        var error = Assert.Throws<InvalidOperationException>(() => PriceTable.Bind(configuration));

        Assert.Contains("inputPerMillion", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Bind_MissingRate_Throws()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Pricing:org/model:inputPerMillion"] = "1",
        }).Build();

        var error = Assert.Throws<InvalidOperationException>(() => PriceTable.Bind(configuration));

        Assert.Contains("outputPerMillion", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Bind_EmptySection_PricesNothing()
    {
        var router = new ModelRouter(new ModelConnection("org/model", null, null), EmptyPrices());

        Assert.Null(router.EstimateCostUsd("org/model", 10, 10));
    }

    private static PriceTable EmptyPrices()
    {
        return PriceTable.Bind(new ConfigurationBuilder().Build());
    }
}
