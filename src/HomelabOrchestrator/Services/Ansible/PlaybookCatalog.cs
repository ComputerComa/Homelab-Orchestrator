using HomelabOrchestrator.Models;
using HomelabOrchestrator.Options;
using Microsoft.Extensions.Options;

namespace HomelabOrchestrator.Services.Ansible;

/// <summary>
/// Lists and resolves playbooks strictly beneath ansible/playbooks. A name only ever resolves to
/// a path this call itself just enumerated from that directory — nothing browser-supplied is
/// ever combined into a path, which is what actually prevents traversal (the resolved-path
/// prefix check below is defense in depth on top of that, not the primary guard).
/// </summary>
public class PlaybookCatalog(IOptions<AnsibleOptions> options) : IPlaybookCatalog
{
    private readonly AnsibleOptions _options = options.Value;

    public Task<IReadOnlyList<PlaybookSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        var directory = PlaybooksDirectory();
        if (!Directory.Exists(directory))
        {
            return Task.FromResult<IReadOnlyList<PlaybookSummary>>([]);
        }

        var playbooks = Directory.EnumerateFiles(directory, "*.yml", SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateFiles(directory, "*.yaml", SearchOption.TopDirectoryOnly))
            .Select(path => new PlaybookSummary(Path.GetFileNameWithoutExtension(path), Path.GetFileName(path)))
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .ToList();

        return Task.FromResult<IReadOnlyList<PlaybookSummary>>(playbooks);
    }

    public async Task<string?> ResolvePathAsync(string name, CancellationToken cancellationToken = default)
    {
        var playbooks = await ListAsync(cancellationToken);
        var match = playbooks.FirstOrDefault(p => p.Name == name);
        if (match is null)
        {
            return null;
        }

        var directory = Path.GetFullPath(PlaybooksDirectory());
        var candidate = Path.GetFullPath(Path.Combine(directory, match.FileName));
        var normalizedDirectory = directory + Path.DirectorySeparatorChar;

        return candidate.StartsWith(normalizedDirectory, StringComparison.Ordinal) ? candidate : null;
    }

    private string PlaybooksDirectory() => Path.Combine(_options.RepositoryRoot, "playbooks");
}
