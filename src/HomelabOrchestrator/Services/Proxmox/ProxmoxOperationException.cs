namespace HomelabOrchestrator.Services.Proxmox;

/// <summary>A sanitized, user-facing failure talking to Proxmox (bad config, API error, task timeout, ...). Never carries the API token.</summary>
public class ProxmoxOperationException(string message) : Exception(message);
