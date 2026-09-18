namespace HomelabOrchestrator.Models;

/// <summary>
/// One Ansible TASK's result for one host. <see cref="Detail"/> is a best-effort "msg" value
/// extracted from a result's JSON block (e.g. a debug task's message) — null when none was found.
/// <see cref="LoopItem"/> is the label from a looped task's <c>=&gt; (item=...)</c> marker — null
/// for a non-looped task. A host can have more than one step for the same task name when the task
/// looped; <see cref="LoopItem"/> is what makes those results distinguishable instead of appearing
/// as indistinguishable duplicates.
/// </summary>
public record AnsibleTaskStep(string TaskName, AnsibleStepStatus Status, string? Detail, string? LoopItem = null);
