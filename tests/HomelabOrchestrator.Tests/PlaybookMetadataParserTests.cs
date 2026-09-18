using HomelabOrchestrator.Services.Ansible;

namespace HomelabOrchestrator.Tests;

/// <summary>Fixtures are the real content of ansible/playbooks/ssh-check.yml, ansible/playbooks/apply-base.yml, and ansible/roles/base/tasks/main.yml, not synthetic examples.</summary>
public class PlaybookMetadataParserTests
{
    private const string SshCheckYaml = """
        ---
        # A basic SSH connectivity check: connects to each target over SSH and reports whether it's
        # reachable. Safe to run against any container — it makes no changes.
        - name: Check SSH connectivity to the target container(s)
          hosts: all
          gather_facts: false
          tasks:
            - name: Ping over SSH
              ansible.builtin.ping:

            - name: Report connectivity result
              ansible.builtin.debug:
                msg: "{{ inventory_hostname }} ({{ ansible_host }}) is reachable via SSH"
        """;

    private const string ApplyBaseYaml = """
        ---
        # Intended to run against a single freshly provisioned container, e.g.:
        #   ansible-playbook -i ansible/inventory/orchestrator-single.yml ansible/playbooks/apply-base.yml
        - name: Apply the base role to a newly provisioned container
          hosts: all
          become: false
          gather_facts: true
          roles:
            - base
        """;

    private const string BaseRoleTasksYaml = """
        ---
        # Baseline setup applied once to every newly provisioned container: keep packages current, set
        # the timezone and enable NTP, and generate/select the configured locale. `base_timezone` and
        # `base_locale` (defaults/main.yml) can be overridden without editing this file.
        - name: Update apt cache and upgrade all packages
          ansible.builtin.apt:
            update_cache: true
            cache_valid_time: 3600
            upgrade: dist

        - name: Configure timezone
          community.general.timezone:
            name: "{{ base_timezone }}"

        # Debian 13 ships systemd-timesyncd by default — no separate NTP package to install. Under an
        # unprivileged LXC this may already be masked/managed by the Proxmox host, so treat this as
        # best-effort rather than a guarantee the clock syncs inside every container.
        - name: Enable and start NTP time sync
          ansible.builtin.systemd:
            name: systemd-timesyncd
            enabled: true
            state: started

        - name: Generate the configured locale
          community.general.locale_gen:
            name: "{{ base_locale }}"
            state: present

        - name: Set the system default locale
          ansible.builtin.lineinfile:
            path: /etc/default/locale
            regexp: '^LANG='
            line: "LANG={{ base_locale }}"
            create: true
        """;

    [Fact]
    public void Parse_ssh_check_uses_the_play_name_as_description_and_its_inline_tasks_as_steps()
    {
        var detail = PlaybookMetadataParser.Parse("ssh-check", SshCheckYaml, new Dictionary<string, string>());

        Assert.Equal("Check SSH connectivity to the target container(s)", detail.Description);
        Assert.Equal(["Ping over SSH", "Report connectivity result"], detail.Steps);
    }

    [Fact]
    public void ExtractRoleNames_finds_the_block_style_roles_entry()
    {
        var roles = PlaybookMetadataParser.ExtractRoleNames(ApplyBaseYaml);

        Assert.Equal(["base"], roles);
    }

    [Fact]
    public void ExtractRoleNames_finds_an_inline_roles_entry()
    {
        var roles = PlaybookMetadataParser.ExtractRoleNames("- name: x\n  hosts: all\n  roles: [base, other]\n");

        Assert.Equal(["base", "other"], roles);
    }

    [Fact]
    public void Parse_apply_base_has_no_inline_steps_when_the_role_cannot_be_resolved()
    {
        var detail = PlaybookMetadataParser.Parse("apply-base", ApplyBaseYaml, new Dictionary<string, string>());

        Assert.Equal("Apply the base role to a newly provisioned container", detail.Description);
        Assert.Empty(detail.Steps);
    }

    [Fact]
    public void Parse_apply_base_resolves_its_roles_tasks_as_steps()
    {
        var roleTasksYaml = new Dictionary<string, string> { ["base"] = BaseRoleTasksYaml };

        var detail = PlaybookMetadataParser.Parse("apply-base", ApplyBaseYaml, roleTasksYaml);

        Assert.Equal("Apply the base role to a newly provisioned container", detail.Description);
        Assert.Equal(
            [
                "Update apt cache and upgrade all packages",
                "Configure timezone",
                "Enable and start NTP time sync",
                "Generate the configured locale",
                "Set the system default locale",
            ],
            detail.Steps);
    }

    [Fact]
    public void Parse_falls_back_to_the_catalog_name_when_the_playbook_has_no_name_line()
    {
        var detail = PlaybookMetadataParser.Parse("mystery", "- hosts: all\n  tasks: []\n", new Dictionary<string, string>());

        Assert.Equal("mystery", detail.Description);
        Assert.Empty(detail.Steps);
    }
}
