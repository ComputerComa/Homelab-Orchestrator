namespace HomelabOrchestrator.Services.Jobs;

/// <summary>
/// A sanitized, user-facing failure in the post-creation configuration steps (waiting for SSH,
/// applying the base role, synchronizing SSH keys) that run after a container is already up. The
/// underlying LXC is never deleted when this is thrown — the message should point the operator at
/// the ordinary Runner/SSH Keys page action that retries just the failed step.
/// </summary>
public class ProvisioningConfigurationException(string message) : Exception(message);
