using System.Reflection;
using HomelabOrchestrator.Models;
using HomelabOrchestrator.Services.Jobs;

namespace HomelabOrchestrator.Tests;

/// <summary>
/// Guards against SSH material creeping back into browser-controlled or job-record models.
/// Keys must only ever be produced server-side, immediately before creation, by
/// <see cref="HomelabOrchestrator.Services.Ssh.ISshPublicKeyProvider"/>.
/// </summary>
public class SshMaterialRemovalTests
{
    [Fact]
    public void ContainerFormModel_has_no_ssh_related_members()
    {
        AssertNoSshMembers(typeof(ContainerFormModel));
    }

    [Fact]
    public void ProvisioningRequest_has_no_ssh_related_members()
    {
        AssertNoSshMembers(typeof(ProvisioningRequest));
    }

    [Fact]
    public void ProvisioningJob_has_no_ssh_related_members()
    {
        // Job records are polled over HTMX and must never carry key material.
        AssertNoSshMembers(typeof(ProvisioningJob));
    }

    private static void AssertNoSshMembers(Type type)
    {
        var offendingMembers = type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .Where(name => name.Contains("Ssh", StringComparison.OrdinalIgnoreCase)
                        || name.Contains("PublicKey", StringComparison.OrdinalIgnoreCase)
                        || name.Contains("PrivateKey", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.True(offendingMembers.Count == 0, $"{type.Name} exposes SSH-related member(s): {string.Join(", ", offendingMembers)}");
    }
}
