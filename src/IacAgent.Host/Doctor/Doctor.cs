using System.Net.Http;

using IacAgent.Core;

namespace IacAgent.Host.Doctor;

/// <summary>Checks local tools and model access. It never prints secret values.</summary>
public sealed class Doctor
{
    private const string TerraformFix = "Install Terraform and ensure it is on PATH: https://developer.hashicorp.com/terraform/install";
    private const string CheckovFix = "Install Checkov with 'pip install checkov' and add the Python Scripts directory to PATH.";
    private const string InfracostFix = "Install Infracost from https://www.infracost.io/docs/ and run infracost auth login. Required only when generate uses --budget.";
    private const string ApiKeyFix = "Set the OPENROUTER_API_KEY environment variable, or pass --endpoint to use a local model.";
    private const string EndpointFix = "Start the local model server, or correct --endpoint. OpenRouter is used when --endpoint is omitted.";

    private readonly IProcessRunner _processRunner;
    private readonly IHttpClientFactory _httpClientFactory;

    /// <summary>Creates a doctor that probes tools through the process runner.</summary>
    public Doctor(IProcessRunner processRunner, IHttpClientFactory httpClientFactory)
    {
        ArgumentNullException.ThrowIfNull(processRunner);
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        _processRunner = processRunner;
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>Runs every prerequisite check.</summary>
    public async Task<DoctorReport> CheckAsync(DoctorRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var checks = new List<DoctorCheck>
        {
            await CheckToolAsync("terraform", ["version"], required: true, TerraformFix, cancellationToken).ConfigureAwait(false),
            await CheckToolAsync("checkov", ["--version"], required: true, CheckovFix, cancellationToken).ConfigureAwait(false),
            await CheckToolAsync("infracost", ["--version"], required: false, InfracostFix, cancellationToken).ConfigureAwait(false),
            await CheckModelAsync(request, cancellationToken).ConfigureAwait(false),
        };
        return new DoctorReport(checks);
    }

    private async Task<DoctorCheck> CheckToolAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        bool required,
        string fix,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            var result = await _processRunner.RunAsync(fileName, arguments, Path.GetTempPath(), timeout.Token).ConfigureAwait(false);
            if (result.ExitCode != 0)
            {
                var error = FirstLine(result.StandardError);
                var detail = error.Length == 0 ? $"{fileName} exited with code {result.ExitCode}." : error;
                return new DoctorCheck(fileName, false, required, detail, fix);
            }

            var version = FirstLine(result.StandardOutput);
            if (version.Length == 0)
            {
                version = FirstLine(result.StandardError);
            }

            var success = version.Length == 0 ? $"{fileName} is available." : version;
            return new DoctorCheck(fileName, true, required, success, null);
        }
        catch (InvalidOperationException exception)
        {
            return new DoctorCheck(fileName, false, required, exception.Message, fix);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new DoctorCheck(fileName, false, required, $"{fileName} did not respond in time.", fix);
        }
    }

    private async Task<DoctorCheck> CheckModelAsync(DoctorRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Endpoint))
        {
            return request.HasOpenRouterApiKey
                ? new DoctorCheck("model", true, true, "Using OpenRouter. The API key is set.", null)
                : new DoctorCheck("model", false, true, "OpenRouter is the default provider and the API key is missing.", ApiKeyFix);
        }

        if (!Uri.TryCreate(request.Endpoint, UriKind.Absolute, out var endpoint))
        {
            return new DoctorCheck("model", false, true, "The local endpoint is not an absolute URL.", EndpointFix);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            var client = _httpClientFactory.CreateClient();
            using var response = await client.GetAsync(endpoint, timeout.Token).ConfigureAwait(false);
            return new DoctorCheck("model", true, true, $"Local endpoint responded with {(int)response.StatusCode}.", null);
        }
        catch (HttpRequestException)
        {
            return new DoctorCheck("model", false, true, "The local endpoint did not respond.", EndpointFix);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new DoctorCheck("model", false, true, "The local endpoint did not respond in time.", EndpointFix);
        }
    }

    private static string FirstLine(string text)
    {
        foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0)
            {
                return trimmed;
            }
        }

        return string.Empty;
    }
}
