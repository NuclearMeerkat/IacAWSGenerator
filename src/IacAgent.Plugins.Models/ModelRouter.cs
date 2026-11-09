using System.ClientModel;

using IacAgent.Core;

using Microsoft.Extensions.AI;

using OpenAI;
using OpenAI.Chat;

namespace IacAgent.Plugins.Models;

/// <summary>Selects OpenRouter, Ollama, or another OpenAI-compatible server.</summary>
internal sealed class ModelRouter(ModelConnection connection) : IModelRouter
{
    private const string OpenRouterEndpoint = "https://openrouter.ai/api/v1";

    /// <inheritdoc />
    public IChatClient GetClient(ModelTier tier)
    {
        var model = tier == ModelTier.Cheap && string.IsNullOrWhiteSpace(connection.CheapModel)
            ? connection.StrongModel
            : tier == ModelTier.Strong ? connection.StrongModel : connection.CheapModel;
        if (string.IsNullOrWhiteSpace(model))
        {
            throw new InvalidOperationException($"Set a model id for the {tier.ToString().ToLowerInvariant()} tier.");
        }

        if (string.IsNullOrWhiteSpace(connection.Endpoint))
        {
            var apiKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new InvalidOperationException("Set OPENROUTER_API_KEY, or pass --endpoint for a local model.");
            }

            return CreateOpenAiClient(model, apiKey, OpenRouterEndpoint);
        }

        var endpoint = new Uri(connection.Endpoint);
        if (IsOllama(endpoint))
        {
            return new OllamaSharp.OllamaApiClient(endpoint, model);
        }

        var localKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
        return CreateOpenAiClient(model, string.IsNullOrWhiteSpace(localKey) ? "local" : localKey, endpoint.AbsoluteUri);
    }

    /// <summary>Ollama is detected by port 11434 or a host name that contains "ollama".</summary>
    public static bool IsOllama(Uri endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        return endpoint.Port == 11434
            || endpoint.Host.Contains("ollama", StringComparison.OrdinalIgnoreCase);
    }

    private static IChatClient CreateOpenAiClient(string model, string apiKey, string endpoint)
    {
        return new ChatClient(
                model,
                new ApiKeyCredential(apiKey),
                new OpenAIClientOptions { Endpoint = new Uri(endpoint) })
            .AsIChatClient();
    }
}
