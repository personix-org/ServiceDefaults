namespace Personix.ServiceDefaults;

/// <summary>Exception type for service-default registration failures.</summary>
/// <param name="message">A human-readable description of the failure.</param>
/// <param name="innerException">The underlying cause, if any.</param>
/// <remarks>
/// Thrown by <see cref="Extensions.AddServiceDefaults{TBuilder}"/> when an OTLP configuration value is
/// present but malformed — for example, <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> set to something that is
/// not a valid absolute URI — naming the offending configuration key in the message. Not thrown when a
/// value is absent entirely: a missing <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> or
/// <c>OTEL_EXPORTER_OTLP_PROTOCOL</c> is treated as a deliberate opt-out of telemetry export, and
/// <see cref="Extensions.AddServiceDefaults{TBuilder}"/> falls back to console-only logging instead of
/// throwing in that case.
/// </remarks>
public sealed class ServiceDefaultRegistrationException(string message, Exception? innerException = null)
    : Exception(message, innerException);
