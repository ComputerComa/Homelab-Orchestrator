namespace HomelabOrchestrator.Models;

/// <summary>
/// The structured view of an Ansible run, parsed from its raw captured output. <see cref="Hosts"/>
/// and <see cref="Tasks"/> are host-centric and task-centric projections of the same underlying
/// per-host, per-task results — nothing is parsed twice. <see cref="Recap"/> is empty until PLAY
/// RECAP has actually appeared (i.e. the run isn't finished yet).
/// </summary>
public record AnsibleRunParsedState(
    IReadOnlyList<AnsibleHostRun> Hosts,
    IReadOnlyList<AnsibleTaskSummary> Tasks,
    IReadOnlyList<AnsibleRecapEntry> Recap,
    IReadOnlyList<string> Notices);
