using HomelabOrchestrator.Models;
using HomelabOrchestrator.Options;
using HomelabOrchestrator.Services.Jobs;
using HomelabOrchestrator.Services.Proxmox;
using HomelabOrchestrator.Services.Ssh;
using Microsoft.Extensions.Logging.Abstractions;

namespace HomelabOrchestrator.Tests;

/// <summary>Exercises the real BackgroundService lifecycle (StartAsync/StopAsync) against fakes — no live Proxmox server.</summary>
public class ProvisioningWorkerTests
{
    [Fact]
    public async Task Worker_fetches_combined_keys_fresh_and_passes_them_to_the_proxmox_request()
    {
        var store = new InMemoryProvisioningJobStore();
        var queue = new ProvisioningJobQueue();
        var proxmox = new CapturingProxmoxService();
        var sshKeys = new FixedSshPublicKeyProvider("ssh-ed25519 AAAAtest orchestrator");
        var options = Microsoft.Extensions.Options.Options.Create(new ProxmoxOptions
        {
            NetworkPrefix = "10.0.150",
            IpHostMin = 2,
            IpHostMax = 254,
        });

        var worker = new ProvisioningWorker(queue, store, proxmox, sshKeys, options, NullLogger<ProvisioningWorker>.Instance);

        var request = new ProvisioningRequest("test-host", Cores: 2, MemoryMB: 2048, SwapMB: 512, DiskGB: 8, Start: true, StartAtBoot: true);
        var job = store.Create(request);
        await queue.EnqueueAsync(job.Id);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            var finished = await WaitForCompletionAsync(store, job.Id);
            Assert.Equal(ProvisioningStage.Succeeded, finished.Stage);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        Assert.Equal(1, sshKeys.CallCount);
        Assert.NotNull(proxmox.LastRequest);
        Assert.Equal("ssh-ed25519 AAAAtest orchestrator", proxmox.LastRequest!.SshPublicKeys);
        Assert.Equal("test-host", proxmox.LastRequest.Hostname);
        Assert.Equal(100, proxmox.LastRequest.Vmid);
        Assert.Equal("10.0.150.100", proxmox.LastRequest.IpAddress);
    }

    private static async Task<ProvisioningJob> WaitForCompletionAsync(IProvisioningJobStore store, Guid jobId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            var job = store.Get(jobId);
            if (job is { IsFinished: true })
            {
                return job;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException("Provisioning job did not finish in time.");
    }

    private sealed class CapturingProxmoxService : IProxmoxService
    {
        public ContainerRequest? LastRequest { get; private set; }

        public Task<int> GetNextVmIdAsync(CancellationToken cancellationToken = default) => Task.FromResult(100);

        public Task<string> FindLatestDebianTemplateAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult("local:vztmpl/debian-13-standard_13.0-1_amd64.tar.zst");

        public Task<IReadOnlyList<ContainerSummary>> ListContainersAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ContainerSummary>>([]);

        public Task<CreatedContainer> CreateContainerAsync(ContainerRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(new CreatedContainer(request.Vmid, request.Hostname, request.IpAddress, request.Template, TimeSpan.Zero));
        }

        public Task<string?> GetContainerAddressAsync(int vmid, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task AddTagAsync(int vmid, string tag, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FixedSshPublicKeyProvider(string keys) : ISshPublicKeyProvider
    {
        public int CallCount { get; private set; }

        public Task<string> GetCombinedPublicKeysAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(keys);
        }

        public Task<string?> GetOrchestratorPublicKeyAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
