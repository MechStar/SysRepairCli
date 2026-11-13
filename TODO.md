# SysRepairCli Engineering TODO

This roadmap turns `PRD.md`, `ARCHITECTURE.md`, and the existing contracts/tests into six sequenced phases. This update records Phase 5 implementation and validation only; **Phase 6 release, cross-platform, and publication gates remain open and are not signed off.**

## Non-negotiable delivery rules

- **Test first:** for every behavior change, add or extend the focused test, run it to confirm it fails for the intended reason, then implement the smallest change and rerun it. Do not weaken or delete a failing assertion to get green. Add broader regression coverage before moving to the next behavior.
- Keep diagnostics read-only. The sole permitted mutation is the selected SSH private-key file's owner-only permissions after a separate, explicit approval for that individual action. No batch approval, implicit approval, elevation, or repair on decline/ambiguous input.
- Treat evidence honestly: `Unsupported`, `NotVerified`, and `NeedsAttention` are not passes; driver-reported CUDA support is not toolkit/runtime/workload verification; local listeners do not establish external reachability; nothing means Vast.ai acceptance.
- Keep operations bounded, cancellation-aware, and local unless an explicitly scoped optional endpoint/connection test is selected. Never execute arbitrary shell commands or remote commands.

## Six-phase map

### Phase 1 — Product scope and acceptance (Product owner + QA)

**Status:** Source established in `PRD.md`.

- [x] Define MVP, out-of-scope work, per-check and overall statuses, platform limits, and measurable outcomes.
- [x] Specify explicit per-action approval and the sole allowed repair.
- [ ] Before implementation, QA converts each applicable PRD criterion into a focused test or a documented manual/CI check; product owner resolves any scope ambiguity without broadening the MVP.

**Gate:** Every implementation item traces to a PRD requirement; unsupported evidence is never represented as success.

### Phase 2 — Architecture and safety contracts (Solution architect + security engineer)

**Status:** Source established in `ARCHITECTURE.md` and `src/SysRepairCli/Contracts/`.

- [x] Define inward dependencies, process execution boundary, diagnostic/repair contracts, bounded operations, storage proposal, and consent flow.
- [x] Reconcile the proposed multi-project layout with the current single executable project; keep the boundaries in focused files in the executable project.
- [x] Record the SQLite history deferral in `README.md`; results are displayed or emitted as JSON and not persisted.

**Gate:** Architecture decisions preserve the PRD's scope and do not create an arbitrary-command, privilege-escalation, or unattended-repair path.

### Phase 3 — Test-first baseline and contracts (QA/test engineer + application engineer)

**Status:** Phase 3 contracts and tests remain in place. The original red scaffold baseline is resolved by the Phase 5 implementation; focused tests now cover the engine, process runner, SSH target/probe, Docker daemon evidence, and permission repair.

- [x] Read the existing test contracts: diagnostic status aggregation, shell-free discrete process arguments, nonzero/timeout failure semantics, and denied-repair behavior.
- [x] Add focused tests for cancellation, empty and mixed check sets, unsupported/not-verified aggregation, output bounds, and approved/denied repair orchestration.
- [x] Add regression coverage for fixed SSH inputs, invalid targets, symlink refusal, and the sole permission mutation. Interactive EOF/ambiguous-input behavior is fail-closed in the CLI and still requires cross-platform/manual verification.

**Historical red baseline (captured 2026-11-13):** `dotnet test SysRepairCli.slnx --no-restore` on .NET SDK 10.0.404 / macOS 15.8: **5 failed, 1 passed, 6 total**. The passing case was `Timed_out_command_is_not_reported_as_successful_even_with_zero_exit_code`; the other five failed because `DiagnosticEngine.RunAsync`, `LocalCommandExecutor.ExecuteAsync`, and `RepairCoordinator.ApplyAsync` threw `NotImplementedException`:

- `DiagnosticEngineTests.Unsupported_check_is_preserved_and_cannot_make_run_ready`
- `DiagnosticEngineTests.Needs_attention_check_takes_precedence_over_unverified_evidence`
- `LocalCommandExecutorTests.Passes_script_path_and_arguments_as_individual_values_without_a_shell`
- `LocalCommandExecutorTests.Reports_nonzero_exit_as_failure_and_preserves_stderr`
- `RepairCoordinatorTests.Approval_denied_does_not_execute_repair`

**Resolution:** Those five failures now pass; the original six behaviors were preserved. Current counts are recorded below under Phase 5.

### Phase 4 — Domain, orchestration, and readiness reporting (Domain/application engineer + CLI engineer; QA owns tests)

- [x] **QA first:** add tests for overall-status precedence, result ordering, cancellation, empty-check behavior, and timestamps.
- [x] **Domain/application engineer:** implement `DiagnosticEngine` status derivation and preserve unsupported evidence.
- [ ] **QA first:** automate CLI output/error tests; this MVP uses the documented local `--help` and `check` smoke checks instead.
- [x] **CLI engineer:** implement the documented readiness command and human-readable/JSON output, labeled for operator review only.
- [x] **QA/security review:** preserve unsupported platform evidence and verify it cannot make a run ready.

