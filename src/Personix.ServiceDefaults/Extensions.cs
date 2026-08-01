using Personix.Otel.Constants;

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

namespace Personix.ServiceDefaults;

/// <summary>
/// Extension methods that wire up the cross-cutting concerns most services need: OpenTelemetry
/// traces, metrics and logs over OTLP, Serilog, health check endpoints, service discovery, and HTTP
/// resilience. Call <see cref="AddServiceDefaults{TBuilder}"/> once during start-up; the rest of this
/// class is used internally or for finer-grained control.
/// </summary>
/// <remarks>
/// See <see href="https://aka.ms/dotnet/aspire/service-defaults"/> for the general shape this kind of
/// file follows across most Aspire-based service-defaults projects.
/// </remarks>
public static class Extensions
{
    private const string Live = "live";

    /// <summary>
    /// Configures logging, health checks, service discovery, HTTP resilience, and OpenTelemetry for
    /// <paramref name="builder"/>. Call this once, early in start-up.
    /// </summary>
    /// <param name="builder">The host or web application builder being configured.</param>
    /// <param name="customMetersNames">
    /// Additional <see cref="System.Diagnostics.Metrics.Meter"/> names to subscribe for metrics,
    /// beyond the ASP.NET Core, HttpClient, and runtime instrumentation that are always included.
    /// </param>
    /// <param name="customActivitySourceNames">
    /// Additional <see cref="System.Diagnostics.ActivitySource"/> names to subscribe for tracing,
    /// beyond the ASP.NET Core, HttpClient, and Entity Framework Core instrumentation that are always
    /// included.
    /// </param>
    /// <param name="serviceName">
    /// The service name recorded on the OpenTelemetry resource. When omitted, falls back — in order —
    /// to the <c>OTEL_SERVICE_NAME</c> configuration value, then the <c>OTEL_SERVICE_NAME</c>
    /// environment variable, then <see cref="IHostEnvironment.ApplicationName"/>.
    /// </param>
    /// <typeparam name="TBuilder">A host or web application builder type.</typeparam>
    /// <remarks>
    /// <para>
    /// Telemetry export is best-effort, not required: when the <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> or
    /// <c>OTEL_EXPORTER_OTLP_PROTOCOL</c> configuration value or environment variable is missing, this
    /// method does not throw. It skips registering the OpenTelemetry SDK and Serilog's OTLP sink, logs
    /// a warning, and leaves the service logging to the console only — a missing or not-yet-started
    /// collector never prevents start-up. Earlier versions threw
    /// <see cref="ServiceDefaultRegistrationException"/> in that situation instead; this package no
    /// longer does.
    /// </para>
    /// <para>
    /// A value that is present but malformed is treated differently from one that is absent: if
    /// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is set to something that is not a valid absolute URI, this
    /// method throws <see cref="ServiceDefaultRegistrationException"/> naming the offending
    /// configuration key and value. That failure means a mistake was made, not a deliberate opt-out —
    /// letting it through would otherwise produce a relative <see cref="Uri"/> that fails later, deep
    /// inside OpenTelemetry SDK registration, with a message that no longer mentions which
    /// configuration key caused it.
    /// </para>
    /// <para>
    /// Always calls <see cref="AddHealthChecksAndDiscovery{TBuilder}"/> and registers the standard
    /// resilience handler and service discovery on the default HTTP client configuration. When
    /// telemetry is enabled, also configures OTLP export for traces, metrics, and logs, over gRPC or
    /// HTTP/protobuf depending on <c>OTEL_EXPORTER_OTLP_PROTOCOL</c>.
    /// </para>
    /// </remarks>
    public static void AddServiceDefaults<TBuilder>(this TBuilder builder, string[]? customMetersNames = null, string[]? customActivitySourceNames = null, string? serviceName = null) where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();

        // Read OTEL config first — used by both Serilog sink and OTel SDK below.
        var otlpEndpointString = builder.Configuration[OtelConstants.OtelExporterEndpointName]
                                 ?? Environment.GetEnvironmentVariable(OtelConstants.OtelExporterEndpointName);

        var otlpProtocol = builder.Configuration[OtelConstants.OtelExporterProtocolName]
                           ?? Environment.GetEnvironmentVariable(OtelConstants.OtelExporterProtocolName);

        // A missing OTEL endpoint is not fatal. Telemetry is valuable but not required for the
        // service to do its job, and logs still reach the console. The SDK is simply not
        // registered and start-up continues.
        //
        // This used to throw ServiceDefaultRegistrationException, which killed processes whose
        // supervisor started them before the collector was up — the service stayed down waiting
        // for something that was only ever meant to observe it.
        var otelEnabled = otlpEndpointString is not null && otlpProtocol is not null;

