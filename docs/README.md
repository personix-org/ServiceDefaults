# Personix.ServiceDefaults

One call that wires up the cross-cutting concerns every service needs: OpenTelemetry traces, metrics
and logs over OTLP, Serilog, health check endpoints, service discovery, and HTTP resilience.

The design decision worth knowing up front: **telemetry is best-effort**. When the collector is not
configured or not reachable, the service starts anyway and logs to the console. Observability must
never be the reason a service is down.

## Installation

```xml
<PackageReference Include="Personix.ServiceDefaults" Version="1.0.0" />
```

## Usage

```csharp
using Personix.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var app = builder.Build();
app.MapDefaultEndpoints();
app.Run();
```

With custom meters and activity sources:

```csharp
builder.AddServiceDefaults(
    customMetersNames: ["MyApp.Orders"],
    customActivitySourceNames: ["MyApp.Application"],
    serviceName: "orders-api");
```

## Configuration

Two keys decide whether telemetry is exported at all. Both are read from configuration first, then
from environment variables:

| Key | Purpose |
|---|---|
| `OTEL_EXPORTER_OTLP_ENDPOINT` | Collector endpoint. Missing → no export. |
| `OTEL_EXPORTER_OTLP_PROTOCOL` | `http/protobuf` or `grpc`. Missing → no export. |
| `OTEL_SERVICE_NAME` | Service name. Optional — see below. |
| `OTEL_RESOURCE_ATTRIBUTES` | Optional. `service.instance.id` is read from here when present, generated otherwise. |

### Service name

Resolved most specific first:

1. the `serviceName` argument,
2. `OTEL_SERVICE_NAME` from configuration or environment,
3. the host's `ApplicationName`.

There is no package-level default — the name of a service belongs to that service.

### Log levels

Serilog reads `Serilog:MinimumLevel:Override` from `appsettings.json`, which is how noisy sources
get quietened per service:

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Override": {
        "Polly": "Fatal",
        "System.Net.Http": "Fatal"
      }
    }
  }
}
```

## What gets registered

- **Serilog** with a console sink, plus an OTLP sink when telemetry is configured.
- **OpenTelemetry** traces and metrics with ASP.NET Core, HttpClient, runtime, and Entity Framework
  instrumentation, exported over OTLP.
- **Health checks** — `MapDefaultEndpoints()` exposes readiness and liveness.
- **Service discovery** restricted to `http` and `https` schemes.
- **HTTP resilience** as the default handler for every `HttpClient`.

## Notes

- The Entity Framework instrumentation upstream has never shipped a stable release, so this package
  depends on a beta of it. That is the only prerelease dependency here.
- `UseHostingOptions()` applies URLs from configuration, but yields to `ASPNETCORE_URLS` when that is
  set — so running under an orchestrator keeps working unchanged.

## Licence

MIT — see [LICENSE](LICENSE).
