using IacAgent.Core;
using IacAgent.Host.Redaction;

namespace IacAgent.Host.Tests.Redaction;

public sealed class RedactorTests
{
    [Fact]
    public void Redact_AppliesEveryRuleInOrder()
    {
        var redactor = new Redactor([new StampRule("a"), new StampRule("b")]);

        Assert.Equal("[b][a]secret", redactor.Redact("secret"));
    }

    [Fact]
    public void Redact_NoRules_ReturnsTheText()
    {
        var redactor = new Redactor([]);

        Assert.Equal("secret", redactor.Redact("secret"));
    }

    private sealed class StampRule(string mark) : IRedactionRule
    {
        public string Redact(string text)
        {
            return $"[{mark}]{text}";
        }
    }
}
