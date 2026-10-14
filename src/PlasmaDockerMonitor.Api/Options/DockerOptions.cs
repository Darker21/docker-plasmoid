namespace PlasmaDockerMonitor.Api.Options;

/// <summary>
/// Options for the docker client to use for connecting
/// </summary>
public class DockerOptions
{
    /// <summary>
    /// The URI for the docker instance, null or empty defaults to the local instance 
    /// </summary>
    public string? Uri { get; set; }
}