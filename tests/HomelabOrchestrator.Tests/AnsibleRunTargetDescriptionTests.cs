using HomelabOrchestrator.Services.Ansible;
using HomelabOrchestrator.Services.Jobs;

namespace HomelabOrchestrator.Tests;

public class AnsibleRunTargetDescriptionTests
{
    [Fact]
    public void Describe_all_target_ignores_any_value()
    {
        Assert.Equal("all running containers", AnsibleRunTargetDescription.Describe(AnsibleRunTargetKind.All, null));
    }

    [Fact]
    public void Describe_vm_target_quotes_the_hostname()
    {
        Assert.Equal("container \"web-01\"", AnsibleRunTargetDescription.Describe(AnsibleRunTargetKind.Vm, "web-01"));
    }

    [Fact]
    public void Describe_tag_group_target_quotes_the_tag()
    {
        Assert.Equal("containers tagged \"mqtt\"", AnsibleRunTargetDescription.Describe(AnsibleRunTargetKind.TagGroup, "mqtt"));
    }
}
