using HomelabOrchestrator.Services.Provisioning;

namespace HomelabOrchestrator.Tests;

public class HostnamePolicyTests
{
    [Theory]
    [InlineData("My-Host", "my-host")]
    [InlineData("  spaced-out  ", "spaced-out")]
    [InlineData("ALLCAPS", "allcaps")]
    public void Normalize_trims_and_lowercases(string input, string expected)
    {
        Assert.Equal(expected, HostnamePolicy.Normalize(input));
    }

    [Theory]
    [InlineData("web-01")]
    [InlineData("a")]
    [InlineData("host9")]
    public void IsValid_accepts_lowercase_alphanumeric_with_hyphens(string hostname)
    {
        Assert.True(HostnamePolicy.IsValid(hostname));
    }

    [Theory]
    [InlineData("")]
    [InlineData("-leading-hyphen")]
    [InlineData("trailing-hyphen-")]
    [InlineData("has_underscore")]
    [InlineData("has space")]
    [InlineData("Has-Uppercase")]
    [InlineData("bad!char")]
    public void IsValid_rejects_everything_else(string hostname)
    {
        Assert.False(HostnamePolicy.IsValid(hostname));
    }
}
