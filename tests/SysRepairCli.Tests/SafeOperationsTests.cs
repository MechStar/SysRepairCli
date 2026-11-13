using FluentAssertions;
using Moq;
using SysRepairCli;

namespace SysRepairCli.Tests;

public sealed class SafeOperationsTests
{
    [Theory]
    [InlineData("operator", "host.example.net")]
    [InlineData("operator", "192.0.2.42")]
    [InlineData("operator", "2001:db8::42")]
    public void Normalizes_safe_ssh_targets(string user, string host)
    {
        SshTargetValidator.TryNormalize(user, host, out var target, out var error).Should().BeTrue(error);

        target.Should().StartWith("operator@");
    }

    [Theory]
    [InlineData("operator;touch", "host.example.net")]
    [InlineData("-oProxyCommand=bad", "host.example.net")]
    [InlineData("operator", "host.example.net;touch")]
    [InlineData("operator", "-oProxyCommand=bad")]
    public void Rejects_ssh_target_values_that_could_be_options_or_commands(string user, string host)
    {
        SshTargetValidator.TryNormalize(user, host, out _, out var error).Should().BeFalse();

        error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Ssh_probe_uses_fixed_remote_entrypoint_and_script()
    {
        var executor = new Mock<ICommandExecutor>();
        executor.Setup(x => x.ExecuteAsync(
                "ssh",
                It.IsAny<IReadOnlyList<string>>(),
                TimeSpan.FromSeconds(10),
                It.IsAny<CancellationToken>(),
                It.IsAny<string>()))
            .ReturnsAsync(new CommandResult(0, "SYSREPAIR_REMOTE_PROBE_OK\nLinux\n", string.Empty));
        var probe = new SshExecutionPath(executor.Object);

        var result = await probe.ProbeAsync("operator", "host.example.net");

        result.Status.Should().Be(CheckStatus.Pass);
        executor.Verify(x => x.ExecuteAsync(
            "ssh",
            It.Is<IReadOnlyList<string>>(arguments =>
                arguments.SequenceEqual(new[]
                {
                    "-T",
                    "-o", "BatchMode=yes",
                    "-o", "StrictHostKeyChecking=yes",
                    "-o", "ConnectTimeout=5",
                    "--",
                    "operator@host.example.net",
                    "sh -s"
                })),
            TimeSpan.FromSeconds(10),
            It.IsAny<CancellationToken>(),
            It.Is<string>(script =>
                script.Contains("SYSREPAIR_REMOTE_PROBE_OK", StringComparison.Ordinal) &&
                script.Contains("uname -s", StringComparison.Ordinal))),
            Times.Once);
    }

    [Fact]
    public async Task Ssh_probe_does_not_start_for_an_invalid_host()
    {
        var executor = new Mock<ICommandExecutor>();
        var result = await new SshExecutionPath(executor.Object)
            .ProbeAsync("operator", "host.example.net; malicious");

        result.Status.Should().Be(CheckStatus.NotVerified);
        executor.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task System_runner_uses_direct_process_and_bounds_captured_output()
    {
        var runner = new SystemProcessRunner();
        var result = await runner.RunAsync(
            new ProcessInvocation("dotnet", ["--version"], TimeSpan.FromSeconds(10), UseShellExecute: false, MaximumOutputCharacters: 3),
            CancellationToken.None);

        result.ExitCode.Should().Be(0);
        result.StandardOutput.Length.Should().BeLessThanOrEqualTo(3);
        result.Elapsed.Should().NotBeNull();
    }

    [Fact]
    public async Task System_runner_honors_cancellation_before_starting_a_process()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var act = () => new SystemProcessRunner().RunAsync(
            new ProcessInvocation("dotnet", ["--version"], TimeSpan.FromSeconds(10), UseShellExecute: false),
            cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Key_repair_changes_only_selected_file_to_owner_read_write()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        var path = Path.Combine(AppContext.BaseDirectory, $"key-permission-fixture-{Guid.NewGuid():N}");
        try
        {
            File.WriteAllText(path, "fixture only");
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite |
                UnixFileMode.GroupRead | UnixFileMode.OtherRead);
            var repair = new SshKeyPermissionRepair();
            var proposal = repair.CreateProposal(path);
            proposal.Should().NotBeNull();
            proposal!.CurrentState.Should().Be("0644");
            proposal.ProposedState.Should().Be("0600");

            var result = await repair.ApplyAsync(proposal, CancellationToken.None);

            result.Outcome.Should().Be(RepairOutcome.Applied);
            File.GetUnixFileMode(path).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void Key_repair_refuses_symbolic_links()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        var target = Path.Combine(AppContext.BaseDirectory, $"key-link-target-{Guid.NewGuid():N}");
        var link = Path.Combine(AppContext.BaseDirectory, $"key-link-{Guid.NewGuid():N}");
        try
        {
            File.WriteAllText(target, "fixture only");
            File.SetUnixFileMode(target, UnixFileMode.UserRead | UnixFileMode.GroupRead);
            File.CreateSymbolicLink(link, target);

            var act = () => new SshKeyPermissionRepair().CreateProposal(link);

            act.Should().Throw<IOException>().WithMessage("*symbolic link*");
        }
        finally
        {
            if (File.Exists(link))
            {
                File.Delete(link);
            }

            if (File.Exists(target))
            {
                File.Delete(target);
            }
        }
    }
}
