namespace HomelabOrchestrator.Services.Ssh;

/// <summary>
/// A bare TCP-level reachability check — never an SSH handshake or authentication attempt. It only
/// answers "is something listening on this port yet," which is the right question for "has this
/// freshly created container finished booting far enough to be worth handing to Ansible." Whether
/// SSH actually authenticates once that's true is Ansible's own job to prove.
/// </summary>
public interface ISshReachabilityChecker
{
    Task<bool> WaitUntilReachableAsync(
        string ipAddress,
        int port,
        TimeSpan timeout,
        TimeSpan pollInterval,
        CancellationToken cancellationToken = default);
}
