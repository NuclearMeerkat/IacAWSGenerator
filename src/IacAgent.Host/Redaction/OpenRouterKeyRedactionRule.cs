using System.Text.RegularExpressions;

using IacAgent.Core;

namespace IacAgent.Host.Redaction;

/// <summary>Masks OpenRouter API keys.</summary>
public sealed partial class OpenRouterKeyRedactionRule : IRedactionRule
{
    /// <inheritdoc />
    public string Redact(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Key().Replace(text, "[redacted]");
    }

    [GeneratedRegex(@"\bsk-or-[A-Za-z0-9_-]{8,}", RegexOptions.CultureInvariant)]
    private static partial Regex Key();
}
