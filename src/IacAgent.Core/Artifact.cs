namespace IacAgent.Core;

/// <summary>The generated files a gate checks.</summary>
public sealed record Artifact(string Directory, IReadOnlyList<GeneratedFile> Files);
