namespace PlasmaDockerMonitor.Api.Models;

/// <summary>
/// A single-snapshot resource usage reading for one container (CPU/memory), returned by
/// <c>GET /containers/{id}/stats</c>. Kept separate from <see cref="ContainerSummaryDto"/>
/// since stats are comparatively expensive to gather and are only fetched per-container
/// when that container's CPU/Mem display toggle is on.
/// </summary>
/// <remarks>Placeholder — properties not yet added.</remarks>
public class ContainerStatsDto
{
    public float CpuPercent { get; set; }
    public double MemUsedMb { get; set; }
    public double MemLimitMb { get; set; }
}