using Docker.DotNet;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace PlasmaDockerMonitor.Api.Tests;

public class ProgramShould
{
    [Fact]
    public async Task RespondSuccessfully_WhenRequestingScalarUi()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/scalar");

        Assert.True(response.IsSuccessStatusCode, $"expected success, got {response.StatusCode}");
    }

    [Fact]
    public void RegisterDockerClient_WithPlatformDefaultSocket_WhenUriIsNotConfigured()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?> { ["Docker:Uri"] = null }));
        });

        var dockerClient = factory.Services.GetRequiredService<IDockerClient>();

        Assert.NotNull(dockerClient.Configuration.EndpointBaseUri);
    }

    [Fact]
    public void RegisterDockerClient_WithConfiguredUri_WhenUriIsProvided()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Docker:Uri"] = "tcp://localhost:2375"
                }));
        });

        var dockerClient = factory.Services.GetRequiredService<IDockerClient>();

        Assert.Equal("tcp://localhost:2375/", dockerClient.Configuration.EndpointBaseUri.ToString());
    }
}
