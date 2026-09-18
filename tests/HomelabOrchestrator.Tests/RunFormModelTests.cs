using HomelabOrchestrator.Models;
using HomelabOrchestrator.Services.Jobs;

namespace HomelabOrchestrator.Tests;

public class RunFormModelTests
{
    [Fact]
    public void ParseTarget_defaults_to_all()
    {
        var form = new RunFormModel { Target = "all" };

        Assert.Equal((AnsibleRunTargetKind.All, (string?)null), form.ParseTarget());
    }

    [Fact]
    public void ParseTarget_reads_a_vm_target()
    {
        var form = new RunFormModel { Target = "vm:web-01" };

        Assert.Equal((AnsibleRunTargetKind.Vm, "web-01"), form.ParseTarget());
    }

    [Fact]
    public void ParseTarget_reads_a_tag_target()
    {
        var form = new RunFormModel { Target = "tag:mqtt" };

        Assert.Equal((AnsibleRunTargetKind.TagGroup, "mqtt"), form.ParseTarget());
    }

    [Fact]
    public void ParseTarget_reads_a_selection_target()
    {
        var form = new RunFormModel { Target = "selection:web-01,cache-01" };

        Assert.Equal((AnsibleRunTargetKind.Selection, "web-01,cache-01"), form.ParseTarget());
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("vm")]
    public void ParseTarget_falls_back_to_all_for_anything_unrecognized(string target)
    {
        var form = new RunFormModel { Target = target };

        Assert.Equal((AnsibleRunTargetKind.All, (string?)null), form.ParseTarget());
    }

    [Fact]
    public void ToRunRequest_carries_the_playbook_name_and_parsed_target()
    {
        var form = new RunFormModel { PlaybookName = "ssh-check", Target = "tag:mqtt" };

        var request = form.ToRunRequest();

        Assert.Equal("ssh-check", request.PlaybookName);
        Assert.Equal(AnsibleRunTargetKind.TagGroup, request.TargetKind);
        Assert.Equal("mqtt", request.TargetValue);
    }
}
