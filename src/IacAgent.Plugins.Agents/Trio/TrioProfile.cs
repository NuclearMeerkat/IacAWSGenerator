using IacAgent.Core;

using Microsoft.Extensions.DependencyInjection;

namespace IacAgent.Plugins.Agents.Trio;

/// <summary>Orchestrator, Developer, and SecurityReviewer.</summary>
internal sealed class TrioProfile : IProfileDefinition
{
    /// <inheritdoc />
    public string Name => "trio";

    /// <inheritdoc />
    public PipelineProfile Build(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        IAgent[] agents =
        [
            services.GetRequiredService<OrchestratorAgent>(),
            services.GetRequiredService<DeveloperAgent>(),
            services.GetRequiredService<SecurityReviewerAgent>(),
        ];
        return new PipelineProfile(Name, agents, new LoopPolicy());
    }
}
