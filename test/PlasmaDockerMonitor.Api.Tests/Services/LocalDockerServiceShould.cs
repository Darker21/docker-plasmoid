using System.Net;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PlasmaDockerMonitor.Api.Enums;
using PlasmaDockerMonitor.Api.Models;
using PlasmaDockerMonitor.Api.Services;
using ContainerState = PlasmaDockerMonitor.Api.Enums.ContainerState;

namespace PlasmaDockerMonitor.Api.Tests.Services;

public class LocalDockerServiceShould
{
    private static LocalDockerService CreateService(out Mock<IContainerOperations> containerOperations)
    {
        containerOperations = new Mock<IContainerOperations>();

        var dockerClient = new Mock<IDockerClient>();
        dockerClient.Setup(c => c.Configuration).Returns(new DockerClientConfiguration());
        dockerClient.Setup(c => c.Containers).Returns(containerOperations.Object);

        return new LocalDockerService(dockerClient.Object, NullLogger<LocalDockerService>.Instance);
    }

    private static ContainerListResponse CreateContainer(
        string id, string name, string state, string? composeProject, DateTime created) =>
        new()
        {
            ID = id,
            Names = [$"/{name}"],
            Image = "nginx:latest",
            State = state,
            Created = created,
            Labels = composeProject is null
                ? new Dictionary<string, string>()
                : new Dictionary<string, string> { ["com.docker.compose.project"] = composeProject }
        };

    private static ContainerStatsResponse CreateStats(
        ulong totalUsage, ulong preTotalUsage, ulong systemUsage, ulong preSystemUsage,
        uint onlineCpus, IList<ulong>? percpuUsage, ulong memUsage, ulong memLimit) =>
        new()
        {
            CPUStats = new CPUStats
            {
                CPUUsage = new CPUUsage { TotalUsage = totalUsage, PercpuUsage = percpuUsage },
                SystemUsage = systemUsage,
                OnlineCPUs = onlineCpus
            },
            PreCPUStats = new CPUStats
            {
                CPUUsage = new CPUUsage { TotalUsage = preTotalUsage },
                SystemUsage = preSystemUsage
            },
            MemoryStats = new MemoryStats { Usage = memUsage, Limit = memLimit }
        };

    // xUnit's Assert.NotNull returns void in this version, so this exists purely to let
    // assertions chain off the data in one line instead of a separate NotNull + null-forgive pair.
    private static TData GetData<TData>(Result<TData> result) where TData : class
    {
        Assert.NotNull(result.Data);
        return result.Data!;
    }

