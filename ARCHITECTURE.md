# SysRepairCli Architecture (Phase 2)

## Design goals

Use a small Clean Architecture-style separation that is easy to explain and implement in a one-hour .NET 10 MVP. Keep the report evidence-based and cross-platform, and make it impossible for diagnostics to silently become repairs. This is a local operator tool, not a host-management service.

## Solution and project layout

```text
SysRepairCli.sln
src/
  SysRepairCli.Cli/             # Console entry point, command parsing, output, DI/composition
  SysRepairCli.Application/     # Use cases, ports, orchestration, approval flow
  SysRepairCli.Domain/          # Check/report/repair models and status rules
  SysRepairCli.Infrastructure/  # OS/process/SSH/SQLite adapters
tests/
  SysRepairCli.Tests/           # Optional focused tests if time permits
```

Dependencies point inward: `Cli` and `Infrastructure` depend on `Application`; `Application` depends on `Domain`; `Domain` has no project dependencies. `Cli` composes application services with infrastructure implementations. For the demo timebox, keep one project per boundary and avoid separate abstractions for every class. A single executable command such as `check` is sufficient; JSON and repair subcommands are additions only if time remains.

## Command and UI boundary

The CLI parses arguments, validates invocation-level options, presents findings, and obtains explicit repair consent. It delegates work to application use cases, for example:

- `RunReadinessCheck` orchestrates the selected diagnostics and persists the resulting run.
- `ProposeKeyPermissionRepair` describes a single selected key change.
- `ApplyKeyPermissionRepair` applies that one change only after the application receives an approval decision.

Keep console input/output out of application and domain code. A simple `IUserInteraction` port may expose output and a per-action confirmation; its CLI implementation can use a prompt or a one-action `--yes` flag. `--yes` applies only to the named, fully displayed action, never to all findings or future actions. Without an affirmative, unambiguous answer, the application records the repair as declined/not applied.

## Application and domain

The application layer defines ports and coordinates work. The domain layer contains plain models and status semantics:

- `CheckStatus`: `Pass`, `NeedsAttention`, `NotVerified`, `Unsupported`.
- `CheckResult`: stable check identifier, display name, status, concise evidence, limitation, optional next step, and observation time.
- `ReadinessRun`: identifier, start/end timestamps, platform/runtime facts, overall status, and its check results.
- Overall status is derived from evidence: `NeedsAttention` if a check needs attention; otherwise `InsufficientEvidence` when required checks are unverified/unsupported; otherwise `ReadyForReview`. It never means marketplace acceptance or certification.
- `RepairProposal`: action identifier, target, observed current state, proposed state, and exact effect.
- `RepairResult`: proposal/action identifier, outcome (`Applied`, `Declined`, `NotApproved`, or `Failed`), evidence, and time.

Use explicit enums and nullable optional fields rather than encoding status in free-form text. Evidence must distinguish facts from limitations; for example, `nvidia-smi`'s CUDA value is driver-reported support, not proof of an installed toolkit, runtime, or successful workload.

Suggested narrow application ports:

```text
IDiagnosticCheck       -> CheckResult ExecuteAsync(context, cancellationToken)
ICommandExecutor       -> bounded local command execution
ISshProbe              -> optional, bounded outbound connection test to an operator-supplied host
IRepairApproval        -> one proposal at a time; returns an explicit decision
IKeyPermissionRepair   -> inspect/apply owner-only permission on a selected key file
IRunStore              -> save run and check results
```

Implement diagnostics as small focused checks (platform, NVIDIA, Docker, local listeners, SSH guidance, optional throughput). Keep SSH guidance informational; do not make an SSH probe a prerequisite for reporting. Do not expose a general-purpose “run arbitrary command” application use case.

## Infrastructure and command execution

`ICommandExecutor` is the local process boundary, implemented with `System.Diagnostics.Process`. It accepts an executable and an argument list as separate values, plus a timeout and cancellation token; returns exit code, bounded stdout/stderr, and elapsed time. Do not build a command-line string, interpolate user input into arguments, or invoke a shell (`sh -c`, PowerShell, `cmd /c`) for ordinary diagnostics. Resolve known executables and use fixed argument sets; validate any configurable port, path, URL, and size before use. Bound output as well as execution time.

