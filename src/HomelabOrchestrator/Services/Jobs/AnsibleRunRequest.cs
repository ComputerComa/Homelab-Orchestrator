namespace HomelabOrchestrator.Services.Jobs;

/// <summary>
/// Operator-supplied inputs for an Ansible run. TargetValue is the raw hostname (Vm) or tag
/// (TagGroup) shown in the picker — the worker re-resolves and re-validates it against the
/// currently running containers immediately before building the ansible-playbook command, the
/// same "never trust a stale browser value" rule provisioning applies to VMID/address/template.
/// </summary>
public record AnsibleRunRequest(string PlaybookName, AnsibleRunTargetKind TargetKind, string? TargetValue);
