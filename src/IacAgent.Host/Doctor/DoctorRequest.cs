namespace IacAgent.Host.Doctor;

/// <summary>What doctor needs to know about model access. The API key value is not included.</summary>
public sealed record DoctorRequest(bool HasOpenRouterApiKey, string? Endpoint);
