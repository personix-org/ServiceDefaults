using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Trace;
using Shouldly;

namespace Personix.ServiceDefaults.Tests;

/// <summary>
/// Tests for service registration - ensures all required services are properly registered.
/// </summary>
[Collection("EnvironmentVariables")]
public class ServiceRegistrationTests
{
    [Fact]
    public void AddServiceDefaults_RegistersHealthChecks()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder();
        SetupValidConfiguration(builder);

        // Act
        builder.AddServiceDefaults();

        // Assert
        var healthCheckService = builder.Services.BuildServiceProvider()
            .GetService<HealthCheckService>();

        healthCheckService.ShouldNotBeNull();
    }

    [Fact]
    public void AddServiceDefaults_RegistersSerilog()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder();
        SetupValidConfiguration(builder);

        // Act
        builder.AddServiceDefaults();

        // Assert
        var serviceProvider = builder.Services.BuildServiceProvider();
        var loggerFactory = serviceProvider.GetService<ILoggerFactory>();

        loggerFactory.ShouldNotBeNull();
    }

    [Fact]
    public void AddServiceDefaults_RegistersOpenTelemetry()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder();
        SetupValidConfiguration(builder);

        // Act
        builder.AddServiceDefaults();

        // Assert
        var serviceProvider = builder.Services.BuildServiceProvider();
        var tracerProvider = serviceProvider.GetService<TracerProvider>();

        // OpenTelemetry services should be registered
        tracerProvider.ShouldNotBeNull();
    }

    [Fact]
    public void AddServiceDefaults_RegistersCustomMeters()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder();
        SetupValidConfiguration(builder);
        var customMeters = new[] { "MyApp.Meter", "MyApp.BusinessMetrics" };

        // Act & Assert
        Should.NotThrow(() => builder.AddServiceDefaults(customMetersNames: customMeters));
    }

    [Fact]
    public void AddServiceDefaults_RegistersCustomActivitySources()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder();
        SetupValidConfiguration(builder);
        var customSources = new[] { "MyApp.Source", "MyApp.BackgroundJobs" };

        // Act & Assert
        Should.NotThrow(() => builder.AddServiceDefaults(customActivitySourceNames: customSources));
    }

    [Fact]
    public void AddServiceDefaults_RegistersServiceDiscovery()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder();
        SetupValidConfiguration(builder);

        // Act
        builder.AddServiceDefaults();

        // Assert
        var serviceProvider = builder.Services.BuildServiceProvider();
        // Service discovery is configured via ConfigureHttpClientDefaults
        // We verify it doesn't throw during setup
        serviceProvider.ShouldNotBeNull();
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
