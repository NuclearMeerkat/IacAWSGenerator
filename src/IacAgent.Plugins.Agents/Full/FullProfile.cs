using IacAgent.Core;
using IacAgent.Plugins.Agents.Trio;

using Microsoft.Extensions.DependencyInjection;

namespace IacAgent.Plugins.Agents.Full;

/// <summary>Orchestrator, architect, FinOps notes, developer, and security reviewer.</summary>
internal sealed class FullProfile : IProfileDefinition
{
    /// <inheritdoc />
    public string Name => "full";

    /// <inheritdoc />
    public PipelineProfile Build(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        IAgent[] agents =
        [
            services.GetRequiredService<OrchestratorAgent>(),
            services.GetRequiredService<ArchitectAgent>(),
            services.GetRequiredService<FinOpsExplainerAgent>(),
            services.GetRequiredService<DeveloperAgent>(),
            services.GetRequiredService<SecurityReviewerAgent>(),
        ];
        return new PipelineProfile(Name, agents, new LoopPolicy());
    }
}
