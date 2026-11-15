using IacAgent.Cli;
using IacAgent.Core;
using IacAgent.Host;
using IacAgent.Host.Benchmark;
using IacAgent.Host.Configuration;
using IacAgent.Host.Pipeline;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace IacAgent.Host.Tests.Benchmark;

public sealed class BenchmarkCommandTests
{
    private const string Suite = """
        {
          "name": "tiny",
          "repeat": 2,
          "profiles": ["solo"],
          "models": [{ "strong": "org/model", "cheap": "org/cheap" }],
          "temperatures": [0],
          "security": ["baseline"],
          "tasks": [{ "id": "hello", "prompt": "hello", "budget": 10 }]
        }
        """;

    [Fact]
    public async Task RunAsync_TwoRepeats_WritesOneRowPerRun()
    {
        try
        {
            var agent = new FileAgent();
            var requests = new List<GenerateRequest>();
            var root = await BenchmarkCommand.RunAsync(Suite, null, null, null, null, false, "test", (request, budget, token) =>
            {
                requests.Add(request);
                Assert.Equal(10m, budget);
                return Pipeline(agent).RunAsync(request, token);
            }, CancellationToken.None);

            var lines = ReadCsv(root);
            Assert.Equal(3, lines.Length);
            Assert.Equal(2, requests.Count);
            Assert.True(requests.TrueForAll(request => request.AutoApprove));
            Assert.Contains("org-model--org-cheap", requests[0].OutputDirectory, StringComparison.Ordinal);
            Assert.Equal("Succeeded", Field(lines[1], 7));
            Assert.Equal("Succeeded", Field(lines[2], 7));
        }
        finally
        {
            DeleteBench();
        }
    }

    [Fact]
    public async Task RunAsync_Resume_SkipsExistingRunJson()
    {
        var agent = new FileAgent();
        try
        {
            var root = await BenchmarkCommand.RunAsync(Suite, null, null, null, null, false, "test", (request, _, token) => Pipeline(agent).RunAsync(request, token), CancellationToken.None);
            var repeatTwo = Directory.GetDirectories(root, "2", SearchOption.AllDirectories).Single();
            Directory.Delete(repeatTwo, true);
            var calls = agent.Calls;

            await BenchmarkCommand.RunAsync(Suite, root, null, null, null, false, "test", (request, _, token) => Pipeline(agent).RunAsync(request, token), CancellationToken.None);

            Assert.Equal(calls + 1, agent.Calls);
            Assert.Equal(3, ReadCsv(root).Length);
        }
        finally
        {
            DeleteBench();
        }
    }

    [Fact]
    public async Task RunAsync_RunThrows_ContinuesAndRecordsTheRow()
    {
        var agent = new ThrowingThenFileAgent();
        try
        {
            var root = await BenchmarkCommand.RunAsync(Suite, null, null, null, null, false, "test", (request, _, token) => Pipeline(agent).RunAsync(request, token), CancellationToken.None);
            var lines = ReadCsv(root);

            Assert.Equal("Error", Field(lines[1], 7));
            Assert.Equal("Succeeded", Field(lines[2], 7));
        }
        finally
        {
            DeleteBench();
        }
    }

    [Fact]
    public void Load_RepeatZero_Throws()
    {
        var error = Assert.Throws<InvalidOperationException>(() => BenchmarkSuite.Load("""{"name":"tiny","repeat":0}"""));

        Assert.Contains("repeat", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Bench_MissingSuite_ReturnsConfigError()
    {
        var code = await Application.RunAsync(["bench", "--suite", Path.Combine(Path.GetTempPath(), "missing-suite.json")]);

        Assert.Equal(ExitCodes.ConfigError, code);
    }

    private static PipelineRunner Pipeline(IAgent agent)
    {
        var services = new ServiceCollection().BuildServiceProvider();
        return new PipelineRunner(
            new ProfileCatalog([new ScriptedProfile(agent)]),
            [new PassingGate()],
            services,
            NullLogger<PipelineRunner>.Instance,
            new IdleRouter(),
            [],
            new QuietProcessRunner());
    }

    private static string[] ReadCsv(string root)
    {
        return File.ReadAllLines(Path.Combine(root, "results.csv")).Where(line => line.Length > 0).ToArray();
    }

    private static string Field(string line, int index)
    {
        return line.Split(',')[index];
    }

    private static void DeleteBench()
    {
        var bench = Path.Combine(Directory.GetCurrentDirectory(), "bench");
        if (Directory.Exists(bench))
        {
            Directory.Delete(bench, true);
        }
    }

    private sealed class ScriptedProfile(IAgent agent) : IProfileDefinition
    {
        public string Name => "solo";

        public PipelineProfile Build(IServiceProvider services)
        {
            return new PipelineProfile(Name, [agent], new LoopPolicy());
        }
    }

    private sealed class FileAgent : IAgent
    {
        public int Calls { get; private set; }

        public string Name => "solo";

        public AgentRole Role => AgentRole.Developer;

        public Task<AgentResult> RunAsync(AgentContext context, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new AgentResult([], Files: [new GeneratedFile("main.tf", "ok\n")]));
        }
    }

    private sealed class ThrowingThenFileAgent : IAgent
    {
        private int _calls;

        public string Name => "solo";

        public AgentRole Role => AgentRole.Developer;

        public Task<AgentResult> RunAsync(AgentContext context, CancellationToken cancellationToken)
        {
            _calls++;
            if (_calls == 1)
            {
                throw new InvalidOperationException("boom");
            }

            return Task.FromResult(new AgentResult([], Files: [new GeneratedFile("main.tf", "ok\n")]));
        }
    }

    private sealed class PassingGate : IGate
    {
        public string Name => "passing";

        public int Order => 1;

        public Task<GateResult> CheckAsync(Artifact artifact, CancellationToken cancellationToken)
        {
            return Task.FromResult(new GateResult(Name, true, []));
        }
    }

    private sealed class IdleRouter : IModelRouter
    {
        public Microsoft.Extensions.AI.IChatClient GetClient(ModelTier tier)
        {
            throw new NotSupportedException();
        }

        public string GetModelId(ModelTier tier)
        {
            return "test-model";
        }

        public Microsoft.Extensions.AI.ChatOptions CreateChatOptions()
        {
            return new Microsoft.Extensions.AI.ChatOptions();
        }

        public decimal? EstimateCostUsd(string modelId, int inputTokens, int outputTokens)
        {
            return null;
        }
    }
}
