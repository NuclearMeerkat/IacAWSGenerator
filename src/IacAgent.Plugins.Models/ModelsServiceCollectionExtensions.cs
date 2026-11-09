using IacAgent.Core;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IacAgent.Plugins.Models;

/// <summary>Registers model providers.</summary>
public static class ModelsServiceCollectionExtensions
{
    /// <summary>Adds the model router. Register <see cref="ModelConnection"/> first.</summary>
    public static IServiceCollection AddModels(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddSingleton<IModelRouter, ModelRouter>();
        return services;
    }
}
