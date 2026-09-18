namespace HomelabOrchestrator.Services.Jobs;

/// <summary>
/// Operator-supplied inputs for an Ansible run. TargetValue is the raw hostname (Vm), tag
/// (TagGroup), or a comma-joined list of hostnames (Selection) shown in the picker — the worker
/// re-resolves and re-validates it against the currently running containers immediately before
/// building the ansible-playbook command, the same "never trust a stale browser value" rule
/// provisioning applies to VMID/address/template. <paramref name="ExtraVars"/> is serialized to a
/// restrictive-permission temporary JSON file and passed as Ansible's own <c>--extra-vars @file</c>
/// — never interpolated into a command-line argument, and never persisted to execution history
/// (it's live-only; a finished run's extra-vars have no further use once the playbook has run).
/// </summary>
public record AnsibleRunRequest(
    string PlaybookName,
    AnsibleRunTargetKind TargetKind,
    string? TargetValue,
    IReadOnlyDictionary<string, object?>? ExtraVars = null);
