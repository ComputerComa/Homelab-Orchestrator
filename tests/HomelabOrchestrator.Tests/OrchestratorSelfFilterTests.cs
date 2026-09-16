using HomelabOrchestrator.Models;
using HomelabOrchestrator.Services.Ansible;

namespace HomelabOrchestrator.Tests;

public class OrchestratorSelfFilterTests
{
    [Fact]
    public void IsSelf_matches_a_container_whose_hostname_equals_this_machines_hostname()
    {
        var container = new ContainerSummary(141, Environment.MachineName, "running", []);

        Assert.True(OrchestratorSelfFilter.IsSelf(container));
    }

    [Fact]
    public void IsSelf_matches_regardless_of_case()
    {
        var container = new ContainerSummary(141, Environment.MachineName.ToUpperInvariant(), "running", []);

        Assert.True(OrchestratorSelfFilter.IsSelf(container));
    }

    [Fact]
    public void IsSelf_does_not_match_a_different_hostname()
    {
        var container = new ContainerSummary(141, $"definitely-not-{Environment.MachineName}", "running", []);

        Assert.False(OrchestratorSelfFilter.IsSelf(container));
    }
}
