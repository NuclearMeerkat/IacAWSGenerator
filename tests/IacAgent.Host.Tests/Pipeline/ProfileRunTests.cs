using System.Text.Json;

using IacAgent.Cli;
using IacAgent.Core;
using IacAgent.Host;
using IacAgent.Host.Configuration;
using IacAgent.Host.Pipeline;
using IacAgent.Plugins.Agents;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IacAgent.Host.Tests.Pipeline;

public sealed class ProfileRunTests
{
    private const string Requirements = """{"assumptions":["region is eu-central-1"],"questions":[],"needsHuman":false,"requirements":{"summary":"api"}}""";
    private const string Files = """{"files":[{"path":"main.tf","content":"output \"ok\" { value = \"ok\" }\n"}]}""";

    [Fact]
    public async Task RunAsync_ThreeProfiles_WriteComparableRunJson()
    {
        string[]? keys = null;
        foreach (var profile in new[] { "solo", "trio", "full" })
        {
            var directory = Directory.CreateTempSubdirectory("iacagent-").FullName;
            try
            {
                var runner = CreateRunner(profile);
                var result = await runner.RunAsync(Request(directory, profile), CancellationToken.None);
                using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "run.json")));
                var names = json.RootElement.EnumerateObject().Select(property => property.Name).ToArray();

                Assert.Equal(RunOutcome.Succeeded, result.Outcome);
                Assert.Equal(profile, json.RootElement.GetProperty("profile").GetString());
                Assert.Equal("hello", json.RootElement.GetProperty("prompt").GetString());
                Assert.Equal("Succeeded", json.RootElement.GetProperty("outcome").GetString());
                if (keys is null)
                {
                    keys = names;
                }
                else
                {
                    Assert.Equal(keys, names);
                }
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task RunAsync_NeedsHuman_StopsBeforeWritingTerraform()
    {
        var directory = Directory.CreateTempSubdirectory("iacagent-").FullName;
        try
        {
            var services = new ServiceCollection().BuildServiceProvider();
            var runner = new PipelineRunner(
                new ProfileCatalog([new HumanProfile()]),
                [],
                services,
                NullLogger<PipelineRunner>.Instance);

            var result = await runner.RunAsync(Request(directory, "human"), CancellationToken.None);

            Assert.Equal(RunOutcome.NeedsHuman, result.Outcome);
            Assert.Equal(ExitCodes.NeedsHuman, ExitCodes.For(result.Outcome));
            Assert.False(File.Exists(Path.Combine(directory, "main.tf")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static PipelineRunner CreateRunner(string profile)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IModelRouter>(new FixedRouter(new ScriptedChatClient(Replies(profile))));
        services.AddSingleton<IUserInteraction, SilentInteraction>();
        services.AddAgents(new ConfigurationBuilder().Build());
        services.AddHost(new ConfigurationBuilder().Build());
        return services.BuildServiceProvider().GetRequiredService<PipelineRunner>();
    }

    private static string[] Replies(string profile)
    {
        return profile switch
        {
            "solo" => [Files],
            "trio" => [Requirements, Files],
            _ => [Requirements, "One HTTP API and one Lambda.", "The Lambda is the only priced resource.", Files],
        };
    }

    private static GenerateRequest Request(string directory, string profile)
    {
        return new GenerateRequest("hello", profile, directory, "test-model", null, null, 1, true);
    }

    private sealed class HumanProfile : IProfileDefinition
    {
        public string Name => "human";

        public PipelineProfile Build(IServiceProvider services)
        {
            return new PipelineProfile(Name, [new HumanAgent(), new FailIfCalledAgent()], new LoopPolicy());
        }
    }

    private sealed class HumanAgent : IAgent
    {
        public string Name => "human";

        public AgentRole Role => AgentRole.Orchestrator;

        public Task<AgentResult> RunAsync(AgentContext context, CancellationToken cancellationToken)
        {
            return Task.FromResult(new AgentResult(["need a region"], NeedsHuman: true));
        }
    }

    private sealed class FailIfCalledAgent : IAgent
    {
        public string Name => "developer";

        public AgentRole Role => AgentRole.Developer;

        public Task<AgentResult> RunAsync(AgentContext context, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("The developer ran after a human decision was required.");
        }
    }

    private sealed class FixedRouter(IChatClient client) : IModelRouter
    {
        public IChatClient GetClient(ModelTier tier)
        {
            return client;
        }

        public string GetModelId(ModelTier tier)
        {
            return "test-model";
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
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, _responses.Dequeue())));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}
