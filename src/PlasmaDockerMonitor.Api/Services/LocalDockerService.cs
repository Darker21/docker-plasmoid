using System.Threading.Channels;
using PlasmaDockerMonitor.Api.Models;
using Docker.DotNet;
using Docker.DotNet.Models;
using PlasmaDockerMonitor.Api.Enums;
using ContainerState = PlasmaDockerMonitor.Api.Enums.ContainerState;

namespace PlasmaDockerMonitor.Api.Services;

internal class LocalDockerService(IDockerClient dockerClient) : IDockerService
{
    private const string DockerComposeProjectLabelKey = "com.docker.compose.project";

    public Task<Result<ContainerSummaryResponse>> GetContainersAsync(string? exclude = null) =>
        PerformDockerRequest(async () =>
        {
            var containers = await dockerClient.Containers.ListContainersAsync(new ContainersListParameters
            {
                All = true
            }, CancellationToken.None);

            // TODO: pull children from compose stack and return

            var summaries = containers.Select(container =>
            {
                _ = container.Labels.TryGetValue(DockerComposeProjectLabelKey, out var composeProject);
                var cleanName = container.Names.FirstOrDefault("<unknown>").Trim(['/', '\\']);

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
            }).ToList();

            return new ContainerSummaryResponse { Containers = summaries };
        });

    public async Task<Result> StartContainerAsync(string id)
    {
        return await PerformDockerRequest(() =>
            dockerClient.Containers.StartContainerAsync(id, new ContainerStartParameters()));
    }

    public async Task<Result> StopContainerAsync(string id)
    {
        return await PerformDockerRequest(() =>
            dockerClient.Containers.StopContainerAsync(id, new ContainerStopParameters()));
    }

    public async Task<Result> RestartContainerAsync(string id)
    {
        return await PerformDockerRequest(() =>
            dockerClient.Containers.RestartContainerAsync(id, new ContainerRestartParameters()));
    }

    private sealed class SingleValueProgress<T> : IProgress<T>
    {
        public T? Value { get; private set; }
        public void Report(T value) => Value = value;
    }
    
    public async Task<Result<ContainerStatsDto>> GetStatsAsync(string id)
    {
        return await PerformDockerRequest(async () =>
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
        });
    }

    // Dummy payload for the non-generic overload below — it only cares about success/failure,
    // but routes through the generic engine so the catch logic below has one home, not two.
    private static readonly object Unit = new();

    private static async Task<Result> PerformDockerRequest(Func<Task> method)
    {
        Result result = await PerformDockerRequest(async () =>
        {
            await method();
            return Unit;
        });

        return result;
    }

    private static async Task<Result<TData>> PerformDockerRequest<TData>(Func<Task<TData>> method)
        where TData : class
    {
        try
        {
            var data = await method();
            return Result<TData>.Ok(data);
        }
        catch (DockerContainerNotFoundException)
        {
            return Result<TData>.Fail(ResultErrorKind.NotFound, "Container not found");
        }
        catch (DockerImageNotFoundException)
        {
            return Result<TData>.Fail(ResultErrorKind.NotFound, "Image not found");
        }
        catch (DockerNetworkNotFoundException)
        {
            return Result<TData>.Fail(ResultErrorKind.NotFound, "Network not found");
        }
        catch (DockerPluginNotFoundException)
        {
            return Result<TData>.Fail(ResultErrorKind.NotFound, "Plugin not found");
        }
        catch (DockerApiException apiException)
        {
            return Result<TData>.Fail(ResultErrorKind.DaemonError, "Internal docker error:\n" + apiException.Message);
        }
        catch (HttpRequestException)
        {
            return Result<TData>.Fail(ResultErrorKind.Unreachable, "Request failed");
        }
        catch (Exception)
        {
            return Result<TData>.Fail(ResultErrorKind.Unknown, "Unknown error");
        }
    }
}