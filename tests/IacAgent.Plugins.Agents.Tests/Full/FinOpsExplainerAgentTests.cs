using IacAgent.Core;

namespace IacAgent.Plugins.Agents.Tests.Full;

public sealed class FinOpsExplainerAgentTests
{
    [Fact]
    public async Task RunAsync_ReturnsCostNotesFromTheCheapModel()
    {
        var client = new ScriptedChatClient("The Lambda is the only priced resource.");
        using var scope = AgentTestHost.Open(client);
        var finops = scope.Profile("full").Agents.Single(agent => agent.Name == "finops-explainer");

        var result = await finops.RunAsync(new AgentContext("hello", true, """{"summary":"api"}"""), CancellationToken.None);

        Assert.Equal("The Lambda is the only priced resource.", result.Review);
        Assert.Equal(ModelTier.Cheap, scope.Router.LastTier);
    }
}