The optional `ISshProbe` is a distinct, narrowly scoped adapter, not an alternate arbitrary command executor. If implemented, use a known SSH client with discrete arguments, fixed probe behavior, strict connection and overall time limits, and host-key verification (never silently disable it). Treat a supplied host as untrusted input: validate/normalize it, pass it as one argument, and do not concatenate it into a remote shell command. Do not accept arbitrary remote commands or use SSH to make repairs. If safe bounded behavior is unavailable, report the probe as `NotVerified` rather than weakening checks.

Never invoke `sudo`, `su`, `runas`, elevation APIs, or any other privilege escalation. The sole permitted mutation is the explicitly approved permission change on the selected SSH private-key file. Inspect the target and proposed owner-only mode before prompting; re-check the target immediately before applying to reduce stale-target risk. Do not follow a symlink to an unintended target; if the path or platform cannot be handled safely, refuse and report the limitation. Diagnostics are read-only.

## Approval, cancellation, and time bounds

Approval is per repair proposal and happens after displaying its exact target, current permission, proposed permission, and effect. No approval, decline, EOF, or ambiguous input means no mutation. Do not provide batch approval. Record the decision and result.

Thread a `CancellationToken` from CLI through use cases, checks, executors, and storage. Give each external operation a short explicit timeout; link its timeout token to the caller token, terminate a timed-out child process, and return a non-pass result with the observed reason. Apply an overall run bound so one unavailable utility cannot hang the report. Keep throughput size and duration configurable and capped. Cancellation should stop pending work without starting a repair; do not convert cancellation into success.

## Platform-dependent evidence

The CLI identifies Windows, macOS, and Linux without requiring administrator/root privileges. Checks declare applicability and capability rather than interpreting missing evidence as success. Linux is the primary target for NVIDIA, Docker daemon, and host-network observations. On Windows/macOS, report Linux-only checks as `Unsupported` or `NotVerified` unless a reliable native implementation exists. A missing utility, inaccessible interface, timeout, or ambiguous result is `NotVerified` or `NeedsAttention`, never `Pass`.

Local TCP listener inspection proves only a local observation, not external reachability or firewall/NAT state. SSH client presence and local inspection do not establish inbound SSH readiness. Keep IOMMU/virtualization as Linux-only best-effort observation, otherwise `NotVerified`; CUDA workload, GPU container passthrough, GPU throughput, remote port reachability, and Vast.ai acceptance remain unverified. Report the `nvidia-smi` CUDA field only as the driver's advertised supported CUDA level.

## Local SQLite persistence

Use one local SQLite database for concise run history; persistence failure should be surfaced, not silently reported as a saved run. Store no credentials, private-key contents, or unnecessary command output. A compact schema is:

```sql
CREATE TABLE runs (
  id            TEXT PRIMARY KEY,
  started_utc   TEXT NOT NULL,
  completed_utc TEXT,
  platform      TEXT NOT NULL,
  overall       TEXT NOT NULL
);

CREATE TABLE checks (
  run_id       TEXT NOT NULL REFERENCES runs(id) ON DELETE CASCADE,
  check_id     TEXT NOT NULL,
  status       TEXT NOT NULL,
  evidence     TEXT NOT NULL,
  limitation   TEXT,
  next_step    TEXT,
  observed_utc TEXT NOT NULL,
  PRIMARY KEY (run_id, check_id)
);
```

Persist only the current check result fields needed to reproduce the concise report. Repair proposals/results can remain in the immediate CLI output for the MVP; if repair history is persisted, add a small `repair_actions` table keyed by run and action, recording target description, decision, outcome, and timestamp—but never key material. Use parameterized SQL and enable foreign keys.

## MVP implementation order

1. Establish domain result types and CLI report formatting.
2. Implement the application orchestration and a few truthful local checks through bounded `ICommandExecutor`.
3. Add SQLite run/check persistence if time permits without delaying truthful reporting.
4. Add the single approved key-permission repair; test declined/no-approval behavior before showing it.
5. Add optional SSH probe or JSON only if the core demonstration is complete and safe.

Keep the first demonstration focused on a concise readiness report and one repair proposal that visibly does nothing unless explicitly approved.
