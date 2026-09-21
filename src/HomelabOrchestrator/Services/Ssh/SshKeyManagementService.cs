using System.Numerics;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using HomelabOrchestrator.Models;
using HomelabOrchestrator.Options;
using HomelabOrchestrator.Services.Jobs;
using HomelabOrchestrator.Services.Proxmox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HomelabOrchestrator.Services.Ssh;

/// <summary>
/// The only thing Pages and the enrollment endpoint call for SSH key management — owns every
/// business rule so none of it leaks into a page model or the endpoint handler. See
/// <see cref="ISshKeyManagementService"/> for the contract.
/// </summary>
public partial class SshKeyManagementService(
    ISshKeyStore keyStore,
    ISshEnrollmentTokenStore tokenStore,
    ISshPublicKeyProvider sshPublicKeyProvider,
    Ansible.IAnsibleRunnerService runnerService,
    IOptions<SshSyncOptions> syncOptions) : ISshKeyManagementService
{
    private const int TokenLifetimeMinutes = 10;
    private const int MaxDeviceNameLength = 64;
    private const int MaxPublicKeyLength = 4096;
    private const int MinimumRsaModulusBits = 3072;

    private static readonly HashSet<string> AllowedAlgorithms = new(StringComparer.Ordinal)
    {
        "ssh-ed25519", "ecdsa-sha2-nistp256", "ecdsa-sha2-nistp384", "ecdsa-sha2-nistp521", "ssh-rsa",
    };

    [GeneratedRegex(@"^[A-Za-z0-9 ._-]{1,64}$")]
    private static partial Regex DeviceNamePattern();

    public Task<IReadOnlyList<SshPublicKey>> ListKeysAsync(CancellationToken cancellationToken = default) =>
        keyStore.ListAsync(cancellationToken);

    public async Task<EnrollmentResult> EnrollAsync(string tokenPlaintext, string? deviceName, string? publicKeyLine, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(deviceName) || deviceName.Length > MaxDeviceNameLength || !DeviceNamePattern().IsMatch(deviceName))
        {
            return EnrollmentResult.Failure(EnrollmentFailureReason.InvalidDeviceName);
        }

        // Cheap rejection before ever attempting to parse as public: a real .pub line is always a
        // single short line with no PEM framing. This catches the classic "pasted the private key
        // by accident" case (always multi-line, always starts with "-----BEGIN ... PRIVATE KEY")
        // without needing a real ASN.1/PEM parser.
        if (string.IsNullOrWhiteSpace(publicKeyLine) ||
            publicKeyLine.Length > MaxPublicKeyLength ||
            publicKeyLine.Contains('\n') ||
            publicKeyLine.Contains("-----BEGIN", StringComparison.Ordinal) ||
            publicKeyLine.Contains("PRIVATE KEY", StringComparison.OrdinalIgnoreCase))
        {
            return EnrollmentResult.Failure(EnrollmentFailureReason.PrivateKeyRejected);
        }

        if (!OpenSshPublicKey.TryParse(publicKeyLine.Trim(), out var record))
        {
            return EnrollmentResult.Failure(EnrollmentFailureReason.MalformedKey);
        }

        if (!IsAllowedAlgorithm(record))
        {
            return EnrollmentResult.Failure(EnrollmentFailureReason.UnsupportedAlgorithm);
        }

        // The submitted comment (if any) is discarded here — only "{Type} {EncodedData}" from
        // `record` is ever stored or used, never the original line's trailing comment.
        var fingerprint = SshKeyFingerprint.Compute(record);
        if (await keyStore.FindByFingerprintAsync(fingerprint, cancellationToken) is not null)
        {
            return EnrollmentResult.Failure(EnrollmentFailureReason.DuplicateFingerprint);
        }

        // Consume the token only now that everything else about the request is known-good — a
        // malformed body must never burn a valid, still-usable token.
        if (!await tokenStore.TryConsumeAsync(SshEnrollmentTokenHasher.Hash(tokenPlaintext), DateTime.UtcNow, cancellationToken))
        {
            return EnrollmentResult.Failure(EnrollmentFailureReason.TokenExpiredOrUsed);
        }

        var key = new SshPublicKey
        {
            Id = Guid.NewGuid(),
            DeviceName = deviceName.Trim(),
            Algorithm = record.Type,
            KeyDataBase64 = record.EncodedData,
            Fingerprint = fingerprint,
            Status = SshKeyStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow,
        };

        try
        {
            await keyStore.AddAsync(key, cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Race backstop: the unique index on Fingerprint is the real guarantee, the
            // FindByFingerprintAsync check above is just the common-case fast path.
            return EnrollmentResult.Failure(EnrollmentFailureReason.DuplicateFingerprint);
        }

        return EnrollmentResult.Success(fingerprint);
    }

    public async Task<string> CreateEnrollmentTokenAsync(string? createdBy, CancellationToken cancellationToken = default)
    {
        var tokenBytes = RandomNumberGenerator.GetBytes(32);
        var plaintext = Convert.ToBase64String(tokenBytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');

        await tokenStore.AddAsync(new SshEnrollmentToken
        {
            Id = Guid.NewGuid(),
            TokenHash = SshEnrollmentTokenHasher.Hash(plaintext),
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(TokenLifetimeMinutes),
            CreatedBy = createdBy,
        }, cancellationToken);

        return plaintext;
    }

    public async Task ApproveAsync(Guid keyId, string? approvedBy, CancellationToken cancellationToken = default)
    {
        var key = await keyStore.GetAsync(keyId, cancellationToken) ?? throw new InvalidOperationException($"SSH key {keyId} does not exist.");
        if (key.Status != SshKeyStatus.Pending)
        {
            throw new InvalidOperationException($"SSH key {keyId} cannot be approved from status {key.Status}; only a Pending key can be approved.");
        }

        await keyStore.UpdateAsync(keyId, k =>
        {
            k.Status = SshKeyStatus.Enabled;
            k.ApprovedAtUtc = DateTime.UtcNow;
            k.ApprovedBy = approvedBy;
        }, cancellationToken);
    }

    public async Task RevokeAsync(Guid keyId, string? revokedBy, CancellationToken cancellationToken = default)
    {
        var key = await keyStore.GetAsync(keyId, cancellationToken) ?? throw new InvalidOperationException($"SSH key {keyId} does not exist.");
        if (key.Status != SshKeyStatus.Enabled)
        {
            throw new InvalidOperationException($"SSH key {keyId} cannot be revoked from status {key.Status}; only an Enabled key can be revoked.");
        }

        await keyStore.UpdateAsync(keyId, k =>
        {
            k.Status = SshKeyStatus.Revoked;
            k.RevokedAtUtc = DateTime.UtcNow;
            k.RevokedBy = revokedBy;
        }, cancellationToken);
    }

    public async Task DeletePendingAsync(Guid keyId, CancellationToken cancellationToken = default)
    {
        var key = await keyStore.GetAsync(keyId, cancellationToken) ?? throw new InvalidOperationException($"SSH key {keyId} does not exist.");
        if (key.Status != SshKeyStatus.Pending)
        {
            throw new InvalidOperationException($"SSH key {keyId} cannot be deleted from status {key.Status}; only a Pending key can be deleted.");
        }

        await keyStore.DeleteAsync(keyId, cancellationToken);
    }

    public async Task<Guid> SyncAsync(string? submittedBy, string? targetHostname = null, CancellationToken cancellationToken = default)
    {
        var enabled = await keyStore.ListEnabledAsync(cancellationToken);

        var orchestratorLine = await sshPublicKeyProvider.GetOrchestratorPublicKeyAsync(cancellationToken)
            ?? throw new InvalidOperationException("The orchestrator's own public key could not be read; refusing to synchronize.");
        if (!OpenSshPublicKey.TryParse(orchestratorLine, out var orchestratorRecord))
        {
            throw new InvalidOperationException("The orchestrator's own public key is not a valid OpenSSH key; refusing to synchronize.");
        }

        // Opaque, non-identifying comments — never the device name or original submitted comment.
        var lines = enabled
            .Select(k => $"{k.Algorithm} {k.KeyDataBase64} homelab-orchestrator-managed-{k.Id:N}")
            .Append($"{orchestratorRecord.Type} {orchestratorRecord.EncodedData} homelab-orchestrator-managed-self")
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var extraVars = new Dictionary<string, object?>
        {
            ["sync_ssh_keys_authorized_keys"] = lines,
            ["sync_ssh_keys_exclusive"] = syncOptions.Value.Exclusive,
        };

        var request = targetHostname is null
            ? new AnsibleRunRequest("sync-ssh-keys", AnsibleRunTargetKind.TagGroup, ProxmoxTags.ManagedByOrchestrator, extraVars)
            : new AnsibleRunRequest("sync-ssh-keys", AnsibleRunTargetKind.Vm, targetHostname, extraVars);
        return await runnerService.SubmitAsync(request, submittedBy, cancellationToken);
    }

    private static bool IsAllowedAlgorithm(SshPublicKeyRecord record)
    {
        if (!AllowedAlgorithms.Contains(record.Type))
        {
            return false;
        }

        if (record.Type != "ssh-rsa")
        {
            return true;
        }

        var blob = Convert.FromBase64String(record.EncodedData);
        var bitLength = TryGetRsaModulusBitLength(blob);
        return bitLength is not null && bitLength >= MinimumRsaModulusBits;
    }

    /// <summary>
    /// The ssh-rsa wire format is: length-prefixed type string, length-prefixed exponent (mpint),
    /// length-prefixed modulus (mpint). This walks past the first two fields to measure the third.
    /// </summary>
    private static int? TryGetRsaModulusBitLength(byte[] blob)
    {
        var offset = 0;
        if (!TrySkipLengthPrefixed(blob, ref offset)) // type string
        {
            return null;
        }

        if (!TrySkipLengthPrefixed(blob, ref offset)) // exponent e
        {
            return null;
        }

        if (!TryReadLength(blob, ref offset, out var modulusLength) || offset + modulusLength > blob.Length)
        {
            return null;
        }

        var bitLength = modulusLength * 8;
        for (var i = 0; i < modulusLength; i++)
        {
            var b = blob[offset + i];
            if (b == 0)
            {
                bitLength -= 8;
                continue;
            }

            bitLength -= 8 - (BitOperations.Log2(b) + 1);
            break;
        }

        return bitLength;
    }

    private static bool TryReadLength(byte[] blob, ref int offset, out int length)
    {
        length = 0;
        if (offset + 4 > blob.Length)
        {
            return false;
        }

        length = (blob[offset] << 24) | (blob[offset + 1] << 16) | (blob[offset + 2] << 8) | blob[offset + 3];
        offset += 4;
        return length >= 0;
    }

    private static bool TrySkipLengthPrefixed(byte[] blob, ref int offset)
    {
        if (!TryReadLength(blob, ref offset, out var length) || offset + length > blob.Length)
        {
            return false;
        }

        offset += length;
        return true;
    }
}
