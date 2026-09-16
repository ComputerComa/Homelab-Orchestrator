using HomelabOrchestrator.Models;
using HomelabOrchestrator.Options;
using HomelabOrchestrator.Services.Proxmox;
using Microsoft.Extensions.Options;

namespace HomelabOrchestrator.Services.Provisioning;

public class ContainerReconciliationService(IProxmoxService proxmox, IOptions<ProxmoxOptions> proxmoxOptions) : IContainerReconciliationService
{
    private readonly ProxmoxOptions _options = proxmoxOptions.Value;

    public async Task<IReadOnlyList<ReconciliationCandidate>> ListCandidatesAsync(CancellationToken cancellationToken = default)
    {
        var containers = await proxmox.ListContainersAsync(cancellationToken);
        var candidates = new List<ReconciliationCandidate>();

        foreach (var container in containers.Where(c => !c.Tags.Contains(ProxmoxTags.ManagedByOrchestrator)))
        {
            // A VMID outside the convention's usable range has no expected address to compare
            // against at all, so there's nothing to offer for it here.
            if (!IpAddressCalculator.TryCalculate(container.Vmid, _options.NetworkPrefix, _options.IpHostMin, _options.IpHostMax, out var expectedAddress))
            {
                continue;
            }

            var actualAddress = await proxmox.GetContainerAddressAsync(container.Vmid, cancellationToken);
            candidates.Add(new ReconciliationCandidate(container.Vmid, container.Hostname, expectedAddress, actualAddress));
        }

        return candidates;
    }

    public async Task<AdoptionResult> AdoptAsync(IReadOnlyList<int> vmids, CancellationToken cancellationToken = default)
    {
        var adopted = new List<int>();
        var skipped = new List<int>();

        foreach (var vmid in vmids)
        {
            if (!IpAddressCalculator.TryCalculate(vmid, _options.NetworkPrefix, _options.IpHostMin, _options.IpHostMax, out var expectedAddress))
            {
                skipped.Add(vmid);
                continue;
            }

            var actualAddress = await proxmox.GetContainerAddressAsync(vmid, cancellationToken);
            if (actualAddress != expectedAddress)
            {
                skipped.Add(vmid);
                continue;
            }

            await proxmox.AddTagAsync(vmid, ProxmoxTags.ManagedByOrchestrator, cancellationToken);
            adopted.Add(vmid);
        }

        return new AdoptionResult(adopted, skipped);
    }
}
