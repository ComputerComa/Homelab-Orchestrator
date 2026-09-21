using HomelabOrchestrator.Models;

namespace HomelabOrchestrator.Services.Ssh;

public interface ISshEnrollmentTokenStore
{
    Task AddAsync(SshEnrollmentToken token, CancellationToken cancellationToken = default);

    /// <summary>Read-only gate used by the enrollment-token authentication handler — does not consume the token.</summary>
    Task<bool> IsValidAsync(string tokenHash, DateTime nowUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically marks the token used iff it exists, is unused, and unexpired, in a single
    /// UPDATE statement — closes the race that a separate check-then-write would leave open.
    /// Returns whether this call is the one that consumed it.
    /// </summary>
    Task<bool> TryConsumeAsync(string tokenHash, DateTime nowUtc, CancellationToken cancellationToken = default);
}
