using System.ComponentModel.DataAnnotations;

namespace Personix.ServiceDefaults;

/// <summary>
/// Options bound from the "Hosting" configuration section and applied by
/// <see cref="Extensions.UseHostingOptions"/>.
/// </summary>
public sealed class HostingOptions
{
    /// <summary>The configuration section this type binds from.</summary>
    public const string SectionName = "Hosting";

    /// <summary>
    /// The URLs Kestrel binds to, as a semicolon-separated list — the same format as the standard
    /// ASP.NET Core <c>urls</c> configuration value. Defaults to <c>http://localhost:5000</c>.
    /// </summary>
    [Required]
    public string Urls { get; set; } = "http://localhost:5000";
}
