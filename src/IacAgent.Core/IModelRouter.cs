using Microsoft.Extensions.AI;

namespace IacAgent.Core;

/// <summary>Selects the chat client for a model tier.</summary>
public interface IModelRouter
{
    /// <summary>Returns the client configured for the tier.</summary>
    IChatClient GetClient(ModelTier tier);

    /// <summary>Returns the model id the tier will use, including the strong-model fallback.</summary>
    string GetModelId(ModelTier tier);
}
