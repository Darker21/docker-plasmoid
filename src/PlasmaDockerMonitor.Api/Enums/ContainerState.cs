namespace PlasmaDockerMonitor.Api.Enums;

/// <summary>
/// A container's lifecycle state, mapped from the Docker Engine API's raw state string.
/// </summary>
public enum ContainerState
{
    /// <summary>The container ran and has since terminated.</summary>
    Exited,

    /// <summary>The container exists but is not running.</summary>
    Stopped,

    /// <summary>The container is in the process of starting.</summary>
    Starting,

    /// <summary>The container is running.</summary>
    Running,

    /// <summary>The container is in the process of stopping.</summary>
    Stopping,

    /// <summary>The container is restarting.</summary>
    Restarting,

    /// <summary>
    /// Docker reported a state string that didn't match any known value —
    /// kept distinct from the other states rather than silently defaulting to one of them.
    /// </summary>
    Unknown = 99
}