namespace IacAgent.Core;

/// <summary>A fixed check the host runs after agents. Profiles cannot remove gates.</summary>
public interface IGate
{
    /// <summary>Gets the gate name stored in run.json.</summary>
    string Name { get; }

    /// <summary>Gets the ascending run order.</summary>
    int Order { get; }

    /// <summary>Checks an artifact and returns structured issues.</summary>
    Task<GateResult> CheckAsync(Artifact artifact, CancellationToken cancellationToken);
}
