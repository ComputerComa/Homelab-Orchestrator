using System.Security.Cryptography;
using System.Text;

namespace HomelabOrchestrator.Services.Ssh;

/// <summary>
/// Shared by <see cref="SshKeyManagementService"/> (issuing/consuming tokens) and
/// <c>EnrollmentTokenAuthenticationHandler</c> (authenticating a presented bearer token) so both
/// hash the same way. The plaintext token is never persisted anywhere — only this hash is.
/// </summary>
public static class SshEnrollmentTokenHasher
{
    public static string Hash(string plaintext) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(plaintext)));
}
