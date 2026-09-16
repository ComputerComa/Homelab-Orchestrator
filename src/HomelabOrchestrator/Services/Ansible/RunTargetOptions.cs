namespace HomelabOrchestrator.Services.Ansible;

/// <summary>The choices shown in the runner's target picker — sourced live, never cached.</summary>
public record RunTargetOptions(IReadOnlyList<string> RunningHostnames, IReadOnlyList<string> Tags);
