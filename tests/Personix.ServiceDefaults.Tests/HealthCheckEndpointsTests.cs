using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Shouldly;

namespace Personix.ServiceDefaults.Tests;

/// <summary>
/// Tests for health check endpoints - ensures /health and /alive endpoints work correctly.
/// </summary>
[Collection("EnvironmentVariables")]
public class HealthCheckEndpointsTests
{
    [Fact]
    public async Task MapDefaultEndpoints_CreatesHealthEndpoint()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder();
        SetupValidConfiguration(builder);
        builder.WebHost.UseTestServer();

        builder.AddServiceDefaults();

        var app = builder.Build();
        app.MapDefaultEndpoints();

        await app.StartAsync();

        // Act
        var client = app.GetTestClient();
        var response = await client.GetAsync("/health");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task MapDefaultEndpoints_CreatesAliveEndpoint()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder();
        SetupValidConfiguration(builder);
        builder.WebHost.UseTestServer();

        builder.AddServiceDefaults();

        var app = builder.Build();
        app.MapDefaultEndpoints();

        await app.StartAsync();

        // Act
        var client = app.GetTestClient();
        var response = await client.GetAsync("/alive");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AliveEndpoint_OnlyChecksLiveTaggedHealthChecks()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder();
        SetupValidConfiguration(builder);
        builder.WebHost.UseTestServer();

        builder.AddServiceDefaults();

        // Add a failing health check without "live" tag
        builder.Services.AddHealthChecks()
            .AddCheck("database", () => HealthCheckResult.Unhealthy("DB is down"));

        var app = builder.Build();
        app.MapDefaultEndpoints();

        await app.StartAsync();

        // Act
        var client = app.GetTestClient();
        var aliveResponse = await client.GetAsync("/alive");
        var healthResponse = await client.GetAsync("/health");

        // Assert
        // /alive should pass (only checks "live" tagged checks, which are healthy)
        aliveResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        // /health should fail (checks all health checks including the failing database check)
        healthResponse.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
    }

    private static void SetupValidConfiguration(WebApplicationBuilder builder)
    {
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://localhost:4318",
            ["OTEL_EXPORTER_OTLP_PROTOCOL"] = "http/protobuf"
        });

        Environment.SetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT", "http://localhost:4318");
        Environment.SetEnvironmentVariable("OTEL_EXPORTER_OTLP_PROTOCOL", "http/protobuf");
        Environment.SetEnvironmentVariable("OTEL_RESOURCE_ATTRIBUTES", "service.instance.id=test-123");
    }
}
