using IacAgent.Core;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IacAgent.Plugins.Terraform;

/// <summary>Registers the Terraform process runner.</summary>
public static class TerraformServiceCollectionExtensions
{
    /// <summary>Adds the process runner used by doctor and, later, the gates.</summary>
    public static IServiceCollection AddTerraform(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        return services;
    }
}
