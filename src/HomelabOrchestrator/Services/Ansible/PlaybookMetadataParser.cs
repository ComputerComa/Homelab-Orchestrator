using System.Text.RegularExpressions;
using HomelabOrchestrator.Models;

namespace HomelabOrchestrator.Services.Ansible;

/// <summary>
/// Best-effort parser turning a playbook's own YAML into a short human-readable description and
/// step list for the Runner page's playbook-picker modal. Mirrors <see cref="AnsibleOutputParser"/>'s
/// style — line-oriented regexes, not a full YAML parser — since these playbooks are simple,
/// single-play files. Pure and stateless: resolving a `roles:` entry into that role's own tasks is
/// the caller's job (<see cref="PlaybookCatalog.GetDetailAsync"/> reads those files and passes their
/// content in), so this stays testable against plain strings with no filesystem access.
/// </summary>
public static partial class PlaybookMetadataParser
{
    [GeneratedRegex(@"^\s*-\s*name:\s*(.+)$")]
    private static partial Regex NameRegex();

    [GeneratedRegex(@"^\s*roles:\s*\[(.+)\]\s*$")]
    private static partial Regex InlineRolesRegex();

    [GeneratedRegex(@"^\s*roles:\s*$")]
    private static partial Regex RolesBlockStartRegex();

    [GeneratedRegex(@"^\s*-\s*([A-Za-z0-9_-]+)\s*$")]
    private static partial Regex RoleListItemRegex();

    /// <summary>Role names a playbook's `roles:` entry (block or inline form) references, in order.</summary>
    public static IReadOnlyList<string> ExtractRoleNames(string playbookYaml)
    {
        var roles = new List<string>();
        var inRolesBlock = false;

        foreach (var rawLine in playbookYaml.Split('\n'))
        {
            var line = rawLine.TrimEnd();

            var inlineMatch = InlineRolesRegex().Match(line);
            if (inlineMatch.Success)
            {
                roles.AddRange(inlineMatch.Groups[1].Value.Split(',').Select(r => r.Trim()));
                inRolesBlock = false;
                continue;
            }

            if (RolesBlockStartRegex().IsMatch(line))
            {
                inRolesBlock = true;
                continue;
            }

            if (!inRolesBlock)
            {
                continue;
            }

            var itemMatch = RoleListItemRegex().Match(line);
            if (itemMatch.Success)
            {
                roles.Add(itemMatch.Groups[1].Value);
            }
            else
            {
                inRolesBlock = false;
            }
        }

        return roles;
    }

    /// <summary>
    /// The playbook's first play-level <c>name:</c> line becomes <see cref="PlaybookDetail.Description"/>
    /// (falling back to <paramref name="name"/> when the playbook has none); every later <c>name:</c>
    /// line — the playbook's own inline tasks, plus each resolvable role's tasks in
    /// <paramref name="roleTasksYaml"/> (keyed by the role names <see cref="ExtractRoleNames"/> found;
    /// a role not present there is silently skipped, same as an unrecognized output line elsewhere in
    /// this app) — becomes a step.
    /// </summary>
    public static PlaybookDetail Parse(string name, string playbookYaml, IReadOnlyDictionary<string, string> roleTasksYaml)
    {
        var names = ExtractNames(playbookYaml);
        var description = names.Count > 0 ? names[0] : name;
        var steps = new List<string>(names.Skip(1));

        foreach (var role in ExtractRoleNames(playbookYaml))
        {
            if (roleTasksYaml.TryGetValue(role, out var roleYaml))
            {
                steps.AddRange(ExtractNames(roleYaml));
            }
        }

        return new PlaybookDetail(name, description, steps);
    }

    private static List<string> ExtractNames(string yaml)
    {
        var names = new List<string>();
        foreach (var rawLine in yaml.Split('\n'))
        {
            var match = NameRegex().Match(rawLine.TrimEnd());
            if (match.Success)
            {
                names.Add(match.Groups[1].Value.Trim().Trim('"'));
            }
        }

        return names;
    }
}
