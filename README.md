# Homelab Orchestrator

Homelab Orchestrator is a small, self-hosted ASP.NET Core application for provisioning and maintaining Proxmox LXC containers.

The goal is to provide an appliance-like workflow:

1. Enter a hostname and resource requirements.
2. Create a Debian LXC through the Proxmox API.
3. Assign an address using the homelab's established IP convention.
4. Wait for the container and SSH to become available.
5. Apply the Ansible `base` role.
6. Use the web interface for later maintenance or approved application playbooks.

This project intentionally does not use OpenTofu or maintain a static Ansible inventory. Proxmox remains the source of truth for compute resources, and inventory is generated only when an Ansible operation runs.

## Status

Milestone 1 (provisioning parity) is implemented except for the two steps that depend on
Ansible, which does not exist in this repository yet: waiting for SSH and applying the
`base` role. Today, from the web UI, an operator can:

- see the next available VMID, the address it maps to, and the newest Debian 13 template,
  refreshed on demand;
- submit a hostname and sizing, review a summary, and confirm — there is no SSH key field;
  containers trust the orchestrator's own combined public keys (see
  [SSH key management](#ssh-key-management));
- have the request queued and executed by a background worker that recalculates the
  VMID/address/template itself immediately before creating anything — the browser preview
  shown earlier is never trusted or reused;
- watch the job's stage update live over HTMX polling until it succeeds or fails.

Outside the web UI, two JSON endpoints generate a standard Ansible dynamic inventory for a single
container or for every currently running one, and a matching custom Ansible inventory plugin
(`ansible/inventory_plugins/homelab_orchestrator.py`) lets `ansible-playbook`/`ansible-inventory`
consume them directly — see [Inventory API](#inventory-api-implemented) and
[Inventory plugin](#inventory-plugin-implemented). This is a first, narrow slice of the "discover
hosts from Proxmox and generate inventory" capability Maintenance will eventually need.

A second page, the [Ansible Runner](#ansible-runner-implemented), does run `ansible-playbook`:
an operator picks one of the playbooks committed under `ansible/playbooks/` and a target — a
specific running container, every running container currently carrying a given Proxmox tag, or
every running container — and a background worker runs it, streaming the captured output back
to the page.

A third page, [Reconcile](#reconcile-pre-existing-containers-implemented), finds containers that
predate the orchestrator (not tagged `managed-by-orchestrator`) and offers to adopt the ones whose
actual configured address already matches what the VMID convention expects — never guessing for
the ones that don't. A top navigation bar (Provision / Ansible Runner / Reconcile) switches
between all three pages.

The whole web UI requires signing in as the single seeded operator account, and the two
inventory endpoints above are restricted to loopback callers — see
[Authentication](#authentication-implemented) for both.

See [Milestone 1](#milestone-1-provisioning-parity) below for the exact checklist.

Maintenance and execution history (Milestones 2 and 4) are design-only — described under
[Planned interface](#planned-interface) but not yet implemented. The playbook catalog
(Milestone 3) is partially implemented by the Ansible Runner above; see that section for what's
still missing (per-playbook metadata/forms and persisted execution history).

## Planned interface

### Provision

Create a Debian 13 LXC and apply its initial configuration.

The operator supplies:

- hostname;
- CPU cores;
- memory;
- swap;
- disk size;
- start and start-at-boot preferences.

The server determines:

- the next VMID;
- the IP address;
- node, storage, bridge, subnet, gateway, and DNS configuration;
- the newest available Debian 13 template;
- the SSH public keys to trust, combined from the orchestrator's own filesystem — never
  entered by the operator (see [SSH key management](#ssh-key-management));
- required Proxmox features and tags.

A provisioning job should expose each stage independently so a failed Ansible run can be retried without recreating the LXC.

### Maintenance

Run common, controlled operations against selected containers, including:

- checking for package updates;
- applying package updates;
- checking failed systemd units;
- checking disk usage;
- identifying containers that require a reboot;
- reapplying the Ansible `base` role;
- rebooting selected containers.

Hosts should be discovered from Proxmox and may be filtered using Proxmox tags.

### Playbooks

Run approved Ansible playbooks against one or more discovered hosts. Examples include:

- installing application software;
- configuring PostgreSQL;
- configuring Nginx;
- performing database maintenance;
- installing monitoring agents.

The UI must only expose playbooks from an approved catalog. It must not provide an arbitrary command or arbitrary playbook-path input.

### Executions

Display current and previous operations with:

- operation type;
- selected targets;
- submitted inputs;
- start and finish times;
- current stage;
- exit status;
- captured output;
- retry actions where appropriate.

## Architecture

```text
Browser
  |
  | Razor Pages + HTMX
  v
ASP.NET Core application
  |
  +-- Background job queue
  |     |
  |     +-- Corsinvest.ProxmoxVE.Api
  |     |     |
  |     |     `-- Proxmox VE
  |     |
  |     `-- ansible-playbook
  |           |
  |           `-- Newly created or selected LXCs
  |
  `-- SQLite execution history (planned)
```

### Technology choices

- .NET 10
- ASP.NET Core Razor Pages
- HTMX
- `Corsinvest.ProxmoxVE.Api`
- hosted background services and channels
- Ansible Core
- SQLite for execution history when persistence is introduced
- native Linux binaries and systemd services

## Source-of-truth rules

- **Proxmox** is the source of truth for LXCs, VMIDs, runtime state, and tags.
- **Git** is the source of truth for application code, approved playbooks, roles, and playbook metadata.
- **Application configuration/secrets** provide Proxmox credentials, SSH material, and environment-specific defaults.
- **SQLite** may store job history and UI metadata, but it must not become a duplicate static machine inventory.
- **Ansible inventory** is generated in memory or under `/run/homelab-orchestrator` for an individual execution and is never committed.

## Repository layout

The intended repository layout is:

```text
homelab-orchestrator/
|-- AGENTS.md
|-- README.md
|-- HomelabOrchestrator.sln
|-- src/
|   `-- HomelabOrchestrator/
|       |-- Authorization/
|       |-- Data/
|       |   `-- Migrations/
|       |-- Models/
|       |-- Options/
|       |-- Pages/
|       |   |-- Account/
|       |   |-- Provision/
|       |   |-- Runner/
|       |   |-- Reconcile/
|       |   |-- Maintenance/
|       |   |-- Playbooks/
|       |   `-- Executions/
|       |-- Services/
|       |   |-- Proxmox/
|       |   |-- Ansible/
|       |   `-- Jobs/
|       `-- wwwroot/
|-- ansible/
|   |-- ansible.cfg
|   |-- requirements.yml
|   |-- playbooks/
|   |   |-- apply-base.yml
|   |   |-- ssh-check.yml
|   |   |-- maintenance/
|   |   `-- catalog/
|   `-- roles/
|       `-- base/
|           |-- defaults/main.yml
|           |-- handlers/main.yml
|           |-- tasks/main.yml
|           `-- templates/
`-- tests/
    `-- HomelabOrchestrator.Tests/
```

## Initial infrastructure defaults

The proof of concept currently assumes:

| Setting | Default |
| --- | --- |
| Proxmox API port | `8006` |
| Proxmox node | `pve` |
| Template storage | `local` |
| Root filesystem storage | `local-lvm` |
| Network bridge | `vmbr0` |
| Container network | `10.0.150.0/16` |
| Gateway | `10.0.1.1` |
| DNS servers | `10.0.200.1`, `10.0.200.2` |
| LXC type | Unprivileged |
| LXC features | `nesting=1` |
| Managed tags | `base`, `managed-by-orchestrator` |
| Default CPU | 2 cores |
| Default memory | 2048 MB |
| Default swap | 512 MB |
| Default disk | 8 GB |

These are configuration defaults, not hard-coded business rules. They should be overridable through ASP.NET Core configuration.

The current IP convention maps the VMID to the final octet:

```text
VMID 102 -> 10.0.150.102
```

The application must reject VMIDs that cannot safely map to a usable final octet. A future address-allocation strategy may replace this convention.

## Configuration

Non-secret defaults belong in `appsettings.json` or environment-specific configuration:

```json
{
  "Proxmox": {
    "Host": "10.0.3.1",
    "Port": 8006,
    "Node": "pve",
    "TemplateStorage": "local",
    "RootfsStorage": "local-lvm",
    "Bridge": "vmbr0",
    "Gateway": "10.0.1.1",
    "NetworkPrefix": "10.0.150",
    "Subnet": 16,
    "NameServers": ["10.0.200.1", "10.0.200.2"],
    "ValidateCertificate": false
  },
  "Ssh": {
    "AuthorizedKeysPath": "/root/.ssh/authorized_keys",
    "OrchestratorPublicKeyPath": "/root/.ssh/id_ed25519.pub",
    "OrchestratorPrivateKeyPath": "/root/.ssh/id_ed25519",
    "RemoteUser": "root",
    "Port": 22
  },
  "Ansible": {
    "RepositoryRoot": "",
    "ExecutablePath": "ansible-playbook",
    "InventoryFile": "inventory/orchestrator.yml",
    "TimeoutSeconds": 600
  },
  "ConnectionStrings": {
    "Default": ""
  },
  "Admin": {
    "Username": "",
    "Password": ""
  }
}
```

`Ansible:RepositoryRoot` left blank (the default) resolves to the `ansible/` directory that ships
alongside `src/` and `tests/` in this repository; set it explicitly if a deployment lays out
files differently. `Ansible:InventoryFile` is resolved relative to `RepositoryRoot`.

`ConnectionStrings:Default` left blank resolves to a `homelab-orchestrator.db` SQLite file next to
the application binary; set it explicitly to store the database elsewhere. `Admin:Username` and
`Admin:Password` seed the single operator account the very first time the app starts with no
account yet in the database — see [Authentication](#authentication-implemented) — and are ignored
on every later boot, so it's safe to leave them in configuration or blank them out again.

Supply the Proxmox token through secrets or the process environment:

```bash
export Proxmox__ApiToken='automation@pve!homelab-orchestrator=TOKEN_SECRET'
```

Never commit real tokens, passwords, private SSH keys, generated inventory, or playbook extra-variable files.

## SSH key management

The orchestrator runs as root inside its own LXC, and provisioning uses that root account's own
SSH material — there is no SSH field anywhere in the web UI, and no key ever passes through the
browser or is stored on a job record.

- **Workstation keys** (yours and anyone else who should be able to log into new containers) go
  in `/root/.ssh/authorized_keys` on the orchestrator, one public key per line — exactly like any
  other machine's `authorized_keys`.
- **The orchestrator's own keypair** is generated once, on the orchestrator itself:

  ```bash
  ssh-keygen -t ed25519 -f /root/.ssh/id_ed25519 -N '' -C 'homelab-orchestrator'
  ```

  This is not automated: the application never generates, rotates, or copies this keypair itself.
  The public half (`/root/.ssh/id_ed25519.pub`) is combined with the workstation keys and copied
  into every new container. The private half (`/root/.ssh/id_ed25519`) never leaves the
  orchestrator — it is reserved for the future Ansible runner to connect back to containers as
  `Ssh:RemoteUser` (`root` by default) on `Ssh:Port` (`22` by default), and nothing in provisioning
  today reads it.
- Only the **combined public keys** — deduplicated, deterministic, newline-delimited — are sent to
  Proxmox's `ssh_public_keys` field when a container is created.
- Editing either source file only affects **containers created afterward**. Existing containers
  keep whatever keys they were built with; update `authorized_keys` on a running container the
  same way you would on any other Linux host.

## Dynamic Ansible inventory

### Inventory API (implemented)

The application exposes two read-only, unauthenticated JSON endpoints that generate a standard
Ansible dynamic-inventory document (the same `_meta`/`hostvars` shape `ansible-inventory --list`
and inventory scripts/plugins produce — see
[SSH key management](#ssh-key-management) for where `ansible_user`/`ansible_port`/
`ansible_ssh_private_key_file` come from). Each host's hostvars also include `tags`: Proxmox's
own semicolon-separated tag string (e.g. `base;managed-by-orchestrator;mqtt`), split, trimmed,
and returned as a JSON array — always present, empty (`[]`) rather than missing or `null` for an
untagged container.

```text
GET /api/inventory/containers/{vmid}   -> inventory containing just that one container
GET /api/inventory/containers/running  -> inventory containing every currently running container
```

```bash
curl http://localhost:5050/api/inventory/containers/141 > inventory.json
ansible-playbook -i inventory.json some-playbook.yml

curl http://localhost:5050/api/inventory/containers/running -o inventory.json
ansible-playbook -i inventory.json some-playbook.yml --limit web-01
```

Both endpoints read live from Proxmox (`GET /nodes/{node}/lxc`) and read each host's **actual**
configured address from its `net0` device (`IProxmoxService.GetContainerAddressAsync`) — nothing
is cached, read from a static file, or computed from the VMID-to-address convention. That
convention only ever predicts what a container's address *should* be (used by provisioning to
assign one, and by [Reconcile](#reconcile-pre-existing-containers-implemented) to check a
candidate); reporting it as fact here would mean Ansible silently connects to the wrong address
the moment a container's real address drifts from — or never matched — that prediction. A
container with no static address (DHCP/manual, or no `net0` at all) is excluded from
`/containers/running` (with a logged warning) and makes `/containers/{vmid}` fail with a clear
error rather than inventing an address for it. `/containers/{vmid}` returns 404 for a VMID
Proxmox doesn't know about and answers regardless of the container's power state;
`/containers/running` silently excludes anything not currently running. Neither endpoint requires
signing in — instead, both are restricted to loopback callers only (see
[Authentication](#authentication-implemented)), since `ansible-playbook` running on the
orchestrator itself is the only thing that ever needs to call
them.

These endpoints only generate inventory; they do not invoke `ansible-playbook` themselves — that
remains a manual step until the runner (Milestone 1's remaining items) exists.

### Inventory plugin (implemented)

`ansible/inventory_plugins/homelab_orchestrator.py` is a custom Ansible inventory plugin that
calls the two endpoints above directly, so `ansible-playbook`/`ansible-inventory` can use this
application as their inventory source with no intermediate file:

```yaml
# ansible/inventory/orchestrator.yml — every running container, grouped by tag
plugin: homelab_orchestrator
api_url: http://localhost:5050
mode: running
keyed_groups:
  - key: tags
    prefix: tag
```

```yaml
# ansible/inventory/orchestrator-single.yml — one container by VMID; the intended
# post-provision inventory source for apply-base.yml
plugin: homelab_orchestrator
api_url: http://localhost:5050
mode: vmid
vmid: 141
```

```bash
cd ansible
ansible-inventory -i inventory/orchestrator.yml --graph
ansible-playbook -i inventory/orchestrator-single.yml playbooks/apply-base.yml
```

`ansible/ansible.cfg` enables the plugin (`enable_plugins = homelab_orchestrator, auto, yaml,
ini`) and points `roles_path` at `ansible/roles` so playbooks under `ansible/playbooks/` can find
roles like `base`. The plugin supports the standard `compose`/`groups`/`keyed_groups`/`strict`
options (via Ansible's `constructed` fragment — that's how `orchestrator.yml` above turns `tags`
into `tag_base`/`tag_mqtt`/... groups) plus `validate_certs` and `timeout`. A parse failure —
Proxmox unreachable, an unknown VMID (HTTP 404), or a malformed response — raises
`AnsibleParserError` with a specific message; it never falls back to a silently empty inventory.

For the initial `base` run right after provisioning, use `orchestrator-single.yml` as shown
above (or, without the plugin, an inline host list works too:
`ansible-playbook -i '10.0.150.102,' -u root ansible/playbooks/apply-base.yml`). `base` itself is
currently a placeholder role (`ansible/roles/base/tasks/main.yml`) — it runs and does nothing
until its real tasks are decided.

For maintenance and catalog playbooks, the application should query Proxmox, filter eligible guests, and generate an inventory JSON document for that execution. Any temporary files must be written beneath:

```text
/run/homelab-orchestrator/<execution-id>/
```

The application must pass arguments through `ProcessStartInfo.ArgumentList`; do not construct a shell command by concatenating user input.

## Ansible Runner (implemented)

The Ansible Runner page (`/Runner`) lets an operator run any playbook committed directly under
`ansible/playbooks/` (not its subdirectories) against a target picked from a grouped list, with no
free-text command or path ever accepted from the browser:

- **Playbooks** are discovered by `PlaybookCatalog` (`Services/Ansible/PlaybookCatalog.cs`),
  which lists `*.yml`/`*.yaml` files directly under `Ansible:RepositoryRoot`/`playbooks` — one
  level only, no subdirectories — every time the page loads. Selecting a playbook only ever
  resolves back to a path this listing just produced; a name that doesn't match is rejected, and
  a defense-in-depth check also confirms the resolved path still sits under `playbooks/`. This is
  a plain listing, not the metadata-driven catalog with per-playbook input forms described under
  [Playbook catalog](#playbook-catalog) below — every playbook here runs with no extra variables.
- **Targets** are one of:
  - a specific container, chosen by hostname, from every *currently running* container;
  - a tag group — every currently running container carrying a given Proxmox tag;
  - all currently running containers (no `--limit` at all).

  Rather than one flat dropdown mixing containers and tags together, "Specific container" and
  "Tag group" are separate, individually collapsible `<details>` sections (plain HTML, no
  JavaScript) — each shows its own count and starts collapsed unless it already holds the
  current selection. The underlying encoding is unchanged (`RunFormModel.ParseTarget()` still
  reads a single `all` / `vm:<hostname>` / `tag:<tag>` value from one radio-button group), so
  this is a presentation-only change.

  The list is populated from a fresh `IProxmoxService.ListContainersAsync()` call on page
  load, but that choice is never trusted as still valid once the run actually starts: the
  background worker (`AnsibleRunWorker`) re-fetches running containers immediately before
  building the `ansible-playbook` command and fails the job — without starting a process — if
  the chosen container is no longer running or no running container still carries the chosen
  tag. A tag is translated to the same group name Ansible's own `keyed_groups` would produce
  (`AnsibleGroupName`, e.g. tag `mqtt` -> group `tag_mqtt`, matching the `tag` prefix configured
  in `inventory/orchestrator.yml`), so `--limit tag_mqtt` matches the live inventory plugin's
  groups.
- **Execution** goes through the same job-queue/background-worker pattern as provisioning
  (`Services/Jobs/AnsibleRunJob*`, `AnsibleRunWorker`): submitting a run enqueues a job and the
  page polls its status over HTMX until it reaches `Succeeded` or `Failed`. `AnsibleProcessRunner`
  is the only place that spawns `ansible-playbook`, via `ProcessStartInfo`/`ArgumentList` with
  `UseShellExecute = false` — `-i <Ansible:InventoryFile>`, an optional `--limit <target>`, then
  the resolved playbook path — and captures stdout/stderr line-by-line onto the job record.
  Captured output is rendered with plain Razor interpolation (auto HTML-encoded), never
  `Html.Raw`. A run that exceeds `Ansible:TimeoutSeconds` is killed (with its full process tree)
  and the job fails.
- **The SSH connectivity check** (`ansible/playbooks/ssh-check.yml`) is a safe, read-only
  playbook — `ansible.builtin.ping` followed by a debug message — suitable for verifying a
  container is reachable over SSH before running anything else against it; it makes no changes.

## Reconcile pre-existing containers (implemented)

Provisioning tags every container it creates `managed-by-orchestrator`, but that tag obviously
can't retroactively appear on containers created before this app existed. The Reconcile page
(`/Reconcile`) finds those and offers to adopt the ones it can verify, rather than either ignoring
them or guessing:

- Lists every container **not already tagged** `managed-by-orchestrator`, alongside the address
  the VMID convention expects it to have (`NetworkPrefix.VMID`) and the address it's **actually**
  configured with in Proxmox — read from its `net0` device
  (`IProxmoxService.GetContainerAddressAsync`), never assumed. A container on DHCP/manual
  addressing (no static `ip=`) has no actual address to compare, so it's shown but never eligible.
- A container is only **eligible** — and only gets a pre-checked checkbox — when its actual
  address already equals the expected one. A mismatch is shown, not hidden, so nothing is a
  silent surprise; it's just not selectable.
- Confirming re-verifies every selected container's address **again**, immediately before
  tagging it (`ContainerReconciliationService.AdoptAsync`) — the same "never trust a stale
  browser value" rule as provisioning and the Ansible Runner. A container that changed or a
  tampered request for a container that was never eligible is skipped, not tagged.
- Adoption only ever adds the `managed-by-orchestrator` tag — never `base`, since adopting a
  container doesn't mean the `base` role has actually been applied to it, and claiming so would
  mislead any future logic that trusts that tag.
- A VMID that falls outside `Proxmox:IpHostMin`/`IpHostMax` has no possible expected address
  under the convention at all, so it's left out of the list entirely rather than shown as a
  permanently-ineligible row.

## Authentication (implemented)

Provisioning, and now the Ansible Runner, can create containers and run playbooks against real
infrastructure, so the whole web UI requires signing in — there is deliberately no public
registration page, since this app is built for exactly one operator:

- **Every page requires a signed-in session** except `/Account/Login` — enforced by an
  `AuthorizeFolder("/")` Razor Pages convention in `Program.cs`, so a new top-level page is
  auth-gated automatically and never needs to opt in by hand.
- **Sign-in is backed by ASP.NET Core Identity** (`Microsoft.AspNetCore.Identity`) with a cookie
  scheme, and Identity's user store lives in SQLite via EF Core (`Data/ApplicationDbContext.cs`).
  Pending migrations are applied automatically at startup (`Database.MigrateAsync()` in
  `Program.cs`) — nothing manual is required to get the schema in place on first run.
- **The single operator account is seeded once, on first boot, from config** — `Admin:Username`
  and `Admin:Password` (see [Configuration](#configuration)) — and never touched again after that:
  once any account exists, those values are ignored on every later boot. There's no way to create
  a second account through the UI; if you need to reset a lost password, that's an operator task
  against the database, not something the app exposes.
- **Change your password** from the "Change password" link in the header once signed in
  (`/Account/ChangePassword`) — the seeded config password is meant to be rotated after first
  login, not used indefinitely.
- **The two `/api/inventory/...` endpoints are gated differently**: a `LocalhostOnly`
  authorization policy (`Authorization/LocalhostOnlyHandler.cs`) checks the request's actual TCP
  remote address and only allows loopback (`127.0.0.1`/`::1`) through, regardless of whether the
  caller is signed in. `ansible-playbook`, via the `homelab_orchestrator` inventory plugin, always
  runs on the orchestrator's own machine and calls `http://localhost:5050` — it never needs (and
  never gets) a login cookie. A rejected call gets a plain `403`, not a redirect to the login page.
- **The rest of the app still listens on `0.0.0.0`** — Kestrel isn't restricted to loopback, only
  those two specific endpoints are, via the policy above, independent of what address Kestrel is
  bound to.

## Playbook catalog

Approved one-off playbooks live under `ansible/playbooks/catalog`. A playbook may have a sidecar metadata document describing the UI form:

```text
install-postgresql.yml
install-postgresql.meta.yml
```

Example metadata:

```yaml
name: Install PostgreSQL
description: Install and configure PostgreSQL
category: Databases
targets:
  multiple: false
  required_tags:
    - base
inputs:
  - name: postgres_version
    label: PostgreSQL version
    type: select
    required: true
    default: "17"
    choices:
      - "16"
      - "17"
```

Input values should be serialized to a temporary JSON extra-vars file rather than interpolated into command-line strings.

## Development

Requirements:

- .NET 10 SDK
- network access to the Proxmox API
- a least-privilege Proxmox API token
- an orchestrator SSH keypair at `/root/.ssh/id_ed25519(.pub)` and workstation keys in
  `/root/.ssh/authorized_keys` (see [SSH key management](#ssh-key-management)) — provisioning
  runs without them, but new containers will have no key-based SSH access until they exist
- Ansible Core (`ansible-playbook`/`ansible-inventory`) and SSH access to provisioned
  containers — required to use the [Ansible Runner](#ansible-runner-implemented) page;
  provisioning itself still runs without it (Milestone 1's `base`/SSH-wait steps remain
  unimplemented)

Restore, build, and run:

```bash
dotnet restore
dotnet build --no-restore
dotnet test --no-build
dotnet run --project src/HomelabOrchestrator
```

The database schema (Identity's tables today) is created and upgraded automatically at startup —
nothing manual is required to run the app. Only creating a *new* migration after changing
`Data/ApplicationDbContext.cs` needs the EF Core tooling, pinned in this repo's local tool
manifest:

```bash
dotnet tool restore
dotnet tool run dotnet-ef migrations add <Name> --project src/HomelabOrchestrator --output-dir Data/Migrations
```

Validate the Ansible content (run from `ansible/` so its `ansible.cfg` — inventory plugin and
`roles_path` — is picked up):

```bash
cd ansible
ansible-playbook --syntax-check -i 'localhost,' playbooks/apply-base.yml
ansible-inventory -i inventory/orchestrator.yml --list
```

## Milestones

### Milestone 1: Provisioning parity

- [x] Connect through `Corsinvest.ProxmoxVE.Api`.
- [x] Display the next VMID, calculated address, and selected template.
- [x] Submit a provisioning job from a Razor Page.
- [x] Create and start a Debian 13 LXC.
- [x] Enable `nesting=1`.
- [x] Poll and display background-job status with HTMX.
- [ ] Wait for SSH.
- [ ] Apply the Ansible `base` role.
- [ ] Allow retrying the base stage without recreating the LXC.

The last three items need Ansible integration, which is a separate, larger piece of work
(see AGENTS.md's Ansible requirements) and has not been started.

### Milestone 2: Maintenance

- [ ] Discover managed LXCs from Proxmox.
- [ ] Select hosts by name or tag.
- [ ] Check and apply operating-system updates.
- [ ] Report failed services, disk usage, and reboot requirements.
- [ ] Reapply the `base` role.

### Milestone 3: Playbook catalog

- [ ] Discover approved playbooks and metadata.
- [ ] Generate validated forms from metadata.
- [ ] Select eligible targets.
- [ ] Run playbooks with temporary inventory and extra-vars files.
- [ ] Capture and display execution output.

### Milestone 4: Operations hardening

- [ ] Persist execution history in SQLite.
- [x] Add authentication and authorization — see [Authentication](#authentication-implemented).
      Scoped to a single seeded operator account with no self-registration; a multi-user/RBAC
      model was deliberately not built, since this app is meant for one operator.
- [ ] Add cancellation and safe retry behavior.
- [ ] Add retention rules for execution output.
- [ ] Package as a native systemd service.
- [ ] Add health checks and structured logging.

## Safety principles

- Preview destructive or disruptive operations before execution.
- Require explicit confirmation before creating, deleting, stopping, or rebooting a guest.
- Never expose API tokens, passwords, SSH private keys, or secret Ansible variables in logs or HTML.
- Never accept arbitrary shell commands from the browser.
- Never accept arbitrary playbook paths from the browser.
- Recalculate VMID and IP immediately before creation; do not trust a stale browser preview.
- Never accept an SSH key from the browser; source public keys only from the orchestrator's own
  filesystem, and never generate, rotate, or copy the orchestrator's private key automatically.
- Require sign-in for every page except the login page itself; never add a new top-level page
  that opts out of that convention.
- Scope any endpoint meant only for same-machine callers (like the Ansible inventory API) to
  loopback via the `LocalhostOnly` authorization policy, rather than assuming network placement
  will keep it safe.
- Prefer idempotent Ansible roles and playbooks.
- Preserve a created LXC when a later configuration stage fails, and make that stage retryable.

## License

Choose and add a license before publishing the project for broader reuse.
