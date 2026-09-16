using System.Text.Json.Serialization;

namespace HomelabOrchestrator.Services.Ansible;

/// <summary>
/// The standard Ansible dynamic-inventory JSON shape (the same contract `ansible-inventory --list`
/// and inventory scripts/plugins produce), so this can be saved to a file and used directly with
/// `-i inventory.json`, or fetched straight into a plugin. See
/// https://docs.ansible.com/ansible/latest/dev_guide/developing_inventory.html.
/// </summary>
public record AnsibleInventory(
    [property: JsonPropertyName("_meta")] AnsibleInventoryMeta Meta,
    [property: JsonPropertyName("all")] AnsibleInventoryGroup All);

public record AnsibleInventoryMeta(
    [property: JsonPropertyName("hostvars")] IReadOnlyDictionary<string, AnsibleHostVars> Hostvars);

public record AnsibleInventoryGroup(
    [property: JsonPropertyName("hosts")] IReadOnlyList<string> Hosts,
    [property: JsonPropertyName("vars")] IReadOnlyDictionary<string, object> Vars);

/// <summary>
/// Per-host connection variables. <see cref="AnsibleSshPrivateKeyFile"/> is a path the future
/// Ansible runner reads locally on the orchestrator — it is never key content.
/// </summary>
public record AnsibleHostVars(
    [property: JsonPropertyName("ansible_host")] string AnsibleHost,
    [property: JsonPropertyName("ansible_user")] string AnsibleUser,
    [property: JsonPropertyName("ansible_port")] int AnsiblePort,
    [property: JsonPropertyName("ansible_ssh_private_key_file")] string AnsibleSshPrivateKeyFile);
