using IacAgent.Core;
using IacAgent.Plugins.Agents;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IacAgent.Plugins.Agents.Tests;

public sealed class AgentsServiceCollectionExtensionsTests
{
    [Fact]
    public void AddAgents_RegistersTrioProfile()
    {
        using var provider = CreateProvider(new ScriptedChatClient("""{"assumptions":[],"questions":[],"requirements":{"summary":"api"}}"""));
        var pipeline = Profile(provider, "trio");

        Assert.Equal("trio", pipeline.Name);
        Assert.Equal(["orchestrator", "developer", "security-reviewer"], pipeline.Agents.Select(agent => agent.Name).ToArray());
    }

    [Fact]
    public async Task Developer_InvalidJsonThenRepaired_ReturnsFiles()
    {
        var client = new ScriptedChatClient(
            "not json",
            """{"files":[{"path":"main.tf","content":"output \"hello\" { value = \"hello\" }\n"}]}""");
        using var provider = CreateProvider(client);
        var developer = Profile(provider, "trio").Agents.Single(agent => agent.Role == AgentRole.Developer);

        var result = await developer.RunAsync(new AgentContext("hello", true, """{"summary":"api"}"""), CancellationToken.None);

        Assert.False(result.Failed);
        var file = Assert.Single(result.Files!);
        Assert.Equal("main.tf", file.RelativePath);
        Assert.Contains("hello", file.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Orchestrator_AutoApprove_DoesNotAsk()
    {
        var interaction = new RecordingInteraction();
        var client = new ScriptedChatClient("""{"assumptions":["region is eu-central-1"],"questions":["Which region?"],"requirements":{"summary":"api"}}""");
        using var provider = CreateProvider(client, interaction);
        var orchestrator = Profile(provider, "trio").Agents.Single(agent => agent.Role == AgentRole.Orchestrator);

        var result = await orchestrator.RunAsync(new AgentContext("api", true), CancellationToken.None);

        Assert.Empty(interaction.Questions);
        Assert.Contains(result.Assumptions, assumption => assumption.Contains("Which region?", StringComparison.Ordinal));
        Assert.Contains("api", result.RequirementsJson, StringComparison.Ordinal);
    }

    [Fact]
    public void AddAgents_RegistersSoloAndFull()
    {
        using var provider = CreateProvider(new ScriptedChatClient("unused"));

        var solo = Profile(provider, "solo");
        var full = Profile(provider, "full");

        Assert.Equal(["solo-developer"], solo.Agents.Select(agent => agent.Name).ToArray());
        Assert.Equal(
            ["orchestrator", "architect", "finops-explainer", "developer", "security-reviewer"],
            full.Agents.Select(agent => agent.Name).ToArray());
    }

    [Fact]
    public async Task Orchestrator_AutoApproveNeedsHuman_StopsForAPerson()
    {
        var interaction = new RecordingInteraction();
        var client = new ScriptedChatClient("""{"assumptions":[],"questions":[],"needsHuman":true,"requirements":{"summary":"choose a database"}}""");
        using var provider = CreateProvider(client, interaction);
        var orchestrator = Profile(provider, "trio").Agents.Single(agent => agent.Role == AgentRole.Orchestrator);

        var result = await orchestrator.RunAsync(new AgentContext("store data", true), CancellationToken.None);

        Assert.Empty(interaction.Questions);
        Assert.True(result.NeedsHuman);
    }

    [Fact]
    public async Task Orchestrator_NotATerminal_AssumesTheDefault()
    {
        var interaction = new SilentInteraction();
        var client = new ScriptedChatClient("""{"assumptions":[],"questions":["Which region?"],"needsHuman":false,"requirements":{"summary":"api"}}""");
        using var provider = CreateProvider(client, interaction);
        var orchestrator = Profile(provider, "trio").Agents.Single(agent => agent.Role == AgentRole.Orchestrator);

        var result = await orchestrator.RunAsync(new AgentContext("api", false), CancellationToken.None);

        Assert.False(result.NeedsHuman);
        Assert.Contains(result.Assumptions, assumption => assumption.Contains("Which region?", StringComparison.Ordinal));
    }

    private static PipelineProfile Profile(ServiceProvider provider, string name)
    {
        return provider.GetServices<IProfileDefinition>().Single(definition => definition.Name == name).Build(provider);
    }

    private static ServiceProvider CreateProvider(IChatClient client, IUserInteraction? interaction = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IModelRouter>(new FixedRouter(client));
        services.AddSingleton(interaction ?? new RecordingInteraction());
        services.AddAgents(new ConfigurationBuilder().Build());
        return services.BuildServiceProvider();
    }

    private sealed class FixedRouter(IChatClient client) : IModelRouter
    {
        public IChatClient GetClient(ModelTier tier)
        {
            return client;
        }
    }

    private sealed class RecordingInteraction : IUserInteraction
    {
        public List<string> Questions { get; } = [];

        public Task<string?> AskAsync(string question, CancellationToken cancellationToken)
        {
            Questions.Add(question);
            return Task.FromResult<string?>("eu-central-1");
        }
    }

    private sealed class SilentInteraction : IUserInteraction
    {
        public Task<string?> AskAsync(string question, CancellationToken cancellationToken)
        {
            return Task.FromResult<string?>(null);
        }
    }

    private sealed class ScriptedChatClient(params string[] responses) : IChatClient
    {
        private readonly Queue<string> _responses = new(responses);

        public void Dispose()
        {
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
        {
            return null;
        }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, _responses.Dequeue()))
            {
                Usage = new UsageDetails { InputTokenCount = 2, OutputTokenCount = 3 },
            };
            return Task.FromResult(response);
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}
