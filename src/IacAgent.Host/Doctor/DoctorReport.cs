namespace IacAgent.Host.Doctor;

/// <summary>The full doctor result.</summary>
public sealed record DoctorReport(IReadOnlyList<DoctorCheck> Checks)
{
    /// <summary>Gets whether every required check passed. Optional checks do not fail the report.</summary>
    public bool Succeeded => Checks.Where(check => check.Required).All(check => check.Passed);
}
