using Microsoft.Extensions.AI;

namespace IacAgent.Core;

/// <summary>Selects the chat client for a model tier.</summary>
public interface IModelRouter
{
    /// <summary>Returns the client configured for the tier.</summary>
    IChatClient GetClient(ModelTier tier);
}
