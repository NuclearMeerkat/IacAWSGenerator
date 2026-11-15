using IacAgent.Core;

namespace IacAgent.Plugins.Agents.Tests.Solo;

public sealed class SoloDeveloperAgentTests
{
    [Fact]
    public async Task RunAsync_FilesJson_ReturnsTheFileAndAssumptions()
    {
        var client = new ScriptedChatClient("""{"assumptions":["region is eu-central-1"],"files":[{"path":"main.tf","content":"ok\n"}]}""");
        using var scope = AgentTestHost.Open(client);
        var solo = scope.Profile("solo").Agents.Single(agent => agent.Name == "solo-developer");

        var result = await solo.RunAsync(new AgentContext("hello", true), CancellationToken.None);

        Assert.False(result.Failed);
        Assert.Equal("main.tf", Assert.Single(result.Files!).RelativePath);
        Assert.Contains("region is eu-central-1", result.Assumptions);
        Assert.Equal(ModelTier.Strong, scope.Router.LastTier);
    }
}
