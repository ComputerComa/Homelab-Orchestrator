namespace HomelabOrchestrator.Options;

/// <summary>Bound from the "Provisioning" configuration section.</summary>
public class ProvisioningOptions
{
    public const string SectionName = "Provisioning";

    /// <summary>How long to wait for a newly created container to accept an SSH TCP connection before failing the job.</summary>
    public int SshReadyTimeoutSeconds { get; set; } = 300;

    /// <summary>How long to wait between SSH reachability attempts.</summary>
    public int SshPollIntervalSeconds { get; set; } = 3;
}
