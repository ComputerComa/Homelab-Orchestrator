namespace HomelabOrchestrator.Services.Jobs;

public enum AnsibleRunTargetKind
{
    All,
    Vm,
    TagGroup,

    /// <summary>
    /// An ad hoc, manually checked set of containers — used only once the operator has adjusted
    /// individual checkboxes away from a quick action's live All/TagGroup selection. Unlike those
    /// two, this is a frozen list of hostnames, not re-evaluated against whatever matches at run
    /// time (beyond each hostname still needing to be running, same as <see cref="Vm"/>).
    /// </summary>
    Selection,
}
