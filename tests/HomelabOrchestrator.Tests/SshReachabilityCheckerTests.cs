using System.Net;
using System.Net.Sockets;
using HomelabOrchestrator.Services.Ssh;

namespace HomelabOrchestrator.Tests;

/// <summary>Real loopback sockets, no fake — this is cheap enough that faking the OS's TCP stack would add risk, not remove it.</summary>
public class SshReachabilityCheckerTests
{
    [Fact]
    public async Task Returns_true_as_soon_as_something_is_listening_on_the_port()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var checker = new SshReachabilityChecker();
        var reachable = await checker.WaitUntilReachableAsync(
            "127.0.0.1", port, TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(50));

        Assert.True(reachable);
    }

    [Fact]
    public async Task Returns_false_after_the_timeout_when_nothing_is_listening()
    {
        // A closed port on loopback — nothing ever accepts a connection here.
        var freePort = GetFreePortWithNothingListening();

        var checker = new SshReachabilityChecker();
        var reachable = await checker.WaitUntilReachableAsync(
            "127.0.0.1", freePort, TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(50));

        Assert.False(reachable);
    }

    private static int GetFreePortWithNothingListening()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
