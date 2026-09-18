using HomelabOrchestrator.Services.Ssh;

namespace HomelabOrchestrator.Tests;

public class SshKeyFingerprintTests
{
    private const string Ed25519A = "AAAAC3NzaC1lZDI1NTE5d29ya3N0YXRpb24tb25l";
    private const string Ed25519B = "AAAAC3NzaC1lZDI1NTE5d29ya3N0YXRpb24tdHdv";

    [Fact]
    public void Compute_returns_the_SHA256_prefixed_no_padding_format()
    {
        var record = new SshPublicKeyRecord("ssh-ed25519", Ed25519A);

        var fingerprint = SshKeyFingerprint.Compute(record);

        Assert.StartsWith("SHA256:", fingerprint);
        Assert.DoesNotContain("=", fingerprint);
    }

    [Fact]
    public void Compute_is_stable_for_the_same_key_data()
    {
        var record = new SshPublicKeyRecord("ssh-ed25519", Ed25519A);

        Assert.Equal(SshKeyFingerprint.Compute(record), SshKeyFingerprint.Compute(record));
    }

    [Fact]
    public void Compute_differs_for_different_key_data()
    {
        var a = new SshPublicKeyRecord("ssh-ed25519", Ed25519A);
        var b = new SshPublicKeyRecord("ssh-ed25519", Ed25519B);

        Assert.NotEqual(SshKeyFingerprint.Compute(a), SshKeyFingerprint.Compute(b));
    }

    [Fact]
    public void Compute_ignores_the_declared_type_and_only_hashes_the_key_blob()
    {
        // Mirrors ssh-keygen's own behavior: the fingerprint is a hash of the decoded key blob,
        // not of the "<type> <data>" line — two records that share EncodedData fingerprint the same
        // regardless of what Type says, matching what `ssh-keygen -lf` would compute from the blob.
        var a = new SshPublicKeyRecord("ssh-ed25519", Ed25519A);
        var b = new SshPublicKeyRecord("ssh-rsa", Ed25519A);

        Assert.Equal(SshKeyFingerprint.Compute(a), SshKeyFingerprint.Compute(b));
    }
}
