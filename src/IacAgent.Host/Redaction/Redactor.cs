using IacAgent.Core;

namespace IacAgent.Host.Redaction;

/// <summary>Applies every redaction rule, in registration order.</summary>
internal sealed class Redactor
{
    private readonly IReadOnlyList<IRedactionRule> _rules;

    public Redactor(IEnumerable<IRedactionRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        _rules = rules.ToList();
    }

    public string Redact(string text)
    {
        foreach (var rule in _rules)
        {
            text = rule.Redact(text);
        }

        return text;
    }
}
