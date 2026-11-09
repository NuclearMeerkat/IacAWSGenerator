namespace IacAgent.Core;

/// <summary>Builds one named profile. Register a class to add a profile.</summary>
public interface IProfileDefinition
{
    /// <summary>Gets the name selected by --profile.</summary>
    string Name { get; }

    /// <summary>Builds the profile from the current services.</summary>
    PipelineProfile Build(IServiceProvider services);
}
