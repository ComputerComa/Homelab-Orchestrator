using System.Text;
using HomelabOrchestrator.Models;
using HomelabOrchestrator.Options;
using HomelabOrchestrator.Services.Ansible;
using HomelabOrchestrator.Services.Jobs;
using HomelabOrchestrator.Services.Proxmox;
using HomelabOrchestrator.Services.Ssh;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HomelabOrchestrator.Tests;

/// <summary>
/// Exercises every business rule in <see cref="SshKeyManagementService"/> against hand-rolled
/// fakes for every dependency — no mocking framework, matching the rest of this test suite.
/// </summary>
public class SshKeyManagementServiceTests
{
    [Fact]
    public async Task EnrollAsync_saves_a_valid_key_as_Pending_with_the_correct_fingerprint_and_discards_the_comment()
    {
        var (service, keys, _, _) = Build();
        var token = await service.CreateEnrollmentTokenAsync("alice");

        var result = await service.EnrollAsync(token, "my-laptop", MakeKeyLine("ssh-ed25519") + " someone@laptop");

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Fingerprint);
        var saved = Assert.Single(keys.Keys);
        Assert.Equal(SshKeyStatus.Pending, saved.Status);
        Assert.Equal(result.Fingerprint, saved.Fingerprint);
        Assert.Equal("my-laptop", saved.DeviceName);
        Assert.DoesNotContain("someone@laptop", saved.KeyDataBase64);
    }

    [Fact]
    public async Task EnrollAsync_rejects_a_duplicate_fingerprint()
    {
        var (service, keys, _, _) = Build();
        var keyLine = MakeKeyLine("ssh-ed25519", payload: [5, 5, 5]);
        var first = await service.EnrollAsync(await service.CreateEnrollmentTokenAsync(null), "device-one", keyLine);
        Assert.True(first.Succeeded);

        var second = await service.EnrollAsync(await service.CreateEnrollmentTokenAsync(null), "device-two", keyLine);

        Assert.False(second.Succeeded);
        Assert.Equal(EnrollmentFailureReason.DuplicateFingerprint, second.FailureReason);
        Assert.Single(keys.Keys);
    }

    [Theory]
    [InlineData("-----BEGIN OPENSSH PRIVATE KEY-----\nabcdef\n-----END OPENSSH PRIVATE KEY-----")]
    [InlineData("ssh-ed25519 AAAA\nsome-other-line")]
    public async Task EnrollAsync_rejects_private_key_material_before_ever_parsing_it(string pasted)
    {
        var (service, keys, _, _) = Build();
        var token = await service.CreateEnrollmentTokenAsync(null);

        var result = await service.EnrollAsync(token, "my-laptop", pasted);

        Assert.False(result.Succeeded);
        Assert.Equal(EnrollmentFailureReason.PrivateKeyRejected, result.FailureReason);
        Assert.Empty(keys.Keys);
    }

    [Fact]
    public async Task EnrollAsync_rejects_a_malformed_key_line()
    {
        var (service, _, _, _) = Build();
        var token = await service.CreateEnrollmentTokenAsync(null);

        var result = await service.EnrollAsync(token, "my-laptop", "ssh-ed25519 not-valid-base64!!!");

        Assert.False(result.Succeeded);
        Assert.Equal(EnrollmentFailureReason.MalformedKey, result.FailureReason);
    }

    [Fact]
    public async Task EnrollAsync_rejects_an_algorithm_that_is_not_on_the_allowlist()
    {
        var (service, _, _, _) = Build();
        var token = await service.CreateEnrollmentTokenAsync(null);

        var result = await service.EnrollAsync(token, "my-laptop", MakeKeyLine("ssh-dss"));

        Assert.False(result.Succeeded);
        Assert.Equal(EnrollmentFailureReason.UnsupportedAlgorithm, result.FailureReason);
    }

    [Fact]
    public async Task EnrollAsync_accepts_RSA_with_a_modulus_of_at_least_3072_bits()
    {
        var (service, keys, _, _) = Build();
        var token = await service.CreateEnrollmentTokenAsync(null);

        var result = await service.EnrollAsync(token, "my-laptop", MakeRsaKeyLine(3072));

        Assert.True(result.Succeeded);
        Assert.Single(keys.Keys);
    }

    [Fact]
    public async Task EnrollAsync_rejects_RSA_with_a_modulus_smaller_than_3072_bits()
    {
        var (service, _, _, _) = Build();
        var token = await service.CreateEnrollmentTokenAsync(null);

        var result = await service.EnrollAsync(token, "my-laptop", MakeRsaKeyLine(2048));

        Assert.False(result.Succeeded);
        Assert.Equal(EnrollmentFailureReason.UnsupportedAlgorithm, result.FailureReason);
    }

    [Fact]
    public async Task EnrollAsync_rejects_an_empty_device_name()
    {
        var (service, _, _, _) = Build();
        var token = await service.CreateEnrollmentTokenAsync(null);

        var result = await service.EnrollAsync(token, "   ", MakeKeyLine("ssh-ed25519"));

        Assert.False(result.Succeeded);
        Assert.Equal(EnrollmentFailureReason.InvalidDeviceName, result.FailureReason);
    }

    [Fact]
    public async Task EnrollAsync_rejects_a_device_name_with_disallowed_characters()
    {
        var (service, _, _, _) = Build();
        var token = await service.CreateEnrollmentTokenAsync(null);

        var result = await service.EnrollAsync(token, "bad/name", MakeKeyLine("ssh-ed25519"));

        Assert.False(result.Succeeded);
        Assert.Equal(EnrollmentFailureReason.InvalidDeviceName, result.FailureReason);
    }

    [Fact]
    public async Task EnrollAsync_rejects_a_device_name_over_64_characters()
    {
        var (service, _, _, _) = Build();
        var token = await service.CreateEnrollmentTokenAsync(null);

        var result = await service.EnrollAsync(token, new string('a', 65), MakeKeyLine("ssh-ed25519"));

        Assert.False(result.Succeeded);
        Assert.Equal(EnrollmentFailureReason.InvalidDeviceName, result.FailureReason);
    }

    [Fact]
    public async Task EnrollAsync_rejects_an_expired_token()
    {
        var (service, keys, tokens, _) = Build();
        const string plaintext = "expired-token";
        tokens.Tokens.Add(new SshEnrollmentToken
        {
            Id = Guid.NewGuid(),
            TokenHash = SshEnrollmentTokenHasher.Hash(plaintext),
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-20),
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-10),
        });

        var result = await service.EnrollAsync(plaintext, "my-laptop", MakeKeyLine("ssh-ed25519"));

        Assert.False(result.Succeeded);
        Assert.Equal(EnrollmentFailureReason.TokenExpiredOrUsed, result.FailureReason);
        Assert.Empty(keys.Keys);
    }

    [Fact]
    public async Task EnrollAsync_consumes_the_token_so_it_cannot_enroll_a_second_key()
    {
        var (service, _, _, _) = Build();
        var token = await service.CreateEnrollmentTokenAsync("alice");

        var first = await service.EnrollAsync(token, "device-one", MakeKeyLine("ssh-ed25519", payload: [9, 9, 9]));
        Assert.True(first.Succeeded);

        var second = await service.EnrollAsync(token, "device-two", MakeKeyLine("ssh-ed25519", payload: [1, 1, 1]));

        Assert.False(second.Succeeded);
        Assert.Equal(EnrollmentFailureReason.TokenExpiredOrUsed, second.FailureReason);
    }

    [Fact]
    public async Task EnrollAsync_does_not_consume_the_token_when_the_request_is_otherwise_invalid()
    {
        var (service, _, _, _) = Build();
        var token = await service.CreateEnrollmentTokenAsync(null);

        var badAttempt = await service.EnrollAsync(token, "my-laptop", "not-a-valid-key-line");
        Assert.False(badAttempt.Succeeded);

        var goodAttempt = await service.EnrollAsync(token, "my-laptop", MakeKeyLine("ssh-ed25519"));
        Assert.True(goodAttempt.Succeeded);
    }

    [Fact]
    public async Task CreateEnrollmentTokenAsync_returns_a_plaintext_token_that_matches_the_stored_hash()
    {
        var (service, _, tokens, _) = Build();

        var plaintext = await service.CreateEnrollmentTokenAsync("alice");

        var stored = Assert.Single(tokens.Tokens);
        Assert.Equal(SshEnrollmentTokenHasher.Hash(plaintext), stored.TokenHash);
        Assert.Equal("alice", stored.CreatedBy);
    }

    [Fact]
    public async Task ApproveAsync_transitions_Pending_to_Enabled()
    {
        var (service, keys, _, _) = Build();
        var key = NewKey(SshKeyStatus.Pending);
        keys.Keys.Add(key);

        await service.ApproveAsync(key.Id, "alice");

        Assert.Equal(SshKeyStatus.Enabled, key.Status);
        Assert.Equal("alice", key.ApprovedBy);
        Assert.NotNull(key.ApprovedAtUtc);
    }

    [Fact]
    public async Task ApproveAsync_throws_when_the_key_is_not_Pending()
    {
        var (service, keys, _, _) = Build();
        var key = NewKey(SshKeyStatus.Enabled);
        keys.Keys.Add(key);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApproveAsync(key.Id, "alice"));
    }

    [Fact]
    public async Task RevokeAsync_transitions_Enabled_to_Revoked()
    {
        var (service, keys, _, _) = Build();
        var key = NewKey(SshKeyStatus.Enabled);
        keys.Keys.Add(key);

        await service.RevokeAsync(key.Id, "alice");

        Assert.Equal(SshKeyStatus.Revoked, key.Status);
        Assert.Equal("alice", key.RevokedBy);
        Assert.NotNull(key.RevokedAtUtc);
    }

    [Fact]
    public async Task RevokeAsync_throws_when_the_key_is_Pending()
    {
        var (service, keys, _, _) = Build();
        var key = NewKey(SshKeyStatus.Pending);
        keys.Keys.Add(key);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RevokeAsync(key.Id, "alice"));
    }

    [Fact]
    public async Task DeletePendingAsync_removes_a_Pending_key()
    {
        var (service, keys, _, _) = Build();
        var key = NewKey(SshKeyStatus.Pending);
        keys.Keys.Add(key);

        await service.DeletePendingAsync(key.Id);

        Assert.Empty(keys.Keys);
    }

    [Fact]
    public async Task DeletePendingAsync_throws_when_the_key_is_Enabled()
    {
        var (service, keys, _, _) = Build();
        var key = NewKey(SshKeyStatus.Enabled);
        keys.Keys.Add(key);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeletePendingAsync(key.Id));
        Assert.Single(keys.Keys);
    }

    [Fact]
    public async Task SyncAsync_excludes_non_Enabled_keys_and_targets_the_managed_by_orchestrator_tag_group()
    {
        var (service, keys, _, runner) = Build();
        keys.Keys.Add(NewKey(SshKeyStatus.Pending, keyData: "AAAApending"));
        keys.Keys.Add(NewKey(SshKeyStatus.Enabled, keyData: "AAAAenabled"));
        keys.Keys.Add(NewKey(SshKeyStatus.Revoked, keyData: "AAAArevoked"));

        await service.SyncAsync("alice");

        Assert.NotNull(runner.LastRequest);
        Assert.Equal("sync-ssh-keys", runner.LastRequest!.PlaybookName);
        Assert.Equal(AnsibleRunTargetKind.TagGroup, runner.LastRequest.TargetKind);
        Assert.Equal(ProxmoxTags.ManagedByOrchestrator, runner.LastRequest.TargetValue);

        var lines = GetAuthorizedKeysLines(runner.LastRequest);
        Assert.Contains(lines, l => l.Contains("AAAAenabled"));
        Assert.DoesNotContain(lines, l => l.Contains("AAAApending"));
        Assert.DoesNotContain(lines, l => l.Contains("AAAArevoked"));
    }

    [Fact]
    public async Task SyncAsync_targets_a_single_vm_by_hostname_when_given_one_instead_of_the_tag_group()
    {
        var (service, keys, _, runner) = Build();
        keys.Keys.Add(NewKey(SshKeyStatus.Enabled, keyData: "AAAAenabled"));

        await service.SyncAsync("provisioning", targetHostname: "new-container");

        Assert.NotNull(runner.LastRequest);
        Assert.Equal(AnsibleRunTargetKind.Vm, runner.LastRequest!.TargetKind);
        Assert.Equal("new-container", runner.LastRequest.TargetValue);

        var lines = GetAuthorizedKeysLines(runner.LastRequest);
        Assert.Contains(lines, l => l.Contains("AAAAenabled"));
    }

    [Fact]
    public async Task SyncAsync_always_includes_the_orchestrators_own_key_even_with_zero_enabled_keys()
    {
        var (service, _, _, runner) = Build();

        await service.SyncAsync(null);

        var lines = GetAuthorizedKeysLines(runner.LastRequest!);
        Assert.Single(lines);
        Assert.Contains("homelab-orchestrator-managed-self", lines[0]);
    }

    [Fact]
    public async Task SyncAsync_throws_and_never_submits_when_the_orchestrators_own_key_cannot_be_read()
    {
        var (service, _, _, runner) = Build(orchestratorKeyReadable: false);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SyncAsync(null));

        Assert.Null(runner.LastRequest);
    }

    [Fact]
    public async Task SyncAsync_passes_the_configured_exclusive_setting_through()
    {
        var (service, _, _, runner) = Build(exclusive: true);

        await service.SyncAsync(null);

        Assert.True((bool)runner.LastRequest!.ExtraVars!["sync_ssh_keys_exclusive"]!);
    }

    private static IReadOnlyList<string> GetAuthorizedKeysLines(AnsibleRunRequest request) =>
        (IReadOnlyList<string>)request.ExtraVars!["sync_ssh_keys_authorized_keys"]!;

    private static SshPublicKey NewKey(SshKeyStatus status, string keyData = "AAAAtest") => new()
    {
        Id = Guid.NewGuid(),
        DeviceName = "device",
        Algorithm = "ssh-ed25519",
        KeyDataBase64 = keyData,
        Fingerprint = $"SHA256:{Guid.NewGuid():N}",
        Status = status,
        CreatedAtUtc = DateTime.UtcNow,
    };

    /// <summary>Builds a syntactically valid "&lt;type&gt; &lt;base64&gt;" line whose decoded blob's embedded type matches <paramref name="type"/>, satisfying OpenSshPublicKey.TryParse's structural check.</summary>
    private static string MakeKeyLine(string type, byte[]? payload = null)
    {
        var typeBytes = Encoding.ASCII.GetBytes(type);
        var blob = LengthPrefixed(typeBytes).Concat(payload ?? [1, 2, 3, 4]).ToArray();
        return $"{type} {Convert.ToBase64String(blob)}";
    }

    /// <summary>Builds a syntactically valid ssh-rsa line whose wire-format modulus is exactly <paramref name="modulusBits"/> bits, matching TryGetRsaModulusBitLength's own parsing.</summary>
    private static string MakeRsaKeyLine(int modulusBits)
    {
        var typeBytes = Encoding.ASCII.GetBytes("ssh-rsa");
        var exponent = new byte[] { 0x01, 0x00, 0x01 };
        var modulus = new byte[modulusBits / 8];
        modulus[0] = 0x80; // top bit set: bit length == 8 * byte count exactly, no leading-zero adjustment
        var blob = LengthPrefixed(typeBytes).Concat(LengthPrefixed(exponent)).Concat(LengthPrefixed(modulus)).ToArray();
        return $"ssh-rsa {Convert.ToBase64String(blob)}";
    }

    private static byte[] LengthPrefixed(byte[] data)
    {
        var length = data.Length;
        byte[] prefix = [(byte)(length >> 24), (byte)(length >> 16), (byte)(length >> 8), (byte)length];
        return [.. prefix, .. data];
    }

    private const string DefaultOrchestratorType = "ssh-ed25519";

    private static (SshKeyManagementService Service, FakeSshKeyStore Keys, FakeSshEnrollmentTokenStore Tokens, FakeAnsibleRunnerService Runner) Build(
        bool exclusive = false, bool orchestratorKeyReadable = true)
    {
        var keys = new FakeSshKeyStore();
        var tokens = new FakeSshEnrollmentTokenStore();
        var provider = new FakeSshPublicKeyProvider(orchestratorKeyReadable ? MakeKeyLine(DefaultOrchestratorType, payload: [7, 7, 7]) : null);
        var runner = new FakeAnsibleRunnerService();
        var options = Microsoft.Extensions.Options.Options.Create(new SshSyncOptions { Exclusive = exclusive });
        var service = new SshKeyManagementService(keys, tokens, provider, runner, options);
        return (service, keys, tokens, runner);
    }

    private sealed class FakeSshKeyStore : ISshKeyStore
    {
        public List<SshPublicKey> Keys { get; } = [];

        public Task<SshPublicKey?> FindByFingerprintAsync(string fingerprint, CancellationToken cancellationToken = default) =>
            Task.FromResult(Keys.FirstOrDefault(k => k.Fingerprint == fingerprint));

        public Task AddAsync(SshPublicKey key, CancellationToken cancellationToken = default)
        {
            if (Keys.Any(k => k.Fingerprint == key.Fingerprint))
            {
                throw new DbUpdateException("Simulated unique-index violation.");
            }

            Keys.Add(key);
            return Task.CompletedTask;
        }

        public Task<SshPublicKey?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Keys.FirstOrDefault(k => k.Id == id));

        public Task<IReadOnlyList<SshPublicKey>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SshPublicKey>>([.. Keys]);

        public Task<IReadOnlyList<SshPublicKey>> ListEnabledAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SshPublicKey>>([.. Keys.Where(k => k.Status == SshKeyStatus.Enabled)]);

        public Task UpdateAsync(Guid id, Action<SshPublicKey> mutate, CancellationToken cancellationToken = default)
        {
            var key = Keys.FirstOrDefault(k => k.Id == id) ?? throw new InvalidOperationException($"SSH key {id} does not exist.");
            mutate(key);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            Keys.RemoveAll(k => k.Id == id);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSshEnrollmentTokenStore : ISshEnrollmentTokenStore
    {
        public List<SshEnrollmentToken> Tokens { get; } = [];

        public Task AddAsync(SshEnrollmentToken token, CancellationToken cancellationToken = default)
        {
            Tokens.Add(token);
            return Task.CompletedTask;
        }

        public Task<bool> IsValidAsync(string tokenHash, DateTime nowUtc, CancellationToken cancellationToken = default) =>
            Task.FromResult(Tokens.Any(t => t.TokenHash == tokenHash && t.UsedAtUtc is null && t.ExpiresAtUtc > nowUtc));

        public Task<bool> TryConsumeAsync(string tokenHash, DateTime nowUtc, CancellationToken cancellationToken = default)
        {
            var token = Tokens.FirstOrDefault(t => t.TokenHash == tokenHash && t.UsedAtUtc is null && t.ExpiresAtUtc > nowUtc);
            if (token is null)
            {
                return Task.FromResult(false);
            }

            token.UsedAtUtc = nowUtc;
            return Task.FromResult(true);
        }
    }

    private sealed class FakeSshPublicKeyProvider(string? orchestratorKeyLine) : ISshPublicKeyProvider
    {
        public Task<string> GetCombinedPublicKeysAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<string?> GetOrchestratorPublicKeyAsync(CancellationToken cancellationToken = default) => Task.FromResult(orchestratorKeyLine);
    }

    private sealed class FakeAnsibleRunnerService : IAnsibleRunnerService
    {
        public AnsibleRunRequest? LastRequest { get; private set; }

        public Task<Guid> SubmitAsync(AnsibleRunRequest request, string? submittedBy = null, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(Guid.NewGuid());
        }

        public Task<IReadOnlyList<PlaybookSummary>> ListPlaybooksAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<PlaybookDetail>> ListPlaybookDetailsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<RunTargetOptions> GetRunTargetOptionsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<AnsibleRunJob?> GetJobOrHistoryAsync(Guid jobId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<AnsibleExecutionRecord>> ListRecentExecutionsAsync(int limit = 100, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<string> GetReconstructedOutputAsync(Guid executionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<AnsibleExecutionLog>> GetLogTailAsync(Guid executionId, long afterSequence, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
