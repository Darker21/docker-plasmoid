namespace PlasmaDockerMonitor.Api.Enums;

/// <summary>
/// Why a <see cref="PlasmaDockerMonitor.Api.Models.Result"/> (or <c>Result&lt;TData&gt;</c>)
/// failed, abstracted away from the underlying Docker.DotNet exception so endpoints can map
/// to an HTTP response without knowing anything about Docker.DotNet's own types.
/// </summary>
public enum ResultErrorKind
{
    /// <summary>Failure didn't match any of the other known kinds.</summary>
    Unknown,

    /// <summary>The requested container doesn't exist (Docker.DotNet's <c>DockerContainerNotFoundException</c>).</summary>
    NotFound,

    /// <summary>Couldn't reach the Docker daemon/socket at all — no HTTP response to inspect.</summary>
    Unreachable,

    /// <summary>The daemon responded but with an error status (Docker.DotNet's base <c>DockerApiException</c>).</summary>
    DaemonError,
}