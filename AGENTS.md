# AGENTS.md

This file defines the working rules for coding agents contributing to Homelab Orchestrator. It applies to the entire repository unless a more specific `AGENTS.md` exists in a subdirectory.

## Start here

Before changing code:

1. Read `README.md` completely.
2. Inspect the existing repository structure and current changes.
3. Read any more-specific `AGENTS.md` governing the files being changed.
4. Preserve user changes and avoid unrelated rewrites.
5. State significant assumptions when requirements are ambiguous.

## Product intent

Homelab Orchestrator provides an appliance-like interface for:

- provisioning Proxmox LXC containers;
- applying a standard Ansible `base` role;
- performing routine maintenance;
- running approved one-off Ansible playbooks.

Keep the application focused. It is not intended to become a general-purpose replacement for Proxmox, Ansible Automation Platform, or a shell terminal in a browser.

## Fixed architectural decisions

Do not change these without an explicit request:

- Use .NET 10 and ASP.NET Core.
- Use Razor Pages with HTMX for the web interface.
- Use `Corsinvest.ProxmoxVE.Api` as the Proxmox API wrapper.
- Use background services for provisioning and Ansible executions.
- Use Ansible Core as an external native process.
- Prefer native Linux deployment with systemd; do not introduce Docker.
- Treat Proxmox as the source of truth for guest inventory and state.
- Do not introduce OpenTofu or Terraform.
- Do not add static managed host inventory to Git.
- The standard Ansible role is named `base`, never `baseline`.
- Keep secrets outside source control.
- Source SSH public keys from the orchestrator's own filesystem
  (`/root/.ssh/authorized_keys` and `/root/.ssh/id_ed25519.pub`), never from a browser field
  or a job record; never generate, rotate, or copy the orchestrator's private key
  automatically. See [SSH key management](#ssh-key-management).

## Boundaries

Maintain clear separation between:

- **Pages:** HTTP binding, validation, and view models only.
- **Application services:** orchestration and use-case logic.
- **Proxmox services:** all calls to `Corsinvest.ProxmoxVE.Api`.
- **Ansible services:** inventory generation, process execution, and output parsing.
- **Job services:** queueing, state transitions, cancellation, and persistence.

Razor Page models must not call Proxmox directly, launch processes, or contain infrastructure rules.

Wrap the third-party Proxmox client behind an application-owned interface such as `IProxmoxService`. Do not allow dynamic response objects from the wrapper to leak into page models or domain models. Translate responses into strongly typed application records at the boundary.

## Provisioning workflow

The expected initial workflow is:

1. Validate operator input.
2. Retrieve the next VMID from Proxmox.
3. calculate the IP address using configured policy.
4. Find the newest downloaded Debian 13 LXC template.
5. Create an unprivileged LXC with `nesting=1`.
6. Configure storage, CPU, memory, swap, network, DNS, tags, and the combined SSH public keys
   from `ISshPublicKeyProvider` (see [SSH key management](#ssh-key-management)).
7. Wait for the Proxmox task to finish.
8. Wait for SSH to become available.
9. Run `ansible/playbooks/apply-base.yml` against the new address.
10. Record and display the outcome of every stage.

Do not treat a browser preview of VMID or IP address as reserved. Recalculate them inside the serialized provisioning worker immediately before creation.

If LXC creation succeeds but Ansible fails, preserve the LXC. Mark the Ansible stage failed and make it independently retryable.

## Proxmox requirements

- Use a least-privilege API token supplied through configuration.
- Never log the token or include it in an exception presented to the browser.
- Certificate validation is configurable. Defaulting it off may be acceptable for initial homelab development, but do not silently disable it in code.
- Use the configured node, storage, bridge, network, gateway, and DNS settings.
- Do not hard-code environment-specific values outside configuration defaults.
- Check `Result.IsSuccessStatusCode` for every API operation.
- Include useful sanitized errors when an API operation fails.
- Poll long-running Proxmox tasks from a background worker with a timeout and cancellation token.
- Serialize allocation/creation or otherwise prevent two simultaneous jobs from receiving the same VMID or calculated address.

Default LXC configuration currently includes:

```text
unprivileged=1
features=nesting=1
tags=base;managed-by-orchestrator
```

## SSH key management

The orchestrator runs as root in its own LXC and uses that root account's own SSH material —
there is no SSH input anywhere in the web UI.

- `SshOptions` (bound from the `Ssh` configuration section) holds `AuthorizedKeysPath`
  (default `/root/.ssh/authorized_keys`), `OrchestratorPublicKeyPath`
  (default `/root/.ssh/id_ed25519.pub`), `OrchestratorPrivateKeyPath`
  (default `/root/.ssh/id_ed25519`), `RemoteUser` (default `root`), and `Port` (default `22`).
- `ISshPublicKeyProvider` asynchronously reads `AuthorizedKeysPath` and
  `OrchestratorPublicKeyPath`, ignores blank and comment-only lines, validates normal OpenSSH
  public-key records, deduplicates identical keys by key type and encoded key data, and returns
  deterministic newline-delimited text. It never reads, returns, or logs
  `OrchestratorPrivateKeyPath`.
- `ProvisioningWorker` calls the provider immediately before creating the container — the same
  "never trust a stale value" rule that applies to VMID/address/template applies here: SSH keys
  are never carried on `ProvisioningRequest`, `ContainerFormModel`, or a job record.
- `OrchestratorPrivateKeyPath` is reserved for the future Ansible runner to connect back to
  provisioned containers as `RemoteUser` on `Port`. Nothing in provisioning today reads it; do
  not wire it up before the Ansible runner exists.
- Never generate an SSH keypair automatically, copy the private key into a container, add an
  SSH key field to any form or API response, or store key material on a job record.
- Changing `AuthorizedKeysPath` or the orchestrator's own key files only affects containers
  created afterward; do not try to retroactively update already-provisioned containers.

## Ansible requirements

- Store roles, approved playbooks, metadata, and dependency declarations in Git.
- Do not store a list of managed hosts in Git.
- For a newly provisioned host, use an inline inventory or an execution-scoped generated inventory.
- For maintenance, discover hosts from Proxmox and generate inventory for the execution.
  `IAnsibleInventoryService` (`Services/Ansible/`) already does this — reuse it rather than
  writing a second inventory generator. It composes `IProxmoxService.ListContainersAsync` with
  the VMID-to-address convention and `SshOptions`, and is also exposed read-only at
  `GET /api/inventory/containers/{vmid}` and `GET /api/inventory/containers/running`
  (`Endpoints/InventoryEndpoints.cs`) for external tooling. Those two endpoints are
  unauthenticated; do not add a third without also covering it under
  [authentication](#security) once that exists.
- Write temporary inventory and extra-vars files beneath `/run/homelab-orchestrator/<execution-id>/`.
- Apply restrictive file permissions to execution directories and remove them after the configured retention period.
- Invoke `ansible-playbook` directly with `ProcessStartInfo`.
- Set `UseShellExecute = false`.
- Put every argument in `ProcessStartInfo.ArgumentList`.
- Never build shell command strings using user input.
- Serialize extra variables to JSON files instead of interpolating them into arguments.
- Capture standard output and standard error asynchronously.
- Preserve ANSI-free output suitable for the web interface.
- Treat nonzero exit codes as failures and retain useful output for diagnosis.
- Make roles and playbooks idempotent whenever possible.

The web playbook runner must only execute playbooks discovered beneath the configured approved catalog directory. Resolve and validate canonical paths to prevent traversal. Never expose a browser field for an arbitrary command or arbitrary filesystem path.

## Jobs and concurrency

- HTTP requests enqueue work and return quickly.
- Long-running operations execute in hosted background services.
- Use explicit job states and stage states rather than a single free-form status string.
- Support safe polling from HTMX.
- Carry cancellation tokens through application-owned asynchronous APIs.
- Use bounded queues or another form of backpressure before production use.
- Provisioning allocation must be serialized unless a proper reservation mechanism is implemented.
- Do not use unobserved `Task.Run` calls for durable operations.
- A process restart may interrupt in-memory jobs; document this until persistent job recovery exists.

Suggested provisioning stages:

```text
Queued
Allocating
CreatingContainer
WaitingForProxmox
WaitingForSsh
ApplyingBase
Succeeded
Failed
Cancelled
```

## Security

- Never commit or print real credentials.
- Never return secrets in API responses, HTML, validation messages, or execution logs.
- Never read, return, or log the orchestrator's private key (`Ssh:OrchestratorPrivateKeyPath`); only its public half is ever combined and sent to Proxmox.
- Do not place secret values in process arguments when a protected file or environment variable is supported.
- Apply authentication before treating the application as production-ready.
- Require explicit confirmation for destructive and disruptive actions.
- Apply anti-forgery protection to state-changing browser requests.
- Validate all submitted values server-side even when the browser validates them.
- Use allowlists for playbooks, actions, tags, and configurable choices where possible.
- Avoid rendering raw Ansible or Proxmox output as HTML.
- Encode output before displaying it.

## C# conventions

- Enable nullable reference types and implicit usings.
- Prefer file-scoped namespaces.
- Use dependency injection and the options pattern.
- Use strongly typed records or classes at integration boundaries.
- Keep methods focused and names explicit.
- Use async APIs for network, filesystem, and process operations.
- Include `Async` on asynchronous method names.
- Accept a `CancellationToken` on application-owned asynchronous methods.
- Do not add abstractions that have only hypothetical value, but preserve the major integration boundaries.
- Log with structured message templates.
- Do not catch `Exception` unless adding useful context, updating job state, or handling it at an application boundary.

## HTMX and UI conventions

- The application remains functional and understandable as server-rendered HTML.
- Use HTMX for partial replacement, polling, and small interactions.
- Do not introduce a SPA framework without an explicit request.
- Page actions must show pending, successful, and failed states clearly.
- Disable or guard duplicate submissions.
- Show the actual target host and operation before disruptive actions.
- Keep provisioning fields minimal; infrastructure defaults belong in configuration.
- Do not expose secret configuration values in forms.

## Tests

Add tests alongside meaningful behavior. Prioritize:

- hostname validation;
- VMID-to-IP calculation and invalid ranges;
- Debian template selection;
- Proxmox response translation;
- SSH public-key parsing, deduplication, and deterministic combined output;
- provisioning state transitions;
- inventory generation;
- playbook-path allowlisting and traversal rejection;
- Ansible argument construction;
- secret redaction;
- retry behavior after a post-creation Ansible failure.

Use test doubles at the `IProxmoxService`, Ansible runner, and process boundaries. Do not require a live Proxmox server for the ordinary test suite.

## Validation before handoff

Run the relevant subset, and preferably all, of:

```bash
dotnet restore
dotnet build --no-restore
dotnet test --no-build
dotnet format --verify-no-changes
ansible-playbook --syntax-check -i 'localhost,' ansible/playbooks/apply-base.yml
```

If a required tool is unavailable, report exactly which validation was not run. Never state that the project compiled or tests passed unless those commands completed successfully.

For changes involving the Proxmox wrapper, verify method signatures against the installed `Corsinvest.ProxmoxVE.Api` package version rather than relying on examples from another major version.

## Documentation

Update `README.md` when a change affects:

- setup;
- configuration;
- architecture;
- supported workflows;
- security expectations;
- operator-visible behavior.

Do not put secrets or real token values in examples. Keep examples consistent with the role name `base` and the no-static-inventory design.

## Change discipline

- Make the smallest cohesive change that completes the requested behavior.
- Do not reformat unrelated files.
- Do not delete or overwrite user changes.
- Do not make infrastructure mutations merely to test application code.
- Do not create, destroy, stop, or reboot a real guest unless explicitly authorized.
- Do not push commits, create releases, or alter external systems unless explicitly requested.
- Explain any remaining operational or security limitation at handoff.
