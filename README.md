# Nwo.ServiceDefaults

Shared Aspire service defaults for new-world-order services. Configures OpenTelemetry (traces, metrics, logs), Serilog, health checks, service discovery, and HTTP resilience in one call.

## Dependencies

- `Nwo.Constants`

## Usage

```xml
<PackageReference Include="Nwo.ServiceDefaults" Version="1.0.0" />
```

```csharp
using ServiceDefaults;

// In Program.cs – call before building the app
builder.AddServiceDefaults(customMetersNames: ["MyApp.Meter"]);

// Map health check endpoints (/health, /alive)
app.MapDefaultEndpoints();
```

### Required configuration / environment variables

| Key | Description |
|-----|-------------|
| `OTEL_EXPORTER_OTLP_ENDPOINT` | OTLP exporter endpoint URL |
| `OTEL_EXPORTER_OTLP_PROTOCOL` | `http/protobuf` or `grpc` |
| `OTEL_RESOURCE_ATTRIBUTES` | Comma-separated resource attributes (must include `service.instance.id=...`) |

### NuGet feed

This package depends on `Nwo.Constants`. Ensure your `nuget.config` includes the private NWO feed:

```xml
<add key="private-nwo-feed" value="https://your-feed-url/v3/index.json" />
```

## Part of the NWO package family

| Package | Description |
|---------|-------------|
| Nwo.Constants | Shared constants |
| Nwo.Options | DI options validation |
| Nwo.StartUp | Startup coordination |
| Nwo.Persistence | EF Core / SQLite base |
| **Nwo.ServiceDefaults** | Aspire service defaults (OTel, Serilog, health checks) |
