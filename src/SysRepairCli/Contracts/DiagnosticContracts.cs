namespace SysRepairCli;

public enum CheckStatus
{
    Pass,
    NeedsAttention,
    NotVerified,
    Unsupported
}

public enum OverallStatus
{
    ReadyForReview,
    NeedsAttention,
    InsufficientEvidence
}

public sealed record CheckResult(
    string CheckId,
    string DisplayName,
    CheckStatus Status,
    string Evidence,
    string? Limitation = null,
    string? NextStep = null,
    DateTimeOffset? ObservedAtUtc = null);

public sealed record ReadinessRun(
    IReadOnlyList<CheckResult> Checks,
    OverallStatus OverallStatus,
    string? RunId = null,
    DateTimeOffset? StartedAtUtc = null,
    DateTimeOffset? CompletedAtUtc = null);

public interface IDiagnosticCheck
{
    Task<CheckResult> ExecuteAsync(CancellationToken cancellationToken);
}

public sealed class DiagnosticEngine
{
    private readonly IReadOnlyList<IDiagnosticCheck> checks;

    public DiagnosticEngine(IEnumerable<IDiagnosticCheck> checks)
    {
        ArgumentNullException.ThrowIfNull(checks);
        this.checks = checks.ToArray();
    }

    public async Task<ReadinessRun> RunAsync(CancellationToken cancellationToken = default)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var results = new List<CheckResult>(checks.Count);
        foreach (var check in checks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await check.ExecuteAsync(cancellationToken);
            results.Add(result with { ObservedAtUtc = result.ObservedAtUtc ?? DateTimeOffset.UtcNow });
        }

        var status = results.Any(result => result.Status == CheckStatus.NeedsAttention)
            ? OverallStatus.NeedsAttention
            : results.Count == 0 || results.Any(result =>
                result.Status is CheckStatus.NotVerified or CheckStatus.Unsupported)
                ? OverallStatus.InsufficientEvidence
                : OverallStatus.ReadyForReview;

        return new ReadinessRun(
            results,
            status,
            Guid.NewGuid().ToString("N"),
            startedAt,
            DateTimeOffset.UtcNow);
    }
}
