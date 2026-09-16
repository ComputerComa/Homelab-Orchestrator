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

Outside the web UI, two unauthenticated JSON endpoints generate a standard Ansible dynamic
inventory for a single container or for every currently running one, and a matching custom
Ansible inventory plugin (`ansible/inventory_plugins/homelab_orchestrator.py`) lets
`ansible-playbook`/`ansible-inventory` consume them directly — see
[Inventory API](#inventory-api-implemented) and [Inventory plugin](#inventory-plugin-implemented).
This is a first, narrow slice of the "discover hosts from Proxmox and generate inventory"
capability Maintenance will eventually need; nothing here runs `ansible-playbook` automatically.

See [Milestone 1](#milestone-1-provisioning-parity) below for the exact checklist.

Maintenance, the playbook catalog, and execution history (Milestones 2-4) are design-only —
described under [Planned interface](#planned-interface) but not yet implemented.

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
|       |-- Models/
|       |-- Options/
|       |-- Pages/
|       |   |-- Provision/
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
  }
}
```

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

Both endpoints read live from Proxmox (`GET /nodes/{node}/lxc`) and derive each host's address
from its VMID using the same convention provisioning uses — nothing is cached or read from a
static file. `/containers/{vmid}` returns 404 for a VMID Proxmox doesn't know about and answers
regardless of the container's power state; `/containers/running` silently excludes anything not
currently running. Neither endpoint requires authentication yet — do not expose this port beyond
a trusted network until [authentication](#milestones) is added.

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
- Ansible Core, SSH access to provisioned containers — only once Ansible integration
  (Milestone 1's remaining items) is implemented; not required to run what exists today

Restore, build, and run:

```bash
dotnet restore
dotnet build --no-restore
dotnet test --no-build
dotnet run --project src/HomelabOrchestrator
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
- [ ] Add authentication and authorization.
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
- Prefer idempotent Ansible roles and playbooks.
- Preserve a created LXC when a later configuration stage fails, and make that stage retryable.

## License

Choose and add a license before publishing the project for broader reuse.