        if (!otelEnabled)
        {
            // Console-only Serilog: no OTLP sink, no exporters.
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
        // ReadFrom.Configuration honours "Serilog:MinimumLevel:Override" from appsettings.json,
        // which is how Polly retry and resilience log spam gets silenced per service.
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

        var otlpEndpoint = ParseOtlpEndpoint(otlpEndpointString, OtelConstants.OtelExporterEndpointName);
        var resolvedProtocol = RequireOtelConfigurationValue(otlpProtocol, OtelConstants.OtelExporterProtocolName);
        var otlpProto = resolvedProtocol.Equals(OtelConstants.OtelExporterProtocolValueGrpc, StringComparison.OrdinalIgnoreCase)
            ? OtlpExportProtocol.Grpc
            : OtlpExportProtocol.HttpProtobuf;

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource =>
            {
                // A missing OTEL_RESOURCE_ATTRIBUTES is a warning, not a crash — service.instance.id
                // is generated when it cannot be read from there.
                var resourceAttributes = Environment.GetEnvironmentVariable(
                    OtelConstants.OtelResourceAttributesName);

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

                // Service name resolution, most specific first: an explicit argument, then the
                // standard OTEL_SERVICE_NAME from configuration or environment, and finally the
                // host's own application name. No package-level default — the name of a service
                // belongs to that service.
                var resolvedServiceName = serviceName
                    ?? builder.Configuration[OtelConstants.OtelServiceNameName]
                    ?? Environment.GetEnvironmentVariable(OtelConstants.OtelServiceNameName)
                    ?? builder.Environment.ApplicationName;

                resource.AddService(
                    serviceName: resolvedServiceName,
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

    /// <summary>
    /// Returns <paramref name="value"/> unchanged if it is not <see langword="null"/>; otherwise
    /// throws <see cref="ServiceDefaultRegistrationException"/> naming <paramref name="configKey"/>.
    /// </summary>
    /// <param name="value">The configuration or environment variable value to check.</param>
    /// <param name="configKey">
    /// The configuration/environment key <paramref name="value"/> was read from, used to make the
    /// exception message actionable.
    /// </param>
    /// <remarks>
    /// Called only after <see cref="AddServiceDefaults{TBuilder}"/> has already established that OTEL
    /// export is enabled (both the endpoint and protocol keys are present) — a caller reaching this
    /// point with a <see langword="null"/> <paramref name="value"/> means that invariant was violated,
    /// which is itself a bug worth a clear message rather than a bare
    /// <see cref="NullReferenceException"/> a few lines further down.
    /// </remarks>
    internal static string RequireOtelConfigurationValue(string? value, string configKey)
    {
        if (value is null)
        {
            throw new ServiceDefaultRegistrationException(
                $"Configuration key '{configKey}' is required to enable OpenTelemetry export but no " +
                "value was found in configuration or environment variables.");
        }

        return value;
    }

    /// <summary>
    /// Parses <paramref name="value"/> as an absolute <see cref="Uri"/>, throwing
    /// <see cref="ServiceDefaultRegistrationException"/> naming <paramref name="configKey"/> when
    /// <paramref name="value"/> is missing or is not a valid absolute URI.
    /// </summary>
    /// <param name="value">The configured OTLP endpoint value.</param>
    /// <param name="configKey">
    /// The configuration/environment key <paramref name="value"/> was read from, used to make the
    /// exception message actionable.
    /// </param>
    /// <remarks>
    /// Absolute is a deliberate requirement, not the .NET default: <see cref="AddServiceDefaults{TBuilder}"/>
    /// combines the returned <see cref="Uri"/> with relative route segments (for example
    /// <see cref="OtelConstants.TracesRoute"/>) using the two-argument <see cref="Uri"/> constructor,
    /// which throws <see cref="InvalidOperationException"/> for a relative base URI. Validating here,
    /// with <see cref="UriKind.Absolute"/>, turns a value such as <c>"not-a-valid-uri"</c> — which
    /// <c>new Uri(string)</c> would silently accept as a *relative* URI — into a clear failure at the
    /// point the mistake was made, instead of a confusing one deep inside SDK registration.
    /// </remarks>
    internal static Uri ParseOtlpEndpoint(string? value, string configKey)
    {
        var endpoint = RequireOtelConfigurationValue(value, configKey);

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
        {
            throw new ServiceDefaultRegistrationException(
                $"Configuration key '{configKey}' has value '{endpoint}', which is not a valid " +
                "absolute URI. Set it to a full URI, for example 'http://localhost:4318'.");
        }

        return uri;
    }

    /// <summary>
    /// Registers a default liveness health check tagged <c>"live"</c> and turns on service discovery.
    /// </summary>
    /// <param name="builder">The host or web application builder being configured.</param>
    /// <typeparam name="TBuilder">A host or web application builder type.</typeparam>
    /// <remarks>
    /// Called by <see cref="AddServiceDefaults{TBuilder}"/>. Call it directly instead when a service
    /// wants health checks and service discovery without the logging and OpenTelemetry setup that
    /// <see cref="AddServiceDefaults{TBuilder}"/> also does.
    /// </remarks>
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
    /// <param name="builder">The web application builder whose Kestrel URLs are being set.</param>
    public static void UseHostingOptions(this WebApplicationBuilder builder)
    {
        var aspnetCoreUrls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
        if (aspnetCoreUrls is not null)
        {
            return;
        }

        var hostingOptions = builder.Configuration
            .GetSection(HostingOptions.SectionName)
            .Get<HostingOptions>();

        if (hostingOptions?.Urls is not null)
        {
            builder.WebHost.UseUrls(hostingOptions.Urls);
        }
    }

    /// <summary>Maps the <c>/health</c> and <c>/alive</c> endpoints.</summary>
    /// <param name="app">The application to map the endpoints on.</param>
    /// <remarks>
    /// <c>/health</c> requires every registered health check to pass. <c>/alive</c> only requires
    /// checks tagged <c>"live"</c> to pass — by default, just the one
    /// <see cref="AddHealthChecksAndDiscovery{TBuilder}"/> registers — so a dependency check (for
    /// example a database) can fail <c>/health</c> without also failing <c>/alive</c> and getting the
    /// instance killed by an orchestrator that only probes liveness.
    /// </remarks>
    public static void MapDefaultEndpoints(this WebApplication app)
    {
        // All health checks must pass for app to be considered ready to accept traffic after starting
        app.MapHealthChecks("/health");

        // Only health checks tagged with the "live" tag must pass for app to be considered alive
        app.MapHealthChecks("/alive", new HealthCheckOptions { Predicate = r => r.Tags.Contains(Live) });
    }
}
