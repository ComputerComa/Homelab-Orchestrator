using HomelabOrchestrator.Models;

namespace HomelabOrchestrator.Services.Ansible;

/// <summary>Discovers playbooks under the configured ansible/playbooks directory — the only playbooks the runner will ever execute.</summary>
public interface IPlaybookCatalog
{
    Task<IReadOnlyList<PlaybookSummary>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Resolves a playbook name to its canonical path, or null if it isn't a currently known playbook.</summary>
    Task<string?> ResolvePathAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Parses a playbook's own YAML (and any role it references) into its description/steps, or null if it isn't a currently known playbook.</summary>
    Task<PlaybookDetail?> GetDetailAsync(string name, CancellationToken cancellationToken = default);
}
