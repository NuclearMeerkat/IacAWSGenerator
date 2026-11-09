using System.Text.RegularExpressions;

using IacAgent.Core;

namespace IacAgent.Host.Redaction;

/// <summary>Masks secret values assigned in text.</summary>
public sealed partial class AssignedSecretRedactionRule : IRedactionRule
{
    /// <inheritdoc />
    public string Redact(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Assignment().Replace(text, "$1=[redacted]");
    }

    [GeneratedRegex(@"(?i)\b(api[_-]?key|secret_key|password|token)\b\s*[:=]\s*['""]?[^\s'""]{8,}", RegexOptions.CultureInvariant)]
    private static partial Regex Assignment();
}
