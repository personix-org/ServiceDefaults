using Personix.Otel.Constants;
using Shouldly;

namespace Personix.ServiceDefaults.Tests;

/// <summary>
/// Covers the internal helpers <see cref="Extensions"/> uses to turn OTLP configuration values into
/// the <see cref="Uri"/> and protocol they need. Unlike <see cref="ConfigurationValidationTests"/>,
/// which drives everything through the public <see cref="Extensions.AddServiceDefaults{TBuilder}"/>
/// entry point, these tests call the helpers directly so that a value being present-but-malformed can
/// be exercised in isolation from the "no OTEL config at all" opt-out path.
/// </summary>
public class OtlpConfigurationParsingTests
{
    private const string ConfigKey = OtelConstants.OtelExporterEndpointName;
    private const string ValidEndpoint = "http://localhost:4318";
    private const string NotAnAbsoluteUri = "not-a-valid-uri";

    [Fact]
    public void RequireOtelConfigurationValue_ReturnsTheValue_WhenPresent()
    {
        var result = Extensions.RequireOtelConfigurationValue(ValidEndpoint, ConfigKey);

        result.ShouldBe(ValidEndpoint);
    }

    [Fact]
    public void RequireOtelConfigurationValue_Throws_NamingTheMissingKey_WhenValueIsNull()
    {
        var exception = Should.Throw<ServiceDefaultRegistrationException>(
            () => Extensions.RequireOtelConfigurationValue(null, ConfigKey));

        exception.Message.ShouldContain(ConfigKey);
    }

    [Fact]
    public void ParseOtlpEndpoint_ReturnsTheUri_WhenValueIsAValidAbsoluteUri()
    {
        var uri = Extensions.ParseOtlpEndpoint(ValidEndpoint, ConfigKey);

        uri.ShouldBe(new Uri(ValidEndpoint));
    }

    [Fact]
    public void ParseOtlpEndpoint_Throws_NamingTheMissingKey_WhenValueIsNull()
    {
        var exception = Should.Throw<ServiceDefaultRegistrationException>(
            () => Extensions.ParseOtlpEndpoint(null, ConfigKey));

        exception.Message.ShouldContain(ConfigKey);
    }

    [Fact]
    public void ParseOtlpEndpoint_Throws_NamingTheKeyAndValue_WhenValueIsNotAValidAbsoluteUri()
    {
        var exception = Should.Throw<ServiceDefaultRegistrationException>(
            () => Extensions.ParseOtlpEndpoint(NotAnAbsoluteUri, ConfigKey));

        exception.Message.ShouldContain(ConfigKey);
        exception.Message.ShouldContain(NotAnAbsoluteUri);
    }

    [Fact]
    public void ParseOtlpEndpoint_UsesADifferentMessage_ForMissingVersusInvalidValue()
    {
        // The two failure modes lead to different fixes (set the key at all vs. fix its value), so
        // they must not collapse into one generic "something is wrong" message.
        var missingKeyException = Should.Throw<ServiceDefaultRegistrationException>(
            () => Extensions.ParseOtlpEndpoint(null, ConfigKey));

        var invalidValueException = Should.Throw<ServiceDefaultRegistrationException>(
            () => Extensions.ParseOtlpEndpoint(NotAnAbsoluteUri, ConfigKey));

        missingKeyException.Message.ShouldNotBe(invalidValueException.Message);
    }
}
