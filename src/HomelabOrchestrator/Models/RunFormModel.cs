using System.ComponentModel.DataAnnotations;
using HomelabOrchestrator.Services.Jobs;

namespace HomelabOrchestrator.Models;

/// <summary>
/// The runner's form. <see cref="Target"/> is a single encoded choice — "all", "vm:&lt;hostname&gt;",
/// "tag:&lt;tag&gt;", or "selection:&lt;host1&gt;,&lt;host2&gt;,..." — computed client-side from the
/// checkbox panel (see wwwroot/js/runner.js) into one hidden field, so the page model still only
/// ever binds one string.
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
            ["selection", var value] => (AnsibleRunTargetKind.Selection, value),
            _ => (AnsibleRunTargetKind.All, null),
        };
    }

    public AnsibleRunRequest ToRunRequest()
    {
        var (kind, value) = ParseTarget();
        return new AnsibleRunRequest(PlaybookName, kind, value);
    }
}
