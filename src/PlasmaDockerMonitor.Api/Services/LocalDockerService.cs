using System.Net;
using PlasmaDockerMonitor.Api.Models;
using Docker.DotNet;
using Docker.DotNet.Models;
using PlasmaDockerMonitor.Api.Enums;
using PlasmaDockerMonitor.Api.Logging;
using ContainerState = PlasmaDockerMonitor.Api.Enums.ContainerState;

namespace PlasmaDockerMonitor.Api.Services;

internal class LocalDockerService(IDockerClient dockerClient, ILogger<LocalDockerService> logger) : IDockerService
{
    private const string DockerComposeProjectLabelKey = "com.docker.compose.project";

    public Task<Result<ContainerSummaryResponse>> GetContainersAsync(string? exclude = null) =>
        PerformDockerRequest("GetContainers", async () =>
        {
            logger.LogInformation("Listing containers for host: {Host}", dockerClient.Configuration.EndpointBaseUri);
            var containers = await dockerClient.Containers.ListContainersAsync(new ContainersListParameters
            {
                All = true
            }, CancellationToken.None);

            logger.LogTrace("GET container count: {Count}", containers.Count);

            var summaries = containers.Select(MapContainer).ToList();

            var grouped = summaries.GroupBy(c => c.ComposeProject).ToList();

            var standalone = grouped.Where(g => g.Key is null).SelectMany(g => g).ToList();

            var stacks = grouped.Where(g => g.Key is not null)
                .Select(g => BuildStackDto(g.Key!, g.ToList()))
                .ToList();

            logger.LogInformation(
                "Successfully retrieved {Count} containers ({StandaloneCount} standalone, {StackCount} stacks)",
                summaries.Count, standalone.Count, stacks.Count);

            return new ContainerSummaryResponse { Standalone = standalone, Stacks = stacks };
        });

    public async Task<Result> StartContainerAsync(string id)
    {
        return await PerformDockerRequest($"StartContainer:{id}", () =>
            dockerClient.Containers.StartContainerAsync(id, new ContainerStartParameters()));
    }

    public async Task<Result> StopContainerAsync(string id)
    {
        return await PerformDockerRequest($"StopContainer:{id}", () =>
            dockerClient.Containers.StopContainerAsync(id, new ContainerStopParameters()));
    }

    public async Task<Result> RestartContainerAsync(string id)
    {
        return await PerformDockerRequest($"RestartContainer:{id}", () =>
            dockerClient.Containers.RestartContainerAsync(id, new ContainerRestartParameters()));
    }

    private sealed class SingleValueProgress<T> : IProgress<T>
    {
        public T? Value { get; private set; }
        public void Report(T value) => Value = value;
    }
    
    public async Task<Result<ContainerStatsDto>> GetContainerStatsAsync(string id)
    {
        return await PerformDockerRequest($"GetStats:{id}", () => GetSingleContainerStatsAsync(id));
    }

    public async Task<Result<ComposeStackDto>> GetComposeStackStatsAsync(string stackName)
    {
        return await PerformDockerRequest($"GetComposeStackStats:{stackName}", async () =>
        {
            var containers = await dockerClient.Containers.ListContainersAsync(new ContainersListParameters
            {
                All = true,
                Filters = new Dictionary<string, IDictionary<string, bool>>
                {
                    ["label"] = new Dictionary<string, bool>
                    {
                        [$"{DockerComposeProjectLabelKey}={stackName}"] = true
                    }
                }
            }, CancellationToken.None);

            if (containers.Count == 0)
            {
                // No such label value matched — the daemon returns an empty list rather than an
                // error, so this is the one case we have to turn into a "not found" ourselves.
                throw new DockerContainerNotFoundException(HttpStatusCode.NotFound,
                    $"No containers found for compose project '{stackName}'");
            }

            var summaries = containers.Select(MapContainer).ToList();

            var containerStats = await Task.WhenAll(summaries.Select(c => GetSingleContainerStatsAsync(c.Id)));

            var stack = BuildStackDto(stackName, summaries);
            stack.AggregateCpuPercent = containerStats.Sum(s => s.CpuPercent);
            stack.AggregateMemUsedMb = containerStats.Sum(s => s.MemUsedMb);

            return stack;
        });
    }

