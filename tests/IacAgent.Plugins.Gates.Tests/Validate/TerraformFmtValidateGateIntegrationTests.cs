using IacAgent.Core;
using IacAgent.Plugins.Gates.Validate;
using IacAgent.Plugins.Terraform;

namespace IacAgent.Plugins.Gates.Tests.Validate;

public sealed class TerraformFmtValidateGateIntegrationTests
{
    [RequiresToolFact("terraform")]
    [Trait("Category", "Integration")]
    public async Task CheckAsync_ValidFixture_Passes()
    {
        var directory = CopyFixture("valid");
        try
        {
            var gate = new TerraformFmtValidateGate(new ProcessRunner());
            var result = await gate.CheckAsync(new Artifact(directory, []), CancellationToken.None);

            Assert.True(result.Passed, string.Join("; ", result.Issues.Select(issue => issue.Message)));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [RequiresToolFact("terraform")]
    [Trait("Category", "Integration")]
    public async Task CheckAsync_InvalidSyntax_Fails()
    {
        var directory = CopyFixture("invalid-syntax");
        try
        {
            var gate = new TerraformFmtValidateGate(new ProcessRunner());
            var result = await gate.CheckAsync(new Artifact(directory, []), CancellationToken.None);

            Assert.False(result.Passed);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CopyFixture(string name)
    {
        var source = Path.Combine(AppContext.BaseDirectory, "fixtures", name);
        var directory = Directory.CreateTempSubdirectory("iacagent-gate-").FullName;
        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(directory, Path.GetFileName(file)));
        }

        return directory;
    }
}
