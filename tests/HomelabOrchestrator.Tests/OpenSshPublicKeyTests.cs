using HomelabOrchestrator.Services.Ssh;

namespace HomelabOrchestrator.Tests;

public class OpenSshPublicKeyTests
{
    // Each constant decodes to a 4-byte big-endian length prefix followed by the declared
    // type string and some payload bytes — the same shape a real OpenSSH key blob has.
    private const string Ed25519A = "AAAAC3NzaC1lZDI1NTE5d29ya3N0YXRpb24tb25l";
    private const string Rsa = "AAAAB3NzaC1yc2FsZWdhY3ktcnNhLWtleQ==";

    [Theory]
    [InlineData($"ssh-ed25519 {Ed25519A}")]
    [InlineData($"ssh-ed25519 {Ed25519A} someone@laptop")]
    [InlineData($"ssh-rsa {Rsa} comment with spaces in it")]
    [InlineData($"  ssh-ed25519 {Ed25519A}  ")]
    public void TryParse_accepts_normal_records(string line)
    {
        Assert.True(OpenSshPublicKey.TryParse(line.Trim(), out _));
    }

    [Fact]
    public void TryParse_returns_the_type_and_encoded_data()
    {
        Assert.True(OpenSshPublicKey.TryParse($"ssh-ed25519 {Ed25519A} me@host", out var record));

        Assert.Equal("ssh-ed25519", record.Type);
        Assert.Equal(Ed25519A, record.EncodedData);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-key-at-all")]
    [InlineData("ssh-ed25519")]
    [InlineData("ssh-ed25519 not-valid-base64!!!")]
    public void TryParse_rejects_malformed_lines(string line)
    {
        Assert.False(OpenSshPublicKey.TryParse(line, out _));
    }

    [Fact]
    public void TryParse_rejects_a_declared_type_that_does_not_match_the_encoded_blob()
    {
        // Declares ed25519 but the blob's embedded type is actually ssh-rsa.
        Assert.False(OpenSshPublicKey.TryParse($"ssh-ed25519 {Rsa}", out _));
    }

    [Fact]
    public void TryParse_rejects_base64_that_is_too_short_to_contain_a_type()
    {
        Assert.False(OpenSshPublicKey.TryParse("ssh-ed25519 QQ==", out _));
    }
}
