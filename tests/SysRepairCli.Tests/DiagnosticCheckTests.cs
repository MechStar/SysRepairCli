using FluentAssertions;
using Moq;
using SysRepairCli;

namespace SysRepairCli.Tests;

public sealed class DiagnosticCheckTests
{
    [Fact]
    public async Task Docker_cli_success_without_daemon_version_is_not_a_pass()
    {
        var executor = new Mock<ICommandExecutor>();
        executor.Setup(x => x.ExecuteAsync(
                "docker",
                It.Is<IReadOnlyList<string>>(arguments => arguments.SequenceEqual(new[] { "--version" })),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>()))
            .ReturnsAsync(new CommandResult(0, "Docker version 26.0", string.Empty));
        executor.Setup(x => x.ExecuteAsync(
                "docker",
                It.Is<IReadOnlyList<string>>(arguments => arguments.SequenceEqual(new[]
                {
                    "--host",
                    OperatingSystem.IsWindows() ? "npipe:////./pipe/docker_engine" : "unix:///var/run/docker.sock",
                    "info",
                    "--format",
                    "{{.ServerVersion}}"
                })),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>()))
            .ReturnsAsync(new CommandResult(0, string.Empty, string.Empty));

        var result = await new DockerCheck(executor.Object).ExecuteAsync(CancellationToken.None);

        result.Status.Should().Be(CheckStatus.NeedsAttention);
        result.Evidence.Should().Contain("returned no server version");
    }
}
