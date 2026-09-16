using HomelabOrchestrator.Models;
using HomelabOrchestrator.Options;
using HomelabOrchestrator.Services.Proxmox;
using Microsoft.Extensions.Options;

namespace HomelabOrchestrator.Services.Ansible;

/// <summary>
/// Combines raw Proxmox facts (<see cref="IProxmoxService.ListContainersAsync"/>) and each
/// container's actual configured address (<see cref="IProxmoxService.GetContainerAddressAsync"/>)
/// with the orchestrator's own SSH connection details to produce a standard Ansible
/// dynamic-inventory document. The VMID-to-address convention is never used here — it's only a
/// prediction for provisioning and reconciliation, and reporting it as fact would mean Ansible
/// silently connects to the wrong address for any container whose real address ever drifts from
/// (or never matched) that convention.
/// </summary>
public class AnsibleInventoryService(
    IProxmoxService proxmox,
    IOptions<SshOptions> sshOptions,
    ILogger<AnsibleInventoryService> logger) : IAnsibleInventoryService
{
    private readonly SshOptions _sshOptions = sshOptions.Value;

    public async Task<AnsibleInventory?> GetContainerInventoryAsync(int vmid, CancellationToken cancellationToken = default)
    {
        var containers = await proxmox.ListContainersAsync(cancellationToken);
        var container = containers.FirstOrDefault(c => c.Vmid == vmid);

        if (container is null)
        {
            return null;
        }

        var hostEntry = await TryBuildHostEntryAsync(container, cancellationToken);
        if (hostEntry is null)
        {
            throw new ProxmoxOperationException(
                $"Container {container.Vmid} has no static IPv4 address configured (net0 has none, or uses DHCP/manual); " +
                "it cannot be added to Ansible inventory.");
        }

        var (hostname, hostVars) = hostEntry.Value;
        return BuildInventory([hostname], new Dictionary<string, AnsibleHostVars> { [hostname] = hostVars });
    }

    public async Task<AnsibleInventory> GetRunningContainersInventoryAsync(CancellationToken cancellationToken = default)
    {
        var containers = await proxmox.ListContainersAsync(cancellationToken);

        var hosts = new List<string>();
        var hostvars = new Dictionary<string, AnsibleHostVars>();

        foreach (var container in containers.Where(c => c.IsRunning))
        {
            if (OrchestratorSelfFilter.IsSelf(container))
            {
                // The orchestrator's own container — never a valid Ansible target for a blanket
                // "every running container" sweep. An explicit GetContainerInventoryAsync(vmid)
                // lookup by its exact VMID is still honored; only this "give me everything" path
                // excludes it.
                continue;
            }

            var hostEntry = await TryBuildHostEntryAsync(container, cancellationToken);
            if (hostEntry is null)
            {
                logger.LogWarning(
                    "Skipping container {Vmid} ({Hostname}) from inventory: no static IPv4 address configured.",
                    container.Vmid, container.Hostname);
                continue;
            }

            var (hostname, hostVars) = hostEntry.Value;
            hosts.Add(hostname);
            hostvars[hostname] = hostVars;
        }

        return BuildInventory(hosts, hostvars);
    }

    private async Task<(string Hostname, AnsibleHostVars HostVars)?> TryBuildHostEntryAsync(
        ContainerSummary container, CancellationToken cancellationToken)
    {
        var ipAddress = await proxmox.GetContainerAddressAsync(container.Vmid, cancellationToken);
        if (ipAddress is null)
        {
            return null;
        }

        var hostname = string.IsNullOrWhiteSpace(container.Hostname) ? container.Vmid.ToString() : container.Hostname;
        var hostVars = new AnsibleHostVars(
            AnsibleHost: ipAddress,
            AnsibleUser: _sshOptions.RemoteUser,
            AnsiblePort: _sshOptions.Port,
            AnsibleSshPrivateKeyFile: _sshOptions.OrchestratorPrivateKeyPath,
            Tags: container.Tags);
        return (hostname, hostVars);
    }

    private static AnsibleInventory BuildInventory(IReadOnlyList<string> hosts, IReadOnlyDictionary<string, AnsibleHostVars> hostvars) =>
        new(Meta: new AnsibleInventoryMeta(hostvars), All: new AnsibleInventoryGroup(hosts, new Dictionary<string, object>()));
}
