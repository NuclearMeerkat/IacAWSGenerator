using IacAgent.Core;

namespace IacAgent.Host;

/// <summary>Finds registered profile definitions by name. It has no built-in list of profiles.</summary>
public sealed class ProfileCatalog
{
    private readonly Dictionary<string, IProfileDefinition> _profiles;

    /// <summary>Creates a catalog from the profiles registered in dependency injection.</summary>
    public ProfileCatalog(IEnumerable<IProfileDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        _profiles = new Dictionary<string, IProfileDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in definitions)
        {
            if (!_profiles.TryAdd(definition.Name, definition))
            {
                throw new InvalidOperationException($"Profile '{definition.Name}' is registered more than once.");
            }
        }
    }

    /// <summary>Builds the named profile, or returns null when the name is unknown.</summary>
    public PipelineProfile? Create(string name, IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (!_profiles.TryGetValue(name, out var definition))
        {
            return null;
        }

        return definition.Build(services);
    }
}
