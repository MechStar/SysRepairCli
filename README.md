# SysRepairCli

SysRepairCli is a small .NET 10 CLI that collects evidence for an operator reviewing a prospective GPU host. It is not a Vast.ai onboarding tool, acceptance test, certification, or guarantee of suitability.

## Quick start

Prerequisites: the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), and Linux, macOS, or Windows for the local report. NVIDIA/Docker/OpenSSH checks depend on their corresponding tools being installed.

Clone and run the checks from the repository root:

```sh
git clone https://github.com/MechStar/SysRepairCli.git
cd SysRepairCli
dotnet restore SysRepairCli.slnx
dotnet build SysRepairCli.slnx
dotnet test SysRepairCli.slnx
dotnet run --project src/SysRepairCli -- check
```

Use `--help` to display the command summary:

```sh
dotnet run --project src/SysRepairCli -- --help
```

## Usage

Run commands from the repository root. While developing, use `dotnet run --project src/SysRepairCli -- <command>`. After building, the compiled application is under `src/SysRepairCli/bin/<Configuration>/net10.0/`.

| Command | Description |
| --- | --- |
| `check` | Run local readiness checks and print a human-readable report. |
| `check --json` | Emit the same report as JSON, suitable for capture or processing. |
| `check --throughput <HTTP(S)-URL>` | In addition, measure a bounded download from the supplied endpoint. |
| `ssh-check --user <USER> --host <HOST>` | Make an optional outbound SSH connection and run the fixed, read-only remote probe. |
| `repair-key --key <PATH>` | On Linux/macOS, propose restricting a selected SSH key file to owner read/write (`0600`). Requires a separate interactive confirmation. |
| `--help` | Show the command summary. |

Examples:

```sh
# Human-readable local report
dotnet run --project src/SysRepairCli -- check

# JSON report
dotnet run --project src/SysRepairCli -- check --json

# Optional bounded transfer measurement against an endpoint you trust
dotnet run --project src/SysRepairCli -- check --throughput https://example.net/test-file

# Optional outbound SSH probe; trust the host key first (see below)
dotnet run --project src/SysRepairCli -- ssh-check --user operator --host host.example.net

# Optional, explicitly approved private-key permission repair
dotnet run --project src/SysRepairCli -- repair-key --key ~/.ssh/id_ed25519
```

The throughput check requests only the caller-supplied HTTP(S) URL, reads at most 1 MiB, and stops after 15 seconds. It is endpoint- and network-dependent, not a GPU benchmark. Without an endpoint, throughput is reported as **Not verified**.

### Exit codes

For `check`, exit code `0` means **Ready for review**; `1` means **Needs attention** or **Insufficient evidence**; `2` means invocation failure. `ssh-check` returns `0` only when its probe passes and `1` when it does not. `repair-key` returns `0` when the action was applied, `1` when it was declined or unavailable, and `2` for invocation/repair failure. Cancellation exits `130`. A nonzero readiness result means requested evidence is incomplete or a check found an issue—not that the host is necessarily unsuitable.

## Checks and interpretation

Each check reports **Pass**, **Needs attention**, **Not verified**, or **Unsupported**, along with evidence and limitations. The overall status is **Ready for review**, **Needs attention**, or **Insufficient evidence**; it never means Vast.ai has accepted the host.

- Platform and .NET runtime identification.
- Linux NVIDIA inventory and driver version through bounded `nvidia-smi` calls. Any displayed CUDA version is the driver's advertised support only—not an installed toolkit/runtime or a successful workload.
- Docker CLI and the daemon on the standard local socket/pipe are queried separately. Rootless/custom sockets are not detected; this does not verify NVIDIA Container Toolkit or GPU passthrough.
- Local TCP listeners on ports 22, 8080, and 10000. These observations do not reveal firewall, NAT, cloud security-group, or external reachability state.
- SSH client availability and operator guidance. Client presence does not verify inbound SSH readiness.
- Best-effort Linux IOMMU group observation; it does not establish firmware settings, isolation, or passthrough readiness.
- Explicitly not verified: CUDA workloads, GPU-container execution, GPU compute throughput, external reachability, and Vast.ai acceptance.

Linux is the primary target for NVIDIA and IOMMU evidence. On Windows and macOS, those Linux-only checks are reported unsupported. Missing tools, denied access, timeouts, and inconclusive observations are never turned into successful evidence.

## Optional outbound SSH probe

```sh
dotnet run --project src/SysRepairCli -- ssh-check --user operator --host host.example.net
```

The probe validates the username and host, uses OpenSSH with batch mode and strict host-key checking, and passes a fixed read-only script to the fixed remote `sh -s` entrypoint. It does not accept a remote command, disable host-key verification, alter remote state, or test inbound access to the local machine. Add the remote host key to the normal known-hosts store before running; the command will not prompt to trust an unknown key. Verify and trust the host key independently using your normal process; do not disable strict host-key checking to work around an unknown or changed key.

## Guarded SSH-key permission repair

On Linux or macOS, a selected regular file may be restricted to owner read/write (`0600`):

```sh
dotnet run --project src/SysRepairCli -- repair-key --key ~/.ssh/id_ed25519
```

First verify the path identifies the intended private-key file. The CLI displays the exact target, current mode, proposed mode, and effect, then asks for a separate interactive confirmation for this action. Proceed only if those details are correct. Redirected/non-interactive use cannot approve it. There is no `--yes`, batch approval, elevation, or `sudo` path. Declining, EOF, unsupported platforms, symlinks, and stale permission state cause no change. The selected file's contents are never read or printed.

## Troubleshooting

- **Docker daemon unavailable:** confirm the local Docker daemon is running and accessible to your user. Custom/rootless sockets are not detected by this MVP.
- **NVIDIA or IOMMU check unsupported:** these observations are Linux-only. Unsupported means the check was not performed, not that the host passed or failed it.
- **SSH probe cannot connect:** confirm the OpenSSH client is installed, the hostname/account are correct, network access is available, and the host key is already trusted in your normal `known_hosts` file. The probe intentionally does not prompt to trust a key.
- **`Needs attention` / `Insufficient evidence`:** review each check's evidence, limitation, and next step. These statuses are not a host suitability verdict.
- **Permission repair not available:** the repair is limited to regular files on Linux/macOS and fails closed on symlinks, stale state, unsupported platforms, or non-interactive approval.

## Scope and limitations

Diagnostics are bounded and read-only; the only mutation is the explicitly confirmed permission repair above. No packages, services, firewall/network rules, SSH daemon, kernel, firmware, or cloud configuration are changed. This MVP does not scan remote systems, validate external ports, certify performance, or perform host acceptance.

The architecture's application, domain, infrastructure, and CLI boundaries are kept as focused files in one executable project rather than four projects, to avoid project boilerplate in the interview-scale MVP. The architecture describes optional local SQLite run history; it is intentionally deferred to keep the truthful checks and guarded repair complete. Results are displayed or emitted as JSON and are not persisted. No credentials, key material, or command output are stored.

## Contributing

Bug reports and focused improvements are welcome. Before opening a change, run:

```sh
dotnet build SysRepairCli.slnx
dotnet test SysRepairCli.slnx
```

Add or update tests for behavior changes. Preserve the safety boundaries: diagnostics are read-only, subprocesses use fixed/validated arguments, SSH runs only the fixed probe with host-key verification, and the sole repair is individually confirmed and never elevated.

## License

No license file is currently included. Until one is added, do not assume the repository grants permissions beyond those provided by applicable law.
