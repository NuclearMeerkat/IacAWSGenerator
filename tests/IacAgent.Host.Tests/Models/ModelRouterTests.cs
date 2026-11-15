using IacAgent.Plugins.Models;

namespace IacAgent.Host.Tests.Models;

public sealed class ModelRouterTests
{
    [Fact]
    public void CreateChatOptions_Temperature_ReachesTheOptions()
    {
        var router = new ModelRouter(new ModelConnection("strong", null, null, 0.2f));

        Assert.Equal(0.2f, router.CreateChatOptions().Temperature);
    }

    [Fact]
    public void CreateChatOptions_None_SendsNullTemperature()
    {
        var router = new ModelRouter(new ModelConnection("strong", null, null, null));

        Assert.Null(router.CreateChatOptions().Temperature);
    }
}
