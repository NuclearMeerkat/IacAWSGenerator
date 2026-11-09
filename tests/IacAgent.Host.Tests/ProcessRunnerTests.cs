using IacAgent.Plugins.Terraform;

namespace IacAgent.Host.Tests;

public sealed class ProcessRunnerTests
{
    [Fact]
    public async Task RunAsync_Timeout_StopsTheProcess()
    {
        var runner = new ProcessRunner();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync(
            "cmd.exe",
            ["/c", "ping", "-n", "30", "127.0.0.1"],
            Path.GetTempPath(),
            TimeSpan.FromSeconds(1),
            CancellationToken.None));

        Assert.Contains("timed out", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
