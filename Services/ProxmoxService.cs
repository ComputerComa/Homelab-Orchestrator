using System.Diagnostics;
using Corsinvest.ProxmoxVE.Api;
using HomelabOrchestrator.Models;
using Microsoft.Extensions.Options;

namespace HomelabOrchestrator.Services;

/// <summary>
/// Thin wrapper around <see cref="PveClient"/>: builds an authenticated client from
/// <see cref="ProxmoxOptions"/> and exposes the two operations the UI needs.
/// </summary>
public class ProxmoxService(IOptions<ProxmoxOptions> options, ILogger<ProxmoxService> logger) : IProxmoxService
{
    private const string DebianTemplateMarker = "debian-13-";
    private readonly ProxmoxOptions _options = options.Value;

    private PveClient CreateClient() => new(_options.Host)
    {
        ApiToken = $"{_options.TokenId}={_options.TokenSecret}",
        ValidateCertificate = _options.ValidateTlsCertificate,
    };

    public async Task<ClusterPlacement> GetConnectionInfoAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var client = CreateClient();

            var nextIdResult = await client.Cluster.Nextid.Nextid();
            EnsureSuccess(nextIdResult, "get the next available VMID");
            var vmid = int.Parse((string)nextIdResult.ToData());

            return new ClusterPlacement
            {
                Vmid = vmid,
                IpAddress = CalculateIpAddress(vmid),
                Template = await FindLatestDebian13TemplateAsync(client),
                Node = _options.Node,
                Bridge = _options.Bridge,
                Gateway = _options.Gateway,
                Subnet = _options.Subnet,
            };
        }
        catch (Exception ex) when (ex is not ProxmoxOperationException)
        {
            throw WrapUnexpected(ex, "reach Proxmox");
        }
    }

    public async Task<CreatedContainer> CreateContainerAsync(ContainerRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = CreateClient();
            var stopwatch = Stopwatch.StartNew();

            var netConfig = $"name=eth0,bridge={_options.Bridge},ip={request.IpAddress}/{_options.Subnet},gw={_options.Gateway}";

            var result = await client.Nodes[_options.Node].Lxc.CreateVm(
                ostemplate: request.Template,
                vmid: request.Vmid,
                hostname: request.Hostname,
                rootfs: $"{_options.RootfsStorage}:{request.DiskGB}",
                cores: request.Cores,
                memory: request.MemoryMB,
                swap: request.SwapMB,
                netN: new Dictionary<int, string> { [0] = netConfig },
                nameserver: _options.Nameserver,
                unprivileged: true,
                onboot: request.StartAtBoot,
                start: request.Start,
                tags: "base;managed-by-orchestrator",
                ssh_public_keys: request.SshPublicKey,
                features: "nesting=1");

            EnsureSuccess(result, "create the LXC container");

            logger.LogInformation("Proxmox is creating container {Vmid} ({Hostname})", request.Vmid, request.Hostname);

            var finished = await client.WaitForTaskToFinishAsync(result, wait: 2000, timeout: 8 * 60 * 1000);
            if (!finished)
            {
                throw new ProxmoxOperationException(
                    $"Proxmox did not finish creating container {request.Vmid} within the expected time. " +
                    "Check the task log in the Proxmox web UI.");
            }

            return new CreatedContainer(request.Vmid, request.Hostname, request.IpAddress, request.Template, stopwatch.Elapsed);
        }
        catch (Exception ex) when (ex is not ProxmoxOperationException)
        {
            throw WrapUnexpected(ex, $"create container {request.Vmid}");
        }
    }

    private ProxmoxOperationException WrapUnexpected(Exception ex, string action)
    {
        logger.LogError(ex, "Unexpected error trying to {Action}", action);
        return new ProxmoxOperationException($"Could not {action}: {ex.Message}");
    }

    private string CalculateIpAddress(int vmid)
    {
        if (vmid < _options.IpHostMin || vmid > _options.IpHostMax)
        {
            throw new ProxmoxOperationException(
                $"VMID {vmid} cannot be mapped safely to {_options.IpNetworkPrefix}.x; " +
                $"the last octet must be between {_options.IpHostMin} and {_options.IpHostMax}.");
        }

        return $"{_options.IpNetworkPrefix}.{vmid}";
    }

    private async Task<string> FindLatestDebian13TemplateAsync(PveClient client)
    {
        var content = await client.Nodes[_options.Node].Storage[_options.TemplateStorage].Content.Index(content: "vztmpl");
        EnsureSuccess(content, $"list templates in storage '{_options.TemplateStorage}'");

        var template = content.ToEnumerable()
            .Select(item => (string)item.volid)
            .Where(volid => volid.Contains(":vztmpl/", StringComparison.OrdinalIgnoreCase)
                         && volid.Contains(DebianTemplateMarker, StringComparison.OrdinalIgnoreCase))
            .OrderBy(volid => volid, StringComparer.Ordinal)
            .LastOrDefault();

        if (template is null)
        {
            throw new ProxmoxOperationException(
                $"No Debian 13 LXC template found in storage '{_options.TemplateStorage}'. " +
                "Download one from the Proxmox template browser first.");
        }

        return template;
    }

    private static void EnsureSuccess(Result result, string action)
    {
        if (result.IsSuccessStatusCode && !result.ResponseInError) return;

        var detail = result.ResponseInError ? result.GetError() : result.ReasonPhrase;
        throw new ProxmoxOperationException($"Failed to {action}: {detail}");
    }
}
