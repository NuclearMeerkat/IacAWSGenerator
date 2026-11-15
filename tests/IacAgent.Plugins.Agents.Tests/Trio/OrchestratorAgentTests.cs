using IacAgent.Core;

namespace IacAgent.Plugins.Agents.Tests.Trio;

public sealed class OrchestratorAgentTests
{
    [Fact]
    public async Task RunAsync_AutoApprove_DoesNotAsk()
    {
        var interaction = new RecordingInteraction();
        var client = new ScriptedChatClient("""{"assumptions":["region is eu-central-1"],"questions":["Which region?"],"requirements":{"summary":"api"}}""");
        using var scope = AgentTestHost.Open(client, interaction);
        var orchestrator = scope.Profile("trio").Agents.Single(agent => agent.Role == AgentRole.Orchestrator);

        var result = await orchestrator.RunAsync(new AgentContext("api", true), CancellationToken.None);

        Assert.Empty(interaction.Questions);
        Assert.Contains(result.Assumptions, assumption => assumption.Contains("Which region?", StringComparison.Ordinal));
        Assert.Contains("api", result.RequirementsJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_AutoApproveNeedsHuman_StopsForAPerson()
    {
        var interaction = new RecordingInteraction();
        var client = new ScriptedChatClient("""{"assumptions":[],"questions":[],"needsHuman":true,"requirements":{"summary":"choose a database"}}""");
        using var scope = AgentTestHost.Open(client, interaction);
        var orchestrator = scope.Profile("trio").Agents.Single(agent => agent.Role == AgentRole.Orchestrator);

        var result = await orchestrator.RunAsync(new AgentContext("store data", true), CancellationToken.None);

        Assert.Empty(interaction.Questions);
        Assert.True(result.NeedsHuman);
    }

    [Fact]
    public async Task RunAsync_NotATerminal_AssumesTheDefault()
    {
        var interaction = new SilentInteraction();
        var client = new ScriptedChatClient("""{"assumptions":[],"questions":["Which region?"],"needsHuman":false,"requirements":{"summary":"api"}}""");
        using var scope = AgentTestHost.Open(client, interaction);
        var orchestrator = scope.Profile("trio").Agents.Single(agent => agent.Role == AgentRole.Orchestrator);

        var result = await orchestrator.RunAsync(new AgentContext("api", false), CancellationToken.None);

        Assert.False(result.NeedsHuman);
        Assert.Contains(result.Assumptions, assumption => assumption.Contains("Which region?", StringComparison.Ordinal));
    }
}
