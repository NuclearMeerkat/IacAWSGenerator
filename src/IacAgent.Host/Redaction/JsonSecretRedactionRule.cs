using System.Text.RegularExpressions;

using IacAgent.Core;

namespace IacAgent.Host.Redaction;

/// <summary>Masks secret values stored as JSON strings.</summary>
public sealed partial class JsonSecretRedactionRule : IRedactionRule
{
    /// <inheritdoc />
    public string Redact(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Secret().Replace(text, "\"$1\": \"[redacted]\"");
    }

    [GeneratedRegex("\"(?i)(api[_-]?key|secret|password|token|access_key)\"\\s*:\\s*\"[^\"]{4,}\"", RegexOptions.CultureInvariant)]
    private static partial Regex Secret();
}
