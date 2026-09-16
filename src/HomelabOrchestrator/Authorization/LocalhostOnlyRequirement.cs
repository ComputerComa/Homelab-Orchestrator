using Microsoft.AspNetCore.Authorization;

namespace HomelabOrchestrator.Authorization;

/// <summary>
/// Satisfied only when the request's TCP connection originates from loopback. Used to scope the
/// Ansible inventory endpoints to the same machine — the only thing that ever needs to call them
/// is ansible-playbook running locally via the homelab_orchestrator inventory plugin.
/// </summary>
public class LocalhostOnlyRequirement : IAuthorizationRequirement;
