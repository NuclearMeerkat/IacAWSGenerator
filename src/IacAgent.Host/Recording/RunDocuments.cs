namespace IacAgent.Host.Recording;

internal sealed class RunTokensDocument
{
    public int Input { get; init; }

    public int Output { get; init; }
}

internal sealed class RunGateDocument
{
    public required string Name { get; init; }

    public bool Passed { get; init; }

    public int IssueCount { get; init; }
}

internal sealed class RunIterationDocument
{
    public int Index { get; init; }

    public required IReadOnlyList<RunGateDocument> Gates { get; init; }

    public required RunTokensDocument Tokens { get; init; }
}

internal sealed class RunModelsDocument
{
    public required string Strong { get; init; }

    public required string Cheap { get; init; }

    public required string Provider { get; init; }
}

internal sealed class RunDocument
{
    public int SchemaVersion { get; init; }

    public required string RunId { get; init; }

    public required string StartedAtUtc { get; init; }

    public long DurationMs { get; init; }

    public required string Profile { get; init; }

    public required string Prompt { get; init; }

    public required RunModelsDocument Models { get; init; }

    public required RunTokensDocument Tokens { get; init; }

    public required IReadOnlyList<RunIterationDocument> Iterations { get; init; }

    public required IReadOnlyList<string> Assumptions { get; init; }

    public required string Outcome { get; init; }
}
