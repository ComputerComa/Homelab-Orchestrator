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
every running container — and confirming navigates to that run's own page, which streams the
captured output back live.

A third page, [Job history](#job-history-implemented), lists every past (and in-flight) Ansible
run, persisted in SQLite, and links to each one's own live-or-historical detail page.

A fourth page, [Reconcile](#reconcile-pre-existing-containers-implemented), finds containers that
predate the orchestrator (not tagged `managed-by-orchestrator`) and offers to adopt the ones whose
actual configured address already matches what the VMID convention expects — never guessing for
the ones that don't. A top navigation bar (Provision / Ansible Runner / Job History / Reconcile)
switches between all four pages.

The whole web UI requires signing in as the single seeded operator account, and the two
inventory endpoints above are restricted to loopback callers — see
[Authentication](#authentication-implemented) for both.

See [Milestone 1](#milestone-1-provisioning-parity) below for the exact checklist.

Maintenance (Milestone 2) is design-only — described under [Planned interface](#planned-interface)
but not yet implemented. The playbook catalog (Milestone 3) is partially implemented by the
Ansible Runner above; see that section for what's still missing (per-playbook metadata/forms).
Ansible run execution history, one item of Milestone 4, is now persisted in SQLite — see
[Job history](#job-history-implemented); the rest of that milestone (cancellation/retry, retention,
systemd packaging, health checks) remains unimplemented.

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

Ansible playbook runs already have this, persisted in SQLite — see
[Job history](#job-history-implemented), including a **Run again** action that resubmits the same
request as a new execution. Provisioning runs aren't included yet, and have no such action.

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
  `-- SQLite execution history (Ansible runs)
```

### Technology choices

- .NET 10
- ASP.NET Core Razor Pages
- HTMX
- `Corsinvest.ProxmoxVE.Api`
- hosted background services and channels
- Ansible Core
- SQLite for Ansible run execution history
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
`/containers/running` silently excludes anything not currently running — and also excludes the
orchestrator's own container (`OrchestratorSelfFilter`, matched by comparing each container's
Proxmox hostname against `Environment.MachineName`). The orchestrator usually runs as an LXC on
the same node it manages, so without this exclusion it would show up in "every running
container," and any playbook run against all of them — including the SSH connectivity check —
would end up trying to connect back to itself. An explicit `/containers/{vmid}` lookup by the
orchestrator's own VMID is not affected by this; only the "give me everything" listing excludes
it. Neither endpoint requires signing in — instead, both are restricted to loopback callers only
(see [Authentication](#authentication-implemented)), since `ansible-playbook` running on the
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

  Picking one happens in a modal (a native `<dialog>`, opened/closed by a small amount of vanilla
  JS in `wwwroot/js/runner.js` — still no JS framework) rather than a flat `<select>`, so it holds
  up as the number of playbooks grows: a search box filters a compact, independently-scrolling
  list of names on the left, and clicking one shows its full **parsed name, description, and
  steps** in a detail pane on the right without resizing the modal. That parsing is
  `PlaybookMetadataParser` (`Services/Ansible/PlaybookMetadataParser.cs`) plus
  `PlaybookCatalog.GetDetailAsync` — a line-oriented, best-effort reader of the playbook's own
  YAML (no full YAML parser, mirroring `AnsibleOutputParser`'s style): a play's first `name:` line
  becomes the description, and every later `name:` line becomes a step — including, for a
  playbook that only has `roles: [...]` and no inline `tasks:` (like `apply-base.yml`), each
  referenced role's own `tasks/main.yml`, resolved and parsed the same way. All playbooks' details
  are loaded up front when the page loads, since there are only ever a handful.
- **Targets** are chosen from a checkbox list of every *currently running* container (excluding
  the orchestrator's own — see below), filterable by tag via a dropdown above the list. Two quick
  actions populate it without touching a single checkbox — "Select all running" and, once a tag is
  picked in the filter, "Select" next to it — and stay **live**: submitting without further manual
  changes still submits as `all` or `tag:<tag>`, re-evaluated by the worker against whatever is
  actually running immediately before the run starts, exactly as before. Checking or unchecking
  even one box locks the target into a frozen, ad hoc hostname list instead (`AnsibleRunTargetKind.Selection`,
  encoded as `RunFormModel.Target = "selection:<host1>,<host2>,..."`, resolved by
  `AnsibleRunWorker.ResolveLimitAsync` into a comma-joined `--limit` — Ansible's own flag already
  accepts a host list this way) — every hostname in it is still re-validated as currently running
  immediately before the run starts, the same "never trust a stale browser value" rule as the
  other two kinds; any that no longer is fails the job, without starting a process, naming exactly
  which one(s).

  Every "running containers" listing this page and its worker use — the checkbox list itself,
  target validation immediately before a run, and the underlying inventory "All" resolves
  against — excludes the orchestrator's own container (`OrchestratorSelfFilter`). It typically
  runs as an LXC on the same node it manages, so without this exclusion it would be offered (and,
  for "All", silently included) as a target for every playbook, including the SSH connectivity
  check trying to connect back to itself.

  The list is populated from a fresh `IProxmoxService.ListContainersAsync()` call on page load
  (`RunTargetOptions.RunningContainers`, each with its own tags, for the checkbox rows and the tag
  filter), but that choice is never trusted as still valid once the run actually starts: the
  background worker (`AnsibleRunWorker`) re-fetches running containers immediately before building
  the `ansible-playbook` command and fails the job — without starting a process — if a chosen
  container is no longer running or no running container still carries a chosen tag. A tag is
  translated to the same group name Ansible's own `keyed_groups` would produce (`AnsibleGroupName`,
  e.g. tag `mqtt` -> group `tag_mqtt`, matching the `tag` prefix configured in
  `inventory/orchestrator.yml`), so `--limit tag_mqtt` matches the live inventory plugin's groups.
- **Execution** goes through the same job-queue/background-worker pattern as provisioning
  (`Services/Jobs/AnsibleRunJob*`, `AnsibleRunWorker`): submitting a run enqueues a job and
  confirming it navigates the browser straight to that run's page under
  [Job history](#job-history-implemented) (`/Executions/{id}`) — watching a run and launching one
  are two separate screens, rather than one growing page. `AnsibleProcessRunner` is the only place
  that spawns `ansible-playbook`, via `ProcessStartInfo`/`ArgumentList` with
  `UseShellExecute = false` — `-i <Ansible:InventoryFile>`, an optional `--limit <target>`, then
  the resolved playbook path — and captures stdout/stderr line-by-line, tagged with which stream
  each line came from. A run that exceeds `Ansible:TimeoutSeconds` is killed (with its full process
  tree) and the job is marked `TimedOut`.
- **Output is captured durably and shown structured, not as one flat scrolling stream that grows a
  single in-memory string.** See [Job history](#job-history-implemented) for how output is
  persisted; `AnsibleOutputParser` (`Services/Ansible/AnsibleOutputParser.cs`) turns that
  reconstructed text into both a host-centric view (a collapsible section per host, `<details>`,
  no JavaScript, collapsed by default and auto-expanded only when that host has a failed or
  unreachable step) and a task-centric one (one row per task, aggregating every host's result for
  it, with a failed row visible without expanding anything else) from the same underlying data.
  Each step shows a spinner while still in progress, a check once it completes, or an X on
  failure/unreachable, plus any `msg` text a task reported or, for a looped task, which item that
  result was for. Once `ansible-playbook` prints its `PLAY RECAP`, a summary table
  (Ok/Changed/Unreachable/Failed/Skipped per host) appears too. This is a best-effort parser of
  Ansible's own *default* plain-text stdout — not a custom callback plugin — so an output shape it
  doesn't recognize simply isn't broken out into steps; it's never lost, since the full raw output
  is still available in its own tab.
- **The SSH connectivity check** (`ansible/playbooks/ssh-check.yml`) is a safe, read-only
  playbook — `ansible.builtin.ping` followed by a debug message — suitable for verifying a
  container is reachable over SSH before running anything else against it; it makes no changes.

## Job history (implemented)

Ansible run history — both the execution record and its full captured output — is persisted in
SQLite (via the same `ApplicationDbContext` Identity uses — one `DbContext`, not a second store)
rather than only living in memory for the process's lifetime, and is browsable independently of
launching a new run:

- **`/Executions`** lists every run, most recent first (capped at the 100 most recent — a fixed
  cap, not real pagination, matching this app's homelab scale elsewhere), with its playbook,
  target, a status badge, duration, and exit code. Each row links to that run's own page.
- **`/Executions/{id}`** is a wide, Rundeck/Semaphore-style execution page: a compact header
  (playbook, status badge, requested target, resolved hosts, submitting user, submitted/started/
  completed timestamps, duration, exit code, a sanitized error if any, **Run again** and
  **Download log** actions), summary counts (hosts/tasks/changed/failed/unreachable), and three
  tabs — **Tasks** (default), **Hosts**, and **Raw Output** (a fixed-height console with search,
  line-wrap toggle, copy, and download). It transparently renders either an in-flight run (from the
  in-memory `IAnsibleRunJobStore`) or an already-finished one loaded from SQLite, including after a
  restart — `IAnsibleRunnerService.GetJobOrHistoryAsync` checks the in-memory store first and only
  falls back to persisted history when a run isn't found there. **Run again** resubmits the exact
  same request (same playbook, same target) as a brand-new execution and navigates to its page,
  rather than resubmitting a raw form.

  While a run is in flight, the header, the Tasks panel, the Hosts panel, and the console each poll
  their own small HTMX fragment independently — never the whole page — so selected tab, the
  "only failed" filter, and the console's scroll position all survive every poll. The console
  specifically *appends* new lines rather than replacing itself, via a cursor
  (`?handler=LogTail&after=<sequence>`) that only ever fetches entries newer than what's already
  shown. Every fragment stops polling once the run reaches a terminal stage
  (`Succeeded`/`Failed`/`TimedOut`/`Cancelled`/`Interrupted`).
- **What's persisted, and when:** `IAnsibleExecutionStore`/`EfAnsibleExecutionStore`
  (`Services/Ansible/`) save the execution row a small, bounded number of times — when it's queued,
  when it starts running, and once more at its terminal state — never per captured output line.
  Captured output is a separate, append-only `AnsibleExecutionLogs` table
  (`IAnsibleExecutionLogStore`/`EfAnsibleExecutionLogStore`), written in batches (roughly every 40
  lines or 300ms, whichever first) by `AnsibleExecutionLogBuffer` rather than one row per line, and
  guaranteed to flush whatever's left when a run ends. The raw-output tab and the download both read
  through `IAnsibleRunnerService.GetReconstructedOutputAsync`, which transparently serves the live
  in-memory buffer while a run is in flight, the log table once it's finished, or (for a run
  persisted before this table existed) the legacy output column as a fallback. Retention/pruning of
  old history isn't implemented yet (an unbounded SQLite table); see Milestone 4 below.
- **Lifecycle**: beyond `Queued`/`Running`/`Succeeded`/`Failed`, a run can end `TimedOut` (killed
  for exceeding `Ansible:TimeoutSeconds`) or `Interrupted` (the orchestrator process stopped mid-run
  — either shut down while the run was active, or, if the process was already stopped, a run left
  `Queued`/`Running` in the database is swept to `Interrupted` at the next startup, so its page
  doesn't poll forever). `Cancelled` exists in the model but has no way to be triggered yet — there
  is no cancel action in this app. A failure to persist a run's history is always distinct from a
  failure of the playbook itself: saving history failing never changes an already-determined
  `Succeeded`/`Failed` outcome, and never stops the worker from picking up the next queued run.
- The Ansible Runner page (`/Runner`) itself now only launches a run — submitting one navigates
  the browser to its `/Executions/{id}` page rather than growing content on `/Runner` — plus shows
  a small "Recent runs" teaser linking into the full history.

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

- [x] Persist execution history in SQLite — see [Job history](#job-history-implemented).
- [x] Add authentication and authorization — see [Authentication](#authentication-implemented).
      Scoped to a single seeded operator account with no self-registration; a multi-user/RBAC
      model was deliberately not built, since this app is meant for one operator.
- [ ] Add cancellation and safe retry behavior. `AnsibleRunStage.Cancelled` and the
      `TimedOut`/`Interrupted` terminal stages are modeled and exhaustively handled (see
      [Job history](#job-history-implemented)), but there is still no operator-facing way to cancel
      an in-flight run, and "safe retry" today is only **Run again** (a brand-new execution of the
      same request), not a retry of the same run.
- [ ] Add retention rules for execution output. Execution rows and their per-line output
      (`AnsibleExecutionLogs`) are both durably persisted (see
      [Job history](#job-history-implemented)) but never pruned — both tables grow without bound.
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
