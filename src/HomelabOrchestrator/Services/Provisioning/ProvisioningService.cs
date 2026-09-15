using HomelabOrchestrator.Models;
using HomelabOrchestrator.Options;
using HomelabOrchestrator.Services.Jobs;
using HomelabOrchestrator.Services.Proxmox;
using Microsoft.Extensions.Options;

namespace HomelabOrchestrator.Services.Provisioning;

public class ProvisioningService(
    IProxmoxService proxmox,
    IProvisioningJobStore store,
    IProvisioningJobQueue queue,
    IOptions<ProxmoxOptions> options) : IProvisioningService
{
    private readonly ProxmoxOptions _options = options.Value;

    public async Task<ClusterPlacement> GetPlacementPreviewAsync(CancellationToken cancellationToken = default)
    {
        var vmid = await proxmox.GetNextVmIdAsync(cancellationToken);
        if (!IpAddressCalculator.TryCalculate(vmid, _options.NetworkPrefix, _options.IpHostMin, _options.IpHostMax, out var ipAddress))
        {
            throw new ProxmoxOperationException(
                $"VMID {vmid} cannot be mapped safely to {_options.NetworkPrefix}.x; " +
                $"the last octet must be between {_options.IpHostMin} and {_options.IpHostMax}.");
        }

        var template = await proxmox.FindLatestDebianTemplateAsync(cancellationToken);

        return new ClusterPlacement
        {
            Vmid = vmid,
            IpAddress = ipAddress,
            Template = template,
            Node = _options.Node,
            Bridge = _options.Bridge,
            Gateway = _options.Gateway,
            Subnet = _options.Subnet,
        };
    }

    public async Task<Guid> SubmitAsync(ProvisioningRequest request, CancellationToken cancellationToken = default)
    {
        var job = store.Create(request);
        await queue.EnqueueAsync(job.Id, cancellationToken);
        return job.Id;
    }

    public ProvisioningJob? GetJob(Guid jobId) => store.Get(jobId);
}
