namespace HomelabOrchestrator.Services.Ansible;

/// <summary>A sanitized, user-facing failure preparing or running a playbook.</summary>
public class AnsibleRunException(string message) : Exception(message);
