# SysRepairCli — Phase 1 Product Requirements

## Product

SysRepairCli is a lightweight, cross-platform .NET 10 command-line MVP that helps an individual host operator assess a machine before onboarding it as a GPU host on Vast.ai. It produces a concise, evidence-based readiness report, explains checks that it cannot perform, and offers only narrowly scoped repairs that require explicit approval for each action.

**MVP constraint:** Optimize for a one-hour interview demonstration. Favor a small, truthful diagnostic set over broad host automation or production-grade coverage.

## Goal and non-goals

### Goal

Let an operator run one command to learn which key prerequisites appear ready, which need attention, and what to do next—without SysRepairCli silently changing the host or running privileged commands.

### Out of scope

- Vast.ai account setup, host registration, marketplace actions, or guarantees of host acceptance.
- Installing/upgrading drivers, CUDA, Docker, kernels, or other system packages.
- Changing firewall rules, router/NAT settings, cloud security groups, SSH daemon configuration, BIOS/UEFI, IOMMU, or virtualization settings.
- Running as root/Administrator, invoking elevation tools, unattended fixes, persistent agents, telemetry, or remote administration.
- Production-grade compatibility databases, continuous monitoring, exhaustive hardware validation, or GPU performance certification.
- Claiming GPU compute throughput from device inventory or driver metadata.

## Users and user stories

1. **As a prospective Vast.ai host operator**, I want a single readiness command so I can quickly see which checks passed, failed, were skipped, or are unsupported on this OS.
2. **As an operator troubleshooting a GPU host**, I want detected GPU and NVIDIA driver facts separated from CUDA runtime compatibility claims so I can tell what was actually verified.
3. **As an operator preparing a host for remote access**, I want safe SSH and port guidance that does not alter my network or SSH configuration.
4. **As an operator who wants a repair**, I want to review the exact proposed change and approve each action separately, so no change happens without my consent.
5. **As an operator on a non-Linux platform**, I want platform-specific gaps called out as unsupported or not verified rather than presented as successful checks.

## MVP scope and check semantics

Every check has one of these explicit outcomes: **Pass**, **Needs attention**, **Not verified**, or **Unsupported**. A skipped check is not a pass. Reports include the observed evidence, any limitation, and a next step when useful. The overall result is **Ready for review**, **Needs attention**, or **Insufficient evidence**; it must never mean Vast.ai has accepted or certified the host.

### Reliable MVP checks (when their required local tools are available)

- **Platform and prerequisites:** Identify OS/runtime; report missing commands or access limitations without treating them as successful checks.
- **NVIDIA GPU and driver:** On supported Linux hosts, query `nvidia-smi` with a bounded timeout. Report detected GPU model(s), driver version, and whether the utility can communicate with the driver. If unavailable or unsuccessful, say “not verified” or “needs attention” with the observed reason.
- **CUDA compatibility facts:** Report the CUDA version advertised by `nvidia-smi` as the driver’s supported CUDA level, if present. Do not equate this with an installed CUDA Toolkit, a loaded CUDA runtime, application compatibility, or a successful CUDA workload. The MVP does not certify those.
- **Container runtime:** Detect Docker CLI/daemon availability and report the evidence separately. A working Docker daemon does not establish NVIDIA Container Toolkit or GPU passthrough. GPU-container execution is not a required MVP pass check.
- **Ports:** Check only local TCP listening state for a small, documented set of relevant/configurable ports where the OS can provide reliable evidence. A local listener does not prove external reachability; no listener does not prove a firewall block. Do not scan remote systems.
- **SSH access guidance:** Report whether the SSH client is available and provide operator-facing steps for checking a remote connection, inbound reachability, and Vast.ai-specific requirements. Do not claim remote SSH readiness without a configured target and an explicit, bounded connection test; no inbound configuration is changed.
- **Throughput:** Provide an optional, bounded network throughput measurement only when the operator supplies a test URL/endpoint. Report measured transfer, duration, and approximate rate, and label results as endpoint- and network-dependent. Without an endpoint, report “Not measured.” This is not a GPU benchmark or a promise of marketplace performance.

### Unsupported or platform-specific checks

- **Linux:** The primary diagnostic target for NVIDIA, Docker, and host-network checks. Even on Linux, firewall policy, router/NAT, cloud networking, and remote port reachability are not inferred from local inspection.
- **Windows and macOS:** The CLI must run and identify its platform, but Linux-specific host checks must be reported as unsupported/not verified unless implemented with reliable native evidence. NVIDIA GPU hosting readiness is not implied by a successful CLI run.
- **Virtualization/IOMMU:** At most, report a clearly labeled Linux-only best-effort observation when a known readable system interface is present. If not detectable, report “Not verified”; do not infer firmware or passthrough readiness. Windows/macOS checks are unsupported in the MVP.
- **CUDA workload, NVIDIA container passthrough, GPU compute throughput, thermal/stability testing, external port reachability, and Vast.ai acceptance:** Not verified by this MVP. Explain what additional operator-side validation is needed; do not report success.

