namespace IacAgent.Plugins.Models;

/// <summary>Resolved model ids and an optional local endpoint for one run.</summary>
public sealed record ModelConnection(string? StrongModel, string? CheapModel, string? Endpoint);