    private static void SetupStats(Mock<IContainerOperations> containerOperations, string id, ContainerStatsResponse? stats)
    {
        containerOperations
            .Setup(c => c.GetContainerStatsAsync(
                id,
                It.IsAny<ContainerStatsParameters>(),
                It.IsAny<IProgress<ContainerStatsResponse>>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, ContainerStatsParameters, IProgress<ContainerStatsResponse>, CancellationToken>(
                (_, _, progress, _) =>
                {
                    if (stats is not null)
                        progress.Report(stats);
                })
            .Returns(Task.CompletedTask);
    }

    // ---------------------------------------------------------------- GetContainersAsync

    [Fact]
    public async Task GetContainersAsync_ReturnStandaloneAndStacks_WhenContainersBelongToMultipleGroups()
    {
        var service = CreateService(out var containerOperations);

        var standaloneContainer = CreateContainer("c1", "my-container", "running", null, new DateTime(2026, 1, 1));
        var unknownStateContainer = CreateContainer("c2", "weird-container", "some-bogus-state", null, new DateTime(2026, 1, 2));
        var stackContainerOld = CreateContainer("c3", "stack-a", "running", "winboat", new DateTime(2026, 1, 1));
        var stackContainerNew = CreateContainer("c4", "stack-b", "exited", "winboat", new DateTime(2026, 1, 5));

        containerOperations
            .Setup(c => c.ListContainersAsync(It.IsAny<ContainersListParameters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IList<ContainerListResponse>)
                [standaloneContainer, unknownStateContainer, stackContainerOld, stackContainerNew]);

        var result = await service.GetContainersAsync();

        Assert.True(result.Success);
        var data = GetData(result);

        Assert.Equal(2, data.Standalone.Count);
        Assert.Contains(data.Standalone, c => c.Name == "my-container" && c.State == ContainerState.Running);
        Assert.Contains(data.Standalone, c => c.Name == "weird-container" && c.State == ContainerState.Unknown);

        var stack = Assert.Single(data.Stacks);
        Assert.Equal("winboat", stack.ProjectName);
        Assert.Equal(2, stack.TotalCount);
        Assert.Equal(1, stack.RunningCount);
        Assert.Equal(new DateTime(2026, 1, 1), stack.RepresentativeStartedAt);
        Assert.Null(stack.AggregateCpuPercent);
        Assert.Null(stack.AggregateMemUsedMb);
    }

    [Fact]
    public async Task GetContainersAsync_ReturnEmptyResponse_WhenNoContainersExist()
    {
        var service = CreateService(out var containerOperations);

        containerOperations
            .Setup(c => c.ListContainersAsync(It.IsAny<ContainersListParameters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IList<ContainerListResponse>)[]);

        var result = await service.GetContainersAsync();

        Assert.True(result.Success);
        var data = GetData(result);
        Assert.Empty(data.Standalone);
        Assert.Empty(data.Stacks);
    }

    // ---------------------------------------------------------------- StartContainerAsync (exception matrix)

    [Fact]
    public async Task StartContainerAsync_ReturnSuccess_WhenContainerStartsSuccessfully()
    {
        var service = CreateService(out var containerOperations);
        containerOperations
            .Setup(c => c.StartContainerAsync("c1", It.IsAny<ContainerStartParameters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await service.StartContainerAsync("c1");

        Assert.True(result.Success);
    }

    [Fact]
    public async Task StartContainerAsync_ReturnNotFoundResult_WhenContainerDoesNotExist()
    {
        var service = CreateService(out var containerOperations);
        containerOperations
            .Setup(c => c.StartContainerAsync("missing", It.IsAny<ContainerStartParameters>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DockerContainerNotFoundException(HttpStatusCode.NotFound, "no such container"));

        var result = await service.StartContainerAsync("missing");

        Assert.False(result.Success);
        Assert.Equal(ResultErrorKind.NotFound, result.ErrorKind);
    }

    [Fact]
    public async Task StartContainerAsync_ReturnNotFoundResult_WhenImageDoesNotExist()
    {
        var service = CreateService(out var containerOperations);
        containerOperations
            .Setup(c => c.StartContainerAsync("c1", It.IsAny<ContainerStartParameters>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DockerImageNotFoundException(HttpStatusCode.NotFound, "no such image"));

        var result = await service.StartContainerAsync("c1");

        Assert.False(result.Success);
        Assert.Equal(ResultErrorKind.NotFound, result.ErrorKind);
    }

    [Fact]
    public async Task StartContainerAsync_ReturnNotFoundResult_WhenNetworkDoesNotExist()
    {
        var service = CreateService(out var containerOperations);
        containerOperations
            .Setup(c => c.StartContainerAsync("c1", It.IsAny<ContainerStartParameters>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DockerNetworkNotFoundException(HttpStatusCode.NotFound, "no such network"));

        var result = await service.StartContainerAsync("c1");

        Assert.False(result.Success);
        Assert.Equal(ResultErrorKind.NotFound, result.ErrorKind);
    }

    [Fact]
    public async Task StartContainerAsync_ReturnNotFoundResult_WhenPluginDoesNotExist()
    {
        var service = CreateService(out var containerOperations);
        containerOperations
            .Setup(c => c.StartContainerAsync("c1", It.IsAny<ContainerStartParameters>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DockerPluginNotFoundException(HttpStatusCode.NotFound, "no such plugin"));

        var result = await service.StartContainerAsync("c1");

        Assert.False(result.Success);
        Assert.Equal(ResultErrorKind.NotFound, result.ErrorKind);
    }

    [Fact]
    public async Task StartContainerAsync_ReturnDaemonErrorResult_WhenDockerApiExceptionThrown()
    {
        var service = CreateService(out var containerOperations);
        containerOperations
            .Setup(c => c.StartContainerAsync("c1", It.IsAny<ContainerStartParameters>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DockerApiException(HttpStatusCode.InternalServerError, "daemon blew up"));

        var result = await service.StartContainerAsync("c1");

        Assert.False(result.Success);
        Assert.Equal(ResultErrorKind.DaemonError, result.ErrorKind);
    }

    [Fact]
    public async Task StartContainerAsync_ReturnUnreachableResult_WhenHttpRequestExceptionThrown()
    {
        var service = CreateService(out var containerOperations);
        containerOperations
            .Setup(c => c.StartContainerAsync("c1", It.IsAny<ContainerStartParameters>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("connection refused"));

        var result = await service.StartContainerAsync("c1");

        Assert.False(result.Success);
        Assert.Equal(ResultErrorKind.Unreachable, result.ErrorKind);
    }

    [Fact]
    public async Task StartContainerAsync_ReturnUnknownErrorResult_WhenUnexpectedExceptionThrown()
    {
        var service = CreateService(out var containerOperations);
        containerOperations
            .Setup(c => c.StartContainerAsync("c1", It.IsAny<ContainerStartParameters>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("something unexpected"));

        var result = await service.StartContainerAsync("c1");

        Assert.False(result.Success);
        Assert.Equal(ResultErrorKind.Unknown, result.ErrorKind);
    }

    // ---------------------------------------------------------------- StopContainerAsync / RestartContainerAsync

    [Fact]
    public async Task StopContainerAsync_ReturnSuccess_WhenContainerStopsSuccessfully()
    {
        var service = CreateService(out var containerOperations);
        containerOperations
            .Setup(c => c.StopContainerAsync("c1", It.IsAny<ContainerStopParameters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await service.StopContainerAsync("c1");

        Assert.True(result.Success);
    }

    [Fact]
    public async Task RestartContainerAsync_ReturnSuccess_WhenContainerRestartsSuccessfully()
    {
        var service = CreateService(out var containerOperations);
        containerOperations
            .Setup(c => c.RestartContainerAsync("c1", It.IsAny<ContainerRestartParameters>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await service.RestartContainerAsync("c1");

        Assert.True(result.Success);
    }

    // ---------------------------------------------------------------- GetContainerStatsAsync

    [Fact]
    public async Task GetContainerStatsAsync_ReturnComputedCpuAndMemoryStats_WhenStatsAreAvailable()
    {
        var service = CreateService(out var containerOperations);
        var stats = CreateStats(
            totalUsage: 2_000_000, preTotalUsage: 1_000_000,
            systemUsage: 20_000_000, preSystemUsage: 10_000_000,
            onlineCpus: 2, percpuUsage: null,
            memUsage: 100 * 1024 * 1024, memLimit: 200 * 1024 * 1024);
        SetupStats(containerOperations, "c1", stats);

        var result = await service.GetContainerStatsAsync("c1");

        Assert.True(result.Success);
        var data = GetData(result);
        // cpuDelta=1_000_000, systemDelta=10_000_000, onlineCpus=2 -> (1_000_000/10_000_000)*2*100 = 20
        Assert.Equal(20f, data.CpuPercent, precision: 3);
        Assert.Equal(100, data.MemUsedMb, precision: 3);
        Assert.Equal(200, data.MemLimitMb, precision: 3);
    }

    [Fact]
    public async Task GetContainerStatsAsync_ReturnZeroCpuPercent_WhenSystemDeltaIsNotPositive()
    {
        var service = CreateService(out var containerOperations);
        var stats = CreateStats(
            totalUsage: 2_000_000, preTotalUsage: 1_000_000,
            systemUsage: 10_000_000, preSystemUsage: 10_000_000, // no system delta
            onlineCpus: 2, percpuUsage: null,
            memUsage: 0, memLimit: 0);
        SetupStats(containerOperations, "c1", stats);

        var result = await service.GetContainerStatsAsync("c1");

        Assert.True(result.Success);
        Assert.Equal(0f, GetData(result).CpuPercent);
    }

    [Fact]
    public async Task GetContainerStatsAsync_ReturnZeroCpuPercent_WhenCpuDeltaIsNotPositive()
    {
        var service = CreateService(out var containerOperations);
        var stats = CreateStats(
            totalUsage: 1_000_000, preTotalUsage: 1_000_000, // no cpu delta
            systemUsage: 20_000_000, preSystemUsage: 10_000_000,
            onlineCpus: 2, percpuUsage: null,
            memUsage: 0, memLimit: 0);
        SetupStats(containerOperations, "c1", stats);

        var result = await service.GetContainerStatsAsync("c1");

        Assert.True(result.Success);
        Assert.Equal(0f, GetData(result).CpuPercent);
    }

    [Fact]
    public async Task GetContainerStatsAsync_FallBackToPercpuUsageCount_WhenOnlineCpusIsZero()
    {
        var service = CreateService(out var containerOperations);
        var stats = CreateStats(
            totalUsage: 2_000_000, preTotalUsage: 1_000_000,
            systemUsage: 20_000_000, preSystemUsage: 10_000_000,
            onlineCpus: 0, percpuUsage: [0, 0, 0, 0], // 4 entries
            memUsage: 0, memLimit: 0);
        SetupStats(containerOperations, "c1", stats);

        var result = await service.GetContainerStatsAsync("c1");

        Assert.True(result.Success);
        // cpuDelta=1_000_000, systemDelta=10_000_000, onlineCpus falls back to 4 -> 40
        Assert.Equal(40f, GetData(result).CpuPercent, precision: 3);
    }

    [Fact]
    public async Task GetContainerStatsAsync_FallBackToSingleCpu_WhenOnlineCpusAndPercpuUsageAreBothUnavailable()
    {
        var service = CreateService(out var containerOperations);
        var stats = CreateStats(
            totalUsage: 2_000_000, preTotalUsage: 1_000_000,
            systemUsage: 20_000_000, preSystemUsage: 10_000_000,
            onlineCpus: 0, percpuUsage: null,
            memUsage: 0, memLimit: 0);
        SetupStats(containerOperations, "c1", stats);

        var result = await service.GetContainerStatsAsync("c1");

        Assert.True(result.Success);
        // cpuDelta=1_000_000, systemDelta=10_000_000, onlineCpus falls back to 1 -> 10
        Assert.Equal(10f, GetData(result).CpuPercent, precision: 3);
    }

    [Fact]
    public async Task GetContainerStatsAsync_ReturnUnknownErrorResult_WhenDaemonNeverReportsStats()
    {
        var service = CreateService(out var containerOperations);
        SetupStats(containerOperations, "c1", stats: null);

        var result = await service.GetContainerStatsAsync("c1");

        Assert.False(result.Success);
        Assert.Equal(ResultErrorKind.Unknown, result.ErrorKind);
    }

    // ---------------------------------------------------------------- GetComposeStackStatsAsync

    [Fact]
    public async Task GetComposeStackStatsAsync_ReturnAggregatedStackStats_WhenContainersMatchTheStack()
    {
        var service = CreateService(out var containerOperations);

        var container1 = CreateContainer("c1", "stack-web", "running", "winboat", new DateTime(2026, 1, 1));
        var container2 = CreateContainer("c2", "stack-cache", "running", "winboat", new DateTime(2026, 1, 3));

        containerOperations
            .Setup(c => c.ListContainersAsync(
                It.Is<ContainersListParameters>(p =>
                    p.Filters != null &&
                    p.Filters["label"].ContainsKey("com.docker.compose.project=winboat")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IList<ContainerListResponse>)[container1, container2]);

        SetupStats(containerOperations, "c1",
            CreateStats(2_000_000, 1_000_000, 20_000_000, 10_000_000, 1, null, 50 * 1024 * 1024, 100 * 1024 * 1024));
        SetupStats(containerOperations, "c2",
            CreateStats(2_000_000, 1_000_000, 20_000_000, 10_000_000, 1, null, 30 * 1024 * 1024, 100 * 1024 * 1024));

        var result = await service.GetComposeStackStatsAsync("winboat");

        Assert.True(result.Success);
        var stack = GetData(result);
        Assert.Equal("winboat", stack.ProjectName);
        Assert.Equal(2, stack.TotalCount);
        Assert.Equal(2, stack.RunningCount);
        Assert.Equal(new DateTime(2026, 1, 1), stack.RepresentativeStartedAt);
        Assert.NotNull(stack.AggregateCpuPercent);
        Assert.NotNull(stack.AggregateMemUsedMb);
        Assert.Equal(20f, stack.AggregateCpuPercent.Value, precision: 3); // 10 + 10
        Assert.Equal(80, stack.AggregateMemUsedMb.Value, precision: 3); // 50 + 30
    }

    [Fact]
    public async Task GetComposeStackStatsAsync_ReturnNotFoundResult_WhenNoContainersMatchTheStackName()
    {
        var service = CreateService(out var containerOperations);
        containerOperations
            .Setup(c => c.ListContainersAsync(It.IsAny<ContainersListParameters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IList<ContainerListResponse>)[]);

        var result = await service.GetComposeStackStatsAsync("doesnotexist");

        Assert.False(result.Success);
        Assert.Equal(ResultErrorKind.NotFound, result.ErrorKind);
    }
}
