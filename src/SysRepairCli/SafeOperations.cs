using System.Net;
using System.Text.RegularExpressions;

namespace SysRepairCli;

public static class SshTargetValidator
{
    private static readonly Regex UserPattern = new(
        "^[A-Za-z_][A-Za-z0-9_-]{0,31}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool TryNormalize(string user, string host, out string target, out string error)
    {
        target = string.Empty;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(user) || !UserPattern.IsMatch(user))
        {
            error = "SSH user must be 1–32 ASCII letters, digits, underscores, or hyphens, and may not start with a digit or hyphen.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(host) || host.Length > 253 ||
            host.StartsWith("-", StringComparison.Ordinal) ||
            host.Contains('@') ||
            host.Any(character => char.IsControl(character) || char.IsWhiteSpace(character)))
        {
            error = "SSH host must be a DNS name or IP address without whitespace or user information.";
            return false;
        }

        var normalizedHost = host.TrimEnd('.');
        if (IPAddress.TryParse(normalizedHost, out var address))
        {
            normalizedHost = address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
                ? $"[{address}]"
                : address.ToString();
        }
        else
        {
            var labels = normalizedHost.Split('.');
            if (labels.Length == 0 || labels.Any(label =>
                    label.Length is < 1 or > 63 ||
                    label[0] == '-' ||
                    label[^1] == '-' ||
                    label.Any(character =>
                        !(character is >= 'A' and <= 'Z') &&
                        !(character is >= 'a' and <= 'z') &&
                        !(character is >= '0' and <= '9') &&
                        character != '-')))
            {
                error = "SSH host must be a valid DNS name or IP address.";
                return false;
            }
        }

        target = $"{user}@{normalizedHost}";
        return true;
    }
}

public sealed class SshExecutionPath(ICommandExecutor executor)
{
    private const string RemoteScript = "printf 'SYSREPAIR_REMOTE_PROBE_OK\\n'; uname -s\n";

    public async Task<CheckResult> ProbeAsync(
        string user,
        string host,
        CancellationToken cancellationToken = default)
    {
        if (!SshTargetValidator.TryNormalize(user, host, out var target, out var validationError))
        {
            return new CheckResult(
                "ssh-remote-probe",
                "Outbound SSH probe",
                CheckStatus.NotVerified,
                $"Probe not run: {validationError}",
                "No SSH command was started.");
        }

        try
        {
            var result = await executor.ExecuteAsync(
                "ssh",
                [
                    "-T",
                    "-o", "BatchMode=yes",
                    "-o", "StrictHostKeyChecking=yes",
                    "-o", "ConnectTimeout=5",
                    "--",
                    target,
                    "sh -s"
                ],
                TimeSpan.FromSeconds(10),
                cancellationToken,
                RemoteScript);

            if (result.TimedOut)
            {
                return new CheckResult(
                    "ssh-remote-probe",
                    "Outbound SSH probe",
                    CheckStatus.NotVerified,
                    $"The bounded SSH probe to {target} timed out.",
                    "This does not test inbound access to the local machine.");
            }

            if (!result.Succeeded ||
                !result.StandardOutput.StartsWith("SYSREPAIR_REMOTE_PROBE_OK", StringComparison.Ordinal))
            {
                return new CheckResult(
                    "ssh-remote-probe",
                    "Outbound SSH probe",
                    CheckStatus.NotVerified,
                    $"The outbound SSH probe to {target} did not complete: {Trim(result.StandardError)}",
                    "Host-key verification remains enabled; this does not establish inbound SSH readiness.");
            }

            var platform = result.StandardOutput
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Skip(1)
                .FirstOrDefault() ?? "remote platform unavailable";
            return new CheckResult(
                "ssh-remote-probe",
                "Outbound SSH probe",
                CheckStatus.Pass,
                $"A bounded outbound SSH probe reached {target}; remote uname: {platform}.",
                "This checks outbound access to the supplied host only. It does not test inbound access to this machine.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new CheckResult(
                "ssh-remote-probe",
                "Outbound SSH probe",
                CheckStatus.NotVerified,
                $"The SSH probe could not be completed: {exception.Message}",
                "No host-key check was disabled and no remote repair command is accepted.");
        }
    }

