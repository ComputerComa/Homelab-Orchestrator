using System.Text;
using System.Text.RegularExpressions;
using HomelabOrchestrator.Models;

namespace HomelabOrchestrator.Services.Ansible;

/// <summary>
/// Turns ansible-playbook's default plain-text stdout into a structured per-host, per-task view.
/// Pure and stateless — called fresh from the Executions pages on every render against the run's
/// reconstructed output (<see cref="IAnsibleRunnerService.GetReconstructedOutputAsync"/>); the
/// parsed result is never itself persisted. This is a best-effort text parser against Ansible's
/// own default ("linear" strategy) callback format, not a custom callback plugin — an unrecognized
/// line is silently ignored rather than breaking the whole parse; the full raw output stays
/// available separately as a fallback for anything this misses.
/// </summary>
public static partial class AnsibleOutputParser
{
    [GeneratedRegex(@"^TASK \[(?<name>.+)\] \*+$")]
    private static partial Regex TaskHeaderRegex();

    [GeneratedRegex(@"^PLAY RECAP \*+$")]
    private static partial Regex RecapStartRegex();

    [GeneratedRegex(@"^(?<host>\S+)\s*:\s*ok=(?<ok>\d+)\s+changed=(?<changed>\d+)\s+unreachable=(?<unreachable>\d+)\s+failed=(?<failed>\d+)\s+skipped=(?<skipped>\d+)\s+rescued=(?<rescued>\d+)\s+ignored=(?<ignored>\d+)\s*$")]
    private static partial Regex RecapRowRegex();

    [GeneratedRegex(@"^fatal:\s*\[(?<host>[^\]]+)\]:\s*(?<kind>UNREACHABLE|FAILED)!\s*(?<tail>.*)$")]
    private static partial Regex FatalResultRegex();

    [GeneratedRegex(@"^(?<status>ok|changed|skipping|failed)\s*:\s*\[(?<host>[^\]]+)\](?<tail>.*)$")]
    private static partial Regex StatusResultRegex();

    [GeneratedRegex("\"msg\"\\s*:\\s*\"(?<msg>.*?)\"", RegexOptions.Singleline)]
    private static partial Regex MsgRegex();

    // A looped task's result carries an extra "=> (item=...)" marker before any JSON result block
    // (or in place of one, for a task with no registered/debug output) — e.g.
    // "changed: [host] => (item=a)" or "ok: [host] => (item=a) => {...}". Matching and stripping
    // this first lets the existing JSON/brace handling below run unchanged against whatever's left.
    [GeneratedRegex(@"^\s*=>\s*\(item=(?<item>.*?)\)\s*(?<rest>.*)$", RegexOptions.Singleline)]
    private static partial Regex LoopItemRegex();

