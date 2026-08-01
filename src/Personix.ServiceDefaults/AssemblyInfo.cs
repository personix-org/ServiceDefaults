using System.Runtime.CompilerServices;

// Grants the test assembly access to internal validation helpers (see Extensions.cs) that are
// implementation details, not part of the package's public API, but are still unit-tested in
// isolation from the WebApplicationBuilder wiring in AddServiceDefaults.
[assembly: InternalsVisibleTo("Personix.ServiceDefaults.Tests")]
