using Microsoft.Extensions.Configuration;

namespace IacAgent.Plugins.Gates.Checkov;

/// <summary>Host policy for which Checkov findings block a run.</summary>
internal sealed class CheckovOptions
{
    /// <summary>Checks verified with <c>checkov --list --framework terraform</c> 3.3.30. Each one is a clear hole and is fixed by changing an attribute.</summary>
    public static readonly string[] DefaultBlockingChecks =
    [
        "CKV_AWS_24", // SSH (22) open to 0.0.0.0/0
        "CKV_AWS_25", // RDP (3389) open to 0.0.0.0/0
        "CKV_AWS_277", // all ports open to 0.0.0.0/0
        "CKV_AWS_232", // NACL allows SSH from 0.0.0.0/0
        "CKV_AWS_231", // NACL allows RDP from 0.0.0.0/0
        "CKV_AWS_62", // IAM policy allows full *-* administrative privileges
        "CKV_AWS_274", // AdministratorAccess policy is attached
        "CKV_AWS_20", // S3 ACL allows public read
        "CKV_AWS_57", // S3 ACL allows public write
        "CKV_AWS_70", // S3 bucket policy allows any principal
        "CKV_AWS_53", // S3 block public ACLs is off
        "CKV_AWS_54", // S3 block public policy is off
        "CKV_AWS_55", // S3 ignore public ACLs is off
        "CKV_AWS_56", // S3 restrict public buckets is off
        "CKV_AWS_3", // EBS volume encryption flag is off
        "CKV_AWS_8", // launch configuration EBS encryption flag is off
        "CKV_AWS_16", // RDS storage encryption flag is off
        "CKV_AWS_19", // S3 default encryption is off
        "CKV_AWS_17", // RDS publicly_accessible is true
    ];

    public SecurityLevel Level { get; set; } = SecurityLevel.Baseline;

    public List<string> BlockingChecks { get; set; } = [.. DefaultBlockingChecks];

    public List<string> StrictSkipChecks { get; set; } = [];

    public static CheckovOptions Bind(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var text = configuration["Checkov:Level"];
        if (!string.IsNullOrWhiteSpace(text) && !Enum.TryParse<SecurityLevel>(text, ignoreCase: true, out _))
        {
            throw new InvalidOperationException($"Security level '{text}' is not baseline or strict.");
        }

        var options = new CheckovOptions();
        if (Enum.TryParse<SecurityLevel>(text, ignoreCase: true, out var level))
        {
            options.Level = level;
        }

        var blocking = ReadList(configuration, "Checkov:BlockingChecks");
        if (blocking.Count > 0)
        {
            options.BlockingChecks = blocking;
        }

        options.StrictSkipChecks = ReadList(configuration, "Checkov:StrictSkipChecks");
        return options;
    }

    private static List<string> ReadList(IConfiguration configuration, string section)
    {
        var values = new List<string>();
        foreach (var child in configuration.GetSection(section).GetChildren())
        {
            if (!string.IsNullOrWhiteSpace(child.Value))
            {
                values.Add(child.Value);
            }
        }

        return values;
    }
}
