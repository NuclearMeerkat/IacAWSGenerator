using IacAgent.Core;

namespace IacAgent.Core.Tests;

public sealed class LoopPolicyTests
{
    [Fact]
    public void LoopPolicy_Default_AllowsThreeRepairs()
    {
        var policy = new LoopPolicy();

        Assert.Equal(3, policy.MaxRepairIterations);
        Assert.Null(policy.MaxTotalTokens);
    }
}
