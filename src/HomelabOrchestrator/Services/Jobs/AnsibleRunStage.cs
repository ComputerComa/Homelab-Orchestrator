namespace HomelabOrchestrator.Services.Jobs;

public enum AnsibleRunStage
{
    Queued,
    Running,
    Succeeded,
    Failed,
}
