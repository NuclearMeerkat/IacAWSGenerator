namespace IacAgent.Core;

/// <summary>Collects iteration data and writes run.json.</summary>
public interface IRunRecorder
{
    /// <summary>Adds one finished iteration.</summary>
    void RecordIteration(IterationRecord record);

    /// <summary>Writes run.json for the run.</summary>
    Task WriteAsync(string outputDirectory, RunOutcome outcome, CancellationToken cancellationToken);
}
