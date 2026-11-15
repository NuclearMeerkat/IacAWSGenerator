using IacAgent.Core;
using IacAgent.Plugins.Gates.Checkov;
using IacAgent.Plugins.Terraform;

using Microsoft.Extensions.Configuration;

namespace IacAgent.Plugins.Gates.Tests.Checkov;

public sealed class CheckovGateIntegrationTests
{
    [RequiresToolFact("checkov")]
    [Trait("Category", "Integration")]
    public async Task CheckAsync_InsecureFixture_Blocks()
    {
        var source = Path.Combine(AppContext.BaseDirectory, "fixtures", "insecure", "main.tf");
        var directory = Directory.CreateTempSubdirectory("iacagent-checkov-").FullName;
        try
        {
            File.Copy(source, Path.Combine(directory, "main.tf"));
            var gate = new CheckovGate(new ProcessRunner(), CheckovOptions.Bind(new ConfigurationBuilder().Build()));

            var result = await gate.CheckAsync(new Artifact(directory, []), CancellationToken.None);

            Assert.False(result.Passed);
            Assert.Contains(result.Issues, issue => issue.RuleId == "CKV_AWS_24" && issue.IsBlocking);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
