using System.Reflection;

namespace PlasmaDockerMonitor.Api;

/// <summary>
/// The release version the API was built as, stamped in via the <c>ReleaseVersion</c> MSBuild property
/// (the Docker image's <c>VERSION</c> build arg)
/// </summary>
internal static class AppVersion
{
    private const string MetadataKey = "ReleaseVersion";
    private const string DevVersion = "dev";

    /// <summary>
    /// The release version of this build, or "dev" when it wasn't built as a release
    /// </summary>
    public static string Current { get; } = Resolve(typeof(AppVersion).Assembly);

    /// <summary>
    /// Reads the release version stamped into <paramref name="assembly"/>, falling back to "dev"
    /// </summary>
    public static string Resolve(Assembly assembly) =>
        assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == MetadataKey)?.Value
        ?? DevVersion;
}
