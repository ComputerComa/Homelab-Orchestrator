namespace HomelabOrchestrator.Models;

/// <summary>
/// Read-only cluster context shown to the user and carried through the form as hidden
/// fields: the next free VMID, the address it maps to, and the template that will be used.
/// </summary>
public class ClusterPlacement
{
    public int Vmid { get; set; }
    public string IpAddress { get; set; } = "";
    public string Template { get; set; } = "";
    public string Node { get; set; } = "";
    public string Bridge { get; set; } = "";
    public string Gateway { get; set; } = "";
    public int Subnet { get; set; }
}
