using Constants;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ServiceDiscovery;

using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

using Serilog;

namespace ServiceDefaults;

// Adds common Aspire services: service discovery, resilience, health checks, and OpenTelemetry.
// This project should be referenced by each service project in your solution.
// To learn more about using this project, see https://aka.ms/dotnet/aspire/service-defaults
public static class Extensions
{
    private const string Live = "live";

    public static void AddServiceDefaults<TBuilder>(this TBuilder builder, string[]? customMetersNames = null) where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .WriteTo.OpenTelemetry(otel =>
            {
                otel.Endpoint = builder.Configuration[OtelConstants.OtelExporterEndpointName]!;

                otel.Protocol = Serilog.Sinks.OpenTelemetry.OtlpProtocol.HttpProtobuf;
            })
            .CreateLogger();

        builder.Services.AddSerilog();

        builder.AddHealthChecksAndDiscovery();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            // Turn on resilience by default
            http.AddStandardResilienceHandler();

            // Turn on service discovery by default
            http.AddServiceDiscovery();
        });

        builder.Services.Configure<ServiceDiscoveryOptions>(options => { options.AllowedSchemes = ["http"]; });
        builder.Logging.AddConsole();

        var otlpEndpointString = builder.Configuration[OtelConstants.OtelExporterEndpointName]
                                 ?? Environment.GetEnvironmentVariable(OtelConstants.OtelExporterEndpointName)!;

        if (otlpEndpointString is null)
        {
            throw new ServiceDefaultRegistrationException(
                $"OpenTelemetry Exporter Endpoint is not configured. Please set the '{OtelConstants.OtelExporterEndpointName}' configuration value or environment variable.");
        }

        var otlpProtocol = builder.Configuration[OtelConstants.OtelExporterProtocolName]
                           ?? Environment.GetEnvironmentVariable(OtelConstants.OtelExporterProtocolName)!;

        if (otlpProtocol is null)
        {
            throw new ServiceDefaultRegistrationException($"OpenTelemetry Exporter Protocol is not configured. Please set the '{OtelConstants.OtelExporterProtocolName}' configuration value or environment variable.");
        }

        var otlpEndpoint = new Uri(otlpEndpointString);
        var otlpProto = otlpProtocol.Equals(OtelConstants.OtelExporterProtocolValueGrpc, StringComparison.OrdinalIgnoreCase)
            ? OtlpExportProtocol.Grpc
            : OtlpExportProtocol.HttpProtobuf;

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource =>
            {
                var resourceAttributes = Environment.GetEnvironmentVariable("OTEL_RESOURCE_ATTRIBUTES");

                if (resourceAttributes is null)
                {
                    throw new ServiceDefaultRegistrationException("OTEL_RESOURCE_ATTRIBUTES environment variable is not set.");
                }

                string? instanceId = null;

                if (!string.IsNullOrEmpty(resourceAttributes))
                {
                    instanceId = resourceAttributes.Split(',')
                        .Select(part => part.Split('='))
                        .Where(parts => parts.Length == 2 && parts[0].Trim() == "service.instance.id")
                        .Select(parts => parts[1].Trim())
                        .FirstOrDefault();
                }

                resource.AddService(
                    serviceName: OtelConstants.AspireServiceName,
                    serviceInstanceId: instanceId,
                    autoGenerateServiceInstanceId: instanceId is null);

                resource.AddEnvironmentVariableDetector();
            })
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();

                if (customMetersNames is not null)
                {
                    foreach (var meterName in customMetersNames)
                    {
                        metrics.AddMeter(meterName);
                    }
                }

                metrics.AddOtlpExporter(o =>
                {
                    o.Endpoint = otlpProto == OtlpExportProtocol.HttpProtobuf
                        ? new Uri(otlpEndpoint, OtelConstants.MetricsRoute)
                        : otlpEndpoint;
                    o.Protocol = otlpProto;
                });
            })
            .WithTracing(tracing =>
            {
                tracing.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddEntityFrameworkCoreInstrumentation();

                tracing.AddOtlpExporter(o =>
                {
                    o.Endpoint = otlpProto == OtlpExportProtocol.HttpProtobuf
                        ? new Uri(otlpEndpoint, OtelConstants.TracesRoute)
                        : otlpEndpoint;
                    o.Protocol = otlpProto;
                });
            });

        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;

            logging.AddOtlpExporter(o =>
            {
                o.Endpoint = otlpProto == OtlpExportProtocol.HttpProtobuf
                    ? new Uri(otlpEndpoint, OtelConstants.LogsRoute)
                    : otlpEndpoint;
                o.Protocol = otlpProto;
            });
        });
    }

    public static void AddHealthChecksAndDiscovery<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            // Add a default liveness check to ensure app is responsive
            .AddCheck("self", () => HealthCheckResult.Healthy(), [Live]);

        builder.Services.AddServiceDiscovery();
    }

    public static void MapDefaultEndpoints(this WebApplication app)
    {
        // All health checks must pass for app to be considered ready to accept traffic after starting
        app.MapHealthChecks("/health");

        // Only health checks tagged with the "live" tag must pass for app to be considered alive
        app.MapHealthChecks("/alive", new HealthCheckOptions { Predicate = r => r.Tags.Contains(Live) });
    }
}
