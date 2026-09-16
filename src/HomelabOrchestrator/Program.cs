using HomelabOrchestrator.Authorization;
using HomelabOrchestrator.Data;
using HomelabOrchestrator.Endpoints;
using HomelabOrchestrator.Options;
using HomelabOrchestrator.Services.Ansible;
using HomelabOrchestrator.Services.Jobs;
using HomelabOrchestrator.Services.Provisioning;
using HomelabOrchestrator.Services.Proxmox;
using HomelabOrchestrator.Services.Ssh;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages(options =>
{
    // Provisioning is the primary workflow, so it is also the landing page.
    options.Conventions.AddPageRoute("/Provision/Index", "");

    // Every page requires a signed-in operator except the login page itself.
    options.Conventions.AuthorizeFolder("/");
    options.Conventions.AllowAnonymousToPage("/Account/Login");
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

builder.Services
    .AddOptions<AdminOptions>()
    .Bind(builder.Configuration.GetSection(AdminOptions.SectionName))
    .Validate(
        o => string.IsNullOrEmpty(o.Username) == string.IsNullOrEmpty(o.Password),
        "Admin:Username and Admin:Password must both be set, or both left empty.")
    .ValidateOnStart();

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? $"Data Source={Path.Combine(builder.Environment.ContentRootPath, "homelab-orchestrator.db")}";
builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(connectionString));

builder.Services.AddHttpContextAccessor();

// Cookie-based sign-in for the single seeded operator account (see the admin-seeding step below)
// — there is no self-registration page.
builder.Services
    .AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddIdentityCookies();
builder.Services
    .AddIdentityCore<IdentityUser>(options => options.SignIn.RequireConfirmedAccount = false)
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/Login";

    // ASP.NET Core's cookie handler treats any failed authorization from an unauthenticated
    // caller as a login challenge, even for the IP-based LocalhostOnly policy below, which has
    // nothing to do with sign-in — without this, a rejected API call would 302 to an HTML login
    // page instead of getting a plain 403.
    options.Events.OnRedirectToLogin = RejectApiRequestsWithForbidden;
    options.Events.OnRedirectToAccessDenied = RejectApiRequestsWithForbidden;
});

static Task RejectApiRequestsWithForbidden(Microsoft.AspNetCore.Authentication.RedirectContext<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions> context)
{
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }

    context.Response.Redirect(context.RedirectUri);
    return Task.CompletedTask;
}

// Scopes the read-only Ansible inventory endpoints to the same machine — ansible-playbook is the
// only caller (via the homelab_orchestrator inventory plugin hitting http://localhost), so they
// never need to be reachable from the network the rest of the app listens on.
builder.Services.AddAuthorization(options =>
    options.AddPolicy("LocalhostOnly", policy => policy.Requirements.Add(new LocalhostOnlyRequirement())));
builder.Services.AddSingleton<IAuthorizationHandler, LocalhostOnlyHandler>();

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

// Adopts pre-existing containers whose actual configured address already matches the VMID
// convention, so they can be tagged managed-by-orchestrator without guessing.
builder.Services.AddSingleton<IContainerReconciliationService, ContainerReconciliationService>();

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

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await dbContext.Database.MigrateAsync();

    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
    var adminOptions = scope.ServiceProvider.GetRequiredService<IOptions<AdminOptions>>().Value;

    // First-run only: seed the single operator account from config, then never touch it again.
    // Once any account exists, Admin:Username/Password are ignored — change the password through
    // the app's own change-password page instead.
    if (!string.IsNullOrEmpty(adminOptions.Username) && !userManager.Users.Any())
    {
        var result = await userManager.CreateAsync(new IdentityUser(adminOptions.Username), adminOptions.Password);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Could not create the seeded admin account: {string.Join("; ", result.Errors.Select(e => e.Description))}");
        }
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapRazorPages();
app.MapInventoryEndpoints();

app.Run();
