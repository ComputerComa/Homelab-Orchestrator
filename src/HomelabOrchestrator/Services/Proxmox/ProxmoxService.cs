using System.Diagnostics;
using Corsinvest.ProxmoxVE.Api;
using HomelabOrchestrator.Models;
using HomelabOrchestrator.Options;
using HomelabOrchestrator.Services.Provisioning;
using Microsoft.Extensions.Options;

namespace HomelabOrchestrator.Services.Proxmox;

/// <summary>
/// Thin wrapper around <see cref="PveClient"/>. Registered as a singleton so the underlying
/// <see cref="PveClient"/> (and its internal <see cref="HttpClient"/>) is built once and reused,
/// instead of a fresh handler being opened for every call.
/// </summary>
public class ProxmoxService : IProxmoxService
{
    private readonly ProxmoxOptions _options;
    private readonly ILogger<ProxmoxService> _logger;
    private readonly Lazy<PveClient> _client;

    public ProxmoxService(IOptions<ProxmoxOptions> options, ILogger<ProxmoxService> logger)
        : this(options, logger, injectedClient: null)
    {
    }

    /// <summary>
    /// Test seam: lets the test suite supply a <see cref="PveClient"/> wired to a fake
    /// <see cref="HttpMessageHandler"/>, so Proxmox response translation can be verified
    /// without a live Proxmox server. Not intended for application code.
    /// </summary>
    internal ProxmoxService(IOptions<ProxmoxOptions> options, ILogger<ProxmoxService> logger, PveClient? injectedClient)
    {
        _options = options.Value;
        _logger = logger;
        _client = new Lazy<PveClient>(() => injectedClient ?? new PveClient(_options.Host, _options.Port)
        {
            ApiToken = _options.ApiToken,
            ValidateCertificate = _options.ValidateCertificate,
        });
    }

    public async Task<int> GetNextVmIdAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _client.Value.Cluster.Nextid.Nextid();
            EnsureSuccess(result, "get the next available VMID");
            return int.Parse((string)result.ToData());
        }
        catch (Exception ex) when (ex is not ProxmoxOperationException)
        {
            throw WrapUnexpected(ex, "get the next available VMID");
        }
    }

    public async Task<string> FindLatestDebianTemplateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var content = await _client.Value.Nodes[_options.Node].Storage[_options.TemplateStorage].Content.Index(content: "vztmpl");
            EnsureSuccess(content, $"list templates in storage '{_options.TemplateStorage}'");

            var volids = content.ToEnumerable().Select(item => (string)item.volid);
            var template = DebianTemplateSelector.SelectLatest(volids);

            if (template is null)
            {
                throw new ProxmoxOperationException(
                    $"No Debian 13 LXC template found in storage '{_options.TemplateStorage}'. " +
                    "Download one from the Proxmox template browser first.");
            }

            return template;
        }
        catch (Exception ex) when (ex is not ProxmoxOperationException)
        {
            throw WrapUnexpected(ex, $"list templates in storage '{_options.TemplateStorage}'");
        }
    }

    public async Task<CreatedContainer> CreateContainerAsync(ContainerRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var stopwatch = Stopwatch.StartNew();
            var netConfig = $"name=eth0,bridge={_options.Bridge},ip={request.IpAddress}/{_options.Subnet},gw={_options.Gateway}";

            var result = await _client.Value.Nodes[_options.Node].Lxc.CreateVm(
                ostemplate: request.Template,
                vmid: request.Vmid,
                hostname: request.Hostname,
                rootfs: $"{_options.RootfsStorage}:{request.DiskGB}",
                cores: request.Cores,
                memory: request.MemoryMB,
                swap: request.SwapMB,
                netN: new Dictionary<int, string> { [0] = netConfig },
                nameserver: string.Join(' ', _options.NameServers),
                unprivileged: true,
                onboot: request.StartAtBoot,
                start: request.Start,
                tags: "base;managed-by-orchestrator",
                ssh_public_keys: request.SshPublicKey,
                features: "nesting=1");

            EnsureSuccess(result, "create the LXC container");

            _logger.LogInformation("Proxmox is creating container {Vmid} ({Hostname})", request.Vmid, request.Hostname);

            var finished = await _client.Value.WaitForTaskToFinishAsync(result, wait: 2000, timeout: 8 * 60 * 1000);
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
        _logger.LogError(ex, "Unexpected error trying to {Action}", action);
        return new ProxmoxOperationException($"Could not {action}. See the server log for details.");
    }

    private static void EnsureSuccess(Result result, string action)
    {
        if (result.IsSuccessStatusCode && !result.ResponseInError) return;

        var detail = result.ResponseInError ? result.GetError() : result.ReasonPhrase;
        throw new ProxmoxOperationException($"Failed to {action}: {detail}");
    }
}
