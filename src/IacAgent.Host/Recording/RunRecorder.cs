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
    private readonly string _security;
    private readonly string _runId;

    /// <summary>Gets the id used for run.json and the temporary workspace.</summary>
    public string RunId => _runId;
    private readonly DateTime _startedAtUtc;
    private readonly Stopwatch _stopwatch;
    private readonly List<RunIterationDocument> _iterations = [];
    private readonly List<string> _assumptions = [];
    private List<string> _files = [];
    private TerraformResourceInventory.Result _resources = new(0, new Dictionary<string, int>());
    private int _inputTokens;
    private int _outputTokens;
    private decimal? _costUsd;
    private string _terraform = string.Empty;
    private string _checkov = string.Empty;
    private string? _error;

    /// <summary>Starts timing a run.</summary>
    public RunRecorder(GenerateRequest request, IEnumerable<IRedactionRule> rules, string strongModel, string cheapModel, string security)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(rules);
        _request = request;
        _rules = rules.ToList();
        _strongModel = strongModel;
        _cheapModel = cheapModel;
        _security = string.IsNullOrWhiteSpace(security) ? "baseline" : security;
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
                BlockingCount = gate.BlockingCount,
                DurationMs = gate.DurationMs,
            }).ToList(),
            Tokens = new RunTokensDocument
            {
                Input = record.Tokens.Input,
                Output = record.Tokens.Output,
            },
            BlockingIssues = record.Gates.Sum(gate => gate.BlockingCount),
            AdvisoryIssues = record.Gates.Sum(gate => gate.IssueCount - gate.BlockingCount),
            CostUsd = record.Tokens.CostUsd,
        });
        _costUsd = SumCost(_costUsd, record.Tokens.CostUsd);
    }

    /// <summary>Adds tokens from a call that happened after the iteration was recorded.</summary>
    public void Include(TokenCount tokens)
    {
        if (tokens.Input == 0 && tokens.Output == 0 && tokens.CostUsd is null)
        {
            return;
        }

        _inputTokens += tokens.Input;
        _outputTokens += tokens.Output;
        _costUsd = SumCost(_costUsd, tokens.CostUsd);
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
            BlockingIssues = last.BlockingIssues,
            AdvisoryIssues = last.AdvisoryIssues,
            CostUsd = SumCost(last.CostUsd, tokens.CostUsd),
        };
    }

    /// <summary>Records the relative paths this run wrote.</summary>
    public void SetFiles(IEnumerable<string> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        _files = files.Select(file => file.Replace('\\', '/').TrimStart('/')).ToList();
    }

    /// <summary>Records the resource count for the final files.</summary>
    internal void SetResources(TerraformResourceInventory.Result resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        _resources = resources;
    }

    /// <summary>Records terraform and checkov versions for this run.</summary>
    public void SetTools(string terraform, string checkov)
    {
        _terraform = terraform;
        _checkov = checkov;
    }

    /// <summary>Stores the first line of a failure. Secrets are removed when the file is written.</summary>
    public void SetError(string? error)
    {
        _error = error;
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
            SchemaVersion = 2,
            RunId = _runId,
            StartedAtUtc = _startedAtUtc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
            DurationMs = _stopwatch.ElapsedMilliseconds,
            Profile = _request.Profile,
            Prompt = Redact(_request.Prompt),
            Security = _security,
            ToolVersion = _request.ToolVersion,
            Tools = new RunToolsDocument { Terraform = _terraform, Checkov = _checkov },
            Models = new RunModelsDocument
            {
                Strong = string.IsNullOrWhiteSpace(_strongModel) ? _request.ModelStrong ?? string.Empty : _strongModel,
                Cheap = string.IsNullOrWhiteSpace(_cheapModel) ? _request.ModelCheap ?? string.Empty : _cheapModel,
                Provider = string.IsNullOrWhiteSpace(_request.Endpoint) ? "openrouter" : "local",
                Temperature = _request.Temperature,
            },
            Tokens = new RunTokensDocument
            {
                Input = _inputTokens,
                Output = _outputTokens,
            },
            Iterations = _iterations,
            Files = _files,
            Resources = new RunResourcesDocument
            {
                Count = _resources.Count,
                Types = _resources.Types.ToDictionary(pair => pair.Key, pair => pair.Value),
            },
            Assumptions = _assumptions.Select(Redact).ToList(),
            Outcome = outcome.ToString(),
            CostUsd = _costUsd,
            Error = _error is null ? null : Redact(_error),
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

    private static decimal? SumCost(decimal? left, decimal? right)
    {
        if (left is null && right is null)
        {
            return null;
        }

        return (left ?? 0m) + (right ?? 0m);
    }
}
