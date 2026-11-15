namespace IacAgent.Plugins.Gates.Checkov;

/// <summary>Decides whether a Checkov finding blocks the run.</summary>
internal sealed class CheckovPolicy(CheckovOptions options)
{
    public bool IsBlocking(string ruleId)
    {
        if (options.Level == SecurityLevel.Strict)
        {
            return !Contains(options.StrictSkipChecks, ruleId);
        }

        return Contains(options.BlockingChecks, ruleId);
    }

    private static bool Contains(IReadOnlyList<string> ids, string ruleId)
    {
        return ids.Any(id => id.Equals(ruleId, StringComparison.OrdinalIgnoreCase));
    }
}
