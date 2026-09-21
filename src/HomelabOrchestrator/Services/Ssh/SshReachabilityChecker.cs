using System.Net.Sockets;

namespace HomelabOrchestrator.Services.Ssh;

public class SshReachabilityChecker : ISshReachabilityChecker
{
    private static readonly TimeSpan ConnectAttemptTimeout = TimeSpan.FromSeconds(5);

    public async Task<bool> WaitUntilReachableAsync(
        string ipAddress,
        int port,
        TimeSpan timeout,
        TimeSpan pollInterval,
        CancellationToken cancellationToken = default)
    {
        var deadline = DateTime.UtcNow + timeout;
        do
        {
            if (await TryConnectOnceAsync(ipAddress, port, cancellationToken))
            {
                return true;
            }

            await Task.Delay(pollInterval, cancellationToken);
        }
        while (DateTime.UtcNow < deadline);

        return false;
    }

    private static async Task<bool> TryConnectOnceAsync(string ipAddress, int port, CancellationToken cancellationToken)
    {
        using var client = new TcpClient();
        using var attemptTimeout = new CancellationTokenSource(ConnectAttemptTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, attemptTimeout.Token);

        try
        {
            await client.ConnectAsync(ipAddress, port, linked.Token);
            return true;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Connection refused, timed out, or the host isn't up yet — all just mean "not reachable yet".
            return false;
        }
    }
}
