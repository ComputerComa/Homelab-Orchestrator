using HomelabOrchestrator.Models;
using HomelabOrchestrator.Options;
using HomelabOrchestrator.Services.Provisioning;
using HomelabOrchestrator.Services.Proxmox;
using Microsoft.Extensions.Options;

namespace HomelabOrchestrator.Services.Ansible;

/// <summary>
/// Combines raw Proxmox facts (<see cref="IProxmoxService.ListContainersAsync"/>) with the
/// homelab's VMID-to-address convention and the orchestrator's own SSH connection details to
/// produce a standard Ansible dynamic-inventory document. Never queries Proxmox itself.
/// </summary>
public class AnsibleInventoryService(
    IProxmoxService proxmox,
    IOptions<ProxmoxOptions> proxmoxOptions,
    IOptions<SshOptions> sshOptions,
    ILogger<AnsibleInventoryService> logger) : IAnsibleInventoryService
{
    private readonly ProxmoxOptions _proxmoxOptions = proxmoxOptions.Value;
    private readonly SshOptions _sshOptions = sshOptions.Value;

    public async Task<AnsibleInventory?> GetContainerInventoryAsync(int vmid, CancellationToken cancellationToken = default)
    {
        var containers = await proxmox.ListContainersAsync(cancellationToken);
        var container = containers.FirstOrDefault(c => c.Vmid == vmid);

        if (container is null)
        {
            return null;
        }

        if (!TryBuildHostEntry(container, out var hostname, out var hostVars))
        {
            throw new ProxmoxOperationException(
                $"VMID {container.Vmid} cannot be mapped safely to {_proxmoxOptions.NetworkPrefix}.x; " +
                $"the last octet must be between {_proxmoxOptions.IpHostMin} and {_proxmoxOptions.IpHostMax}.");
        }

        return BuildInventory([hostname], new Dictionary<string, AnsibleHostVars> { [hostname] = hostVars });
    }

    public async Task<AnsibleInventory> GetRunningContainersInventoryAsync(CancellationToken cancellationToken = default)
    {
        var containers = await proxmox.ListContainersAsync(cancellationToken);

        var hosts = new List<string>();
        var hostvars = new Dictionary<string, AnsibleHostVars>();

        foreach (var container in containers.Where(c => c.IsRunning))
        {
            if (!TryBuildHostEntry(container, out var hostname, out var hostVars))
            {
                logger.LogWarning(
                    "Skipping container {Vmid} ({Hostname}) from inventory: VMID cannot be mapped to an address under {NetworkPrefix}.x.",
                    container.Vmid, container.Hostname, _proxmoxOptions.NetworkPrefix);
                continue;
            }

            hosts.Add(hostname);
            hostvars[hostname] = hostVars;
        }

        return BuildInventory(hosts, hostvars);
    }

    private bool TryBuildHostEntry(ContainerSummary container, out string hostname, out AnsibleHostVars hostVars)
    {
        hostname = string.IsNullOrWhiteSpace(container.Hostname) ? container.Vmid.ToString() : container.Hostname;
        hostVars = null!;

        if (!IpAddressCalculator.TryCalculate(container.Vmid, _proxmoxOptions.NetworkPrefix, _proxmoxOptions.IpHostMin, _proxmoxOptions.IpHostMax, out var ipAddress))
        {
            return false;
        }

        hostVars = new AnsibleHostVars(
            AnsibleHost: ipAddress,
            AnsibleUser: _sshOptions.RemoteUser,
            AnsiblePort: _sshOptions.Port,
            AnsibleSshPrivateKeyFile: _sshOptions.OrchestratorPrivateKeyPath);
        return true;
    }

    private static AnsibleInventory BuildInventory(IReadOnlyList<string> hosts, IReadOnlyDictionary<string, AnsibleHostVars> hostvars) =>
        new(Meta: new AnsibleInventoryMeta(hostvars), All: new AnsibleInventoryGroup(hosts, new Dictionary<string, object>()));
}
