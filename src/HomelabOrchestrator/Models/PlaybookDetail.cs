namespace HomelabOrchestrator.Models;

/// <summary>A playbook's parsed name/description/steps, shown in the Runner's playbook-picker modal.</summary>
public record PlaybookDetail(string Name, string Description, IReadOnlyList<string> Steps);
