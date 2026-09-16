using System.Net;
using Corsinvest.ProxmoxVE.Api;
using HomelabOrchestrator.Options;
using HomelabOrchestrator.Services.Proxmox;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HomelabOrchestrator.Tests;

/// <summary>
/// Exercises ProxmoxService's response translation against a fake HTTP handler — no live
/// Proxmox server required, per AGENTS.md's testing guidance.
/// </summary>
public class ProxmoxServiceTests
{
    private static ProxmoxService BuildService(Func<HttpRequestMessage, (HttpStatusCode, string)> respond)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new ProxmoxOptions
        {
            Host = "fake-proxmox.invalid",
            ApiToken = "test@pam!unit-tests=00000000-0000-0000-0000-000000000000",
            Node = "testnode",
            TemplateStorage = "local",
        });

        var httpClient = FakeProxmoxHttpHandler.BuildClient(respond);
        var pveClient = new PveClient(options.Value.Host, options.Value.Port, httpClient);

        return new ProxmoxService(options, NullLogger<ProxmoxService>.Instance, pveClient);
    }

    [Fact]
    public async Task GetNextVmIdAsync_parses_the_data_field_as_an_integer()
    {
        var service = BuildService(_ => (HttpStatusCode.OK, """{"data":"142"}"""));

        Assert.Equal(142, await service.GetNextVmIdAsync());
    }

    [Fact]
    public async Task GetNextVmIdAsync_translates_a_proxmox_error_into_a_sanitized_exception()
    {
        var service = BuildService(_ => (HttpStatusCode.BadRequest, """{"data":null,"errors":{"vmid":"already exists"}}"""));

        var ex = await Assert.ThrowsAsync<ProxmoxOperationException>(() => service.GetNextVmIdAsync());
        Assert.Contains("already exists", ex.Message);
    }

    [Fact]
    public async Task FindLatestDebianTemplateAsync_returns_the_newest_matching_template()
    {
        var service = BuildService(_ => (HttpStatusCode.OK, """
            {"data":[
                {"volid":"local:vztmpl/debian-13-standard_13.0-1_amd64.tar.zst"},
                {"volid":"local:vztmpl/debian-13-standard_13.2-1_amd64.tar.zst"},
                {"volid":"local:vztmpl/ubuntu-24.04-standard_24.04-1_amd64.tar.zst"}
            ]}
            """));

        Assert.Equal("local:vztmpl/debian-13-standard_13.2-1_amd64.tar.zst", await service.FindLatestDebianTemplateAsync());
    }

    [Fact]
    public async Task FindLatestDebianTemplateAsync_throws_a_sanitized_exception_when_none_exist()
    {
        var service = BuildService(_ => (HttpStatusCode.OK, """{"data":[]}"""));

        var ex = await Assert.ThrowsAsync<ProxmoxOperationException>(() => service.FindLatestDebianTemplateAsync());
        Assert.Contains("No Debian 13", ex.Message);
    }

    [Fact]
    public async Task CreateContainerAsync_waits_for_the_task_and_returns_the_created_container()
    {
        const string upid = "UPID:testnode:00001234:00005678:0123ABCD:lxccreate:141:test@pam!unit-tests:";

        var service = BuildService(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            return path.EndsWith("/lxc", StringComparison.Ordinal)
                ? (HttpStatusCode.OK, $$"""{"data":"{{upid}}"}""")
                : path.Contains("/status", StringComparison.Ordinal)
                    ? (HttpStatusCode.OK, """{"data":{"status":"stopped","exitstatus":"OK"}}""")
                    : (HttpStatusCode.NotFound, """{"data":null}""");
        });

        var request = new Models.ContainerRequest
        {
            Vmid = 141,
            Hostname = "test-host",
            IpAddress = "10.0.150.141",
            Template = "local:vztmpl/debian-13-standard_13.2-1_amd64.tar.zst",
            SshPublicKeys = "ssh-ed25519 AAAAtest",
            Cores = 2,
            MemoryMB = 2048,
            SwapMB = 512,
            DiskGB = 8,
            Start = true,
            StartAtBoot = true,
        };

        var created = await service.CreateContainerAsync(request);

        Assert.Equal(141, created.Vmid);
        Assert.Equal("test-host", created.Hostname);
        Assert.Equal("10.0.150.141", created.IpAddress);
    }

    [Fact]
    public async Task CreateContainerAsync_throws_a_sanitized_exception_when_the_task_reports_failure_status()
    {
        var service = BuildService(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            return path.EndsWith("/lxc", StringComparison.Ordinal)
                ? (HttpStatusCode.BadRequest, """{"data":null,"errors":{"vmid":"already exists"}}""")
                : (HttpStatusCode.NotFound, """{"data":null}""");
        });

        var request = new Models.ContainerRequest
        {
            Vmid = 141,
            Hostname = "test-host",
            IpAddress = "10.0.150.141",
            Template = "local:vztmpl/debian-13-standard_13.2-1_amd64.tar.zst",
            SshPublicKeys = "ssh-ed25519 AAAAtest",
            Cores = 2,
            MemoryMB = 2048,
            SwapMB = 512,
            DiskGB = 8,
            Start = true,
            StartAtBoot = true,
        };

        var ex = await Assert.ThrowsAsync<ProxmoxOperationException>(() => service.CreateContainerAsync(request));
        Assert.Contains("already exists", ex.Message);
    }

    [Fact]
    public async Task ListContainersAsync_translates_the_node_lxc_index_into_container_summaries()
    {
        var service = BuildService(_ => (HttpStatusCode.OK, """
            {"data":[
                {"vmid":141,"name":"web-01","status":"running"},
                {"vmid":142,"name":"db-01","status":"stopped"}
            ]}
            """));

        var containers = await service.ListContainersAsync();

        Assert.Equal(2, containers.Count);
        Assert.Equal(141, containers[0].Vmid);
        Assert.Equal("web-01", containers[0].Hostname);
        Assert.True(containers[0].IsRunning);
        Assert.Equal(142, containers[1].Vmid);
        Assert.Equal("db-01", containers[1].Hostname);
        Assert.False(containers[1].IsRunning);
    }

    [Fact]
    public async Task ListContainersAsync_returns_an_empty_list_when_there_are_no_containers()
    {
        var service = BuildService(_ => (HttpStatusCode.OK, """{"data":[]}"""));

        Assert.Empty(await service.ListContainersAsync());
    }

    [Fact]
    public async Task ListContainersAsync_translates_a_proxmox_error_into_a_sanitized_exception()
    {
        var service = BuildService(_ => (HttpStatusCode.InternalServerError, """{"data":null}"""));

        await Assert.ThrowsAsync<ProxmoxOperationException>(() => service.ListContainersAsync());
    }
}
