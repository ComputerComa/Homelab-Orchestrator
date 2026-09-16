using HomelabOrchestrator.Options;
using Microsoft.Extensions.Options;

namespace HomelabOrchestrator.Services.Ssh;

/// <summary>
/// Reads <see cref="SshOptions.AuthorizedKeysPath"/> (the operator's workstations) and
/// <see cref="SshOptions.OrchestratorPublicKeyPath"/> (the orchestrator's own key), then combines
/// them into the value Proxmox's <c>ssh_public_keys</c> field expects. Blank and comment-only
/// lines are ignored, malformed records are skipped with a warning rather than failing the whole
/// read, and identical keys (same type and encoded data, regardless of comment) are deduplicated.
/// Never reads, returns, or logs <see cref="SshOptions.OrchestratorPrivateKeyPath"/>.
/// </summary>
public class SshPublicKeyProvider(IOptions<SshOptions> options, ILogger<SshPublicKeyProvider> logger) : ISshPublicKeyProvider
{
    private readonly SshOptions _options = options.Value;

    public async Task<string> GetCombinedPublicKeysAsync(CancellationToken cancellationToken = default)
    {
        var seen = new HashSet<SshPublicKeyRecord>();
        var keys = new List<string>();

        await CollectKeysAsync(_options.AuthorizedKeysPath, seen, keys, cancellationToken);
        await CollectKeysAsync(_options.OrchestratorPublicKeyPath, seen, keys, cancellationToken);

        if (keys.Count == 0)
        {
            logger.LogWarning(
                "No SSH public keys were found in {AuthorizedKeysPath} or {OrchestratorPublicKeyPath}; " +
                "new containers will have no key-based SSH access.",
                _options.AuthorizedKeysPath, _options.OrchestratorPublicKeyPath);
        }

        return string.Join('\n', keys);
    }

    private async Task CollectKeysAsync(string path, HashSet<SshPublicKeyRecord> seen, List<string> keys, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            logger.LogDebug("SSH public key source {Path} does not exist; skipping.", path);
            return;
        }

        string[] lines;
        try
        {
            lines = await File.ReadAllLinesAsync(path, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not read SSH public key source {Path}.", path);
            return;
        }

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (!OpenSshPublicKey.TryParse(line, out var record))
            {
                logger.LogWarning("Skipping line {LineNumber} of {Path}: not a valid OpenSSH public key.", i + 1, path);
                continue;
            }

            if (seen.Add(record))
            {
                keys.Add(line);
            }
        }
    }
}
