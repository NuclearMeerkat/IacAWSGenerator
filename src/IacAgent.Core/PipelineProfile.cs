namespace IacAgent.Core;

/// <summary>Agents and loop limits for one named profile. Gates stay on the host.</summary>
public sealed record PipelineProfile(string Name, IReadOnlyList<IAgent> Agents, LoopPolicy Loop);
