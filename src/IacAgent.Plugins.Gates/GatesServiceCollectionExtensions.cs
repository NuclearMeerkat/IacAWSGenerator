using IacAgent.Core;
using IacAgent.Plugins.Gates.Checkov;
using IacAgent.Plugins.Gates.Infracost;
using IacAgent.Plugins.Gates.Validate;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IacAgent.Plugins.Gates;

/// <summary>Registers gates. The host runs every registered gate.</summary>
public static class GatesServiceCollectionExtensions
{
    /// <summary>Adds terraform fmt/validate and Checkov.</summary>
    public static IServiceCollection AddGates(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddSingleton<IGate, TerraformFmtValidateGate>();
        services.AddSingleton<IGate, CheckovGate>();
        services.AddSingleton<IGate, InfracostGate>();
        return services;
    }
}
