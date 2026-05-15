# Nwo.ServiceDefaults

Shared Aspire service defaults for new-world-order services. Configures OpenTelemetry (traces, metrics, logs), Serilog, health checks, service discovery, and HTTP resilience in one call.

## What This Package Provides

- ✅ **OpenTelemetry** (traces, metrics, logs) with OTLP export
- ✅ **Serilog** structured logging with OpenTelemetry sink
- ✅ **Health checks** (`/health`, `/alive`) for K8s probes
- ✅ **Service discovery** for HTTP clients
- ✅ **HTTP resilience** (retries, circuit breakers, timeouts)

## Dependencies

- `Nwo.Constants`

## Usage (quick start for agents)

1) Add package:

```xml
<PackageReference Include="Nwo.ServiceDefaults" Version="1.0.0" />
```

2) Use in `Program.cs` (call before `Build()`):

```csharp
using ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

// In Program.cs – call before building the app
builder.AddServiceDefaults(customMetersNames: ["MyApp.Meter"]);

var app = builder.Build();

// Map health check endpoints (/health, /alive)
app.MapDefaultEndpoints();

app.Run();
```

## Required configuration / environment variables

| Variable | Description | Example | Required |
|----------|-------------|---------|----------|
| `OTEL_EXPORTER_OTLP_ENDPOINT` | OTLP collector endpoint | `http://localhost:4318` | Yes |
| `OTEL_EXPORTER_OTLP_PROTOCOL` | Protocol type | `http/protobuf` or `grpc` | Yes |
| `OTEL_RESOURCE_ATTRIBUTES` | Resource attributes | `service.instance.id=my-service-123` | Yes |

**Note:** `OTEL_RESOURCE_ATTRIBUTES` must include `service.instance.id` for proper service identification.

## Architecture

### Configuration Flow

```
Program.cs
   ↓
AddServiceDefaults()
   ├── Clear default logging → Add Serilog
   ├── Configure Serilog with OTLP sink
   ├── Add health checks + service discovery
   ├── Configure HTTP clients (resilience + discovery)
   ├── Validate OTEL env vars (endpoint, protocol, attributes)
   └── Configure OpenTelemetry (metrics, traces, logs)
```

### Components

- **Extensions.cs** - Main extension methods
  - `AddServiceDefaults<TBuilder>()` - Configures all observability
  - `MapDefaultEndpoints()` - Maps health check endpoints
- **ServiceDefaultRegistrationException** - Custom exception for configuration errors

## Agent checklist (no source dive needed)

- Added `Nwo.ServiceDefaults` package reference
- `AddServiceDefaults(...)` is called before `Build()`
- `MapDefaultEndpoints()` is called after `Build()`
- OTEL env vars are configured (see table above)
- Private NWO NuGet feed is configured (see below)

### NuGet feed

This package depends on `Nwo.Constants`. Ensure your `nuget.config` includes the private NWO feed:

```xml
<add key="private-nwo-feed" value="https://your-feed-url/v3/index.json" />
```

## Health Checks

### `/health` Endpoint
Checks **all** registered health checks. Returns `200 OK` if all pass, `503 Service Unavailable` if any fail.
**Use for:** Kubernetes readiness probes

### `/alive` Endpoint  
Checks only health checks tagged with `"live"`. Returns `200 OK` if live checks pass.
**Use for:** Kubernetes liveness probes

**Default:** A "self" check is always registered with the "live" tag.

## OpenTelemetry Details

### Automatically Instrumented
- **Metrics:** ASP.NET Core, HTTP client, .NET runtime, custom meters (if specified)
- **Traces:** ASP.NET Core requests, HTTP client calls, EF Core queries
- **Logs:** Serilog with OpenTelemetry sink, formatted messages, log scopes

### HTTP Protocol Routes
- Metrics: `{endpoint}/v1/metrics`
- Traces: `{endpoint}/v1/traces`
- Logs: `{endpoint}/v1/logs`

## Best Practices

1. ✅ Call `AddServiceDefaults()` **before** `Build()`
2. ✅ Call `MapDefaultEndpoints()` **after** `Build()`
3. ✅ Use environment-specific configuration for OTLP endpoints
4. ✅ Tag custom health checks: "live" = liveness, none = readiness
5. ✅ Monitor both `/health` and `/alive` endpoints in production

## Common Issues

**OpenTelemetry Exporter Endpoint is not configured**  
→ Set `OTEL_EXPORTER_OTLP_ENDPOINT` environment variable

**service.instance.id not found**  
→ Ensure `OTEL_RESOURCE_ATTRIBUTES` includes `service.instance.id=<unique-id>`

**Health checks always fail**  
→ Check if you have non-"live" tagged checks failing. Use `/alive` for liveness, `/health` for readiness.

## Testing

The package includes comprehensive unit and integration tests:

```bash
dotnet test
```

Tests cover:
- Configuration validation (missing OTEL env vars)
- Service registration (health checks, OpenTelemetry, Serilog)
- Health check endpoints (`/health`, `/alive`)
- Custom meters support
- Protocol selection (HTTP/gRPC)

**Test stack:** xUnit, FluentAssertions, Microsoft.AspNetCore.Mvc.Testing

## Part of the NWO package family

| Package | Description |
|---------|-------------|
| Nwo.Constants | Shared constants |
| Nwo.Options | DI options validation |
| Nwo.StartUp | Startup coordination |
| Nwo.Persistence | EF Core / SQLite base |
| **Nwo.ServiceDefaults** | Aspire service defaults (OTel, Serilog, health checks) |
