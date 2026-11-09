using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;

namespace IacAgent.Spike;

/// <summary>Asks OpenRouter and a local Ollama model through <see cref="IChatClient"/>.</summary>
internal static class ChatProbe
{
    private const string OpenRouterEndpoint = "https://openrouter.ai/api/v1";
    private const string Prompt = "Reply with exactly: pong";

    public static async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var failures = 0;
        failures += await RunOpenRouterAsync(cancellationToken) ? 0 : 1;
        failures += await RunOllamaAsync(cancellationToken) ? 0 : 1;
        return failures;
    }

    private static async Task<bool> RunOpenRouterAsync(CancellationToken cancellationToken)
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
        var model = Environment.GetEnvironmentVariable("IACAGENT_MODEL_STRONG");
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(model))
        {
            Console.WriteLine("openrouter: skipped. Set OPENROUTER_API_KEY and IACAGENT_MODEL_STRONG.");
            return false;
        }

        IChatClient client = new ChatClient(
                model,
                new ApiKeyCredential(apiKey),
                new OpenAIClientOptions { Endpoint = new Uri(OpenRouterEndpoint) })
            .AsIChatClient();

        return await AskAsync("openrouter", model, client, apiKey, cancellationToken);
    }

    private static async Task<bool> RunOllamaAsync(CancellationToken cancellationToken)
    {
        var endpoint = Environment.GetEnvironmentVariable("OLLAMA_HOST");
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            endpoint = "http://127.0.0.1:11434";
        }

        var model = Environment.GetEnvironmentVariable("OLLAMA_MODEL");
        if (string.IsNullOrWhiteSpace(model))
        {
            model = Environment.GetEnvironmentVariable("IACAGENT_MODEL_CHEAP");
        }

        if (string.IsNullOrWhiteSpace(model))
        {
            Console.WriteLine("ollama: skipped. Set OLLAMA_MODEL (or IACAGENT_MODEL_CHEAP).");
            return false;
        }

        IChatClient client = new OllamaSharp.OllamaApiClient(new Uri(endpoint), model);
        return await AskAsync("ollama", model, client, secret: null, cancellationToken);
    }

    private static async Task<bool> AskAsync(
        string provider,
        string model,
        IChatClient client,
        string? secret,
        CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            var response = await client.GetResponseAsync(Prompt, options: null, linked.Token);
            var text = response.Text?.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                Console.WriteLine($"{provider} ({model}): empty response.");
                return false;
            }

            Console.WriteLine($"{provider} ({model}): {text}");
            return true;
        }
        catch (Exception exception) when (exception is HttpRequestException or ClientResultException or OperationCanceledException or InvalidOperationException)
        {
            Console.WriteLine($"{provider} ({model}): {Redact(exception.Message, secret)}");
            return false;
        }
    }

    private static string Redact(string text, string? secret)
    {
        if (string.IsNullOrEmpty(secret))
        {
            return text;
        }

        return text.Replace(secret, "[redacted]", StringComparison.Ordinal);
    }
}
