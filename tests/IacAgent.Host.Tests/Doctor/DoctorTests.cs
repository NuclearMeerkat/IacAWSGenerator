using System.Net.Http;

using IacAgent.Core;
using IacAgent.Host.Doctor;

namespace IacAgent.Host.Tests.Doctor;

public sealed class DoctorTests
{
    [Fact]
    public async Task CheckAsync_MissingTerraform_IncludesFixAndFails()
    {
        var doctor = new IacAgent.Host.Doctor.Doctor(
            new FakeProcessRunner(fileName => throw new InvalidOperationException($"'{fileName}' was not found on PATH.")),
            new ThrowingHttpClientFactory());

        var report = await doctor.CheckAsync(new DoctorRequest(true, null), CancellationToken.None);

        Assert.False(report.Succeeded);
        var terraform = Assert.Single(report.Checks, check => check.Name == "terraform");
        Assert.False(terraform.Passed);
        Assert.Contains("PATH", terraform.Fix, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckAsync_InfracostMissing_DoesNotFailWhenRequiredToolsExist()
    {
        var doctor = new IacAgent.Host.Doctor.Doctor(new FakeProcessRunner(RunTools), new ThrowingHttpClientFactory());

        var report = await doctor.CheckAsync(new DoctorRequest(true, null), CancellationToken.None);

        Assert.True(report.Succeeded);
        var infracost = Assert.Single(report.Checks, check => check.Name == "infracost");
        Assert.False(infracost.Passed);
        Assert.False(infracost.Required);
    }

    [Fact]
    public async Task CheckAsync_MissingApiKeyWithoutEndpoint_Fails()
    {
        var doctor = new IacAgent.Host.Doctor.Doctor(new FakeProcessRunner(RunTools), new ThrowingHttpClientFactory());

        var report = await doctor.CheckAsync(new DoctorRequest(false, null), CancellationToken.None);

        Assert.False(report.Succeeded);
        var model = Assert.Single(report.Checks, check => check.Name == "model");
        Assert.Contains("OPENROUTER_API_KEY", model.Fix, StringComparison.Ordinal);
        Assert.Contains("--endpoint", model.Fix, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckAsync_OpenRouterMode_DoesNotContactEndpoint()
    {
        var doctor = new IacAgent.Host.Doctor.Doctor(new FakeProcessRunner(RunTools), new ThrowingHttpClientFactory());

        var report = await doctor.CheckAsync(new DoctorRequest(true, "  "), CancellationToken.None);

        Assert.True(report.Succeeded);
        Assert.Contains(report.Checks, check => check.Name == "model" && check.Passed);
    }

    private static ProcessResult RunTools(string fileName)
    {
        if (fileName == "infracost")
        {
            throw new InvalidOperationException("'infracost' was not found on PATH.");
        }

        return new ProcessResult(0, fileName + " 1.0", string.Empty);
    }

    private sealed class FakeProcessRunner(Func<string, ProcessResult> run) : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            string workingDirectory,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(run(fileName));
        }
    }

    private sealed class ThrowingHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            throw new InvalidOperationException("The doctor contacted a model endpoint.");
        }
    }
}
