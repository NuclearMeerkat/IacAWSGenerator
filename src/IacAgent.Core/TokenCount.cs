namespace IacAgent.Core;

/// <summary>Input and output token counts, plus a USD cost when one is known.</summary>
public sealed record TokenCount(int Input, int Output, decimal? CostUsd = null)
{
    /// <summary>Adds another count. A missing cost stays missing until one side has a cost.</summary>
    public TokenCount Plus(TokenCount? other)
    {
        if (other is null)
        {
            return this;
        }

        decimal? cost = CostUsd is null && other.CostUsd is null ? null : (CostUsd ?? 0m) + (other.CostUsd ?? 0m);
        return new TokenCount(Input + other.Input, Output + other.Output, cost);
    }
}
