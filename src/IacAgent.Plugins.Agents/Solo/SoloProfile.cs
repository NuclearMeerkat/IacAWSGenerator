using IacAgent.Core;

using Microsoft.Extensions.DependencyInjection;

namespace IacAgent.Plugins.Agents.Solo;

/// <summary>One developer with a combined prompt.</summary>
internal sealed class SoloProfile : IProfileDefinition
{
    /// <inheritdoc />
    public string Name => "solo";

    /// <inheritdoc />
    public PipelineProfile Build(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return new PipelineProfile(Name, [services.GetRequiredService<SoloDeveloperAgent>()], new LoopPolicy());
    }
}
