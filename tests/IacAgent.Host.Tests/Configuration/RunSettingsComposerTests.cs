using IacAgent.Host.Configuration;

namespace IacAgent.Host.Tests.Configuration;

public sealed class RunSettingsComposerTests
{
    [Fact]
    public void Compose_CommandOverridesEnvironmentAndFile()
    {
        var file = new SettingsOverlay { Profile = "from-file", ModelStrong = "file-model", Endpoint = "http://file" };
        var environment = new SettingsOverlay { Profile = "from-env", ModelStrong = "env-model" };
        var command = new SettingsOverlay { ModelStrong = "command-model", Endpoint = "http://command" };

        var settings = RunSettingsComposer.Compose(file, environment, command);

        Assert.Equal("from-env", settings.Profile);
        Assert.Equal("command-model", settings.ModelStrong);
        Assert.Equal("http://command", settings.Endpoint);
    }

    [Fact]
    public void Compose_AllLayersMissing_UsesTrioAndOpenRouter()
    {
        var settings = RunSettingsComposer.Compose(new SettingsOverlay(), new SettingsOverlay(), new SettingsOverlay());

        Assert.Equal("trio", settings.Profile);
        Assert.Equal("./out", settings.OutputDirectory);
        Assert.Equal(3, settings.MaxRepairIterations);
        Assert.Null(settings.Endpoint);
        Assert.Null(settings.ModelStrong);
    }

    [Fact]
    public void Compose_CommandBudget_OverridesFile()
    {
        var settings = RunSettingsComposer.Compose(
            new SettingsOverlay { Budget = "100" },
            new SettingsOverlay(),
            new SettingsOverlay { Budget = "10.50" });

        Assert.Equal(10.50m, settings.MonthlyBudgetUsd);
    }

    [Fact]
    public void Validate_BudgetWords_ReturnsMessage()
    {
        var settings = RunSettingsComposer.Compose(
            new SettingsOverlay(),
            new SettingsOverlay(),
            new SettingsOverlay { Budget = "ten" });

        Assert.Equal("--budget must be a number of USD per month, such as 25 or 10.50.", RunSettingsComposer.Validate(settings));
    }

    [Fact]
    public void Validate_NegativeRetries_ReturnsMessage()
    {
        var settings = RunSettingsComposer.Compose(
            new SettingsOverlay(),
            new SettingsOverlay(),
            new SettingsOverlay { MaxRepairIterations = -1 });

        var error = RunSettingsComposer.Validate(settings);

        Assert.Equal("--max-retries must be zero or greater.", error);
    }
}
