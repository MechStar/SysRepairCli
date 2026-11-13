using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace SysRepairCli;

public static class StandardDiagnosticChecks
{
    public static IReadOnlyList<IDiagnosticCheck> Create(
        ICommandExecutor executor,
        string? throughputUrl = null) =>
    [
        new PlatformCheck(),
        new NvidiaCheck(executor),
        new DockerCheck(executor),
        new LocalPortsCheck(),
        new SshGuidanceCheck(executor),
        new IommuCheck(),
        new ThroughputCheck(throughputUrl),
        new NotVerifiedCapabilitiesCheck()
    ];
}

public sealed class PlatformCheck : IDiagnosticCheck
{
    public Task<CheckResult> ExecuteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var platform = OperatingSystem.IsLinux() ? "Linux" :
            OperatingSystem.IsWindows() ? "Windows" :
            OperatingSystem.IsMacOS() ? "macOS" : RuntimeInformation.OSDescription;
        return Task.FromResult(new CheckResult(
            "platform",
            "Platform and runtime",
            CheckStatus.Pass,
            $"{platform}; .NET {Environment.Version}; {RuntimeInformation.ProcessArchitecture}.",
            "Platform identification does not establish host compatibility."));
    }
}

public sealed class NvidiaCheck(ICommandExecutor executor) : IDiagnosticCheck
{
    public async Task<CheckResult> ExecuteAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux())
        {
            return new CheckResult(
                "nvidia",
                "NVIDIA GPU and driver",
                CheckStatus.Unsupported,
                "The MVP checks NVIDIA driver facts only on Linux.");
        }

        CommandResult query;
        try
        {
            query = await executor.ExecuteAsync(
                "nvidia-smi",
                ["--query-gpu=name,driver_version", "--format=csv,noheader"],
                TimeSpan.FromSeconds(6),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new CheckResult(
                "nvidia",
                "NVIDIA GPU and driver",
                CheckStatus.NotVerified,
                $"Could not run nvidia-smi: {exception.Message}",
                "No GPU or driver status is inferred from a missing utility.");
        }

        if (query.TimedOut)
        {
            return new CheckResult(
                "nvidia",
                "NVIDIA GPU and driver",
                CheckStatus.NotVerified,
                "nvidia-smi timed out after 6 seconds.");
        }

        if (!query.Succeeded)
        {
            return new CheckResult(
                "nvidia",
                "NVIDIA GPU and driver",
                CheckStatus.NeedsAttention,
                $"nvidia-smi exited with code {query.ExitCode}: {Trim(query.StandardError)}",
                "The utility did not provide verified GPU/driver facts.",
                "Check the NVIDIA driver installation and device access.");
        }

        var devices = query.StandardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (devices.Length == 0)
        {
            return new CheckResult(
                "nvidia",
                "NVIDIA GPU and driver",
                CheckStatus.NeedsAttention,
                "nvidia-smi returned no GPU rows.",
                "No GPU was verified.",
                "Check host GPU availability and driver/device access.");
        }

        var summary = string.Join("; ", devices.Select(device => $"GPU {device}"));
        string? cudaSupport = null;
        string? cudaLimitation = null;
        try
        {
            var version = await executor.ExecuteAsync(
                "nvidia-smi",
                [],
                TimeSpan.FromSeconds(6),
                cancellationToken);
            if (version.TimedOut)
            {
                cudaLimitation = "The driver-reported CUDA support query timed out.";
            }
            else if (!version.Succeeded)
            {
                cudaLimitation = $"The driver-reported CUDA support query exited with code {version.ExitCode}.";
            }
            else
            {
                var match = Regex.Match(version.StandardOutput, @"CUDA Version:\s*([0-9.]+)");
                if (match.Success)
                {
                    cudaSupport = match.Groups[1].Value;
                }
                else
                {
                    cudaLimitation = "The nvidia-smi output had no CUDA support field.";
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            cudaLimitation = $"The driver-reported CUDA support query failed: {Trim(exception.Message)}";
        }

        var cudaFact = cudaSupport is null
            ? " Driver-advertised CUDA support was not reported."
            : $" Driver-advertised CUDA support: {cudaSupport}.";
        return new CheckResult(
            "nvidia",
            "NVIDIA GPU and driver",
            CheckStatus.Pass,
            $"{summary}.{cudaFact}",
            string.Join(" ", new[]
            {
                "The CUDA value is driver-reported support, not an installed toolkit/runtime or workload test.",
                cudaLimitation
            }.Where(text => !string.IsNullOrWhiteSpace(text))),
            "Run a separate CUDA workload and GPU-container check if those capabilities are required.");
    }

    private static string Trim(string value) =>
        string.IsNullOrWhiteSpace(value) ? "no additional error details" : value.Trim();
}

public sealed class DockerCheck(ICommandExecutor executor) : IDiagnosticCheck
{
    public async Task<CheckResult> ExecuteAsync(CancellationToken cancellationToken)
    {
        CommandResult cli;
        try
        {
            cli = await executor.ExecuteAsync("docker", ["--version"], TimeSpan.FromSeconds(5), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new CheckResult(
                "docker",
                "Docker CLI and daemon",
                CheckStatus.NotVerified,
                $"Docker CLI could not be run: {Compact(exception.Message)}",
                "Docker CLI and daemon availability were not verified.",
                "Install or locate Docker if container hosting is intended.");
        }

        if (cli.TimedOut)
        {
            return new CheckResult("docker", "Docker CLI and daemon", CheckStatus.NotVerified, "Docker CLI check timed out.");
        }

        if (!cli.Succeeded)
        {
            return new CheckResult(
                "docker",
                "Docker CLI and daemon",
                CheckStatus.NotVerified,
                $"Docker CLI exited with code {cli.ExitCode}: {Compact(cli.StandardError)}");
        }

        CommandResult daemon;
        try
        {
            var localHost = OperatingSystem.IsWindows()
                ? "npipe:////./pipe/docker_engine"
                : "unix:///var/run/docker.sock";
            daemon = await executor.ExecuteAsync(
                "docker",
                ["--host", localHost, "info", "--format", "{{.ServerVersion}}"],
                TimeSpan.FromSeconds(6),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new CheckResult(
                "docker",
                "Docker CLI and daemon",
                CheckStatus.NeedsAttention,
                $"CLI available ({cli.StandardOutput.Trim()}), daemon could not be queried: {Compact(exception.Message)}",
                "A CLI installation does not establish daemon availability.",
                "Start or connect to the Docker daemon using the operator's normal workflow.");
        }

        if (!daemon.Succeeded || daemon.TimedOut || string.IsNullOrWhiteSpace(daemon.StandardOutput))
        {
            return new CheckResult(
                "docker",
                "Docker CLI and daemon",
                CheckStatus.NeedsAttention,
                $"CLI available ({cli.StandardOutput.Trim()}), local daemon unavailable, timed out, or returned no server version: {Compact(daemon.StandardError)}",
                "Only the standard local Docker socket/pipe is checked; rootless or custom sockets are not detected. NVIDIA Container Toolkit and GPU passthrough are not tested.",
                "Check daemon access; separately validate GPU-container support if needed.");
        }

        return new CheckResult(
            "docker",
            "Docker CLI and daemon",
            CheckStatus.Pass,
            $"CLI: {cli.StandardOutput.Trim()}; daemon server version: {daemon.StandardOutput.Trim()}.",
            "A working daemon does not establish NVIDIA Container Toolkit or GPU passthrough. Only the standard local socket/pipe is checked.");
    }

    private static string Compact(string value)
    {
        var compact = string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return compact.Length <= 240 ? compact : $"{compact[..237]}...";
    }
}

public sealed class LocalPortsCheck : IDiagnosticCheck
{
    private static readonly int[] PortsToReport = [22, 8080, 10000];

    public Task<CheckResult> ExecuteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var listeners = IPGlobalProperties.GetIPGlobalProperties()
                .GetActiveTcpListeners()
                .Select(endpoint => endpoint.Port)
                .Where(PortsToReport.Contains)
                .Distinct()
                .Order()
                .ToArray();
            var observation = listeners.Length == 0
                ? "No local TCP listeners found on ports 22, 8080, or 10000."
                : $"Local TCP listeners found on ports: {string.Join(", ", listeners)}.";
            return Task.FromResult(new CheckResult(
                "local-ports",
                "Local TCP listeners",
                CheckStatus.Pass,
                observation,
                "This observes local listeners only; it does not establish firewall policy, NAT, cloud rules, or external reachability."));
        }
        catch (Exception exception)
        {
            return Task.FromResult(new CheckResult(
                "local-ports",
                "Local TCP listeners",
                CheckStatus.NotVerified,
                $"Local listener state could not be read: {exception.Message}",
                "No firewall or external reachability conclusion is made."));
        }
    }
}

public sealed class SshGuidanceCheck(ICommandExecutor executor) : IDiagnosticCheck
{
    public async Task<CheckResult> ExecuteAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await executor.ExecuteAsync("ssh", ["-V"], TimeSpan.FromSeconds(5), cancellationToken);
            if (result.TimedOut || result.ExitCode != 0)
            {
                return new CheckResult(
                    "ssh-guidance",
                    "SSH client and access guidance",
                    CheckStatus.NotVerified,
                    $"SSH client check did not succeed: {result.StandardError.Trim()}",
                    "Client presence does not verify inbound SSH or remote reachability.");
            }

            var version = string.Join(" ", new[] { result.StandardOutput, result.StandardError }.Where(text => !string.IsNullOrWhiteSpace(text))).Trim();
            return new CheckResult(
                "ssh-guidance",
                "SSH client and access guidance",
                CheckStatus.Pass,
                $"SSH client is available ({version}).",
                "Inbound SSH, remote connection, host keys, firewall/NAT, and Vast.ai requirements are not verified.",
                "Verify from an external network with the intended account and key; do not expose or change SSH settings based on this check.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new CheckResult(
                "ssh-guidance",
                "SSH client and access guidance",
                CheckStatus.NotVerified,
                $"SSH client is unavailable or could not be checked: {exception.Message}",
                "Inbound access remains unverified.");
        }
    }
}

