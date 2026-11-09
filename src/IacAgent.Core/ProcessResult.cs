namespace IacAgent.Core;

/// <summary>Captured output from one external process.</summary>
public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
