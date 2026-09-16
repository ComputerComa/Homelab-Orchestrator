using HomelabOrchestrator.Options;
using HomelabOrchestrator.Services.Ansible;

namespace HomelabOrchestrator.Tests;

public class PlaybookCatalogTests : IDisposable
{
    private readonly string _repositoryRoot = Directory.CreateTempSubdirectory("playbook-catalog-tests-").FullName;

    public void Dispose() => Directory.Delete(_repositoryRoot, recursive: true);

    private PlaybookCatalog BuildCatalog()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new AnsibleOptions { RepositoryRoot = _repositoryRoot });
        return new PlaybookCatalog(options);
    }

    [Fact]
    public async Task ListAsync_finds_yml_and_yaml_files_only()
    {
        var playbooksDir = Directory.CreateDirectory(Path.Combine(_repositoryRoot, "playbooks"));
        await File.WriteAllTextAsync(Path.Combine(playbooksDir.FullName, "apply-base.yml"), "---\n");
        await File.WriteAllTextAsync(Path.Combine(playbooksDir.FullName, "ssh-check.yaml"), "---\n");
        await File.WriteAllTextAsync(Path.Combine(playbooksDir.FullName, "notes.txt"), "not a playbook");

        var catalog = BuildCatalog();
        var playbooks = await catalog.ListAsync();

        Assert.Equal(["apply-base", "ssh-check"], playbooks.Select(p => p.Name));
    }

    [Fact]
    public async Task ListAsync_does_not_recurse_into_subdirectories()
    {
        var playbooksDir = Directory.CreateDirectory(Path.Combine(_repositoryRoot, "playbooks"));
        var nested = Directory.CreateDirectory(Path.Combine(playbooksDir.FullName, "catalog"));
        await File.WriteAllTextAsync(Path.Combine(nested.FullName, "nested.yml"), "---\n");

        var catalog = BuildCatalog();

        Assert.Empty(await catalog.ListAsync());
    }

    [Fact]
    public async Task ListAsync_returns_empty_when_the_playbooks_directory_does_not_exist()
    {
        var catalog = BuildCatalog();

        Assert.Empty(await catalog.ListAsync());
    }

    [Fact]
    public async Task ResolvePathAsync_returns_the_full_path_for_a_known_playbook()
    {
        var playbooksDir = Directory.CreateDirectory(Path.Combine(_repositoryRoot, "playbooks"));
        var expected = Path.Combine(playbooksDir.FullName, "apply-base.yml");
        await File.WriteAllTextAsync(expected, "---\n");

        var catalog = BuildCatalog();
        var resolved = await catalog.ResolvePathAsync("apply-base");

        Assert.Equal(Path.GetFullPath(expected), resolved);
    }

    [Theory]
    [InlineData("does-not-exist")]
    [InlineData("../ansible.cfg")]
    [InlineData("../../etc/passwd")]
    [InlineData("apply-base.yml")] // must be the bare name, not the filename with extension
    public async Task ResolvePathAsync_returns_null_for_anything_not_in_the_catalog(string name)
    {
        var playbooksDir = Directory.CreateDirectory(Path.Combine(_repositoryRoot, "playbooks"));
        await File.WriteAllTextAsync(Path.Combine(playbooksDir.FullName, "apply-base.yml"), "---\n");

        var catalog = BuildCatalog();

        Assert.Null(await catalog.ResolvePathAsync(name));
    }
}
