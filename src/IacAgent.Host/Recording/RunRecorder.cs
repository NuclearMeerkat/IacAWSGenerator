using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

using IacAgent.Core;
using IacAgent.Host.Configuration;

namespace IacAgent.Host.Recording;

/// <summary>Writes run.json for a single generate invocation.</summary>
public sealed class RunRecorder : IRunRecorder
{
    private readonly GenerateRequest _request;
    private readonly string _runId;
    private readonly DateTime _startedAtUtc;
    private readonly Stopwatch _stopwatch;
    private readonly List<RunIterationDocument> _iterations = [];
    private readonly List<string> _assumptions = [];
    private int _inputTokens;
    private int _outputTokens;

    /// <summary>Starts timing a run.</summary>
    public RunRecorder(GenerateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        _request = request;
        _runId = Guid.NewGuid().ToString("D");
        _startedAtUtc = DateTime.UtcNow;
        _stopwatch = Stopwatch.StartNew();
    }

    /// <summary>Adds assumptions collected from agents.</summary>
    public void AddAssumptions(IEnumerable<string> assumptions)
    {
        ArgumentNullException.ThrowIfNull(assumptions);
        _assumptions.AddRange(assumptions);
    }

    /// <inheritdoc />
    public void RecordIteration(IterationRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        _inputTokens += record.Tokens.Input;
        _outputTokens += record.Tokens.Output;
        _iterations.Add(new RunIterationDocument
        {
            Index = record.Index,
            Gates = record.Gates.Select(gate => new RunGateDocument
            {
                Name = gate.Name,
                Passed = gate.Passed,
                IssueCount = gate.IssueCount,
            }).ToList(),
            Tokens = new RunTokensDocument
            {
                Input = record.Tokens.Input,
                Output = record.Tokens.Output,
            },
        });
    }

    /// <inheritdoc />
    public async Task WriteAsync(string outputDirectory, RunOutcome outcome, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        _stopwatch.Stop();
        var document = CreateDocument(outcome);
        Directory.CreateDirectory(outputDirectory);
        var json = JsonSerializer.Serialize(document, RunJsonContext.Default.RunDocument);
        var path = Path.Combine(outputDirectory, "run.json");
        await File.WriteAllTextAsync(path, json, cancellationToken).ConfigureAwait(false);
    }

    private RunDocument CreateDocument(RunOutcome outcome)
    {
        return new RunDocument
        {
            SchemaVersion = 1,
            RunId = _runId,
            StartedAtUtc = _startedAtUtc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
            DurationMs = _stopwatch.ElapsedMilliseconds,
            Profile = _request.Profile,
            Prompt = _request.Prompt,
            Models = new RunModelsDocument
            {
                Strong = _request.ModelStrong ?? string.Empty,
                Cheap = _request.ModelCheap ?? string.Empty,
                Provider = string.IsNullOrWhiteSpace(_request.Endpoint) ? "openrouter" : "local",
            },
            Tokens = new RunTokensDocument
            {
                Input = _inputTokens,
                Output = _outputTokens,
            },
            Iterations = _iterations,
            Assumptions = _assumptions,
            Outcome = outcome.ToString(),
        };
    }
}
