using System.ComponentModel;
using PlasmaDockerMonitor.Api.Enums;
using PlasmaDockerMonitor.Api.Models;
using PlasmaDockerMonitor.Api.Services;

namespace PlasmaDockerMonitor.Api.Endpoints;

internal static class ContainerEndpoints
{
    public static IEndpointRouteBuilder MapContainerEndpoints(this IEndpointRouteBuilder endpoints, ILogger logger)
    {
        logger.LogTrace("Mapping container endpoints");

        var group = endpoints.MapGroup("/containers")
            .WithTags("Containers")
            .WithDisplayName("Containers");

        group.MapGet("/",
                async (IDockerService dockerService,
                    ILogger<Program> requestLogger,
                    [Description("Comma-seperated list of container names to exclude")]
                    string? exclude) =>
                {
                    requestLogger.LogInformation("GET /containers received");
                    var result = await dockerService.GetContainersAsync(exclude);
                    if (result.Success)
                        return Results.Ok(result);

                    return result.ErrorKind switch
                    {
                        ResultErrorKind.NotFound => Results.NotFound(result),
                        _ => Results.InternalServerError(result)
                    };
                }).Produces<Result<ContainerSummaryResponse>>()
            .Produces<Result>(StatusCodes.Status500InternalServerError);

        group.MapPost("/{containerId}/start", async (IDockerService dockerService,
                ILogger<Program> requestLogger,
                [Description("The id of the container to start")] string containerId) =>
            {
                requestLogger.LogInformation("POST /containers/{ContainerId}/start received", containerId);
                if (string.IsNullOrWhiteSpace(containerId))
                {
                    requestLogger.LogWarning("Container id is required");
                    return Results.BadRequest();
                }

                var result = await dockerService.StartContainerAsync(containerId);
                if (result.Success)
                    return Results.Accepted("/containers/" + containerId, result);

                return result.ErrorKind switch
                {
                    ResultErrorKind.NotFound => Results.NotFound(result),
                    _ => Results.InternalServerError(result)
                };
            }).Produces<Result>()
            .Produces<Result>(StatusCodes.Status404NotFound)
            .Produces<Result>(StatusCodes.Status500InternalServerError);

        group.MapPost("/{containerId}/stop", async (IDockerService dockerService,
                ILogger<Program> requestLogger,
                [Description("The id of the container to stop")] string containerId) =>
            {
                requestLogger.LogInformation("POST /containers/{ContainerId}/stop received", containerId);
                if (string.IsNullOrWhiteSpace(containerId))
                {
                    requestLogger.LogWarning("Container id is required");
                    return Results.BadRequest();
                }

                var result = await dockerService.StopContainerAsync(containerId);
                if (result.Success)
                    return Results.Accepted("/containers/" + containerId, result);

                return result.ErrorKind switch
                {
                    ResultErrorKind.NotFound => Results.NotFound(result),
                    _ => Results.InternalServerError(result)
                };
            }).Produces<Result>()
            .Produces<Result>(StatusCodes.Status404NotFound)
            .Produces<Result>(StatusCodes.Status500InternalServerError);

        group.MapPost("/{containerId}/restart", async (IDockerService dockerService,
                ILogger<Program> requestLogger,
                [Description("The id of the container to restart")] string containerId) =>
            {
                requestLogger.LogInformation("POST /containers/{ContainerId}/restart received", containerId);
                if (string.IsNullOrWhiteSpace(containerId))
                {
                    requestLogger.LogWarning("Container id is required");
                    return Results.BadRequest();
                }

                var result = await dockerService.RestartContainerAsync(containerId);
                if (result.Success)
                    return Results.Accepted("/containers/" + containerId, result);

                return result.ErrorKind switch
                {
                    ResultErrorKind.NotFound => Results.NotFound(result),
                    _ => Results.InternalServerError(result)
                };
            }).Produces<Result>()
            .Produces<Result>(StatusCodes.Status404NotFound)
            .Produces<Result>(StatusCodes.Status500InternalServerError);

        group.MapGet("/{containerId}/stats", async (IDockerService dockerService,
                ILogger<Program> requestLogger,
                [Description("The id of the container to retrieve the stats for")] string containerId) =>
            {
                requestLogger.LogInformation("GET /containers/{ContainerId}/stats received", containerId);
                if (string.IsNullOrWhiteSpace(containerId))
                {
                    requestLogger.LogWarning("Container id is required");
                    return Results.BadRequest();
                }

                var result = await dockerService.GetStatsAsync(containerId);
                if (result.Success)
                    return Results.Ok(result);

                return result.ErrorKind switch
                {
                    ResultErrorKind.NotFound => Results.NotFound(result),
                    _ => Results.InternalServerError(result)
                };
            }).Produces<Result<ContainerStatsDto>>()
            .Produces<Result>(StatusCodes.Status404NotFound)
            .Produces<Result>(StatusCodes.Status500InternalServerError);

        return endpoints;
    }
}