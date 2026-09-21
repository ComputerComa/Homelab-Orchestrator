using System.Security.Cryptography;

namespace HomelabOrchestrator.Services.Ssh;

/// <summary>
/// Computes an OpenSSH-style "SHA256:&lt;base64-no-padding&gt;" fingerprint — the exact format
/// `ssh-keygen -lf` prints, so an operator can compare the value returned by the enrollment
/// endpoint against their own terminal output with zero mental translation. Pure and stateless.
/// </summary>
public static class SshKeyFingerprint
{
    public static string Compute(SshPublicKeyRecord record)
    {
        var blob = Convert.FromBase64String(record.EncodedData);
        var hash = SHA256.HashData(blob);
        return "SHA256:" + Convert.ToBase64String(hash).TrimEnd('=');
    }
}
