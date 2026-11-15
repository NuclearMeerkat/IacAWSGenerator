using IacAgent.Plugins.Agents;

using Microsoft.Extensions.AI;

namespace IacAgent.Plugins.Agents.Tests;

public sealed class AgentsServiceCollectionExtensionsTests
{
    [Fact]
    public void AddAgents_RegistersTrioProfile()
    {
        using var scope = AgentTestHost.Open(new ScriptedChatClient("unused"));
        var pipeline = scope.Profile("trio");

        Assert.Equal("trio", pipeline.Name);
        Assert.Equal(["orchestrator", "developer", "security-reviewer"], pipeline.Agents.Select(agent => agent.Name).ToArray());
    }

    [Fact]
    public void AddAgents_RegistersSoloAndFull()
    {
        using var scope = AgentTestHost.Open(new ScriptedChatClient("unused"));

        var solo = scope.Profile("solo");
        var full = scope.Profile("full");

        Assert.Equal(["solo-developer"], solo.Agents.Select(agent => agent.Name).ToArray());
        Assert.Equal(
            ["orchestrator", "architect", "finops-explainer", "developer", "security-reviewer"],
            full.Agents.Select(agent => agent.Name).ToArray());
    }
}
