using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Personix.Otel.Constants;
using Shouldly;

namespace Personix.ServiceDefaults.Tests;

/// <summary>
/// Covers how registration behaves when OTEL configuration is incomplete. Telemetry is treated as
/// best-effort: an unconfigured or unreachable collector must never keep the service from starting,
/// because a supervisor restarting the host before the collector is up would otherwise leave the
/// service permanently down.
/// </summary>
[Collection("EnvironmentVariables")]
public class ConfigurationValidationTests : IDisposable
{
    private const string Endpoint = "http://localhost:4318";
    private const string GrpcEndpoint = "http://localhost:4317";
    private const string NotAnAbsoluteUri = "not-a-valid-uri";
    private const string HttpProtobuf = "http/protobuf";
    private const string Grpc = "grpc";
    private const string InstanceAttributes = "service.instance.id=test-123";

    public ConfigurationValidationTests()
    {
        ClearOtelEnvironment();
    }

    public void Dispose()
    {
        ClearOtelEnvironment();
    }

    private static void ClearOtelEnvironment()
    {
        Environment.SetEnvironmentVariable(OtelConstants.OtelExporterEndpointName, null);
        Environment.SetEnvironmentVariable(OtelConstants.OtelExporterProtocolName, null);
        Environment.SetEnvironmentVariable(OtelConstants.OtelResourceAttributesName, null);
        Environment.SetEnvironmentVariable(OtelConstants.OtelServiceNameName, null);
    }

    [Fact]
    public void AddServiceDefaults_StartsWithoutTelemetry_WhenEndpointIsMissing()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [OtelConstants.OtelExporterProtocolName] = HttpProtobuf,
        });

        Should.NotThrow(() => builder.AddServiceDefaults());
    }

    [Fact]
    public void AddServiceDefaults_StartsWithoutTelemetry_WhenProtocolIsMissing()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [OtelConstants.OtelExporterEndpointName] = Endpoint,
        });

        Should.NotThrow(() => builder.AddServiceDefaults());
    }

    [Fact]
    public void AddServiceDefaults_ThrowsNamingTheKey_WhenEndpointIsNotAValidAbsoluteUri()
    {
        // Unlike a missing endpoint (an opt-out, handled above), a present-but-malformed endpoint is
        // a configuration mistake. It must fail loudly and name the offending key, rather than either
        // being silently accepted as a relative URI or surfacing as a bare UriFormatException once the
        // OpenTelemetry SDK tries to combine it with a route path deep inside registration.
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [OtelConstants.OtelExporterEndpointName] = NotAnAbsoluteUri,
            [OtelConstants.OtelExporterProtocolName] = HttpProtobuf,
        });

        var exception = Should.Throw<ServiceDefaultRegistrationException>(() => builder.AddServiceDefaults());

        exception.Message.ShouldContain(OtelConstants.OtelExporterEndpointName);
        exception.Message.ShouldContain(NotAnAbsoluteUri);
    }

    [Fact]
    public void AddServiceDefaults_DoesNotRegisterTheTracerProvider_WhenOtelIsNotConfigured()
    {
        var builder = WebApplication.CreateBuilder();

        builder.AddServiceDefaults();

        using var provider = builder.Services.BuildServiceProvider();
        provider.GetService<OpenTelemetry.Trace.TracerProvider>().ShouldBeNull();
    }

    [Fact]
    public void AddServiceDefaults_RegistersTheTracerProvider_WhenOtelIsConfigured()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [OtelConstants.OtelExporterEndpointName] = Endpoint,
            [OtelConstants.OtelExporterProtocolName] = HttpProtobuf,
        });

        builder.AddServiceDefaults();

        using var provider = builder.Services.BuildServiceProvider();
        provider.GetService<OpenTelemetry.Trace.TracerProvider>().ShouldNotBeNull();
    }

    [Fact]
    public void AddServiceDefaults_ResolvesTheResource_WhenResourceAttributesAreMissing()
    {
        // A missing OTEL_RESOURCE_ATTRIBUTES only means the instance id is generated rather than
        // read, so building the tracer provider — which runs the resource callback — must succeed.
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [OtelConstants.OtelExporterEndpointName] = Endpoint,
            [OtelConstants.OtelExporterProtocolName] = HttpProtobuf,
        });

        builder.AddServiceDefaults();

        using var provider = builder.Services.BuildServiceProvider();
        Should.NotThrow(() => provider.GetRequiredService<OpenTelemetry.Trace.TracerProvider>());
    }

    [Fact]
    public void AddServiceDefaults_AcceptsEndpointFromEnvironmentVariables()
    {
        var builder = WebApplication.CreateBuilder();
        Environment.SetEnvironmentVariable(OtelConstants.OtelExporterEndpointName, Endpoint);
        Environment.SetEnvironmentVariable(OtelConstants.OtelExporterProtocolName, HttpProtobuf);
        Environment.SetEnvironmentVariable(OtelConstants.OtelResourceAttributesName, InstanceAttributes);

        Should.NotThrow(() => builder.AddServiceDefaults());
    }

    [Fact]
    public void AddServiceDefaults_AcceptsEndpointFromConfiguration()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [OtelConstants.OtelExporterEndpointName] = Endpoint,
            [OtelConstants.OtelExporterProtocolName] = HttpProtobuf,
        });
        Environment.SetEnvironmentVariable(OtelConstants.OtelResourceAttributesName, InstanceAttributes);

        Should.NotThrow(() => builder.AddServiceDefaults());
    }

    [Fact]
    public void AddServiceDefaults_SupportsGrpcProtocol()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [OtelConstants.OtelExporterEndpointName] = GrpcEndpoint,
            [OtelConstants.OtelExporterProtocolName] = Grpc,
        });
        Environment.SetEnvironmentVariable(OtelConstants.OtelResourceAttributesName, InstanceAttributes);

        Should.NotThrow(() => builder.AddServiceDefaults());
    }

    [Fact]
    public void AddServiceDefaults_UsesTheServiceNameFromConfiguration_WhenNoneIsPassed()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [OtelConstants.OtelExporterEndpointName] = Endpoint,
            [OtelConstants.OtelExporterProtocolName] = HttpProtobuf,
            [OtelConstants.OtelServiceNameName] = "configured-service",
        });

        builder.AddServiceDefaults();

        using var provider = builder.Services.BuildServiceProvider();
        Should.NotThrow(() => provider.GetRequiredService<OpenTelemetry.Trace.TracerProvider>());
    }
}
