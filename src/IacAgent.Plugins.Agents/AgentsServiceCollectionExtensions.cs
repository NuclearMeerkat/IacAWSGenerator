using IacAgent.Core;
using IacAgent.Plugins.Agents.Full;
using IacAgent.Plugins.Agents.Solo;
using IacAgent.Plugins.Agents.Trio;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IacAgent.Plugins.Agents;

/// <summary>Registers the agent profiles.</summary>
public static class AgentsServiceCollectionExtensions
{
    /// <summary>Adds the solo, trio, and full profiles.</summary>
    public static IServiceCollection AddAgents(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddSingleton<OrchestratorAgent>();
        services.AddSingleton<DeveloperAgent>();
        services.AddSingleton<SecurityReviewerAgent>();
        services.AddSingleton<SoloDeveloperAgent>();
        services.AddSingleton<ArchitectAgent>();
        services.AddSingleton<FinOpsExplainerAgent>();
        services.AddSingleton<IProfileDefinition, SoloProfile>();
        services.AddSingleton<IProfileDefinition, TrioProfile>();
        services.AddSingleton<IProfileDefinition, FullProfile>();
        return services;
    }
}
