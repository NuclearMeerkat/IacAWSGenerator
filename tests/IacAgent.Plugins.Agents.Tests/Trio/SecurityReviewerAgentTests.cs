using IacAgent.Core;

namespace IacAgent.Plugins.Agents.Tests.Trio;

public sealed class SecurityReviewerAgentTests
{
    [Fact]
    public async Task RunAsync_ReceivesOnlyAdvisoryIssues()
    {
        var client = new ScriptedChatClient("notes");
        using var scope = AgentTestHost.Open(client);
        var reviewer = scope.Profile("trio").Agents.Single(agent => agent.Role == AgentRole.Reviewer);
        var results = new[]
        {
            new GateResult("checkov", false,
            [
                new GateIssue("main.tf", 1, "CKV_AWS_24", "UNKNOWN", "ssh", true),
                new GateIssue("main.tf", 2, "CKV2_AWS_5", "UNKNOWN", "unattached", false),
            ]),
        };

        await reviewer.RunAsync(new AgentContext("hello", true, null, null, results), CancellationToken.None);

        Assert.Contains("CKV2_AWS_5", client.LastUser, StringComparison.Ordinal);
        Assert.DoesNotContain("CKV_AWS_24", client.LastUser, StringComparison.Ordinal);
        Assert.Equal(ModelTier.Cheap, scope.Router.LastTier);
    }

    [Fact]
    public async Task RunAsync_NoAdvisoryIssues_DoesNotCallTheModel()
    {
        var client = new ScriptedChatClient("unused");
        using var scope = AgentTestHost.Open(client);
        var reviewer = scope.Profile("trio").Agents.Single(agent => agent.Role == AgentRole.Reviewer);
        var results = new[]
        {
            new GateResult("checkov", false, [new GateIssue("main.tf", 1, "CKV_AWS_24", "UNKNOWN", "ssh", true)]),
        };

        var result = await reviewer.RunAsync(new AgentContext("hello", true, null, null, results), CancellationToken.None);

        Assert.Null(client.LastUser);
        Assert.Null(result.Review);
    }
}
