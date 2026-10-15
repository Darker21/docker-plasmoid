using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PlasmaDockerMonitor.Api.Enums;
using PlasmaDockerMonitor.Api.Models;
using PlasmaDockerMonitor.Api.Services;

namespace PlasmaDockerMonitor.Api.Tests.Endpoints;

public class ContainerEndpointsShould
{
    private static HttpClient CreateClient(Mock<IDockerService> mockDockerService)
    {
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services => services.AddSingleton(mockDockerService.Object));
        });

        return factory.CreateClient();
    }

    // ---------------------------------------------------------------- GET /containers

    [Fact]
    public async Task GetContainers_ReturnOk_WhenServiceSucceeds()
    {
        var mock = new Mock<IDockerService>();
        mock.Setup(s => s.GetContainersAsync(It.IsAny<string?>()))
            .ReturnsAsync(Result<ContainerSummaryResponse>.Ok(
                new ContainerSummaryResponse { Standalone = [], Stacks = [] }));

        var response = await CreateClient(mock).GetAsync("/containers");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"success\":true", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task GetContainers_ReturnNotFound_WhenServiceReturnsNotFound()
    {
        var mock = new Mock<IDockerService>();
        mock.Setup(s => s.GetContainersAsync(It.IsAny<string?>()))
            .ReturnsAsync(Result<ContainerSummaryResponse>.Fail(ResultErrorKind.NotFound, "not found"));

        var response = await CreateClient(mock).GetAsync("/containers");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetContainers_ReturnInternalServerError_WhenServiceReturnsOtherError()
    {
        var mock = new Mock<IDockerService>();
        mock.Setup(s => s.GetContainersAsync(It.IsAny<string?>()))
            .ReturnsAsync(Result<ContainerSummaryResponse>.Fail(ResultErrorKind.DaemonError, "boom"));

        var response = await CreateClient(mock).GetAsync("/containers");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    // ---------------------------------------------------------------- POST /containers/{id}/start

    [Fact]
    public async Task StartContainer_ReturnAccepted_WhenServiceSucceeds()
    {
        var mock = new Mock<IDockerService>();
        mock.Setup(s => s.StartContainerAsync("c1")).ReturnsAsync(Result.Ok());

        var response = await CreateClient(mock).PostAsync("/containers/c1/start", null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task StartContainer_ReturnNotFound_WhenServiceReturnsNotFound()
    {
        var mock = new Mock<IDockerService>();
        mock.Setup(s => s.StartContainerAsync("c1"))
            .ReturnsAsync(Result.Fail(ResultErrorKind.NotFound, "not found"));

        var response = await CreateClient(mock).PostAsync("/containers/c1/start", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task StartContainer_ReturnInternalServerError_WhenServiceReturnsOtherError()
    {
        var mock = new Mock<IDockerService>();
        mock.Setup(s => s.StartContainerAsync("c1"))
            .ReturnsAsync(Result.Fail(ResultErrorKind.Unreachable, "boom"));

        var response = await CreateClient(mock).PostAsync("/containers/c1/start", null);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task StartContainer_ReturnBadRequest_WhenContainerIdIsWhitespace()
    {
        var mock = new Mock<IDockerService>();

        var response = await CreateClient(mock).PostAsync("/containers/%20/start", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        mock.Verify(s => s.StartContainerAsync(It.IsAny<string>()), Times.Never);
    }

    // ---------------------------------------------------------------- POST /containers/{id}/stop

    [Fact]
    public async Task StopContainer_ReturnAccepted_WhenServiceSucceeds()
    {
        var mock = new Mock<IDockerService>();
        mock.Setup(s => s.StopContainerAsync("c1")).ReturnsAsync(Result.Ok());

        var response = await CreateClient(mock).PostAsync("/containers/c1/stop", null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task StopContainer_ReturnNotFound_WhenServiceReturnsNotFound()
    {
        var mock = new Mock<IDockerService>();
        mock.Setup(s => s.StopContainerAsync("c1"))
            .ReturnsAsync(Result.Fail(ResultErrorKind.NotFound, "not found"));

        var response = await CreateClient(mock).PostAsync("/containers/c1/stop", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task StopContainer_ReturnInternalServerError_WhenServiceReturnsOtherError()
    {
        var mock = new Mock<IDockerService>();
        mock.Setup(s => s.StopContainerAsync("c1"))
            .ReturnsAsync(Result.Fail(ResultErrorKind.Unknown, "boom"));

        var response = await CreateClient(mock).PostAsync("/containers/c1/stop", null);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task StopContainer_ReturnBadRequest_WhenContainerIdIsWhitespace()
    {
        var mock = new Mock<IDockerService>();

        var response = await CreateClient(mock).PostAsync("/containers/%20/stop", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        mock.Verify(s => s.StopContainerAsync(It.IsAny<string>()), Times.Never);
    }

    // ---------------------------------------------------------------- POST /containers/{id}/restart

    [Fact]
    public async Task RestartContainer_ReturnAccepted_WhenServiceSucceeds()
    {
        var mock = new Mock<IDockerService>();
        mock.Setup(s => s.RestartContainerAsync("c1")).ReturnsAsync(Result.Ok());

        var response = await CreateClient(mock).PostAsync("/containers/c1/restart", null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task RestartContainer_ReturnNotFound_WhenServiceReturnsNotFound()
    {
        var mock = new Mock<IDockerService>();
        mock.Setup(s => s.RestartContainerAsync("c1"))
            .ReturnsAsync(Result.Fail(ResultErrorKind.NotFound, "not found"));

        var response = await CreateClient(mock).PostAsync("/containers/c1/restart", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RestartContainer_ReturnInternalServerError_WhenServiceReturnsOtherError()
    {
        var mock = new Mock<IDockerService>();
        mock.Setup(s => s.RestartContainerAsync("c1"))
            .ReturnsAsync(Result.Fail(ResultErrorKind.DaemonError, "boom"));

        var response = await CreateClient(mock).PostAsync("/containers/c1/restart", null);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task RestartContainer_ReturnBadRequest_WhenContainerIdIsWhitespace()
    {
        var mock = new Mock<IDockerService>();

        var response = await CreateClient(mock).PostAsync("/containers/%20/restart", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        mock.Verify(s => s.RestartContainerAsync(It.IsAny<string>()), Times.Never);
    }

    // ---------------------------------------------------------------- GET /containers/{id}/stats

    [Fact]
    public async Task GetContainerStats_ReturnOk_WhenServiceSucceeds()
    {
        var mock = new Mock<IDockerService>();
        mock.Setup(s => s.GetContainerStatsAsync("c1"))
            .ReturnsAsync(Result<ContainerStatsDto>.Ok(new ContainerStatsDto()));

        var response = await CreateClient(mock).GetAsync("/containers/c1/stats");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetContainerStats_ReturnNotFound_WhenServiceReturnsNotFound()
    {
        var mock = new Mock<IDockerService>();
        mock.Setup(s => s.GetContainerStatsAsync("c1"))
            .ReturnsAsync(Result<ContainerStatsDto>.Fail(ResultErrorKind.NotFound, "not found"));

        var response = await CreateClient(mock).GetAsync("/containers/c1/stats");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetContainerStats_ReturnInternalServerError_WhenServiceReturnsOtherError()
    {
        var mock = new Mock<IDockerService>();
        mock.Setup(s => s.GetContainerStatsAsync("c1"))
            .ReturnsAsync(Result<ContainerStatsDto>.Fail(ResultErrorKind.Unreachable, "boom"));

        var response = await CreateClient(mock).GetAsync("/containers/c1/stats");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task GetContainerStats_ReturnBadRequest_WhenContainerIdIsWhitespace()
    {
        var mock = new Mock<IDockerService>();

        var response = await CreateClient(mock).GetAsync("/containers/%20/stats");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        mock.Verify(s => s.GetContainerStatsAsync(It.IsAny<string>()), Times.Never);
    }

    // ---------------------------------------------------------------- GET /compose/{stackName}/stats

    [Fact]
    public async Task GetComposeStackStats_ReturnOk_WhenServiceSucceeds()
    {
        var mock = new Mock<IDockerService>();
        mock.Setup(s => s.GetComposeStackStatsAsync("winboat"))
            .ReturnsAsync(Result<ComposeStackDto>.Ok(new ComposeStackDto
            {
                ProjectName = "winboat",
                Containers = [],
                RunningCount = 0,
                TotalCount = 0,
                RepresentativeStartedAt = DateTime.UtcNow
            }));

        var response = await CreateClient(mock).GetAsync("/compose/winboat/stats");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetComposeStackStats_ReturnNotFound_WhenServiceReturnsNotFound()
    {
        var mock = new Mock<IDockerService>();
        mock.Setup(s => s.GetComposeStackStatsAsync("winboat"))
            .ReturnsAsync(Result<ComposeStackDto>.Fail(ResultErrorKind.NotFound, "not found"));

        var response = await CreateClient(mock).GetAsync("/compose/winboat/stats");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetComposeStackStats_ReturnInternalServerError_WhenServiceReturnsOtherError()
    {
        var mock = new Mock<IDockerService>();
        mock.Setup(s => s.GetComposeStackStatsAsync("winboat"))
            .ReturnsAsync(Result<ComposeStackDto>.Fail(ResultErrorKind.DaemonError, "boom"));

        var response = await CreateClient(mock).GetAsync("/compose/winboat/stats");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task GetComposeStackStats_ReturnBadRequest_WhenStackNameIsWhitespace()
    {
        var mock = new Mock<IDockerService>();

        var response = await CreateClient(mock).GetAsync("/compose/%20/stats");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        mock.Verify(s => s.GetComposeStackStatsAsync(It.IsAny<string>()), Times.Never);
    }
}
