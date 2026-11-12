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
    private readonly IReadOnlyList<IRedactionRule> _rules;
    private readonly string? _strongModel;
    private readonly string? _cheapModel;
    private readonly string _runId;
    private readonly DateTime _startedAtUtc;
    private readonly Stopwatch _stopwatch;
    private readonly List<RunIterationDocument> _iterations = [];
    private readonly List<string> _assumptions = [];
    private int _inputTokens;
    private int _outputTokens;

    /// <summary>Starts timing a run.</summary>
    public RunRecorder(GenerateRequest request, IEnumerable<IRedactionRule>? rules = null, string? strongModel = null, string? cheapModel = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        _request = request;
        _rules = rules?.ToList() ?? [];
        _strongModel = strongModel;
        _cheapModel = cheapModel;
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

    /// <summary>Adds tokens from a call that happened after the iteration was recorded.</summary>
    public void Include(TokenCount tokens)
    {
        if (tokens.Input == 0 && tokens.Output == 0)
        {
            return;
        }

        _inputTokens += tokens.Input;
        _outputTokens += tokens.Output;
        if (_iterations.Count == 0)
        {
            return;
        }

        var last = _iterations[^1];
        _iterations[^1] = new RunIterationDocument
        {
            Index = last.Index,
            Gates = last.Gates,
            Tokens = new RunTokensDocument
            {
                Input = last.Tokens.Input + tokens.Input,
                Output = last.Tokens.Output + tokens.Output,
            },
        };
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
            Prompt = Redact(_request.Prompt),
            Models = new RunModelsDocument
            {
                Strong = string.IsNullOrWhiteSpace(_strongModel) ? _request.ModelStrong ?? string.Empty : _strongModel,
                Cheap = string.IsNullOrWhiteSpace(_cheapModel) ? _request.ModelCheap ?? string.Empty : _cheapModel,
                Provider = string.IsNullOrWhiteSpace(_request.Endpoint) ? "openrouter" : "local",
            },
            Tokens = new RunTokensDocument
            {
                Input = _inputTokens,
                Output = _outputTokens,
            },
            Iterations = _iterations,
            Assumptions = _assumptions.Select(Redact).ToList(),
            Outcome = outcome.ToString(),
        };
    }

    private string Redact(string text)
    {
        foreach (var rule in _rules)
        {
            text = rule.Redact(text);
        }

        return text;
    }
}
