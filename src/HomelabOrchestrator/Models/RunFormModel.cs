using System.ComponentModel.DataAnnotations;
using HomelabOrchestrator.Services.Jobs;

namespace HomelabOrchestrator.Models;

/// <summary>
/// The runner's form. <see cref="Target"/> is a single encoded choice from one picker —
/// "all", "vm:&lt;hostname&gt;", or "tag:&lt;tag&gt;" — so the page needs no client-side script
/// to keep a target-kind radio and its matching value select in sync.
/// </summary>
public class RunFormModel
{
    [Required(ErrorMessage = "Choose a playbook.")]
    public string PlaybookName { get; set; } = "";

    [Required(ErrorMessage = "Choose a target.")]
    public string Target { get; set; } = "all";

    public (AnsibleRunTargetKind Kind, string? Value) ParseTarget()
    {
        if (string.IsNullOrWhiteSpace(Target) || Target == "all")
        {
            return (AnsibleRunTargetKind.All, null);
        }

        var parts = Target.Split(':', 2);
        return parts switch
        {
            ["vm", var value] => (AnsibleRunTargetKind.Vm, value),
            ["tag", var value] => (AnsibleRunTargetKind.TagGroup, value),
            _ => (AnsibleRunTargetKind.All, null),
        };
    }

    public AnsibleRunRequest ToRunRequest()
    {
        var (kind, value) = ParseTarget();
        return new AnsibleRunRequest(PlaybookName, kind, value);
    }
}
