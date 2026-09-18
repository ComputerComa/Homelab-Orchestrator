using HomelabOrchestrator.Models;

namespace HomelabOrchestrator.Services.Ansible;

/// <summary>
/// The choices shown in the runner's target picker — sourced live, never cached.
/// <see cref="RunningContainers"/> carries each container's own tags so the page can render one
/// checkbox row per container and filter which rows show by tag client-side; <see cref="Tags"/> is
/// the distinct, sorted set of tags across them, for the filter dropdown's options.
/// </summary>
public record RunTargetOptions(IReadOnlyList<ContainerSummary> RunningContainers, IReadOnlyList<string> Tags);
