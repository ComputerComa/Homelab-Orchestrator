using HomelabOrchestrator.Models;
using HomelabOrchestrator.Services.Ansible;

namespace HomelabOrchestrator.Tests;

/// <summary>
/// Fixtures are trimmed excerpts of a real ansible-playbook transcript from a live homelab run
/// against ~30 hosts (kept to a handful here), not synthetic examples — including the exact
/// "[WARNING]: Found variable using reserved name 'tags'" noise, the traceback-context lines
/// ansible-core prints around a real [ERROR], and PLAY RECAP's irregular trailing-space padding.
/// </summary>
public class AnsibleOutputParserTests
{
    // Mid-run: task 2 in progress, nginx-proxy-manager's task-2 result deliberately omitted to
    // simulate what a 1s poll would see while ansible-playbook is still running. No PLAY RECAP yet.
    private const string MidRunTranscript = """
        [WARNING]: Found variable using reserved name 'tags'.
        Origin: <unknown>

        tags


        PLAY [Check SSH connectivity to the target container(s)] ***********************

        TASK [Ping over SSH] ***********************************************************
        [ERROR]: Task failed: Failed to connect to the host via ssh: root@10.0.150.102: Permission denied (publickey,password).
        Origin: /root/Homelab-Orchestrator/ansible/playbooks/ssh-check.yml:8:7

        6   gather_facts: false
        7   tasks:
        8     - name: Ping over SSH
                ^ column 7

        fatal: [homelab-orchestrator]: UNREACHABLE! => {"changed": false, "msg": "Task failed: Failed to connect to the host via ssh: root@10.0.150.102: Permission denied (publickey,password).", "unreachable": true}
        [WARNING]: Host 'mqtt-broket' is using the discovered Python interpreter at '/usr/bin/python3.10', but future installation of another Python interpreter could cause a different interpreter to be discovered. See https://docs.ansible.com/ansible-core/2.19/reference_appendices/interpreter_discovery.html for more information.
        ok: [mqtt-broket]
        [WARNING]: Host 'nginx-proxy-manager' is using the discovered Python interpreter at '/usr/bin/python3.10', but future installation of another Python interpreter could cause a different interpreter to be discovered. See https://docs.ansible.com/ansible-core/2.19/reference_appendices/interpreter_discovery.html for more information.
        ok: [nginx-proxy-manager]
        ok: [adguard-primary]
        ok: [website-v2]

        TASK [Report connectivity result] **********************************************
        ok: [mqtt-broket] => {
            "msg": "mqtt-broket (10.0.150.0) is reachable via SSH"
        }
        ok: [website-v2] => {
            "msg": "website-v2 (10.0.150.3) is reachable via SSH"
        }
        ok: [adguard-primary] => {
            "msg": "adguard-primary (10.0.200.1) is reachable via SSH"
        }
        """;

    // Same run, finished: nginx-proxy-manager's task-2 result has now arrived, plus PLAY RECAP.
    private const string CompletedRunTranscript = MidRunTranscript + """

        ok: [nginx-proxy-manager] => {
            "msg": "nginx-proxy-manager (10.0.150.6) is reachable via SSH"
        }

        PLAY RECAP *********************************************************************
        adguard-primary            : ok=2    changed=0    unreachable=0    failed=0    skipped=0    rescued=0    ignored=0
        homelab-orchestrator       : ok=0    changed=0    unreachable=1    failed=0    skipped=0    rescued=0    ignored=0
        mqtt-broket                : ok=2    changed=0    unreachable=0    failed=0    skipped=0    rescued=0    ignored=0
        nginx-proxy-manager        : ok=2    changed=0    unreachable=0    failed=0    skipped=0    rescued=0    ignored=0
        website-v2                 : ok=2    changed=0    unreachable=0    failed=0    skipped=0    rescued=0    ignored=0
        """;

    // Only the first task has produced any results yet — no second TASK header exists at all.
    private const string FirstTaskOnlyTranscript = """
        PLAY [Check SSH connectivity to the target container(s)] ***********************

        TASK [Ping over SSH] ***********************************************************
        ok: [mqtt-broket]
        """;

    // Synthetic (no real captured transcript in this file has a loop yet) — a `loop:` task
    // produces one result line per item instead of one per host, each carrying an
    // "=> (item=...)" marker. Two hosts, one plain loop (bare ok/changed) and one debug-style
    // loop (item marker followed by a JSON block) so both RecordStep paths are covered.
    private const string LoopedTaskTranscript = """
        PLAY [Apply base role] **********************************************************

        TASK [Ensure packages are present] **********************************************
        ok: [web-01] => (item=curl)
        changed: [web-01] => (item=git)
        changed: [db-01] => (item=curl)

        TASK [Report installed package] *************************************************
        ok: [web-01] => (item=curl) => {
            "msg": "curl is present"
        }
        ok: [web-01] => (item=git) => {
            "msg": "git is present"
        }
        """;

