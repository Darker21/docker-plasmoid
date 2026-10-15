using PlasmaDockerMonitor.Api.Models;

namespace PlasmaDockerMonitor.Api.Services;

/// <summary>
/// Docker service interface
/// </summary>
public interface IDockerService
{
    /// <summary>
    /// Retrieves containers from the docker service
    /// </summary>
    /// <param name="exclude">Comma separated list of container names to exclude from results</param>
    /// <returns>Containers retrieved from docker service</returns>
    Task<Result<ContainerSummaryResponse>> GetContainersAsync(string? exclude = null);
    
    /// <summary>
    /// Start a specific container
    /// </summary>
    /// <param name="id">The ID of the target container</param>
    /// <returns>Operation result denoting a success or failure</returns>
    Task<Result> StartContainerAsync(string id);
    
    /// <summary>
    /// Stop a specific container
    /// </summary>
    /// <param name="id">The ID of the target container</param>
    /// <returns>Operation result denoting a success or failure</returns>
    Task<Result> StopContainerAsync(string id);
    
    /// <summary>
    /// Restart a specific container
    /// </summary>
    /// <param name="id">The ID of the target container</param>
    /// <returns>Operation result denoting a success or failure</returns>
    Task<Result> RestartContainerAsync(string id);
    
    /// <summary>
    /// Get the detailed stats of a container
    /// </summary>
    /// <param name="id">The ID of the target container</param>
    /// <returns>Result object with container stats</returns>
    Task<Result<ContainerStatsDto>> GetContainerStatsAsync(string id);

    /// <summary>
    /// Get the detailed stats for a compose stack
    /// </summary>
    /// <param name="stackName">The name of the target compose stack</param>
    /// <returns>Result object with compose stats</returns>
    Task<Result<ComposeStackDto>> GetComposeStackStatsAsync(string stackName);
}