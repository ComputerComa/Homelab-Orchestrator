using HomelabOrchestrator.Options;
using HomelabOrchestrator.Services.Ssh;
using Microsoft.Extensions.Logging.Abstractions;

namespace HomelabOrchestrator.Tests;

/// <summary>Uses real temporary files so the provider is exercised the same way it runs in production.</summary>
public class SshPublicKeyProviderTests : IDisposable
{
    // Each decodes to a 4-byte length prefix + declared type + distinct payload, matching real OpenSSH key shape.
    private const string Ed25519A = "AAAAC3NzaC1lZDI1NTE5d29ya3N0YXRpb24tb25l"; // "workstation-one"
    private const string Ed25519B = "AAAAC3NzaC1lZDI1NTE5d29ya3N0YXRpb24tdHdv"; // "workstation-two"
    private const string Ed25519Orch = "AAAAC3NzaC1lZDI1NTE5b3JjaGVzdHJhdG9yLWtleQ=="; // "orchestrator-key"

    private readonly string _tempDir = Directory.CreateTempSubdirectory("ssh-provider-tests-").FullName;

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private string PathFor(string name) => Path.Combine(_tempDir, name);

    private SshPublicKeyProvider BuildProvider(string? authorizedKeysPath, string? orchestratorPublicKeyPath)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new SshOptions
        {
            AuthorizedKeysPath = authorizedKeysPath ?? PathFor("does-not-exist-authorized-keys"),
            OrchestratorPublicKeyPath = orchestratorPublicKeyPath ?? PathFor("does-not-exist-orchestrator.pub"),
        });

        return new SshPublicKeyProvider(options, NullLogger<SshPublicKeyProvider>.Instance);
    }

    [Fact]
    public async Task Combines_keys_from_both_files()
    {
        var authorizedKeys = PathFor("authorized_keys");
        var orchestratorKey = PathFor("id_ed25519.pub");
        await File.WriteAllTextAsync(authorizedKeys, $"ssh-ed25519 {Ed25519A} laptop\n");
        await File.WriteAllTextAsync(orchestratorKey, $"ssh-ed25519 {Ed25519Orch} homelab-orchestrator\n");

        var provider = BuildProvider(authorizedKeys, orchestratorKey);
        var result = await provider.GetCombinedPublicKeysAsync();

        Assert.Equal(
            $"ssh-ed25519 {Ed25519A} laptop\nssh-ed25519 {Ed25519Orch} homelab-orchestrator",
            result);
    }

    [Fact]
    public async Task Ignores_blank_lines_and_comments()
    {
        var authorizedKeys = PathFor("authorized_keys");
        await File.WriteAllTextAsync(authorizedKeys, $"""

            # a workstation key
            ssh-ed25519 {Ed25519A} laptop

            # another comment
            """);

        var provider = BuildProvider(authorizedKeys, null);
        var result = await provider.GetCombinedPublicKeysAsync();

        Assert.Equal($"ssh-ed25519 {Ed25519A} laptop", result);
    }

    [Fact]
    public async Task Deduplicates_identical_keys_with_different_comments_keeping_the_first()
    {
        var authorizedKeys = PathFor("authorized_keys");
        var orchestratorKey = PathFor("id_ed25519.pub");
        await File.WriteAllTextAsync(authorizedKeys, $"ssh-ed25519 {Ed25519A} first-comment\n");
        // Same type + encoded data as above, only the comment differs; also appears twice in its own file.
        await File.WriteAllTextAsync(orchestratorKey, $"ssh-ed25519 {Ed25519A} second-comment\nssh-ed25519 {Ed25519A} third-comment\n");

        var provider = BuildProvider(authorizedKeys, orchestratorKey);
        var result = await provider.GetCombinedPublicKeysAsync();

        Assert.Equal($"ssh-ed25519 {Ed25519A} first-comment", result);
    }

    [Fact]
    public async Task Output_is_deterministic_across_calls()
    {
        var authorizedKeys = PathFor("authorized_keys");
        var orchestratorKey = PathFor("id_ed25519.pub");
        await File.WriteAllTextAsync(authorizedKeys, $"ssh-ed25519 {Ed25519B} laptop\nssh-ed25519 {Ed25519A} desktop\n");
        await File.WriteAllTextAsync(orchestratorKey, $"ssh-ed25519 {Ed25519Orch} homelab-orchestrator\n");

        var provider = BuildProvider(authorizedKeys, orchestratorKey);

        var first = await provider.GetCombinedPublicKeysAsync();
        var second = await provider.GetCombinedPublicKeysAsync();

        Assert.Equal(first, second);
        Assert.Equal(
            $"ssh-ed25519 {Ed25519B} laptop\nssh-ed25519 {Ed25519A} desktop\nssh-ed25519 {Ed25519Orch} homelab-orchestrator",
            first);
    }

    [Fact]
    public async Task Missing_files_produce_no_keys_without_throwing()
    {
        var provider = BuildProvider(null, null);

        var result = await provider.GetCombinedPublicKeysAsync();

        Assert.Equal("", result);
    }

    [Fact]
    public async Task Empty_files_produce_no_keys()
    {
        var authorizedKeys = PathFor("authorized_keys");
        await File.WriteAllTextAsync(authorizedKeys, "");

        var provider = BuildProvider(authorizedKeys, null);
        var result = await provider.GetCombinedPublicKeysAsync();

        Assert.Equal("", result);
    }

    [Fact]
    public async Task Invalid_file_contents_are_skipped_without_throwing()
    {
        var authorizedKeys = PathFor("authorized_keys");
        await File.WriteAllTextAsync(authorizedKeys, "this is not an ssh key\nneither is this ===\n");

        var provider = BuildProvider(authorizedKeys, null);
        var result = await provider.GetCombinedPublicKeysAsync();

        Assert.Equal("", result);
    }

    [Fact]
    public async Task Never_reads_the_configured_private_key_file()
    {
        var authorizedKeys = PathFor("authorized_keys");
        var privateKeyPath = PathFor("id_ed25519");
        await File.WriteAllTextAsync(authorizedKeys, $"ssh-ed25519 {Ed25519A} laptop\n");
        await File.WriteAllTextAsync(privateKeyPath, "-----BEGIN OPENSSH PRIVATE KEY-----\nsecret-material\n-----END OPENSSH PRIVATE KEY-----\n");

        var options = Microsoft.Extensions.Options.Options.Create(new SshOptions
        {
            AuthorizedKeysPath = authorizedKeys,
            OrchestratorPublicKeyPath = PathFor("does-not-exist.pub"),
            OrchestratorPrivateKeyPath = privateKeyPath,
        });
        var provider = new SshPublicKeyProvider(options, NullLogger<SshPublicKeyProvider>.Instance);

        var result = await provider.GetCombinedPublicKeysAsync();

        Assert.Equal($"ssh-ed25519 {Ed25519A} laptop", result);
        Assert.DoesNotContain("PRIVATE KEY", result);
        Assert.DoesNotContain("secret-material", result);
    }
}
