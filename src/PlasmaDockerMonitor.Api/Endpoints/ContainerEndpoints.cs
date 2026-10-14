using System.ComponentModel;
using PlasmaDockerMonitor.Api.Enums;
using PlasmaDockerMonitor.Api.Models;
using PlasmaDockerMonitor.Api.Services;

namespace PlasmaDockerMonitor.Api.Endpoints;

internal static class ContainerEndpoints
{
    public static IEndpointRouteBuilder MapContainerEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/containers")
            .WithTags("Containers")
            .WithDisplayName("Containers");

        group.MapGet("/",
                async (IDockerService dockerService,
                    [Description("Comma-seperated list of container names to exclude")]
                    string? exclude) =>
                {
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
                [Description("The id of the container to start")] string containerId) =>
            {
                if (string.IsNullOrWhiteSpace(containerId))
                    return Results.BadRequest();

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
                [Description("The id of the container to stop")] string containerId) =>
            {
                if (string.IsNullOrWhiteSpace(containerId))
                    return Results.BadRequest();

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
                [Description("The id of the container to restart")] string containerId) =>
            {
                if (string.IsNullOrWhiteSpace(containerId))
                    return Results.BadRequest();

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
                [Description("The id of the container to retrieve the stats for")] string containerId) =>
            {
                if (string.IsNullOrWhiteSpace(containerId))
                    return Results.BadRequest();

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