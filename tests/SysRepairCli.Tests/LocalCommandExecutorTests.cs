using FluentAssertions;
using Moq;
using SysRepairCli;

namespace SysRepairCli.Tests;

public sealed class LocalCommandExecutorTests
{
    [Fact]
    public async Task Passes_script_path_and_arguments_as_individual_values_without_a_shell()
    {
        var runner = new Mock<IProcessRunner>();
        runner.Setup(x => x.RunAsync(
                It.IsAny<ProcessInvocation>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult(0, "ok", string.Empty));
        var executor = new LocalCommandExecutor(runner.Object);
        var arguments = new[]
        {
            "/opt/Sys Repair/diagnostic.py",
            "value; do-not-run-this"
        };

        await executor.ExecuteAsync("python3", arguments, TimeSpan.FromSeconds(2));

        runner.Verify(x => x.RunAsync(
                It.Is<ProcessInvocation>(invocation =>
                    invocation.Executable == "python3" &&
                    invocation.Arguments.SequenceEqual(arguments) &&
                    !invocation.UseShellExecute &&
                    invocation.Timeout == TimeSpan.FromSeconds(2)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Reports_nonzero_exit_as_failure_and_preserves_stderr()
    {
        var runner = new Mock<IProcessRunner>();
        runner.Setup(x => x.RunAsync(
                It.IsAny<ProcessInvocation>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult(23, string.Empty, "command failed"));
        var executor = new LocalCommandExecutor(runner.Object);

        var result = await executor.ExecuteAsync(
            "docker",
            ["info"],
            TimeSpan.FromSeconds(2));

        result.Succeeded.Should().BeFalse();
        result.ExitCode.Should().Be(23);
        result.StandardError.Should().Be("command failed");
    }

    [Fact]
    public void Timed_out_command_is_not_reported_as_successful_even_with_zero_exit_code()
    {
        new CommandResult(0, string.Empty, string.Empty, TimedOut: true)
            .Succeeded.Should().BeFalse();
    }
}
