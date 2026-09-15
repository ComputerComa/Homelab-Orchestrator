namespace HomelabOrchestrator.Services;

/// <summary>A user-facing failure talking to Proxmox (bad config, API error, task timeout, ...).</summary>
public class ProxmoxOperationException(string message) : Exception(message);
