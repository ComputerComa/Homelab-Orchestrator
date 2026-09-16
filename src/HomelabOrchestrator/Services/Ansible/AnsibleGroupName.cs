using System.Text.RegularExpressions;

namespace HomelabOrchestrator.Services.Ansible;

/// <summary>
/// Mirrors Ansible's own keyed_groups sanitization (replace any character outside
/// [A-Za-z0-9_] with '_'), so a tag picked in the UI maps to exactly the same group name
/// ansible/inventory/orchestrator.yml's keyed_groups produces at inventory time — otherwise
/// `--limit tag_x` would silently match zero hosts. Verified against a live run: Proxmox tag
/// "managed-by-orchestrator" produces Ansible group "tag_managed_by_orchestrator".
/// </summary>
public static partial class AnsibleGroupName
{
    [GeneratedRegex("[^A-Za-z0-9_]")]
    private static partial Regex InvalidChar();

    public static string ForTag(string tag) => "tag_" + InvalidChar().Replace(tag, "_");
}
