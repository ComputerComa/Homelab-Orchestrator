namespace HomelabOrchestrator.Models;

/// <summary>One host's row from Ansible's own PLAY RECAP, parsed as-is — nothing derived or guessed.</summary>
public record AnsibleRecapEntry(string Hostname, int Ok, int Changed, int Unreachable, int Failed, int Skipped, int Rescued, int Ignored)
{
    public bool HasFailure => Unreachable > 0 || Failed > 0;
}