## Repairs and safety

- The MVP may offer a narrowly scoped repair for SSH private-key file permissions (set the selected key to owner-only access) when the OS and file permissions support a safe check.
- Before any repair, show the target, current and proposed permissions, and exact effect. Require a separate explicit confirmation for every action (for example, `--yes` for one action or an interactive per-action prompt). No broad “approve all” mode.
- If approval is absent, declined, or ambiguous, do not perform the action. Report that it was not applied.
- Never invoke `sudo`, `su`, `runas`, elevation APIs, shells that elevate, or privileged execution. Never modify system packages, services, Docker configuration, firewall/network rules, SSH daemon configuration, kernel/boot settings, or firmware.
- All other findings are guidance-only in the MVP. A diagnostic command must not make host changes.

## MoSCoW priorities

### Must

- Build and run as a .NET 10 CLI on Windows, macOS, and Linux; unsupported checks are explicit per platform.
- Provide one concise readiness-report command with Pass / Needs attention / Not verified / Unsupported outcomes, evidence, and limitations.
- Implement bounded local checks for platform, NVIDIA driver/GPU facts where supported, Docker/runtime availability, and local port-listener state where reliable.
- Distinguish driver-reported CUDA support from an installed toolkit/runtime or workload validation.
- Include safe SSH guidance and an optional bounded endpoint-based network throughput measurement; report “Not measured” when not run.
- Offer only the stated non-privileged SSH key permission repair, with a separate explicit approval for each action and no change on decline/no approval.
- Never run privileged commands or make unattended host changes.
- Keep the operator-facing flow and output suitable for a one-hour interview MVP.

### Should

- Include a Linux-only best-effort IOMMU/virtualization observation where reliable, readable evidence exists, otherwise explicitly say not verified.
- Support machine-readable JSON output alongside human-readable output if it fits the MVP timebox.
- Provide useful remediation guidance for findings without applying those remedies automatically.

### Could

- Add an explicit, bounded outbound SSH connectivity test to an operator-supplied host, clearly separate from inbound SSH readiness.
- Add an optional GPU-container smoke test only when the operator supplies/chooses a known image and explicitly starts the test; its result must not imply Vast.ai certification.

### Won’t (for this MVP)

- Install, configure, upgrade, restart, or remove host software/services.
- Change inbound access, firewall, NAT, cloud networking, or SSH server settings.
- Claim IOMMU, virtualization, remote ports, CUDA runtime, GPU workload, GPU throughput, or Vast.ai acceptance is verified when evidence is unavailable.
- Provide continuous monitoring, full hardware stress tests, automated cloud onboarding, or production fleet management.

## Measurable acceptance criteria

1. On each supported OS, the CLI starts and identifies the OS without requiring administrator/root privileges. It exits with a documented nonzero status on an unrecoverable invocation error.
2. One documented command produces a report with an overall result and an individual status, evidence/limitation, and next step (where applicable) for every in-scope check.
3. If `nvidia-smi` is missing, times out, or returns an error, the NVIDIA check is not reported as Pass. If successful, the report includes available GPU and driver facts and labels any CUDA version as driver-reported support—not toolkit/runtime validation.
4. Docker CLI and daemon status are reported separately when distinguishable. Docker availability alone never marks NVIDIA container passthrough as verified.
5. Port results describe only local listener observations. The output explicitly states that local inspection does not establish external reachability or firewall configuration.
6. SSH guidance never claims inbound SSH readiness from client presence or local inspection alone. No SSH/network configuration is changed.
7. Throughput is either “Not measured” or includes the selected endpoint, transferred bytes, elapsed time, and approximate rate. The request has a configurable timeout/size bound and does not claim GPU throughput.
8. Unsupported or unavailable IOMMU, CUDA workload, remote reachability, and platform-specific checks are labeled Unsupported or Not verified, never Pass.
9. A repair without explicit per-action approval makes no change. With approval, only the selected SSH key’s permissions may be changed; the CLI does not elevate, and reports the result.
10. No diagnostic or repair path performs privileged execution or an unattended host-changing action.
11. The report makes no assertion of Vast.ai acceptance, certification, or guaranteed suitability.

## MVP success measure

In a one-hour interview, an operator can run the CLI, understand the evidence and gaps in its report, and see a proposed repair safely decline or apply only after explicit approval—without mistaking unsupported checks for successful ones.
