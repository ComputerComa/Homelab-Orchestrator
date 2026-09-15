using HomelabOrchestrator.Options;
using HomelabOrchestrator.Services.Jobs;
using HomelabOrchestrator.Services.Provisioning;
using HomelabOrchestrator.Services.Proxmox;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages(options =>
{
    // Provisioning is the only workflow today, so it is also the landing page.
    options.Conventions.AddPageRoute("/Provision/Index", "");
});

builder.Services
    .AddOptions<ProxmoxOptions>()
    .Bind(builder.Configuration.GetSection(ProxmoxOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.Host), "Proxmox:Host is required (set it via user-secrets or the Proxmox__Host environment variable).")
    .Validate(o => !string.IsNullOrWhiteSpace(o.ApiToken), "Proxmox:ApiToken is required (format: user@realm!tokenid=secret).")
    .ValidateOnStart();

// Proxmox service: the only thing that touches Corsinvest.ProxmoxVE.Api. Singleton so its
// PveClient (and internal HttpClient) is built once and reused instead of per-call.
builder.Services.AddSingleton<IProxmoxService, ProxmoxService>();

// Job services: queueing, state, and the background worker that serializes provisioning.
builder.Services.AddSingleton<IProvisioningJobStore, InMemoryProvisioningJobStore>();
builder.Services.AddSingleton<IProvisioningJobQueue, ProvisioningJobQueue>();
builder.Services.AddHostedService<ProvisioningWorker>();

// Application service: the boundary Razor Pages call into instead of Proxmox/job internals directly.
builder.Services.AddSingleton<IProvisioningService, ProvisioningService>();

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

app.Run();
