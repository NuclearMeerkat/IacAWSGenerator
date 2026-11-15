using IacAgent.Plugins.Gates.Checkov;

namespace IacAgent.Plugins.Gates.Tests.Checkov;

public sealed class CheckovOutputParserTests
{
    [Fact]
    public async Task Parse_SavedSecurityGroup_ReadsBothFailedChecks()
    {
        var json = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Samples", "checkov-insecure.json"));

        var issues = CheckovOutputParser.Parse(json);

        Assert.Equal(2, issues.Count);
        Assert.Equal("CKV_AWS_24", issues[0].RuleId);
        Assert.Equal("main.tf", issues[0].File);
        Assert.Equal(1, issues[0].Line);
        Assert.Equal("CKV2_AWS_5", issues[1].RuleId);
        Assert.Equal("UNKNOWN", issues[1].Severity);
    }

    [Fact]
    public void Parse_SpikeSample_ReadsTheFailedCheck()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Samples", "checkov.json"));

        var issue = Assert.Single(CheckovOutputParser.Parse(json));

        Assert.Equal("CKV_AWS_24", issue.RuleId);
        Assert.Equal("main.tf", issue.File);
        Assert.Equal(4, issue.Line);
        Assert.Equal("HIGH", issue.Severity);
    }
}
