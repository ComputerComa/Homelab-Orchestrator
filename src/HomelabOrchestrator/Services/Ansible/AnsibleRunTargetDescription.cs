using HomelabOrchestrator.Services.Jobs;

namespace HomelabOrchestrator.Services.Ansible;

/// <summary>Renders a run's target as short operator-facing text — shared by the review card and the Executions pages.</summary>
public static class AnsibleRunTargetDescription
{
    public static string Describe(AnsibleRunTargetKind kind, string? value) => kind switch
    {
        AnsibleRunTargetKind.Vm => $"container \"{value}\"",
        AnsibleRunTargetKind.TagGroup => $"containers tagged \"{value}\"",
        AnsibleRunTargetKind.Selection => DescribeSelection(value),
        _ => "all running containers",
    };

    private static string DescribeSelection(string? value)
    {
        var hosts = (value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return hosts.Length switch
        {
            0 => "no containers selected",
            1 => $"container \"{hosts[0]}\"",
            _ => $"{hosts.Length} selected containers ({string.Join(", ", hosts)})",
        };
    }
}
