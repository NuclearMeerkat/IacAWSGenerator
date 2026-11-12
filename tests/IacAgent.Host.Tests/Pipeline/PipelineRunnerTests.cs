using System.Text.Json;

using IacAgent.Cli;
using IacAgent.Core;
using IacAgent.Host;
using IacAgent.Host.Configuration;
using IacAgent.Host.Pipeline;
using IacAgent.Plugins.Models;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace IacAgent.Host.Tests.Pipeline;

public sealed class PipelineRunnerTests
{
    [Fact]
    public async Task RunAsync_KnownProfile_WritesSucceededRunJson()
    {
        var directory = Directory.CreateTempSubdirectory("iacagent-").FullName;
        try
        {
            var runner = CreateRunner(new RecordingAgent(["region stays at the account default"]));
            var result = await runner.RunAsync(Request(directory, endpoint: null), CancellationToken.None);
            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "run.json")));
            var root = json.RootElement;

            Assert.Equal(RunOutcome.Succeeded, result.Outcome);
            Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
            Assert.Equal("trio", root.GetProperty("profile").GetString());
            Assert.Equal("hello", root.GetProperty("prompt").GetString());
            Assert.Equal("Succeeded", root.GetProperty("outcome").GetString());
            Assert.Equal("openrouter", root.GetProperty("models").GetProperty("provider").GetString());
            Assert.Equal("test-model", root.GetProperty("models").GetProperty("strong").GetString());
            Assert.Equal(0, root.GetProperty("tokens").GetProperty("input").GetInt32());
            Assert.Equal("region stays at the account default", root.GetProperty("assumptions")[0].GetString());
            Assert.Empty(root.GetProperty("iterations")[0].GetProperty("gates").EnumerateArray());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_EndpointSet_RecordsLocalProvider()
    {
        var directory = Directory.CreateTempSubdirectory("iacagent-").FullName;
        try
        {
            var runner = CreateRunner(new RecordingAgent([]));
            await runner.RunAsync(Request(directory, "http://127.0.0.1:11434"), CancellationToken.None);
            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "run.json")));

            Assert.Equal("local", json.RootElement.GetProperty("models").GetProperty("provider").GetString());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_UnknownProfile_WritesConfigError()
    {
        var directory = Directory.CreateTempSubdirectory("iacagent-").FullName;
        try
        {
            var runner = CreateRunner(new RecordingAgent([]));
            var result = await runner.RunAsync(Request(directory, null) with { Profile = "solo" }, CancellationToken.None);
            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "run.json")));

            Assert.Equal(RunOutcome.ConfigError, result.Outcome);
            Assert.Equal("ConfigError", json.RootElement.GetProperty("outcome").GetString());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_Gates_RunInAscendingOrder()
    {
        var directory = Directory.CreateTempSubdirectory("iacagent-").FullName;
        try
        {
            var gates = new IGate[]
            {
                new NamedGate("later", 20, true),
                new NamedGate("earlier", 10, false),
            };
            var runner = CreateRunner(new RecordingAgent([]), gates);
            var result = await runner.RunAsync(Request(directory, null), CancellationToken.None);
            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "run.json")));
            var names = json.RootElement.GetProperty("iterations")[0].GetProperty("gates").EnumerateArray()
                .Select(gate => gate.GetProperty("name").GetString() ?? string.Empty)
                .ToArray();

            Assert.Equal(RunOutcome.GateFailed, result.Outcome);
            Assert.Equal(["earlier", "later"], names);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Generate_StubProfile_DoesNotRequireApiKeyInRunJson()
    {
        var directory = Directory.CreateTempSubdirectory("iacagent-").FullName;
        try
        {
            var code = await Application.RunAsync(
                ["generate", "hello from cli", "--profile", "missing", "--out", directory, "--model-strong", "cli-model"]);
            var json = await File.ReadAllTextAsync(Path.Combine(directory, "run.json"));
            var key = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");

            Assert.Equal(ExitCodes.ConfigError, code);
            Assert.Contains("\"outcome\": \"ConfigError\"", json, StringComparison.Ordinal);
            Assert.Contains("\"strong\": \"cli-model\"", json, StringComparison.Ordinal);
            Assert.Contains("hello from cli", json, StringComparison.Ordinal);
            if (!string.IsNullOrEmpty(key))
            {
                Assert.True(!json.Contains(key, StringComparison.Ordinal), "run.json contains the API key.");
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void GetModelId_EmptyCheap_UsesStrongModel()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new ModelConnection("strong-id", null, null));
        services.AddModels(new ConfigurationBuilder().Build());

        var id = services.BuildServiceProvider().GetRequiredService<IModelRouter>().GetModelId(ModelTier.Cheap);

        Assert.Equal("strong-id", id);
    }

    [Fact]
    public void For_GateFailed_Returns10()
    {
        Assert.Equal(10, ExitCodes.For(RunOutcome.GateFailed));
        Assert.Equal(11, ExitCodes.For(RunOutcome.BudgetExceeded));
        Assert.Equal(12, ExitCodes.For(RunOutcome.NeedsHuman));
        Assert.Equal(20, ExitCodes.For(RunOutcome.ConfigError));
    }

    private static PipelineRunner CreateRunner(IAgent agent, IReadOnlyList<IGate>? gates = null)
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var catalog = new ProfileCatalog([new FakeProfile("trio", agent)]);
        return new PipelineRunner(catalog, gates ?? [], services, NullLogger<PipelineRunner>.Instance);
    }

    private static GenerateRequest Request(string directory, string? endpoint)
    {
        return new GenerateRequest("hello", "trio", directory, "test-model", null, endpoint, 3, true);
    }

    private sealed class FakeProfile(string name, IAgent agent) : IProfileDefinition
    {
        public string Name => name;

        public PipelineProfile Build(IServiceProvider services)
        {
            return new PipelineProfile(Name, [agent], new LoopPolicy());
        }
    }

    private sealed class RecordingAgent(IReadOnlyList<string> assumptions) : IAgent
    {
        public string Name => "recording";

        public AgentRole Role => AgentRole.Developer;

        public Task<AgentResult> RunAsync(AgentContext context, CancellationToken cancellationToken)
        {
            return Task.FromResult(new AgentResult(assumptions));
        }
    }

    private sealed class NamedGate(string name, int order, bool passed) : IGate
    {
        public string Name => name;

        public int Order => order;

        public Task<GateResult> CheckAsync(Artifact artifact, CancellationToken cancellationToken)
        {
            var issues = passed ? Array.Empty<GateIssue>() : new[] { new GateIssue("main.tf", 1, "rule", "HIGH", "bad") };
            return Task.FromResult(new GateResult(name, passed, issues));
        }
    }
}
