using HomelabOrchestrator.Models;
using HomelabOrchestrator.Options;
using HomelabOrchestrator.Services.Ansible;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HomelabOrchestrator.Tests;

/// <summary>
/// Exercises the real Process/ProcessStartInfo plumbing against a throwaway shell script standing
/// in for ansible-playbook (no real Ansible installation needed) — this verifies the argv shape
/// AnsibleProcessRunner builds, not Ansible's own behavior.
/// </summary>
public class AnsibleProcessRunnerTests : IDisposable
{
    private readonly string _scriptPath = Path.Combine(Path.GetTempPath(), $"fake-ansible-playbook-{Guid.NewGuid():N}.sh");

    public AnsibleProcessRunnerTests()
    {
        File.WriteAllText(_scriptPath, "#!/bin/sh\nfor arg in \"$@\"; do echo \"ARG:$arg\"; done\nexit 0\n");
        File.SetUnixFileMode(_scriptPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    public void Dispose() => File.Delete(_scriptPath);

    [Fact]
    public async Task RunPlaybookAsync_passes_extra_vars_at_file_as_two_separate_arguments()
    {
        var runner = Build();
        var lines = new List<string>();

        await runner.RunPlaybookAsync("/fake/playbook.yml", limit: null, extraVarsFilePath: "/tmp/extra-vars.json", (_, line) => lines.Add(line));

        var index = lines.IndexOf("ARG:--extra-vars");
        Assert.True(index >= 0, "Expected --extra-vars to be an argument on its own.");
        Assert.Equal("ARG:@/tmp/extra-vars.json", lines[index + 1]);
    }

    [Fact]
    public async Task RunPlaybookAsync_omits_extra_vars_entirely_when_no_path_is_given()
    {
        var runner = Build();
        var lines = new List<string>();

        await runner.RunPlaybookAsync("/fake/playbook.yml", limit: null, extraVarsFilePath: null, (_, line) => lines.Add(line));

        Assert.DoesNotContain(lines, l => l.Contains("extra-vars"));
    }

    [Fact]
    public async Task RunPlaybookAsync_passes_the_limit_as_its_own_argument_when_given()
    {
        var runner = Build();
        var lines = new List<string>();

        await runner.RunPlaybookAsync("/fake/playbook.yml", limit: "tag_mqtt", extraVarsFilePath: null, (_, line) => lines.Add(line));

        var index = lines.IndexOf("ARG:--limit");
        Assert.True(index >= 0);
        Assert.Equal("ARG:tag_mqtt", lines[index + 1]);
    }

    private AnsibleProcessRunner Build()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new AnsibleOptions
        {
            RepositoryRoot = Path.GetTempPath(),
            ExecutablePath = _scriptPath,
            InventoryFile = "does-not-need-to-exist.yml",
            TimeoutSeconds = 30,
        });
        return new AnsibleProcessRunner(options, NullLogger<AnsibleProcessRunner>.Instance);
    }
}
