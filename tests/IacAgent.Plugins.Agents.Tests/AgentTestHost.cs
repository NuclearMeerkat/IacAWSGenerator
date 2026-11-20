using IacAgent.Core;
using IacAgent.Plugins.Agents;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IacAgent.Plugins.Agents.Tests;

internal static class AgentTestHost
{
    public static AgentTestScope Open(
        IChatClient client,
        IUserInteraction? interaction = null,
        float? temperature = null,
        decimal? estimatedCost = null,
        IEnumerable<IRedactionRule>? rules = null)
    {
        var router = new FixedRouter(client, temperature, estimatedCost);
        var services = new ServiceCollection();
        services.AddSingleton<IModelRouter>(router);
        services.AddSingleton(interaction ?? new RecordingInteraction());
        foreach (var rule in rules ?? [])
        {
            services.AddSingleton(rule);
        }

        services.AddAgents(new ConfigurationBuilder().Build());
        return new AgentTestScope(services.BuildServiceProvider(), router);
    }
}

internal sealed class AgentTestScope(ServiceProvider provider, FixedRouter router) : IDisposable
{
    public FixedRouter Router { get; } = router;

    public PipelineProfile Profile(string name)
    {
        return provider.GetServices<IProfileDefinition>().Single(definition => definition.Name == name).Build(provider);
    }

    public void Dispose()
    {
        provider.Dispose();
    }
}

internal sealed class FixedRouter(IChatClient client, float? temperature = null, decimal? estimatedCost = null) : IModelRouter
{
    public ModelTier? LastTier { get; private set; }

    public IChatClient GetClient(ModelTier tier)
    {
        LastTier = tier;
        return client;
    }

    public string GetModelId(ModelTier tier)
    {
        return "test-model";
    }

    public ChatOptions CreateChatOptions()
    {
        return new ChatOptions { Temperature = temperature };
    }

    public decimal? EstimateCostUsd(string modelId, int inputTokens, int outputTokens)
    {
        return estimatedCost;
    }
}

internal sealed class RecordingInteraction : IUserInteraction
{
    public List<string> Questions { get; } = [];

    public Task<string?> AskAsync(string question, CancellationToken cancellationToken)
    {
        Questions.Add(question);
        return Task.FromResult<string?>("eu-central-1");
    }
}

internal sealed class SilentInteraction : IUserInteraction
{
    public Task<string?> AskAsync(string question, CancellationToken cancellationToken)
    {
        return Task.FromResult<string?>(null);
    }
}

internal sealed class ScriptedChatClient(params string[] responses) : IChatClient
{
    private readonly Queue<string> _responses = new(responses);

    public void Dispose()
    {
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        return null;
    }

    public string? LastUser { get; private set; }

    public List<string> Users { get; } = [];

    public int Calls { get; private set; }

    public ChatOptions? LastOptions { get; private set; }

    public long? ReportedCost { get; set; }

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        Calls++;
        LastUser = messages.Last().Text;
        Users.Add(LastUser);
        LastOptions = options;
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, _responses.Dequeue()))
        {
            Usage = new UsageDetails
            {
                InputTokenCount = 2,
                OutputTokenCount = 3,
                AdditionalCounts = ReportedCost is { } cost
                    ? new AdditionalPropertiesDictionary<long>(new Dictionary<string, long> { ["cost"] = cost })
                    : null,
            },
        };
        return Task.FromResult(response);
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }
}