    public static AnsibleRunParsedState Parse(string rawOutput)
    {
        var hostOrder = new List<string>();
        var stepsByHost = new Dictionary<string, List<AnsibleTaskStep>>();
        var failedOrUnreachableHosts = new HashSet<string>();
        var taskOrder = new List<string>();
        string? currentTask = null;
        var inRecap = false;
        var recapEntries = new List<AnsibleRecapEntry>();
        var notices = new List<string>();

        // State for absorbing a multi-line "=> {\n ... \n}" JSON result block. Ansible's default
        // callback never interleaves two hosts' blocks, so one absorption state (not per-host) is
        // enough. Brace counting is line-based, not string-aware — a msg value containing a
        // literal '{'/'}' character would throw this off; not observed in practice.
        var absorbing = false;
        var braceDepth = 0;
        var absorbBuffer = new StringBuilder();
        string? absorbingHost = null;

        foreach (var rawLine in rawOutput.Split('\n'))
        {
            var line = rawLine.TrimEnd();

            if (absorbing)
            {
                absorbBuffer.Append(line).Append('\n');
                braceDepth += CountChar(line, '{') - CountChar(line, '}');
                if (braceDepth <= 0)
                {
                    absorbing = false;
                    var absorbedMatch = MsgRegex().Match(absorbBuffer.ToString());
                    if (absorbedMatch.Success && absorbingHost is not null &&
                        stepsByHost.TryGetValue(absorbingHost, out var absorbedSteps) && absorbedSteps.Count > 0)
                    {
                        absorbedSteps[^1] = absorbedSteps[^1] with { Detail = absorbedMatch.Groups["msg"].Value };
                    }

                    absorbingHost = null;
                    absorbBuffer.Clear();
                }

                continue;
            }

            if (inRecap)
            {
                var recapMatch = RecapRowRegex().Match(line);
                if (recapMatch.Success)
                {
                    recapEntries.Add(new AnsibleRecapEntry(
                        recapMatch.Groups["host"].Value,
                        int.Parse(recapMatch.Groups["ok"].Value),
                        int.Parse(recapMatch.Groups["changed"].Value),
                        int.Parse(recapMatch.Groups["unreachable"].Value),
                        int.Parse(recapMatch.Groups["failed"].Value),
                        int.Parse(recapMatch.Groups["skipped"].Value),
                        int.Parse(recapMatch.Groups["rescued"].Value),
                        int.Parse(recapMatch.Groups["ignored"].Value)));
                }

                continue;
            }

            if (RecapStartRegex().IsMatch(line))
            {
                inRecap = true;
                continue;
            }

            var taskMatch = TaskHeaderRegex().Match(line);
            if (taskMatch.Success)
            {
                currentTask = taskMatch.Groups["name"].Value;
                if (!taskOrder.Contains(currentTask))
                {
                    taskOrder.Add(currentTask);
                }

                continue;
            }

            var fatalMatch = FatalResultRegex().Match(line);
            if (fatalMatch.Success)
            {
                var host = fatalMatch.Groups["host"].Value;
                var status = fatalMatch.Groups["kind"].Value == "UNREACHABLE" ? AnsibleStepStatus.Unreachable : AnsibleStepStatus.Failed;
                RecordStep(host, status, fatalMatch.Groups["tail"].Value);
                failedOrUnreachableHosts.Add(host);
                continue;
            }

            var statusMatch = StatusResultRegex().Match(line);
            if (statusMatch.Success)
            {
                var host = statusMatch.Groups["host"].Value;
                var status = statusMatch.Groups["status"].Value switch
                {
                    "ok" => AnsibleStepStatus.Ok,
                    "changed" => AnsibleStepStatus.Changed,
                    "skipping" => AnsibleStepStatus.Skipped,
                    _ => AnsibleStepStatus.Failed,
                };
                RecordStep(host, status, statusMatch.Groups["tail"].Value);
                if (status == AnsibleStepStatus.Failed)
                {
                    failedOrUnreachableHosts.Add(host);
                }

                continue;
            }

            if (line.StartsWith("[WARNING]:", StringComparison.Ordinal) || line.StartsWith("[ERROR]:", StringComparison.Ordinal))
            {
                notices.Add(line);
            }
        }

        // A host known from an earlier task that hasn't reported for the current (non-first) task
        // yet is still running it — Ansible's linear strategy runs every host through the same
        // task before any host starts the next one, except a host that already failed/became
        // unreachable, which is excluded from every later task for the rest of the play.
        if (!inRecap && currentTask is not null && taskOrder.Count > 0 && currentTask != taskOrder[0])
        {
            foreach (var host in hostOrder)
            {
                if (failedOrUnreachableHosts.Contains(host))
                {
                    continue;
                }

                var steps = stepsByHost[host];
                if (steps.All(s => s.TaskName != currentTask))
                {
                    steps.Add(new AnsibleTaskStep(currentTask, AnsibleStepStatus.Running, null));
                }
            }
        }

        var hosts = hostOrder.Select(h => new AnsibleHostRun(h, stepsByHost[h])).ToList();

        // A host that only ever appeared in PLAY RECAP (some earlier line for it didn't match any
        // known shape) still gets a section, just with no per-step detail, rather than vanishing.
        foreach (var recapHost in recapEntries.Select(r => r.Hostname))
        {
            if (!hostOrder.Contains(recapHost))
            {
                hosts.Add(new AnsibleHostRun(recapHost, []));
            }
        }

        // The task-centric projection of the same stepsByHost/taskOrder data hosts is built from —
        // nothing here is reparsed, just regrouped by task instead of by host. A host with more
        // than one result for a task looped; see AnsibleTaskStep.LoopItem.
        var tasks = taskOrder
            .Select((name, index) => new AnsibleTaskSummary(
                name,
                index,
                hostOrder
                    .Select(h => new AnsibleHostTaskResult(h, stepsByHost[h].Where(s => s.TaskName == name).ToList()))
                    .Where(r => r.Results.Count > 0)
                    .ToList()))
            .ToList();

        return new AnsibleRunParsedState(hosts, tasks, recapEntries, notices);

        void RecordStep(string host, AnsibleStepStatus status, string tail)
        {
            if (!stepsByHost.TryGetValue(host, out var steps))
            {
                steps = [];
                stepsByHost[host] = steps;
                hostOrder.Add(host);
            }

            var taskName = currentTask ?? "(unknown)";

            string? loopItem = null;
            var loopMatch = LoopItemRegex().Match(tail);
            if (loopMatch.Success)
            {
                loopItem = loopMatch.Groups["item"].Value;
                tail = loopMatch.Groups["rest"].Value;
            }

            var braceIndex = tail.IndexOf('{');
            if (braceIndex < 0)
            {
                steps.Add(new AnsibleTaskStep(taskName, status, null, loopItem));
                return;
            }

            var jsonPart = tail[braceIndex..];
            var depth = CountChar(jsonPart, '{') - CountChar(jsonPart, '}');
            if (depth <= 0)
            {
                var msgMatch = MsgRegex().Match(jsonPart);
                steps.Add(new AnsibleTaskStep(taskName, status, msgMatch.Success ? msgMatch.Groups["msg"].Value : null, loopItem));
                return;
            }

            steps.Add(new AnsibleTaskStep(taskName, status, null, loopItem));
            absorbing = true;
            braceDepth = depth;
            absorbBuffer.Clear();
            absorbBuffer.Append(jsonPart).Append('\n');
            absorbingHost = host;
        }
    }

    private static int CountChar(string value, char target)
    {
        var count = 0;
        foreach (var c in value)
        {
            if (c == target)
            {
                count++;
            }
        }

        return count;
    }
}
