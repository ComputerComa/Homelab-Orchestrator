using HomelabOrchestrator.Endpoints;
using HomelabOrchestrator.Options;
using HomelabOrchestrator.Services.Ansible;
using HomelabOrchestrator.Services.Jobs;
using HomelabOrchestrator.Services.Provisioning;
using HomelabOrchestrator.Services.Proxmox;
using HomelabOrchestrator.Services.Ssh;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages(options =>
{
    // Provisioning is the primary workflow, so it is also the landing page.
    options.Conventions.AddPageRoute("/Provision/Index", "");
});

builder.Services
    .AddOptions<ProxmoxOptions>()
    .Bind(builder.Configuration.GetSection(ProxmoxOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.Host), "Proxmox:Host is required (set it via user-secrets or the Proxmox__Host environment variable).")
    .Validate(o => !string.IsNullOrWhiteSpace(o.ApiToken), "Proxmox:ApiToken is required (format: user@realm!tokenid=secret).")
    .ValidateOnStart();

builder.Services
    .AddOptions<SshOptions>()
    .Bind(builder.Configuration.GetSection(SshOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.RemoteUser), "Ssh:RemoteUser is required.")
    .Validate(o => o.Port is > 0 and <= 65535, "Ssh:Port must be a valid TCP port.")
    .ValidateOnStart();

builder.Services
    .AddOptions<AnsibleOptions>()
    .Bind(builder.Configuration.GetSection(AnsibleOptions.SectionName))
    .PostConfigure(o =>
    {
        // Local dev layout: ansible/ is a sibling of src/ and tests/. Deployments with a
        // different layout should set Ansible:RepositoryRoot explicitly.
        if (string.IsNullOrWhiteSpace(o.RepositoryRoot))
        {
            o.RepositoryRoot = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "..", "ansible"));
        }
    });

// Proxmox service: the only thing that touches Corsinvest.ProxmoxVE.Api. Singleton so its
// PveClient (and internal HttpClient) is built once and reused instead of per-call.
builder.Services.AddSingleton<IProxmoxService, ProxmoxService>();

// Reads workstation + orchestrator public keys from disk; never touches the private key.
builder.Services.AddSingleton<ISshPublicKeyProvider, SshPublicKeyProvider>();
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(5050);
});

// Job services: queueing, state, and the background worker that serializes provisioning.
builder.Services.AddSingleton<IProvisioningJobStore, InMemoryProvisioningJobStore>();
builder.Services.AddSingleton<IProvisioningJobQueue, ProvisioningJobQueue>();
builder.Services.AddHostedService<ProvisioningWorker>();

// Application service: the boundary Razor Pages call into instead of Proxmox/job internals directly.
builder.Services.AddSingleton<IProvisioningService, ProvisioningService>();

// Application service backing the read-only Ansible inventory endpoints.
builder.Services.AddSingleton<IAnsibleInventoryService, AnsibleInventoryService>();

// Ansible runner: playbook discovery, process execution, and the job queue/worker that
// serializes runs — mirrors the provisioning job pattern above.
builder.Services.AddSingleton<IPlaybookCatalog, PlaybookCatalog>();
builder.Services.AddSingleton<IAnsibleProcessRunner, AnsibleProcessRunner>();
builder.Services.AddSingleton<IAnsibleRunJobStore, InMemoryAnsibleRunJobStore>();
builder.Services.AddSingleton<IAnsibleRunJobQueue, AnsibleRunJobQueue>();
builder.Services.AddHostedService<AnsibleRunWorker>();
builder.Services.AddSingleton<IAnsibleRunnerService, AnsibleRunnerService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();
app.MapRazorPages();
app.MapInventoryEndpoints();

app.Run();
