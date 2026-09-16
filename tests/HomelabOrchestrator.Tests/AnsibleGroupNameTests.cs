using HomelabOrchestrator.Services.Ansible;

namespace HomelabOrchestrator.Tests;

public class AnsibleGroupNameTests
{
    [Theory]
    [InlineData("mqtt", "tag_mqtt")]
    [InlineData("base", "tag_base")]
    // Matches the real, observed behavior of Ansible's keyed_groups sanitization.
    [InlineData("managed-by-orchestrator", "tag_managed_by_orchestrator")]
    [InlineData("a.b.c", "tag_a_b_c")]
    public void ForTag_matches_ansibles_own_keyed_groups_sanitization(string tag, string expectedGroup)
    {
        Assert.Equal(expectedGroup, AnsibleGroupName.ForTag(tag));
    }
}
