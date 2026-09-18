using HomelabOrchestrator.Models;

namespace HomelabOrchestrator.Services.Ssh;

/// <summary>Pure persistence for enrolled SSH keys — no business rules (approval transitions, allowlisting, etc. live in <see cref="ISshKeyManagementService"/>).</summary>
public interface ISshKeyStore
{
    Task<SshPublicKey?> FindByFingerprintAsync(string fingerprint, CancellationToken cancellationToken = default);

    Task AddAsync(SshPublicKey key, CancellationToken cancellationToken = default);

    Task<SshPublicKey?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SshPublicKey>> ListAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SshPublicKey>> ListEnabledAsync(CancellationToken cancellationToken = default);

    /// <summary>Applies <paramref name="mutate"/> to the tracked entity and saves. Throws if <paramref name="id"/> doesn't exist.</summary>
    Task UpdateAsync(Guid id, Action<SshPublicKey> mutate, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
