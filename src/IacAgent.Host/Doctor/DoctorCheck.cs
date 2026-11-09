namespace IacAgent.Host.Doctor;

/// <summary>One prerequisite check.</summary>
public sealed record DoctorCheck(string Name, bool Passed, bool Required, string Detail, string? Fix);
