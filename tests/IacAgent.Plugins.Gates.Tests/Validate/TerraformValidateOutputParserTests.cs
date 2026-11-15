using IacAgent.Plugins.Gates.Validate;

namespace IacAgent.Plugins.Gates.Tests.Validate;

public sealed class TerraformValidateOutputParserTests
{
    [Fact]
    public void Parse_SavedInvalidOutput_KeepsTheDiagnostic()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Samples", "terraform-validate-invalid.json"));

        var issue = Assert.Single(TerraformValidateOutputParser.Parse(json));

        Assert.Equal("main.tf", issue.File);
        Assert.Equal(2, issue.Line);
        Assert.Equal("Invalid expression", issue.RuleId);
        Assert.Contains("invalid expression token", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_SavedValidOutput_HasNoIssues()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Samples", "terraform-validate-valid.json"));

        Assert.Empty(TerraformValidateOutputParser.Parse(json));
    }

    [Fact]
    public void Parse_DiagnosticInASubfolder_KeepsTheFolder()
    {
        const string json = """
            {"diagnostics":[{"severity":"error","summary":"Missing expression","detail":"Expected an expression.","range":{"filename":"modules\\x\\main.tf","start":{"line":4}}}]}
            """;

        var issue = Assert.Single(TerraformValidateOutputParser.Parse(json));

        Assert.Equal("modules/x/main.tf", issue.File);
        Assert.Equal(4, issue.Line);
    }
}
