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

    [Fact]
    public async Task RunAsync_TerraformWithoutCacheVariable_SetsPluginCache()
    {
        var directory = Directory.CreateTempSubdirectory("iacagent-").FullName;
        var originalPath = Environment.GetEnvironmentVariable("PATH");
        var originalCache = Environment.GetEnvironmentVariable("TF_PLUGIN_CACHE_DIR");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "terraform.cmd"), "@echo off\r\necho %TF_PLUGIN_CACHE_DIR%\r\n");
            Environment.SetEnvironmentVariable("TF_PLUGIN_CACHE_DIR", null);
            Environment.SetEnvironmentVariable("PATH", directory + Path.PathSeparator + originalPath);
            var result = await new ProcessRunner().RunAsync("terraform", [], directory, CancellationToken.None);

            Assert.Contains(Path.Combine("iacagent", "plugin-cache"), result.StandardOutput, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
            Environment.SetEnvironmentVariable("TF_PLUGIN_CACHE_DIR", originalCache);
            Directory.Delete(directory, recursive: true);
        }
    }
}
