using IacAgent.Core;

namespace IacAgent.Host.Pipeline;

/// <summary>The outcome of one pipeline run.</summary>
public sealed record PipelineResult(RunOutcome Outcome, string OutputDirectory);
