namespace HomelabOrchestrator.Models;

public record AdoptionResult(IReadOnlyList<int> Adopted, IReadOnlyList<int> Skipped);
