namespace HomelabOrchestrator.Models;

/// <summary>
/// One Ansible TASK's result for one host. <see cref="Detail"/> is a best-effort "msg" value
/// extracted from a result's JSON block (e.g. a debug task's message) — null when none was found.
/// </summary>
public record AnsibleTaskStep(string TaskName, AnsibleStepStatus Status, string? Detail);
