using System.Text.Json;
using System.Text.Json.Serialization;
using Docker.DotNet;
using Microsoft.Extensions.Options;
using PlasmaDockerMonitor.Api.Endpoints;
using PlasmaDockerMonitor.Api.Enums;
using PlasmaDockerMonitor.Api.Options;
using PlasmaDockerMonitor.Api.Services;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

builder.Services.AddOptions<DockerOptions>()
    .Bind(builder.Configuration.GetSection("Docker"))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddSingleton<IDockerClient>(provider =>
{
    var dockerConfig =  provider.GetRequiredService<IOptions<DockerOptions>>().Value;
    return string.IsNullOrWhiteSpace(dockerConfig.Uri)
        ? new DockerClientConfiguration().CreateClient()
        : new DockerClientConfiguration(new Uri(dockerConfig.Uri)).CreateClient();
});
builder.Services.AddSingleton<IDockerService, LocalDockerService>();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter<ContainerState>(JsonNamingPolicy.CamelCase));
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter<ResultErrorKind>(JsonNamingPolicy.CamelCase));
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi("/openapi/{documentName}.yaml");
    app.MapScalarApiReference(opts =>
    {
        opts.WithOpenApiRoutePattern("/openapi/{documentName}.yaml");
    });
}

app.MapGet("/health-check", () => Results.Ok("alive"));

app.MapContainerEndpoints();

app.Run();
