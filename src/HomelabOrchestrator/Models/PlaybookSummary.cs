namespace HomelabOrchestrator.Models;

/// <summary>One playbook discovered under the configured ansible/playbooks directory.</summary>
public record PlaybookSummary(string Name, string FileName);
