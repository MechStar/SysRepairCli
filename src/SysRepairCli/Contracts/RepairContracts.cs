namespace SysRepairCli;

public enum RepairOutcome
{
    Applied,
    Declined,
    NotApproved,
    Failed
}

public sealed record RepairProposal(
    string ActionId,
    string Target,
    string CurrentState,
    string ProposedState,
    string Effect);

public sealed record RepairResult(
    string ActionId,
    RepairOutcome Outcome,
    string Evidence,
    DateTimeOffset? CompletedAtUtc = null);

public interface IRepairApproval
{
    Task<bool> IsApprovedAsync(
        RepairProposal proposal,
        CancellationToken cancellationToken);
}

public interface IKeyPermissionRepair
{
    Task<RepairResult> ApplyAsync(
        RepairProposal proposal,
        CancellationToken cancellationToken);
}

public sealed class RepairCoordinator
{
    private readonly IRepairApproval approval;
    private readonly IKeyPermissionRepair repair;

    public RepairCoordinator(IRepairApproval approval, IKeyPermissionRepair repair)
    {
        this.approval = approval ?? throw new ArgumentNullException(nameof(approval));
        this.repair = repair ?? throw new ArgumentNullException(nameof(repair));
    }

    public async Task<RepairResult> ApplyAsync(
        RepairProposal proposal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        cancellationToken.ThrowIfCancellationRequested();
        if (!await approval.IsApprovedAsync(proposal, cancellationToken))
        {
            return new RepairResult(
                proposal.ActionId,
                RepairOutcome.NotApproved,
                "The action was not approved and no change was made.",
                DateTimeOffset.UtcNow);
        }

        try
        {
            var result = await repair.ApplyAsync(proposal, cancellationToken);
            return result with { CompletedAtUtc = result.CompletedAtUtc ?? DateTimeOffset.UtcNow };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new RepairResult(
                proposal.ActionId,
                RepairOutcome.Failed,
                $"The action failed: {exception.Message}",
                DateTimeOffset.UtcNow);
        }
    }
}
