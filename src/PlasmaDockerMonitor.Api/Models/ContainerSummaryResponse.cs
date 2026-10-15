using System.Runtime.Serialization;
using PlasmaDockerMonitor.Api.Enums;

namespace PlasmaDockerMonitor.Api.Models;

/// <summary>
/// Response body for <c>GET /containers</c>.
/// </summary>
[DataContract]
public class ContainerSummaryResponse
{
    /// <summary>Containers with no compose project label — not part of any stack.</summary>
    [DataMember(Name = "standalone")]
    public required IList<ContainerSummaryDto> Standalone { get; set; }

    /// <summary>Containers grouped by their compose project (<c>com.docker.compose.project</c> label).</summary>
    [DataMember(Name = "stacks")]
    public required IList<ComposeStackDto> Stacks { get; set; }
}

/// <summary>
/// A group of containers belonging to the same docker-compose project.
/// </summary>
[DataContract]
public class ComposeStackDto
{
    /// <summary>The compose project name (the <c>com.docker.compose.project</c> label's value).</summary>
    [DataMember(Name = "projectName")]
    public required string ProjectName { get; set; }

    /// <summary>Every container belonging to this stack.</summary>
    [DataMember(Name = "containers")]
    public required IList<ContainerSummaryDto> Containers { get; set; }

    /// <summary>How many of this stack's containers are currently running.</summary>
    [DataMember(Name = "runningCount")]
    public required int RunningCount { get; set; }

    /// <summary>Total number of containers in this stack, running or not.</summary>
    [DataMember(Name = "totalCount")]
    public required int TotalCount { get; set; }

    /// <summary>
    /// The earliest <see cref="ContainerSummaryDto.StartedAt"/> among this stack's
    /// containers — i.e. its longest-running container, which is what "stack uptime"
    /// means for sorting purposes.
    /// </summary>
    [DataMember(Name = "representativeStartedAt")]
    public required DateTime RepresentativeStartedAt { get; set; }

    /// <summary>
    /// Sum of all child containers' CPU%. Null until per-container stats have actually
    /// been fetched (stats are comparatively expensive and not gathered eagerly here —
    /// see <see cref="ContainerStatsDto"/>), so this is only populated once the caller
    /// has resolved CPU stats for this stack's containers elsewhere.
    /// </summary>
    [DataMember(Name = "aggregateCpuPercent")]
    public float? AggregateCpuPercent { get; set; }

    /// <summary>
    /// Sum of all child containers' memory usage, in MB. Null for the same reason as
    /// <see cref="AggregateCpuPercent"/> — stats aren't fetched eagerly.
    /// </summary>
    [DataMember(Name = "aggregateMemUsedMb")]
    public double? AggregateMemUsedMb { get; set; }
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