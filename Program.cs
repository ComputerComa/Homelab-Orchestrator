using HomelabOrchestrator.Models;
using HomelabOrchestrator.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();

builder.Services
    .AddOptions<ProxmoxOptions>()
    .Bind(builder.Configuration.GetSection(ProxmoxOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.Host), "Proxmox:Host is required (set it via user-secrets or the Proxmox__Host environment variable).")
    .Validate(o => !string.IsNullOrWhiteSpace(o.TokenId), "Proxmox:TokenId is required (format: user@realm!tokenid).")
    .Validate(o => !string.IsNullOrWhiteSpace(o.TokenSecret), "Proxmox:TokenSecret is required.")
    .ValidateOnStart();

builder.Services.AddScoped<IProxmoxService, ProxmoxService>();

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
