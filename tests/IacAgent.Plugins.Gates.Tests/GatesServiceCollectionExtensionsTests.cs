using IacAgent.Core;
using IacAgent.Plugins.Gates;
using IacAgent.Plugins.Gates.Infracost;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IacAgent.Plugins.Gates.Tests;

public sealed class GatesServiceCollectionExtensionsTests
{
    [Fact]
    public void AddGates_RegistersValidateCheckovThenInfracost()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IProcessRunner, IdleProcessRunner>();
        services.AddSingleton(new BudgetLimit(null));
        services.AddGates(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();

        var names = provider.GetServices<IGate>().OrderBy(gate => gate.Order).Select(gate => gate.Name).ToArray();

        Assert.Equal(["validate", "checkov", "infracost"], names);
    }
}
