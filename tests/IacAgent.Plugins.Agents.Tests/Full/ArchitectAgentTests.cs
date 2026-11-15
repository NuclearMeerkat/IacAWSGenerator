using IacAgent.Core;

namespace IacAgent.Plugins.Agents.Tests.Full;

public sealed class ArchitectAgentTests
{
    [Fact]
    public async Task RunAsync_ReturnsDesignNotesFromTheStrongModel()
    {
        var client = new ScriptedChatClient("One HTTP API and one Lambda.");
        using var scope = AgentTestHost.Open(client);
        var architect = scope.Profile("full").Agents.Single(agent => agent.Name == "architect");

        var result = await architect.RunAsync(new AgentContext("hello", true, """{"summary":"api"}"""), CancellationToken.None);

        Assert.Equal("One HTTP API and one Lambda.", result.Review);
        Assert.Contains("summary", client.LastUser, StringComparison.Ordinal);
        Assert.Equal(ModelTier.Strong, scope.Router.LastTier);
    }
}