    [Fact]
    public void Parse_recognizes_hosts_and_task_order_from_bare_ok_lines()
    {
        var result = AnsibleOutputParser.Parse(MidRunTranscript);

        Assert.Contains(result.Hosts, h => h.Hostname == "mqtt-broket");
        var mqtt = result.Hosts.Single(h => h.Hostname == "mqtt-broket");
        Assert.Equal(["Ping over SSH", "Report connectivity result"], mqtt.Steps.Select(s => s.TaskName));
        Assert.All(mqtt.Steps, s => Assert.Equal(AnsibleStepStatus.Ok, s.Status));
    }

    [Fact]
    public void Parse_absorbs_multiline_json_result_blocks_and_extracts_msg_detail()
    {
        var result = AnsibleOutputParser.Parse(MidRunTranscript);

        var mqtt = result.Hosts.Single(h => h.Hostname == "mqtt-broket");
        var reportStep = mqtt.Steps.Single(s => s.TaskName == "Report connectivity result");
        Assert.Equal("mqtt-broket (10.0.150.0) is reachable via SSH", reportStep.Detail);
    }

    [Fact]
    public void Parse_treats_fatal_unreachable_as_a_failed_step_and_marks_the_host_HasFailure()
    {
        var result = AnsibleOutputParser.Parse(MidRunTranscript);

        var orchestrator = result.Hosts.Single(h => h.Hostname == "homelab-orchestrator");
        Assert.True(orchestrator.HasFailure);
        var step = Assert.Single(orchestrator.Steps);
        Assert.Equal(AnsibleStepStatus.Unreachable, step.Status);
        Assert.Contains("Permission denied", step.Detail);
    }

    [Fact]
    public void Parse_ignores_warning_and_error_noise_without_breaking_host_or_task_extraction()
    {
        var result = AnsibleOutputParser.Parse(MidRunTranscript);

        Assert.DoesNotContain(result.Hosts, h => h.Hostname is "tags" or "Origin:" or "6" or "7" or "8");
        Assert.Contains(result.Notices, n => n.Contains("reserved name 'tags'"));
        Assert.Contains(result.Notices, n => n.StartsWith("[ERROR]:"));
    }

    [Fact]
    public void Parse_adds_a_running_placeholder_for_a_host_still_pending_the_second_task()
    {
        var result = AnsibleOutputParser.Parse(MidRunTranscript);

        var nginx = result.Hosts.Single(h => h.Hostname == "nginx-proxy-manager");
        var lastStep = nginx.Steps[^1];
        Assert.Equal("Report connectivity result", lastStep.TaskName);
        Assert.Equal(AnsibleStepStatus.Running, lastStep.Status);
    }

    [Fact]
    public void Parse_does_not_add_a_placeholder_for_a_host_that_already_failed_in_an_earlier_task()
    {
        var result = AnsibleOutputParser.Parse(MidRunTranscript);

        var orchestrator = result.Hosts.Single(h => h.Hostname == "homelab-orchestrator");
        Assert.DoesNotContain(orchestrator.Steps, s => s.TaskName == "Report connectivity result");
    }

    [Fact]
    public void Parse_does_not_synthesize_placeholders_during_the_first_task()
    {
        var result = AnsibleOutputParser.Parse(FirstTaskOnlyTranscript);

        var mqtt = Assert.Single(result.Hosts);
        Assert.Equal("mqtt-broket", mqtt.Hostname);
        var step = Assert.Single(mqtt.Steps);
        Assert.Equal(AnsibleStepStatus.Ok, step.Status);
    }

    [Fact]
    public void Parse_parses_play_recap_rows_tolerant_of_variable_padding_whitespace()
    {
        var result = AnsibleOutputParser.Parse(CompletedRunTranscript);

        var orchestratorRow = result.Recap.Single(r => r.Hostname == "homelab-orchestrator");
        Assert.Equal(new AnsibleRecapEntry("homelab-orchestrator", 0, 0, 1, 0, 0, 0, 0), orchestratorRow);

        var mqttRow = result.Recap.Single(r => r.Hostname == "mqtt-broket");
        Assert.Equal(new AnsibleRecapEntry("mqtt-broket", 2, 0, 0, 0, 0, 0, 0), mqttRow);
    }

