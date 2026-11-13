using FluentAssertions;
using Moq;
using SysRepairCli;

namespace SysRepairCli.Tests;

public sealed class DiagnosticEngineTests
{
    [Fact]
    public async Task Empty_check_set_is_insufficient_evidence()
    {
        var run = await new DiagnosticEngine([]).RunAsync();

        run.Checks.Should().BeEmpty();
        run.OverallStatus.Should().Be(OverallStatus.InsufficientEvidence);
    }

    [Fact]
    public async Task Passing_checks_keep_their_order_and_are_ready_for_review()
    {
        var first = new Mock<IDiagnosticCheck>();
        first.Setup(x => x.ExecuteAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CheckResult("first", "First", CheckStatus.Pass, "Evidence."));
        var second = new Mock<IDiagnosticCheck>();
        second.Setup(x => x.ExecuteAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CheckResult("second", "Second", CheckStatus.Pass, "Evidence."));

        var run = await new DiagnosticEngine([first.Object, second.Object]).RunAsync();

        run.Checks.Select(check => check.CheckId).Should().Equal("first", "second");
        run.OverallStatus.Should().Be(OverallStatus.ReadyForReview);
    }

    [Fact]
    public async Task Unsupported_check_is_preserved_and_cannot_make_run_ready()
    {
        var check = new Mock<IDiagnosticCheck>();
        check.Setup(x => x.ExecuteAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CheckResult(
                "nvidia",
                "NVIDIA driver",
                CheckStatus.Unsupported,
                "This check is not supported on this platform."));
        var engine = new DiagnosticEngine([check.Object]);

        var run = await engine.RunAsync();

        run.Checks.Should().ContainSingle()
            .Which.Status.Should().Be(CheckStatus.Unsupported);
        run.OverallStatus.Should().Be(OverallStatus.InsufficientEvidence);
        run.RunId.Should().NotBeNullOrWhiteSpace();
        run.StartedAtUtc.Should().NotBeNull();
        run.CompletedAtUtc.Should().BeOnOrAfter(run.StartedAtUtc!.Value);
        run.Checks[0].ObservedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task Needs_attention_check_takes_precedence_over_unverified_evidence()
    {
        var needsAttention = new Mock<IDiagnosticCheck>();
        needsAttention.Setup(x => x.ExecuteAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CheckResult(
                "docker",
                "Docker",
                CheckStatus.NeedsAttention,
                "Docker daemon is unavailable."));
        var unsupported = new Mock<IDiagnosticCheck>();
        unsupported.Setup(x => x.ExecuteAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CheckResult(
                "iommu",
                "IOMMU",
                CheckStatus.Unsupported,
                "Unsupported on this platform."));
        var engine = new DiagnosticEngine([needsAttention.Object, unsupported.Object]);

        var run = await engine.RunAsync();

        run.OverallStatus.Should().Be(OverallStatus.NeedsAttention);
    }

    [Fact]
    public async Task Cancellation_is_not_converted_to_a_successful_run()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var check = new Mock<IDiagnosticCheck>();
        check.Setup(x => x.ExecuteAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var act = () => new DiagnosticEngine([check.Object]).RunAsync(cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
