# Homelab Orchestrator

An ASP.NET Core Razor Pages app for provisioning LXC containers on Proxmox VE,
using [`Corsinvest.ProxmoxVE.Api`](https://github.com/Corsinvest/cv4pve-api-dotnet)
to talk to Proxmox and [htmx](https://htmx.org) for the interactive parts of the
UI (no client-side framework, no full page reloads).

This is a Razor Pages port of a Go CLI that did the same thing: look up the next
free VMID, derive its address, find the newest Debian 13 template, ask for a
hostname/sizing, show a confirmation, then create the container and wait for the
Proxmox task to finish.

## Project layout

- `Pages/Index.cshtml(.cs)` — the whole workflow lives on one page:
  - `OnGet` loads the next VMID/address/template from Proxmox.
  - `OnGetRefresh` (htmx) re-fetches just that panel, e.g. if the VMID got taken.
  - `OnPostReview` (htmx) validates the form and renders a confirmation summary.
  - `OnPostCreate` (htmx) creates the container and waits for the task.
- `Services/ProxmoxService.cs` — the only place that talks to `PveClient`.
- `Models/` — configuration (`ProxmoxOptions`), the form model, and the request/result
  types passed to/from the service.

## Configuration

Non-secret defaults live in `appsettings.json` under `Proxmox`. The three
required values (`Host`, `TokenId`, `TokenSecret`) are left blank there and must
be supplied separately — the app fails fast on startup if they're missing.

For local development, use [user-secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets):

```bash
dotnet user-secrets set "Proxmox:Host" "pve.example.lan"
dotnet user-secrets set "Proxmox:TokenId" "root@pam!orchestrator"
dotnet user-secrets set "Proxmox:TokenSecret" "00000000-0000-0000-0000-000000000000"
```

In any other environment, set the equivalent environment variables (ASP.NET
Core maps `__` to nested configuration sections):

```bash
export Proxmox__Host=pve.example.lan
export Proxmox__TokenId="root@pam!orchestrator"
export Proxmox__TokenSecret="00000000-0000-0000-0000-000000000000"
```

Everything else in the `Proxmox` section is a plain override, e.g.:

| Key | Default | Meaning |
|---|---|---|
| `Node` | `pve` | Node to create the container on |
| `TemplateStorage` | `local` | Storage to search for the `debian-13-*` template |
| `RootfsStorage` | `local-lvm` | Storage for the container's root filesystem |
| `Bridge` | `vmbr0` | Network bridge |
| `Gateway` | `10.0.1.1` | Container gateway |
| `IpNetworkPrefix` | `10.0.150` | First three octets; the VMID becomes the last octet |
| `IpHostMin` / `IpHostMax` | `2` / `254` | Valid range for that last octet |
| `DefaultCores` / `DefaultMemoryMB` / `DefaultSwapMB` / `DefaultDiskGB` | `2` / `2048` / `512` / `8` | Form defaults |
| `DefaultSshPublicKeyPath` | _(none)_ | Optional path to a public key file used to pre-fill the SSH key field |
| `ValidateTlsCertificate` | `false` | Set to `true` if Proxmox has a trusted certificate |

The Proxmox API token needs permission to create containers on the configured
node/storage (`VM.Allocate`, `VM.Config.*`, `Datastore.AllocateSpace`, etc.).

## Running

```bash
dotnet restore
dotnet run
```

Then open the URL printed in the console (e.g. `https://localhost:5001`).
