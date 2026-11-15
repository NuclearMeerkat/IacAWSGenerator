using IacAgent.Core;

namespace IacAgent.Plugins.Agents.Tests.Trio;

public sealed class DeveloperAgentTests
{
    [Fact]
    public async Task RunAsync_InvalidJsonThenRepaired_ReturnsFiles()
    {
        var client = new ScriptedChatClient(
            "not json",
            """{"files":[{"path":"main.tf","content":"output \"hello\" { value = \"hello\" }\n"}]}""");
        using var scope = AgentTestHost.Open(client);
        var developer = scope.Profile("trio").Agents.Single(agent => agent.Role == AgentRole.Developer);

        var result = await developer.RunAsync(new AgentContext("hello", true, """{"summary":"api"}"""), CancellationToken.None);

        Assert.False(result.Failed);
        var file = Assert.Single(result.Files!);
        Assert.Equal("main.tf", file.RelativePath);
        Assert.Contains("hello", file.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_RepairMessage_OmitsAdvisoryIssues()
    {
        var client = new ScriptedChatClient("""{"files":[{"path":"main.tf","content":"ok\n"}]}""");
        using var scope = AgentTestHost.Open(client);
        var developer = scope.Profile("trio").Agents.Single(agent => agent.Role == AgentRole.Developer);
        var results = new[]
        {
            new GateResult("checkov", false,
            [
                new GateIssue("main.tf", 1, "CKV_AWS_24", "UNKNOWN", "ssh", true),
                new GateIssue("main.tf", 2, "CKV2_AWS_5", "UNKNOWN", "unattached", false),
            ]),
        };

        await developer.RunAsync(new AgentContext("hello", true, """{"summary":"api"}""", null, results), CancellationToken.None);

        Assert.Contains("CKV_AWS_24", client.LastUser, StringComparison.Ordinal);
        Assert.DoesNotContain("CKV2_AWS_5", client.LastUser, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_RepairMessage_ListsOtherFilesAndDesignNotes()
    {
        var client = new ScriptedChatClient("""{"files":[{"path":"main.tf","content":"ok\n"}]}""");
        using var scope = AgentTestHost.Open(client);
        var developer = scope.Profile("trio").Agents.Single(agent => agent.Role == AgentRole.Developer);
        var context = new AgentContext(
            "hello",
            true,
            """{"summary":"api"}""",
            new Artifact("out", [new GeneratedFile("main.tf", "resource \"aws_s3_bucket\" \"bad\" {}\n")]),
            [new GateResult("validate", false, [new GateIssue("main.tf", 1, "terraform-validate", "error", "broken")])],
            ["variables.tf: aws_s3_bucket.data"],
            "Use one bucket.");

        await developer.RunAsync(context, CancellationToken.None);

        var notes = client.LastUser!.IndexOf("Design notes:", StringComparison.Ordinal);
        var requirements = client.LastUser.IndexOf("\"summary\"", StringComparison.Ordinal);
        var other = client.LastUser.IndexOf("Other files:", StringComparison.Ordinal);
        Assert.True(requirements >= 0 && requirements < notes);
        Assert.True(notes < other);
        Assert.Contains("Use one bucket.", client.LastUser, StringComparison.Ordinal);
        Assert.Contains("variables.tf: aws_s3_bucket.data", client.LastUser, StringComparison.Ordinal);
        Assert.DoesNotContain("File variables.tf:", client.LastUser, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_Temperature_ReachesTheChatClient()
    {
        var client = new ScriptedChatClient("""{"files":[{"path":"main.tf","content":"ok\n"}]}""");
        using var scope = AgentTestHost.Open(client, temperature: 0.5f);
        var developer = scope.Profile("trio").Agents.Single(agent => agent.Role == AgentRole.Developer);

        await developer.RunAsync(new AgentContext("hello", true), CancellationToken.None);

        Assert.Equal(0.5f, client.LastOptions?.Temperature);
    }

    [Fact]
    public async Task RunAsync_TemperatureNone_SendsNull()
    {
        var client = new ScriptedChatClient("""{"files":[{"path":"main.tf","content":"ok\n"}]}""");
        using var scope = AgentTestHost.Open(client);
        var developer = scope.Profile("trio").Agents.Single(agent => agent.Role == AgentRole.Developer);

        await developer.RunAsync(new AgentContext("hello", true), CancellationToken.None);

        Assert.Null(client.LastOptions?.Temperature);
    }

    [Fact]
    public async Task RunAsync_PriceTable_FillsCostWhenTheProviderOmitsIt()
    {
        var client = new ScriptedChatClient("""{"files":[{"path":"main.tf","content":"ok\n"}]}""");
        using var scope = AgentTestHost.Open(client, estimatedCost: 0.42m);
        var developer = scope.Profile("trio").Agents.Single(agent => agent.Role == AgentRole.Developer);

        var result = await developer.RunAsync(new AgentContext("hello", true), CancellationToken.None);

        Assert.Equal(0.42m, result.Tokens!.CostUsd);
    }

    [Fact]
    public async Task RunAsync_ReportedCost_WinsOverThePriceTable()
    {
        var client = new ScriptedChatClient("""{"files":[{"path":"main.tf","content":"ok\n"}]}""")
        {
            ReportedCost = 2,
        };
        using var scope = AgentTestHost.Open(client, estimatedCost: 0.42m);
        var developer = scope.Profile("trio").Agents.Single(agent => agent.Role == AgentRole.Developer);

        var result = await developer.RunAsync(new AgentContext("hello", true), CancellationToken.None);

        Assert.Equal(2m, result.Tokens!.CostUsd);
    }
}
