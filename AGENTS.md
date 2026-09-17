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
- Use EF Core with SQLite for both ASP.NET Core Identity's user store and persisted Ansible run
  execution history — one `DbContext` (`ApplicationDbContext`), not a separate store per concern.
- The app supports exactly one operator account, seeded once from config with no public
  registration page. Do not build multi-user or role-based access control without an explicit
  request. See [Authentication](#authentication).

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

## Container reconciliation

`ContainerReconciliationService` (`Services/Provisioning/`, backing `/Reconcile`) adopts
containers that predate the orchestrator by tagging them `managed-by-orchestrator` — but only
when it can verify they already fit the VMID/address convention, never by assuming it.

- `IProxmoxService.GetContainerAddressAsync` reads the container's actual configured address from
  its `net0` device. Never derive the "actual" address from the VMID convention — that's the
  "expected" side of the comparison, not a substitute for reading real Proxmox state.
- `ListCandidatesAsync` only lists containers not already tagged `managed-by-orchestrator`, and
  marks one eligible only when its actual address equals the VMID-convention-expected address.
  Show a mismatch or a DHCP/no-address container as ineligible; never hide it or silently guess.
- `AdoptAsync` re-reads the address again, fresh, for each container immediately before tagging
  it — the same "never trust a stale browser value" rule as provisioning and the Ansible Runner.
  A container whose address changed, or a request for one that was never eligible, must be
  skipped, not tagged.
- `IProxmoxService.AddTagAsync` adds exactly one tag and nothing else. Adoption must never add
  `base` — that tag means the `base` role has actually been applied, which adoption does not
  guarantee, and any future maintenance logic may trust it.
- A VMID outside `Proxmox:IpHostMin`/`IpHostMax` has no possible expected address under the
  convention; leave it out of the candidate list rather than showing a row with nothing to compare.

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
  each host's **actual** address from `IProxmoxService.GetContainerAddressAsync` and `SshOptions`,
  and is also exposed read-only at `GET /api/inventory/containers/{vmid}` and
  `GET /api/inventory/containers/running` (`Endpoints/InventoryEndpoints.cs`) for external
  tooling. Never use the VMID-to-address convention (`IpAddressCalculator`) here — it only
  predicts what a container's address should be, for provisioning to assign and Reconcile to
  check against; reporting a prediction as inventory fact means Ansible silently connects to the
  wrong address the moment reality drifts from it. A container with no static address is excluded
  (list) or fails clearly (single-container), never assigned a guessed one. Those two endpoints
  don't require sign-in; they're gated by the `LocalhostOnly` policy instead — see
  [Authentication](#authentication). Apply that same policy to a third endpoint if it's ever
  added for a same-machine-only use case; don't leave it open by default.
- The orchestrator usually runs as an LXC on the same Proxmox node it manages, so it appears in
  `IProxmoxService.ListContainersAsync()` like any other guest. `OrchestratorSelfFilter.IsSelf`
  (`Services/Ansible/`, matches a container's Proxmox hostname against `Environment.MachineName`)
  excludes it from every "running containers" collection used for Ansible targeting: the
  running-containers inventory endpoint, the Runner page's target list, and the worker's
  vm/tag-group validation immediately before a run. Apply this filter at every new call site that
  builds a "give me all running containers to target" list — otherwise a playbook run against
  "all" (or a tag the orchestrator happens to share) will try to connect back to itself. It does
  not apply to an explicit single-VMID inventory lookup; that stays a deliberate, honored request.
- `ansible/inventory_plugins/homelab_orchestrator.py` consumes those same two endpoints from the
  Ansible side (enabled via `ansible/ansible.cfg`'s `enable_plugins`). Extend this plugin — don't
  add a second one — if another endpoint or hostvar needs to reach Ansible inventory.
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

The Ansible Runner page (`/Runner`, `Pages/Runner/`) implements this for `ansible/playbooks/`
(top-level files only): `PlaybookCatalog` (`Services/Ansible/PlaybookCatalog.cs`) is the only
component that lists playbooks and resolves a name to a path, and it only ever resolves a name
to a path it just discovered itself — extend it rather than adding a second lookup if playbook
discovery needs to change. Its run targets — a single running container, a Proxmox-tag group, all
running containers, or an ad hoc checked set of them (`AnsibleRunTargetKind.Selection`, a
comma-joined hostname list in `TargetValue`) — are re-resolved against live Proxmox state inside
`AnsibleRunWorker.ResolveLimitAsync` immediately before building the `ansible-playbook` command —
the same "never trust a stale browser value" rule as VMID/address/template — never inside the page
model. Tag targets are translated to Ansible group names with `AnsibleGroupName`, which must keep
matching Ansible's own `keyed_groups` sanitization (`[^A-Za-z0-9_]` -> `_`) so `--limit` matches
the live inventory plugin's groups. `Selection` re-validates every listed hostname is still
running and fails naming exactly which one(s) aren't, then joins the rest with a comma for
`--limit` (Ansible's own flag accepts a host list this way) — never drop hosts silently to
produce a smaller-than-requested run.

The Runner page's checkbox-based target picker distinguishes "live" from "frozen" client-side: a
quick action ("Select all running", or "Select" next to the tag filter once a tag is picked) sets
`RunFormModel.Target` to the same `all`/`tag:<tag>` encoding as before and is meant to stay that
way — re-evaluated at run time, not a snapshot — right up until the operator manually
checks/unchecks any individual box, which locks it into `selection:<host1>,<host2>,...` from then
on. This state lives entirely in `wwwroot/js/runner.js` (vanilla JS, no framework, consistent with
the HTMX-only architectural decision) and is invisible to the page model — it only ever sees
whatever single encoded string ends up in the hidden `Form.Target` input at submit time, exactly
like the pre-existing `all`/`vm:`/`tag:` encodings. Don't add a distinct persisted "mode" field
anywhere server-side; the encoding itself is the only state that needs to round-trip.

`PlaybookMetadataParser` (`Services/Ansible/PlaybookMetadataParser.cs`) plus
`PlaybookCatalog.GetDetailAsync` parse a playbook's own YAML into the name/description/steps shown
in the Runner page's playbook-picker modal — a line-oriented, best-effort parser (no YAML library),
mirroring `AnsibleOutputParser`'s style: pure, stateless, testable against plain strings. The
play's first `name:` line is the description; every later `name:` line is a step, including a
referenced role's own `tasks/main.yml` when the playbook only has `roles: [...]` (e.g.
`apply-base.yml`) rather than inline `tasks:` — resolving that is `PlaybookCatalog`'s job (it reads
the role's file and hands its content to the parser), not the parser's; the parser itself never
touches the filesystem.

`AnsibleOutputParser` (`Services/Ansible/AnsibleOutputParser.cs`) turns a job's captured
`ansible-playbook` stdout/stderr into the per-host, per-task view `_RunJobStatus.cshtml` renders.
It is pure and stateless — `string -> AnsibleRunParsedState`, called fresh on every render (every
~1s poll while a job is running, once more on its terminal render) — and is never persisted onto
`AnsibleRunJob` or wired into `AnsibleRunWorker`/`AnsibleProcessRunner`/the job store; the parsed
view is a read-time projection of the same `Output` string the job already accumulates. It's a
best-effort regex parser of Ansible's own default ("linear" strategy) plain-text callback output,
not a custom callback plugin — an unrecognized line is ignored rather than breaking the parse, and
the full raw output stays available underneath as a fallback. A host's synthetic `Running`
placeholder step is only ever added for the second and later tasks, and never for a host already
recorded as failed/unreachable in an earlier task — Ansible's own linear strategy runs every host
through a task together before any host starts the next one, except a host that already
failed/became unreachable, which is excluded from every later task for the rest of the play; don't
"fix" this into showing a spinner for a host that will never run that task.

Ansible run history is persisted via `IAnsibleExecutionStore`/`EfAnsibleExecutionStore`
(`Services/Ansible/`), backed by `ApplicationDbContext.AnsibleExecutions` — the same `DbContext`
Identity uses, per the fixed architectural decision above. It is separate from
`IAnsibleRunJobStore`, which stays the in-memory hot path for the ~1s live-polling loop and is
never touched by this change. A run is saved at most three times — enqueued (`Queued`, from
`AnsibleRunnerService.SubmitAsync`), started (`Running`), and once more at its terminal state, both
from `AnsibleRunWorker` — never per captured output line; only the final save carries the complete
`Output`. `IAnsibleRunnerService.GetJobOrHistoryAsync` is the only way a page should look up a run
by ID: it checks the in-memory store first (cheap, and holds every run from this process's
lifetime) and only falls back to `IAnsibleExecutionStore` when not found there. `EfAnsibleExecutionStore`
takes an `IDbContextFactory<ApplicationDbContext>`, not `ApplicationDbContext` directly, since it's
registered as a singleton alongside the other Ansible job services and needs to open its own
short-lived context per call. `AnsibleExecutionRecord`'s timestamps are plain UTC `DateTime`, not
`DateTimeOffset` like `AnsibleRunJob`'s — the SQLite EF Core provider can't translate an `ORDER BY`
over a `DateTimeOffset` column, which the `/Executions` list needs; don't change this back without
re-checking that.

## Authentication

The app supports exactly one operator account. There is no self-registration page and no
multi-user/RBAC model — do not add either without an explicit request.

- `Data/ApplicationDbContext.cs` (`IdentityDbContext<IdentityUser>`, EF Core + SQLite) backs
  ASP.NET Core Identity's cookie-based sign-in. Migrations apply automatically at startup
  (`Database.MigrateAsync()` in `Program.cs`); don't add a manual migration step to any
  deployment doc.
- `AdminOptions` (`Admin:Username`/`Admin:Password`) is read exactly once, in `Program.cs`,
  immediately after migrating: if no account exists yet, one is created from those values; if any
  account already exists, they're ignored. Never read `AdminOptions` anywhere else, and never make
  that seeding step overwrite an existing account's password.
- Razor Pages are protected by an `AuthorizeFolder("/")` convention with
  `AllowAnonymousToPage("/Account/Login")` as the one exception. A new top-level page needs no
  extra wiring to be protected — don't add a per-page `[Authorize]`/`[AllowAnonymous]` attribute
  unless a page genuinely needs to deviate from that default.
- `/Account/ChangePassword` is the only way to rotate the account's password; there's no
  password-reset flow (no email, no second factor to reset through). Losing the password is an
  operator-level database task, not something the app exposes.
- `Authorization/LocalhostOnlyHandler.cs` (policy name `"LocalhostOnly"`) is a separate gate from
  sign-in, for endpoints that should only ever be called from the orchestrator's own machine (the
  Ansible inventory endpoints today). It checks the actual TCP `RemoteIpAddress`, not whether the
  caller is authenticated — a signed-in browser calling from off-box must still be rejected, and
  an anonymous local caller must still be allowed. Read `HttpContext` through
  `IHttpContextAccessor`, never `AuthorizationHandlerContext.Resource` (endpoint-routing
  authorization passes the matched `Endpoint` there, not the request, so relying on it would fail
  closed for everyone including localhost). `Program.cs` also overrides
  `OnRedirectToLogin`/`OnRedirectToAccessDenied` so a rejected `/api/...` call gets a plain `403`
  instead of a redirect to the HTML login page — keep that in place for any future API-shaped
  endpoint.

## Jobs and concurrency

- HTTP requests enqueue work and return quickly.
- Long-running operations execute in hosted background services.
- Use explicit job states and stage states rather than a single free-form status string.
- Support safe polling from HTMX.
- Carry cancellation tokens through application-owned asynchronous APIs.
- Use bounded queues or another form of backpressure before production use.
- Provisioning allocation must be serialized unless a proper reservation mechanism is implemented.
- Do not use unobserved `Task.Run` calls for durable operations.
- A process restart may interrupt in-memory jobs; document this until persistent job recovery
  exists. An Ansible run's history entry (`IAnsibleExecutionStore`) surviving a restart is not the
  same thing as the run itself resuming — an interrupted run just stays in whatever stage it was
  last saved at (typically `Running`) and is never picked back up automatically.

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
- Every page requires a signed-in operator except `/Account/Login` — see [Authentication](#authentication).
- Scope a same-machine-only endpoint to loopback with the `LocalhostOnly` policy; don't rely on
  network placement (which address Kestrel binds) as the only protection.
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
- Do not introduce a SPA framework without an explicit request. A small amount of plain vanilla
  JS (e.g. `wwwroot/js/runner.js`) is fine for client-only widget state that has nothing to do with
  the server — a modal's open/closed state, a checkbox panel's selected-count display — as long as
  the value that actually reaches the server is still just an ordinary form field, no different in
  shape from one HTMX alone would produce.
- Page actions must show pending, successful, and failed states clearly.
- Disable or guard duplicate submissions.
- Show the actual target host and operation before disruptive actions.
- Keep provisioning fields minimal; infrastructure defaults belong in configuration.
- Do not expose secret configuration values in forms.
- The shared layout (`Pages/Shared/_Layout.cshtml`) carries a top navigation bar linking every
  top-level page (currently Provision, Ansible Runner, Job History, and Reconcile). Add new
  top-level pages there rather than leaving them reachable only by typing a URL.

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
- retry behavior after a post-creation Ansible failure;
- the `LocalhostOnly` authorization requirement, for both loopback and non-loopback addresses;
- reconciliation eligibility (matching/mismatched/DHCP address, already-tagged, out-of-range VMID) and that adoption re-verifies before tagging instead of trusting the caller's selection;
- inventory generation reporting a container's actual address even when it differs from what the VMID convention would predict;
- `OrchestratorSelfFilter` excluding the orchestrator's own container from every running-containers/target list it feeds.

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


At the end of every session either:
Open a Pull requst if a major refactor took place
Automatically merge and commit back to main if major infastructure changes were not made.
