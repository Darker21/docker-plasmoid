using System.Reflection;

// Stands in for a release build's stamped metadata, so Resolve can be exercised against a real assembly
[assembly: AssemblyMetadata("ReleaseVersion", "9.9.9-test")]

namespace PlasmaDockerMonitor.Api.Tests;

public class AppVersionShould
{
    [Fact]
    public void ResolveStampedVersion_WhenAssemblyHasReleaseVersionMetadata()
    {
        var version = AppVersion.Resolve(typeof(AppVersionShould).Assembly);

        Assert.Equal("9.9.9-test", version);
    }

    [Fact]
    public void ResolveDev_WhenAssemblyHasNoReleaseVersionMetadata()
    {
        var version = AppVersion.Resolve(typeof(FactAttribute).Assembly);

        Assert.Equal("dev", version);
    }
}
