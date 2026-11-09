namespace IacAgent.Core;

/// <summary>One file proposed by an agent.</summary>
public sealed record GeneratedFile(string RelativePath, string Content);