    [Fact]
    public void Parse_marks_a_recap_entry_with_unreachable_or_failed_counts_as_HasFailure()
    {
        var result = AnsibleOutputParser.Parse(CompletedRunTranscript);

        Assert.True(result.Recap.Single(r => r.Hostname == "homelab-orchestrator").HasFailure);
        Assert.False(result.Recap.Single(r => r.Hostname == "mqtt-broket").HasFailure);
    }

    [Fact]
    public void Parse_completed_run_no_longer_shows_a_running_placeholder()
    {
        var result = AnsibleOutputParser.Parse(CompletedRunTranscript);

        var nginx = result.Hosts.Single(h => h.Hostname == "nginx-proxy-manager");
        Assert.All(nginx.Steps, s => Assert.NotEqual(AnsibleStepStatus.Running, s.Status));
    }

    [Fact]
    public void Parse_returns_an_empty_state_for_empty_output()
    {
        var result = AnsibleOutputParser.Parse("");

        Assert.Empty(result.Hosts);
        Assert.Empty(result.Recap);
        Assert.Empty(result.Notices);
    }

    [Fact]
    public void Parse_captures_a_distinct_LoopItem_per_result_for_a_looped_task()
    {
        var result = AnsibleOutputParser.Parse(LoopedTaskTranscript);

        var web01 = result.Hosts.Single(h => h.Hostname == "web-01");
        var installSteps = web01.Steps.Where(s => s.TaskName == "Ensure packages are present").ToList();
        Assert.Equal(2, installSteps.Count);
        Assert.Contains(installSteps, s => s.LoopItem == "curl" && s.Status == AnsibleStepStatus.Ok);
        Assert.Contains(installSteps, s => s.LoopItem == "git" && s.Status == AnsibleStepStatus.Changed);

        var db01 = result.Hosts.Single(h => h.Hostname == "db-01");
        var db01Step = db01.Steps.Single(s => s.TaskName == "Ensure packages are present");
        Assert.Equal("curl", db01Step.LoopItem);
        Assert.Equal(AnsibleStepStatus.Changed, db01Step.Status);
    }

    [Fact]
    public void Parse_absorbs_a_loop_items_json_block_after_stripping_the_item_marker()
    {
        var result = AnsibleOutputParser.Parse(LoopedTaskTranscript);

        var web01 = result.Hosts.Single(h => h.Hostname == "web-01");
        var reportSteps = web01.Steps.Where(s => s.TaskName == "Report installed package").ToList();
        Assert.Equal(2, reportSteps.Count);
        Assert.Contains(reportSteps, s => s.LoopItem == "curl" && s.Detail == "curl is present");
        Assert.Contains(reportSteps, s => s.LoopItem == "git" && s.Detail == "git is present");
    }

    [Fact]
    public void Parse_task_centric_projection_preserves_task_order_and_derives_from_the_same_data_as_Hosts()
    {
        var result = AnsibleOutputParser.Parse(CompletedRunTranscript);

        Assert.Equal(["Ping over SSH", "Report connectivity result"], result.Tasks.Select(t => t.TaskName));
        Assert.Equal([0, 1], result.Tasks.Select(t => t.Order));

        var pingTask = result.Tasks[0];
        Assert.True(pingTask.HasFailure); // homelab-orchestrator was UNREACHABLE for this task
        Assert.Contains(pingTask.HostResults, h => h.Hostname == "homelab-orchestrator" && h.Results[0].Status == AnsibleStepStatus.Unreachable);

        var reportTask = result.Tasks[1];
        Assert.False(reportTask.HasFailure);
        // homelab-orchestrator never reached this task (it was unreachable in the first one), so
        // it must not appear here at all — unlike the synthetic Running placeholder case below.
        Assert.DoesNotContain(reportTask.HostResults, h => h.Hostname == "homelab-orchestrator");
    }

    [Fact]
    public void Parse_task_centric_projection_aggregates_looped_results_per_host_under_one_task_row()
    {
        var result = AnsibleOutputParser.Parse(LoopedTaskTranscript);

        var installTask = result.Tasks.Single(t => t.TaskName == "Ensure packages are present");
        var web01Results = installTask.HostResults.Single(h => h.Hostname == "web-01").Results;
        Assert.Equal(2, web01Results.Count);
        Assert.Equal(["curl", "git"], web01Results.Select(r => r.LoopItem));

        var reportTask = result.Tasks.Single(t => t.TaskName == "Report installed package");
        // db-01 never reported for the second task — it gets the synthetic Running placeholder,
        // which still shows up here as a single (unlooped) result for that host.
        var db01Results = reportTask.HostResults.Single(h => h.Hostname == "db-01").Results;
        var db01Result = Assert.Single(db01Results);
        Assert.Equal(AnsibleStepStatus.Running, db01Result.Status);
    }
}
