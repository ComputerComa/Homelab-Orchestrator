using System.Text.RegularExpressions;

namespace HomelabOrchestrator.Services.Provisioning;

/// <summary>Normalization and validation rules for container hostnames. Pure and unit-testable.</summary>
public static partial class HostnamePolicy
{
    // Lowercase letters, digits, and hyphens; must start and end with a letter or digit.
    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?$")]
    private static partial Regex Pattern();

    public static string Normalize(string hostname) => hostname.Trim().ToLowerInvariant();

    public static bool IsValid(string normalizedHostname) => Pattern().IsMatch(normalizedHostname);
}
