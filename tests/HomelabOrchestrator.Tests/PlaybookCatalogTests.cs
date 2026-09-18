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

    [Fact]
    public async Task GetDetailAsync_parses_the_playbooks_own_inline_tasks()
    {
        var playbooksDir = Directory.CreateDirectory(Path.Combine(_repositoryRoot, "playbooks"));
        await File.WriteAllTextAsync(
            Path.Combine(playbooksDir.FullName, "ssh-check.yml"),
            "---\n- name: Check SSH connectivity\n  hosts: all\n  tasks:\n    - name: Ping over SSH\n      ansible.builtin.ping:\n");

        var catalog = BuildCatalog();
        var detail = await catalog.GetDetailAsync("ssh-check");

        Assert.NotNull(detail);
        Assert.Equal("Check SSH connectivity", detail.Description);
        Assert.Equal(["Ping over SSH"], detail.Steps);
    }

    [Fact]
    public async Task GetDetailAsync_resolves_a_referenced_roles_tasks_file()
    {
        var playbooksDir = Directory.CreateDirectory(Path.Combine(_repositoryRoot, "playbooks"));
        await File.WriteAllTextAsync(
            Path.Combine(playbooksDir.FullName, "apply-base.yml"),
            "---\n- name: Apply the base role\n  hosts: all\n  roles:\n    - base\n");
        var roleTasksDir = Directory.CreateDirectory(Path.Combine(_repositoryRoot, "roles", "base", "tasks"));
        await File.WriteAllTextAsync(Path.Combine(roleTasksDir.FullName, "main.yml"), "---\n- name: Update apt cache\n  ansible.builtin.apt:\n");

        var catalog = BuildCatalog();
        var detail = await catalog.GetDetailAsync("apply-base");

        Assert.NotNull(detail);
        Assert.Equal(["Update apt cache"], detail.Steps);
    }

    [Fact]
    public async Task GetDetailAsync_returns_null_for_a_playbook_not_in_the_catalog()
    {
        var catalog = BuildCatalog();

        Assert.Null(await catalog.GetDetailAsync("does-not-exist"));
    }
}
