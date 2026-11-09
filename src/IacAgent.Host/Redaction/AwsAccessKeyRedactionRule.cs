using System.Text.RegularExpressions;

using IacAgent.Core;

namespace IacAgent.Host.Redaction;

/// <summary>Masks AWS access key ids.</summary>
public sealed partial class AwsAccessKeyRedactionRule : IRedactionRule
{
    /// <inheritdoc />
    public string Redact(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return AccessKey().Replace(text, "[redacted]");
    }

    [GeneratedRegex(@"\bAKIA[0-9A-Z]{16}\b", RegexOptions.CultureInvariant)]
    private static partial Regex AccessKey();
}