    private static string Trim(string value) =>
        string.IsNullOrWhiteSpace(value) ? "no additional error details" : value.Trim();
}

public sealed class SshKeyPermissionRepair : IKeyPermissionRepair
{
    private const UnixFileMode OwnerReadWrite = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private const UnixFileMode AllPermissions = UnixFileMode.UserRead | UnixFileMode.UserWrite |
        UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupWrite |
        UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherWrite |
        UnixFileMode.OtherExecute | UnixFileMode.SetUser | UnixFileMode.SetGroup | UnixFileMode.StickyBit;

    public RepairProposal? CreateProposal(string selectedPath)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException("Owner-only Unix file permissions are supported only on Linux and macOS.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(selectedPath);
        var fullPath = Path.GetFullPath(selectedPath);
        var mode = InspectRegularFile(fullPath);
        var current = FormatMode(mode);
        if ((mode & AllPermissions & ~OwnerReadWrite) == 0)
        {
            return null;
        }

        return new RepairProposal(
            "key-permissions",
            fullPath,
            current,
            "0600",
            "Restrict the selected SSH private-key file to owner read/write access.");
    }

    public Task<RepairResult> ApplyAsync(
        RepairProposal proposal,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return Task.FromResult(new RepairResult(
                proposal.ActionId,
                RepairOutcome.Failed,
                "Owner-only Unix permissions are unsupported on this platform."));
        }

        if (proposal.ActionId != "key-permissions" ||
            proposal.ProposedState != "0600")
        {
            return Task.FromResult(new RepairResult(
                proposal.ActionId,
                RepairOutcome.Failed,
                "The proposed action is unsupported or does not match the permitted key-permission repair."));
        }

        try
        {
            var fullPath = Path.GetFullPath(proposal.Target);
            if (!string.Equals(fullPath, proposal.Target, StringComparison.Ordinal))
            {
                return Task.FromResult(new RepairResult(
                    proposal.ActionId,
                    RepairOutcome.Failed,
                    "The target path changed after proposal inspection; no change was made."));
            }

            var currentMode = InspectRegularFile(fullPath);
            if (!string.Equals(FormatMode(currentMode), proposal.CurrentState, StringComparison.Ordinal))
            {
                return Task.FromResult(new RepairResult(
                    proposal.ActionId,
                    RepairOutcome.Failed,
                    "The target permissions changed after proposal inspection; no change was made."));
            }

            var mode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            File.SetUnixFileMode(fullPath, mode);
            var appliedMode = InspectRegularFile(fullPath);
            if (appliedMode != mode)
            {
                return Task.FromResult(new RepairResult(
                    proposal.ActionId,
                    RepairOutcome.Failed,
                    $"Permission verification did not match the requested mode; observed {FormatMode(appliedMode)}."));
            }

            return Task.FromResult(new RepairResult(
                proposal.ActionId,
                RepairOutcome.Applied,
                $"Selected file permissions changed from {proposal.CurrentState} to 0600."));
        }
        catch (Exception exception)
        {
            return Task.FromResult(new RepairResult(
                proposal.ActionId,
                RepairOutcome.Failed,
                $"Repair was refused or failed without elevation: {exception.Message}"));
        }
    }

    private static UnixFileMode InspectRegularFile(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Unix file permissions are unavailable on this platform.");
        }

        var attributes = File.GetAttributes(path);
        if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
        {
            throw new IOException("Target must be a regular file, not a directory or symbolic link.");
        }

        return File.GetUnixFileMode(path);
    }

    private static string FormatMode(UnixFileMode mode)
    {
        var value = (int)mode & (int)AllPermissions;
        return Convert.ToString(value, 8).PadLeft(4, '0');
    }
}
