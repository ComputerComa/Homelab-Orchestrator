using System.Diagnostics;
using HomelabOrchestrator.Options;
using Microsoft.Extensions.Options;

namespace HomelabOrchestrator.Services.Ansible;

/// <summary>
/// The only place that spawns `ansible-playbook`: always via <see cref="ProcessStartInfo"/> with
/// <c>UseShellExecute = false</c> and every argument in <see cref="ProcessStartInfo.ArgumentList"/>
/// — never a concatenated shell string.
/// </summary>
public class AnsibleProcessRunner(IOptions<AnsibleOptions> options, ILogger<AnsibleProcessRunner> logger) : IAnsibleProcessRunner
{
    private readonly AnsibleOptions _options = options.Value;

    public async Task<int> RunPlaybookAsync(
        string playbookPath,
        string? limit,
        Action<string> onOutputLine,
        CancellationToken cancellationToken = default)
    {
        var inventoryPath = Path.Combine(_options.RepositoryRoot, _options.InventoryFile);
        var configPath = Path.Combine(_options.RepositoryRoot, "ansible.cfg");

        var startInfo = new ProcessStartInfo
        {
            FileName = _options.ExecutablePath,
            WorkingDirectory = _options.RepositoryRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(inventoryPath);
        if (!string.IsNullOrWhiteSpace(limit))
        {
            startInfo.ArgumentList.Add("--limit");
            startInfo.ArgumentList.Add(limit);
        }

        startInfo.ArgumentList.Add(playbookPath);

        // Belt-and-suspenders alongside WorkingDirectory: guarantees ansible.cfg (the inventory
        // plugin and roles_path) is found regardless of the server process's own working directory.
        startInfo.Environment["ANSIBLE_CONFIG"] = configPath;
        startInfo.Environment["ANSIBLE_FORCE_COLOR"] = "0";
        startInfo.Environment["ANSIBLE_NOCOLOR"] = "1";

        using var process = new Process { StartInfo = startInfo };

        void Capture(string? line)
        {
            if (line is not null)
            {
                onOutputLine(line);
            }
        }

        process.OutputDataReceived += (_, e) => Capture(e.Data);
        process.ErrorDataReceived += (_, e) => Capture(e.Data);

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(_options.TimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        logger.LogInformation(
            "Running {Executable} {Args} in {WorkingDirectory}",
            _options.ExecutablePath, string.Join(' ', startInfo.ArgumentList), _options.RepositoryRoot);

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(linkedCts.Token);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            TryKill(process);
            throw new TimeoutException($"ansible-playbook did not finish within {_options.TimeoutSeconds}s.");
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        return process.ExitCode;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // Process already exited between the check and the kill attempt — nothing to do.
        }
    }
}
