using IacAgent.Core;
using IacAgent.Host;

namespace IacAgent.Host.Tests;

public sealed class ProfileCatalogTests
{
    [Fact]
    public void ProfileCatalog_DuplicateName_Throws()
    {
        var first = new FakeProfile("trio");
        var second = new FakeProfile("TRIO");

        Assert.Throws<InvalidOperationException>(() => new ProfileCatalog([first, second]));
    }

    [Fact]
    public void ProfileCatalog_UnknownName_ReturnsNull()
    {
        var catalog = new ProfileCatalog([new FakeProfile("trio")]);

        var profile = catalog.Create("solo", new DefaultServiceProvider());

        Assert.Null(profile);
    }

    private sealed class FakeProfile(string name) : IProfileDefinition
    {
        public string Name => name;

        public PipelineProfile Build(IServiceProvider services)
        {
            return new PipelineProfile(Name, [], new LoopPolicy());
        }
    }

    private sealed class DefaultServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            return null;
        }
    }
}
