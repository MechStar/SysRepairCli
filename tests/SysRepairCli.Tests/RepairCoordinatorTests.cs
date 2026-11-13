using FluentAssertions;
using Moq;
using SysRepairCli;

namespace SysRepairCli.Tests;

public sealed class RepairCoordinatorTests
{
    [Fact]
    public async Task Approval_denied_does_not_execute_repair()
    {
        var proposal = new RepairProposal(
            "key-permissions",
            "/home/operator/.ssh/id_ed25519",
            "0644",
            "0600",
            "Restrict the private key to its owner.");
        var approval = new Mock<IRepairApproval>();
        approval.Setup(x => x.IsApprovedAsync(proposal, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var repair = new Mock<IKeyPermissionRepair>();
        var coordinator = new RepairCoordinator(approval.Object, repair.Object);

        var result = await coordinator.ApplyAsync(proposal);

        result.Outcome.Should().Be(RepairOutcome.NotApproved);
        repair.Verify(
            x => x.ApplyAsync(proposal, It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Approved_proposal_applies_only_that_action()
    {
        var proposal = new RepairProposal(
            "key-permissions",
            "/home/operator/.ssh/id_ed25519",
            "0644",
            "0600",
            "Restrict the private key to its owner.");
        var approval = new Mock<IRepairApproval>();
        approval.Setup(x => x.IsApprovedAsync(proposal, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var expected = new RepairResult("key-permissions", RepairOutcome.Applied, "Permissions updated.");
        var repair = new Mock<IKeyPermissionRepair>();
        repair.Setup(x => x.ApplyAsync(proposal, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);
        var coordinator = new RepairCoordinator(approval.Object, repair.Object);

        var result = await coordinator.ApplyAsync(proposal);

        result.ActionId.Should().Be(expected.ActionId);
        result.Outcome.Should().Be(expected.Outcome);
        result.Evidence.Should().Be(expected.Evidence);
        result.CompletedAtUtc.Should().NotBeNull();
        repair.Verify(x => x.ApplyAsync(proposal, It.IsAny<CancellationToken>()), Times.Once);
    }
}