public sealed class IommuCheck : IDiagnosticCheck
{
    public Task<CheckResult> ExecuteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsLinux())
        {
            return Task.FromResult(new CheckResult(
                "iommu",
                "IOMMU / virtualization",
                CheckStatus.Unsupported,
                "The MVP only performs a best-effort Linux sysfs observation."));
        }

        const string groupPath = "/sys/kernel/iommu_groups";
        try
        {
            if (!Directory.Exists(groupPath))
            {
                return Task.FromResult(new CheckResult(
                    "iommu",
                    "IOMMU / virtualization",
                    CheckStatus.NotVerified,
                    "Linux IOMMU group interface is not present or readable.",
                    "Firmware, virtualization, and passthrough readiness cannot be inferred."));
            }

            var groups = Directory.EnumerateDirectories(groupPath).Take(4097).Count();
            var groupCount = groups > 4096 ? "4096+" : groups.ToString(CultureInfo.InvariantCulture);
            return Task.FromResult(new CheckResult(
                "iommu",
                "IOMMU / virtualization",
                CheckStatus.NotVerified,
                groups > 0
                    ? $"Linux exposes {groupCount} IOMMU group(s)."
                    : "Linux exposes no IOMMU groups in the observed sysfs interface.",
                "This best-effort observation does not verify firmware settings, device isolation, or passthrough readiness."));
        }
        catch (Exception exception)
        {
            return Task.FromResult(new CheckResult(
                "iommu",
                "IOMMU / virtualization",
                CheckStatus.NotVerified,
                $"Linux IOMMU groups could not be inspected: {exception.Message}",
                "Firmware and passthrough readiness remain unverified."));
        }
    }
}