    private ContainerSummaryDto MapContainer(ContainerListResponse container)
    {
        var cleanName = container.Names.FirstOrDefault("<unknown>").Trim(['/', '\\']);
        logger.LogTrace("Mapping container {ContainerName}", cleanName);

        _ = container.Labels.TryGetValue(DockerComposeProjectLabelKey, out var composeProject);

        return new ContainerSummaryDto
        {
            Id = container.ID,
            Name = cleanName,
            State = Enum.TryParse<ContainerState>(container.State, ignoreCase: true, out var state)
                ? state
                : ContainerState.Unknown,
            Image = container.Image,
            ComposeProject = composeProject,
            StartedAt = container.Created
        };
    }

    private static ComposeStackDto BuildStackDto(string projectName, IReadOnlyCollection<ContainerSummaryDto> containers) =>
        new()
        {
            ProjectName = projectName,
            Containers = containers.ToList(),
            TotalCount = containers.Count,
            RunningCount = containers.Count(c => c.State == ContainerState.Running),
            RepresentativeStartedAt = containers.Min(c => c.StartedAt)
        };

    private async Task<ContainerStatsDto> GetSingleContainerStatsAsync(string id)
    {
        var progressHandler = new SingleValueProgress<ContainerStatsResponse>();

        await dockerClient.Containers.GetContainerStatsAsync(id, new ContainerStatsParameters
            {
                OneShot = false,
                Stream = false
            },
            progressHandler);

        var stats = progressHandler.Value ??
                    throw new InvalidOperationException("Daemon closed the stats stream without reporting a value");

        var cpuDelta = stats.CPUStats.CPUUsage.TotalUsage - stats.PreCPUStats.CPUUsage.TotalUsage;
        var systemDelta = stats.CPUStats.SystemUsage - stats.PreCPUStats.SystemUsage;
        var onlineCpus = stats.CPUStats.OnlineCPUs > 0
            ? stats.CPUStats.OnlineCPUs
            : (uint)(stats.CPUStats.CPUUsage.PercpuUsage?.Count ?? 1);

        var cpuPercent = systemDelta > 0 && cpuDelta > 0
            ? (float)((double)cpuDelta / systemDelta * onlineCpus * 100.0)
            : 0f;

        return new ContainerStatsDto
        {
            CpuPercent = cpuPercent,
            MemUsedMb = stats.MemoryStats.Usage / 1024.0 / 1024.0,
            MemLimitMb = stats.MemoryStats.Limit / 1024.0 / 1024.0
        };
    }

    // Dummy payload for the non-generic overload below — it only cares about success/failure,
    // but routes through the generic engine so the catch logic below has one home, not two.
    private static readonly object Unit = new();

    private async Task<Result> PerformDockerRequest(string operationName, Func<Task> method)
    {
        Result result = await PerformDockerRequest(operationName, async () =>
        {
            await method();
            return Unit;
        });

        return result;
    }

    private async Task<Result<TData>> PerformDockerRequest<TData>(string operationName, Func<Task<TData>> method)
        where TData : class
    {
        var safeOperationName = operationName.SanitizeForLog();
        using var scope = logger.BeginScope("{Operation}", safeOperationName);

        logger.LogInformation("Starting Docker operation {Operation}", safeOperationName);

        try
        {
            var data = await method();
            logger.LogInformation("Docker operation {Operation} completed successfully", safeOperationName);
            return Result<TData>.Ok(data);
        }
        catch (DockerContainerNotFoundException ex)
        {
            logger.LogWarning(ex, "Container not found during {Operation}", safeOperationName);
            return Result<TData>.Fail(ResultErrorKind.NotFound, "Container not found");
        }
        catch (DockerImageNotFoundException ex)
        {
            logger.LogWarning(ex, "Image not found during {Operation}", safeOperationName);
            return Result<TData>.Fail(ResultErrorKind.NotFound, "Image not found");
        }
        catch (DockerNetworkNotFoundException ex)
        {
            logger.LogWarning(ex, "Network not found during {Operation}", safeOperationName);
            return Result<TData>.Fail(ResultErrorKind.NotFound, "Network not found");
        }
        catch (DockerPluginNotFoundException ex)
        {
            logger.LogWarning(ex, "Plugin not found during {Operation}", safeOperationName);
            return Result<TData>.Fail(ResultErrorKind.NotFound, "Plugin not found");
        }
        catch (DockerApiException apiException)
        {
            logger.LogError(apiException, "Docker API error during {Operation}", safeOperationName);
            return Result<TData>.Fail(ResultErrorKind.DaemonError, "Internal docker error:\n" + apiException.Message);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Could not reach Docker daemon during {Operation}", safeOperationName);
            return Result<TData>.Fail(ResultErrorKind.Unreachable, "Request failed");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception during {Operation}", safeOperationName);
            return Result<TData>.Fail(ResultErrorKind.Unknown, "Unknown error");
        }
    }
}