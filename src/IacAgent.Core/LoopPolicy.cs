namespace IacAgent.Core;

/// <summary>Limits for the repair loop. Gates are not part of a profile.</summary>
public sealed record LoopPolicy(int MaxRepairIterations = 3, int? MaxTotalTokens = null);
