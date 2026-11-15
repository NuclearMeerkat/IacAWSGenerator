using IacAgent.Core;
using IacAgent.Plugins.Gates.Validate;

namespace IacAgent.Plugins.Gates.Tests.Validate;

public sealed class TerraformFmtValidateGateTests
{
    [Fact]
    public async Task CheckAsync_FmtExitZero_HasNoFmtIssue()
    {
        var gate = new TerraformFmtValidateGate(new FmtProcessRunner(0, string.Empty));

        var result = await gate.CheckAsync(new Artifact(Path.GetTempPath(), []), CancellationToken.None);

        Assert.True(result.Passed);
        Assert.DoesNotContain(result.Issues, issue => issue.RuleId == "terraform-fmt");
    }

    [Fact]
    public async Task CheckAsync_FmtExitNonZero_ReturnsOneBlockingIssue()
    {
        var gate = new TerraformFmtValidateGate(new FmtProcessRunner(2, "Invalid character"));

        var result = await gate.CheckAsync(new Artifact(Path.GetTempPath(), []), CancellationToken.None);

        Assert.False(result.Passed);
        var issue = Assert.Single(result.Issues);
        Assert.Equal("terraform-fmt", issue.RuleId);
        Assert.Equal("Invalid character", issue.Message);
        Assert.True(issue.IsBlocking);
    }
}