**Green gate:** focused tests pass, all results remain attributable to their check, and overall status follows the documented evidence rules. No output claims unsupported verification.

### Phase 5 — Safe local checks, repair, and operator documentation (Infrastructure engineer + repair/application engineer + documentation engineer; QA/security own tests)

- [x] **QA first:** cover discrete process arguments/no shell, nonzero/stderr, timeout-result semantics, pre-start cancellation, and bounded output.
- [x] **Infrastructure engineer:** implement `ArgumentList` process execution, cancellation, timeouts, process-tree termination, output bounds, and elapsed-time reporting; no shell command construction.
- [x] **QA first:** add focused diagnostics/SSH coverage for Docker daemon evidence, unsupported platform handling, SSH target validation, and fixed remote script execution. NVIDIA and throughput paths are implemented but not fully integration-tested on this macOS host.
- [x] **Infrastructure/application engineers:** implement bounded local platform, NVIDIA, Docker, local listener, SSH guidance, IOMMU, and optional endpoint throughput checks with explicit evidence limits.
- [x] **QA/security first:** cover denied/approved repair orchestration, unsafe SSH targets, symlink refusal, and owner-only mutation. Redirected interactive approval is refused; cross-platform EOF/manual behavior remains part of Phase 6.
- [x] **Repair/application engineer:** inspect and recheck the selected file; display exact target/current/proposed/effect; only apply 0600 after one interactive confirmation. No elevation or batch approval.
- [x] **Documentation engineer:** create `README.md` with setup, usage, statuses, limits, and repair safety.
- [x] **Repository maintainer:** create `.gitignore` for .NET outputs, IDE artifacts, local DBs, and common key files.
- [x] **SQLite decision:** defer run/check persistence for this MVP and document the narrow deviation in `README.md`.

**Phase 5 validation (macOS 15.8, .NET SDK 10.0.404):** restore succeeded; build succeeded with **0 warnings / 0 errors**; tests passed **24/24**; `--help` ran successfully. Human and JSON readiness smoke checks returned the expected exit **1 / Needs attention** because the local Docker daemon was unavailable; Linux-only NVIDIA/IOMMU checks were explicitly unsupported. Linux/Windows verification is not performed here.

### Phase 6 — Cross-platform verification and safe publication (QA/release engineer + security reviewer)

**Status: NOT RELEASE SIGNED OFF.** The requested source-level security review is complete and has no findings, and the verified MVP has been published on `main`. Cross-platform CI remains pending, so this is not a full release sign-off.

- [x] **Clean-checkout verification (2026-11-13):** cloned the local commit into a fresh directory; `dotnet restore SysRepairCli.slnx`, `dotnet build SysRepairCli.slnx --no-restore` (0 warnings/errors), `dotnet test SysRepairCli.slnx --no-build` (24/24 passed), and `dotnet run --project src/SysRepairCli --no-build -- --help` all succeeded.
- [ ] **QA/release engineer:** run CI or equivalent builds/tests on Windows, macOS, and Linux. Verify each OS is identified and unsupported checks are explicit; confirm tests require no administrator/root privileges.
- [x] **Security reviewer (scoped sign-off, 2026-11-13):** reviewed the current local process, SSH, and repair-approval paths for command injection, privilege escalation, target validation, and approval bypass. **No substantiated vulnerabilities found; those reviewed paths merit sign-off.** Confirmed discrete local arguments/no shell, constrained SSH target with fixed remote entrypoint/script and strict host-key checking, and fail-closed per-action permission repair without elevation. This does not sign off unverified platforms or replace clean-checkout testing.
- [x] **Repository maintainer:** inspected the target `https://github.com/MechStar/SysRepairCli` before publishing. The repository is empty and has no branch/commit history; local workspace currently has no Git metadata.
- [ ] **Repository maintainer:** initialize the local repository, configure `origin`, and inspect `git remote -v`; preserve and integrate any history if the remote state changes before publication.
- [x] **Repository maintainer:** initialized the local Git repository on `main`, configured `origin` as `https://github.com/MechStar/SysRepairCli.git`, and verified the target was empty with no existing refs before the first push.
- [x] **Publication:** after local and clean-checkout verification, pushed `main` with a normal non-force push; GitHub confirmed the branch and commit at `https://github.com/MechStar/SysRepairCli`.

**Final acceptance:**

- **Green:** all targeted tests, full solution tests, build, and supported-platform CI pass; no known failing baseline behavior remains.
- **Test:** tests cover the existing six contract behaviors plus status derivation, truthful unsupported evidence, cancellation/time/output bounds, platform check semantics, and all repair approval/no-mutation cases. The report and README agree.
- **Security:** no privilege escalation or unattended repair; every repair requires a separate explicit approval after exact details are shown; decline/no approval causes no change; no other repair or diagnostic changes the host; no sensitive key contents are stored or printed.
- **Publishing:** remote history was inspected first; local verification is complete; push is a normal non-force update that preserves existing history.

**Remaining release gate:** Windows and Linux CI/manual verification have not been performed in this macOS environment.
