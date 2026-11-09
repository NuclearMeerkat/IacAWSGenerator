using IacAgent.Host.Doctor;
using IacAgent.Host.Pipeline;
using IacAgent.Host.Review;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IacAgent.Host;

/// <summary>Registers the host pipeline, profile catalog, and doctor.</summary>
public static class HostServiceCollectionExtensions
{
    /// <summary>Adds host services. Gates and profiles come from plugins.</summary>
    public static IServiceCollection AddHost(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddHttpClient();
        services.AddSingleton<ProfileCatalog>();
        services.AddSingleton<PipelineRunner>();
        services.AddSingleton<Doctor.Doctor>();
        services.AddSingleton<PlanReviewer>();
        return services;
    }
}
