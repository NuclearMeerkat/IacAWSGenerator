namespace IacAgent.Core;

/// <summary>Masks one kind of secret before text reaches a model or a log.</summary>
public interface IRedactionRule
{
    /// <summary>Returns the text with this rule's secrets masked.</summary>
    string Redact(string text);
}