public sealed class ThroughputCheck(string? endpoint) : IDiagnosticCheck
{
    private const int MaximumBytes = 1_048_576;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    public async Task<CheckResult> ExecuteAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return new CheckResult(
                "throughput",
                "Network throughput",
                CheckStatus.NotVerified,
                "Not measured; no endpoint was supplied.",
                "No GPU throughput is measured or implied.",
                "Optionally provide a trusted HTTP(S) URL to measure up to 1 MiB of endpoint-dependent transfer.");
        }

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            return new CheckResult(
                "throughput",
                "Network throughput",
                CheckStatus.NotVerified,
                "Not measured; endpoint must be an absolute HTTP(S) URL without embedded credentials.");
        }

        using var timeout = new CancellationTokenSource(Timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            using var client = new HttpClient { Timeout = Timeout };
            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, linked.Token);
            if (!response.IsSuccessStatusCode)
            {
                return new CheckResult(
                    "throughput",
                    "Network throughput",
                    CheckStatus.NotVerified,
                    $"Not measured; endpoint returned HTTP {(int)response.StatusCode}.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(linked.Token);
            var buffer = new byte[16_384];
            var bytes = 0;
            var timer = Stopwatch.StartNew();
            while (bytes < MaximumBytes)
            {
                var read = await stream.ReadAsync(
                    buffer.AsMemory(0, Math.Min(buffer.Length, MaximumBytes - bytes)),
                    linked.Token);
                if (read == 0)
                {
                    break;
                }

                bytes += read;
            }

            timer.Stop();
            var seconds = Math.Max(timer.Elapsed.TotalSeconds, 0.001);
            var megabits = bytes * 8d / seconds / 1_000_000d;
            return new CheckResult(
                "throughput",
                "Network throughput",
                CheckStatus.Pass,
                $"Endpoint {uri.GetLeftPart(UriPartial.Path)}: received {bytes} bytes in {timer.Elapsed.TotalSeconds:F2}s (approximately {megabits:F2} Mbit/s){(bytes == MaximumBytes ? ", capped at 1 MiB" : string.Empty)}.",
                "This bounded result is endpoint- and network-dependent; it is not GPU throughput or a marketplace performance promise.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return new CheckResult(
                "throughput",
                "Network throughput",
                CheckStatus.NotVerified,
                "Not measured; endpoint request exceeded the 15-second bound.");
        }
        catch (Exception exception)
        {
            return new CheckResult(
                "throughput",
                "Network throughput",
                CheckStatus.NotVerified,
                $"Not measured; endpoint request failed: {exception.Message}");
        }
    }
}

public sealed class NotVerifiedCapabilitiesCheck : IDiagnosticCheck
{
    public Task<CheckResult> ExecuteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new CheckResult(
            "unverified-capabilities",
            "Other host capabilities",
            CheckStatus.NotVerified,
            "CUDA workload, NVIDIA container passthrough, GPU compute throughput, external port reachability, and Vast.ai acceptance are not tested.",
            "This CLI provides evidence for operator review only; it does not certify or guarantee host acceptance.",
            "Complete separate workload, remote-network, and marketplace-specific validation as applicable."));
    }
}
