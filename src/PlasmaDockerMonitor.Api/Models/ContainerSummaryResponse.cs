using System.Runtime.Serialization;
using PlasmaDockerMonitor.Api.Enums;

namespace PlasmaDockerMonitor.Api.Models;

/// <summary>
/// Response body for <c>GET /containers</c>.
/// </summary>
[DataContract]
public class ContainerSummaryResponse
{
    /// <summary>
    /// Every container known to this host. Currently a flat list — not yet split
    /// into standalone containers vs. compose stacks.
    /// </summary>
    [DataMember(Name = "containers")]
    public required IList<ContainerSummaryDto> Containers { get; set; }
}

/// <summary>
/// A single Docker container, mapped from the Docker Engine API's raw container
/// listing onto a shape that doesn't depend on Docker.DotNet's own types.
/// </summary>
[DataContract]
public class ContainerSummaryDto
{
    /// <summary>The Docker-assigned container ID.</summary>
    [DataMember(Name = "id")]
    public required string Id { get; set; }

    /// <summary>The container's given name, with Docker's leading slash stripped.</summary>
    [DataMember(Name = "name")]
    public required string Name { get; set; }

    /// <summary>The container's current lifecycle state.</summary>
    [DataMember(Name = "state")]
    public required ContainerState State { get; set; }

    /// <summary>The image the container was created from, e.g. <c>nginx:latest</c>.</summary>
    [DataMember(Name = "image")]
    public required string Image { get; set; }

    /// <summary>
    /// When the container was created. Note: this is Docker's container-creation
    /// time, not the last time it was actually started — the Docker Engine API only
    /// exposes the real start time via a per-container inspect call, which this
    /// mapping doesn't make. Revisit before relying on this for uptime sorting.
    /// </summary>
    [DataMember(Name = "startedAt")]
    public DateTime StartedAt { get; set; }

    /// <summary>
    /// The compose project this container belongs to, read from the
    /// <c>com.docker.compose.project</c> label. Null means the container is
    /// standalone.
    /// </summary>
    [DataMember(Name = "composeProject")]
    public string? ComposeProject { get; set; }
}