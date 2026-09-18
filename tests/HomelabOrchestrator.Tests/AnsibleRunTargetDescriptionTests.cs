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

    [Fact]
    public void Describe_selection_target_with_no_hosts()
    {
        Assert.Equal("no containers selected", AnsibleRunTargetDescription.Describe(AnsibleRunTargetKind.Selection, null));
    }

    [Fact]
    public void Describe_selection_target_with_one_host_matches_a_single_vm_description()
    {
        Assert.Equal("container \"web-01\"", AnsibleRunTargetDescription.Describe(AnsibleRunTargetKind.Selection, "web-01"));
    }

    [Fact]
    public void Describe_selection_target_with_several_hosts_lists_them()
    {
        Assert.Equal(
            "3 selected containers (web-01, cache-01, nginx-proxy-manager)",
            AnsibleRunTargetDescription.Describe(AnsibleRunTargetKind.Selection, "web-01,cache-01,nginx-proxy-manager"));
    }
}
