namespace HomelabOrchestrator.Services.Provisioning;

/// <summary>Picks the newest downloaded Debian 13 LXC template from a storage's content listing. Pure and unit-testable.</summary>
public static class DebianTemplateSelector
{
    private const string DebianMarker = "debian-13-";

    public static string? SelectLatest(IEnumerable<string> volids) =>
        volids
            .Where(volid => volid.Contains(":vztmpl/", StringComparison.OrdinalIgnoreCase)
                         && volid.Contains(DebianMarker, StringComparison.OrdinalIgnoreCase))
            .OrderBy(volid => volid, StringComparer.Ordinal)
            .LastOrDefault();
}
