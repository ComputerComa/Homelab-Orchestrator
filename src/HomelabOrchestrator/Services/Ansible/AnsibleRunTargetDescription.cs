using HomelabOrchestrator.Services.Jobs;

namespace HomelabOrchestrator.Services.Ansible;

/// <summary>Renders a run's target as short operator-facing text — shared by the review card and the Executions pages.</summary>
public static class AnsibleRunTargetDescription
{
    public static string Describe(AnsibleRunTargetKind kind, string? value) => kind switch
    {
        AnsibleRunTargetKind.Vm => $"container \"{value}\"",
        AnsibleRunTargetKind.TagGroup => $"containers tagged \"{value}\"",
        _ => "all running containers",
    };
}
