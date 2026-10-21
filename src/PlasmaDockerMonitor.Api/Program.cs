using System.Text.Json;
using System.Text.Json.Serialization;
using Docker.DotNet;
using Microsoft.Extensions.Options;
using PlasmaDockerMonitor.Api;
using PlasmaDockerMonitor.Api.Endpoints;
using PlasmaDockerMonitor.Api.Enums;
using PlasmaDockerMonitor.Api.Ioc;
using PlasmaDockerMonitor.Api.Options;
using PlasmaDockerMonitor.Api.Services;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSerilogLogging(builder.Configuration);

builder.Services.AddOpenApi(AppVersion.Current, options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info.Version = AppVersion.Current;
        return Task.CompletedTask;
    });
});

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

var logger = app.Services.GetRequiredService<ILogger<Program>>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi("/openapi/{documentName}.yaml");
    app.MapScalarApiReference(opts =>
    {
        opts.WithOpenApiRoutePattern("/openapi/{documentName}.yaml")
            .AddDocument(AppVersion.Current);
    });
    logger.LogTrace("Development tools attached");
}

app.MapGet("/health-check", () => Results.Ok("alive"));

app.MapContainerEndpoints(logger);
app.MapComposeEndpoint(logger);

app.Run();
