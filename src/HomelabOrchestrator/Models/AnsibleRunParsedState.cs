namespace HomelabOrchestrator.Models;

/// <summary>
/// The structured view of an Ansible run, parsed from its raw captured output. <see cref="Recap"/>
/// is empty until PLAY RECAP has actually appeared (i.e. the run isn't finished yet).
/// </summary>
public record AnsibleRunParsedState(
    IReadOnlyList<AnsibleHostRun> Hosts,
    IReadOnlyList<AnsibleRecapEntry> Recap,
    IReadOnlyList<string> Notices);
