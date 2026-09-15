using HomelabOrchestrator.Services.Provisioning;

namespace HomelabOrchestrator.Tests;

public class IpAddressCalculatorTests
{
    [Theory]
    [InlineData(2, "10.0.150.2")]
    [InlineData(141, "10.0.150.141")]
    [InlineData(254, "10.0.150.254")]
    public void TryCalculate_maps_vmid_to_last_octet(int vmid, string expected)
    {
        var ok = IpAddressCalculator.TryCalculate(vmid, "10.0.150", hostMin: 2, hostMax: 254, out var ip);

        Assert.True(ok);
        Assert.Equal(expected, ip);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(255)]
    [InlineData(1000)]
    public void TryCalculate_rejects_vmids_outside_the_configured_range(int vmid)
    {
        var ok = IpAddressCalculator.TryCalculate(vmid, "10.0.150", hostMin: 2, hostMax: 254, out var ip);

        Assert.False(ok);
        Assert.Equal("", ip);
    }
}
