using Constants;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
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

    public static void AddServiceDefaults<TBuilder>(this TBuilder builder, string[]? customMetersNames = null, string[]? customActivitySourceNames = null, string? serviceName = null) where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();

        // Read OTEL config first — used by both Serilog sink and OTel SDK below.
        var otlpEndpointString = builder.Configuration[OtelConstants.OtelExporterEndpointName]
                                 ?? Environment.GetEnvironmentVariable(OtelConstants.OtelExporterEndpointName);

        var otlpProtocol = builder.Configuration[OtelConstants.OtelExporterProtocolName]
                           ?? Environment.GetEnvironmentVariable(OtelConstants.OtelExporterProtocolName);

        // RESILIENCE: pokud OTel endpoint chybí, NE-throwujeme — app musí běžet i bez telemetrie.
        // Telemetrie je nice-to-have, ne kritická pro funkci. Logy stále jdou na konzoli/launchd
        // přes Serilog Console sink. OTel SDK se prostě neregistruje a app pokračuje normálně.
        //
        // Bývalý behavior throwoval ServiceDefaultRegistrationException, což zabilo launchd procesy
        // závislé na env vars (např. AspireWatchdog po restartu systému dokud OtelCollector neběžel).
        var otelEnabled = otlpEndpointString is not null && otlpProtocol is not null;

        if (!otelEnabled)
        {
            // Setup Serilog jen na konzoli (žádný OTel sink, žádné exporters).
            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(builder.Configuration)
                .MinimumLevel.Information()
                .Enrich.FromLogContext()
                .WriteTo.Console()
                .CreateLogger();

            builder.Services.AddSerilog();
            builder.AddHealthChecksAndDiscovery();

            builder.Services.ConfigureHttpClientDefaults(http =>
            {
                http.AddStandardResilienceHandler();
                http.AddServiceDiscovery();
            });

            builder.Services.Configure<ServiceDiscoveryOptions>(options =>
            {
                options.AllowedSchemes = ["http", "https"];
            });

            Log.Logger.Warning(
                "OpenTelemetry endpoint not configured (env vars {Endpoint}, {Protocol} missing). " +
                "Running WITHOUT telemetry export — logs go to console only. " +
                "Set env vars to enable OTLP export.",
                OtelConstants.OtelExporterEndpointName,
                OtelConstants.OtelExporterProtocolName);
            return;
        }

        // Serilog: Console is the primary sink; OTEL sink is best-effort (fire-and-forget).
        // If the OTEL collector is not reachable the Console sink ensures logs are always visible.
        // ReadFrom.Configuration: respect "Serilog:MinimumLevel:Override" v appsettings.json
        // pro silnencing Polly retry / Resilience log spam.
        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(builder.Configuration)
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .WriteTo.OpenTelemetry(otel =>
            {
                otel.Endpoint = otlpEndpointString;
                otel.Protocol = Serilog.Sinks.OpenTelemetry.OtlpProtocol.HttpProtobuf;
            })
            .CreateLogger();

        builder.Services.AddSerilog();

        builder.AddHealthChecksAndDiscovery();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            // Turn on resilience by default — but not for OTEL exporters (they use their own transport)
            http.AddStandardResilienceHandler();

            // Turn on service discovery by default
            http.AddServiceDiscovery();
        });

        builder.Services.Configure<ServiceDiscoveryOptions>(options => { options.AllowedSchemes = ["http", "https"]; });

        var otlpEndpoint = new Uri(otlpEndpointString);
        var otlpProto = otlpProtocol.Equals(OtelConstants.OtelExporterProtocolValueGrpc, StringComparison.OrdinalIgnoreCase)
            ? OtlpExportProtocol.Grpc
            : OtlpExportProtocol.HttpProtobuf;

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource =>
            {
                // RESILIENCE: missing OTEL_RESOURCE_ATTRIBUTES = warning, ne crash.
                // Service.instance.id se autogeneruje pokud chybí.
                var resourceAttributes = Environment.GetEnvironmentVariable("OTEL_RESOURCE_ATTRIBUTES");

                if (resourceAttributes is null)
                {
                    Log.Logger.Warning(
                        "OTEL_RESOURCE_ATTRIBUTES env var not set — service.instance.id will be auto-generated.");
                    resourceAttributes = string.Empty;
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
                    serviceName: serviceName ?? OtelConstants.AspireServiceName,
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
                    o.TimeoutMilliseconds = 3000;
                });
            })
            .WithTracing(tracing =>
            {
                tracing.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddEntityFrameworkCoreInstrumentation();

                if (customActivitySourceNames is not null)
                {
                    foreach (var sourceName in customActivitySourceNames)
                    {
                        tracing.AddSource(sourceName);
                    }
                }

                tracing.AddOtlpExporter(o =>
                {
                    o.Endpoint = otlpProto == OtlpExportProtocol.HttpProtobuf
                        ? new Uri(otlpEndpoint, OtelConstants.TracesRoute)
                        : otlpEndpoint;
                    o.Protocol = otlpProto;
                    o.TimeoutMilliseconds = 3000;
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
                o.TimeoutMilliseconds = 3000;
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

    /// <summary>
    /// Configures Kestrel URLs from the "Hosting" configuration section.
    /// Under Aspire, ASPNETCORE_URLS env var takes precedence and this is skipped.
    /// </summary>
    public static void UseHostingOptions(this WebApplicationBuilder builder)
    {
        var aspnetCoreUrls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
        if (aspnetCoreUrls is not null)
            return;

        var hostingOptions = builder.Configuration
            .GetSection(HostingOptions.SectionName)
            .Get<HostingOptions>();

        if (hostingOptions?.Urls is not null)
            builder.WebHost.UseUrls(hostingOptions.Urls);
    }

    public static void MapDefaultEndpoints(this WebApplication app)
    {
        // All health checks must pass for app to be considered ready to accept traffic after starting
        app.MapHealthChecks("/health");

        // Only health checks tagged with the "live" tag must pass for app to be considered alive
        app.MapHealthChecks("/alive", new HealthCheckOptions { Predicate = r => r.Tags.Contains(Live) });
    }
}
