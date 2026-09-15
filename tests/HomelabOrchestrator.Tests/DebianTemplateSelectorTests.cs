using HomelabOrchestrator.Services.Provisioning;

namespace HomelabOrchestrator.Tests;

public class DebianTemplateSelectorTests
{
    [Fact]
    public void SelectLatest_picks_the_highest_sorting_debian_13_template()
    {
        var volids = new[]
        {
            "local:vztmpl/debian-13-standard_13.0-1_amd64.tar.zst",
            "local:vztmpl/debian-13-standard_13.2-1_amd64.tar.zst",
            "local:vztmpl/debian-13-standard_13.1-1_amd64.tar.zst",
        };

        Assert.Equal("local:vztmpl/debian-13-standard_13.2-1_amd64.tar.zst", DebianTemplateSelector.SelectLatest(volids));
    }

    [Fact]
    public void SelectLatest_ignores_other_distributions_and_non_template_content()
    {
        var volids = new[]
        {
            "local:vztmpl/ubuntu-24.04-standard_24.04-1_amd64.tar.zst",
            "local:iso/debian-13-netinst.iso",
            "local-lvm:141/vm-141-disk-0.raw",
        };

        Assert.Null(DebianTemplateSelector.SelectLatest(volids));
    }

    [Fact]
    public void SelectLatest_returns_null_when_nothing_matches()
    {
        Assert.Null(DebianTemplateSelector.SelectLatest([]));
    }
}
