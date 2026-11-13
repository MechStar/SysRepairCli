using System.Text.Json;
using System.Text.Json.Serialization;
using Spectre.Console;

namespace SysRepairCli;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "--help" or "-h" or "help")
        {
            WriteHelp();
            return 0;
        }

        try
        {
            return args[0] switch
            {
                "check" => await RunCheckAsync(args[1..]),
                "ssh-check" => await RunSshCheckAsync(args[1..]),
                "repair-key" => await RunRepairAsync(args[1..]),
                _ => UsageError($"Unknown command '{args[0]}'.")
            };
        }
        catch (OperationCanceledException)
        {
            AnsiConsole.MarkupLine("[yellow]Cancelled. No pending repair was started.[/]");
            return 130;
        }
        catch (Exception exception)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(exception.Message)}");
            return 2;
        }
    }

    private static async Task<int> RunCheckAsync(string[] args)
    {
        var options = ParseOptions(args, ["--json", "--throughput"]);
        if (options is null)
        {
            return 2;
        }

        var json = options.ContainsKey("--json");
        var throughputUrl = options.GetValueOrDefault("--throughput");
        var executor = new LocalCommandExecutor(new SystemProcessRunner());
        var run = await new DiagnosticEngine(StandardDiagnosticChecks.Create(executor, throughputUrl))
            .RunAsync();

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(run, JsonOptions));
        }
        else
        {
            RenderRun(run);
        }

        return run.OverallStatus == OverallStatus.ReadyForReview ? 0 : 1;
    }

    private static async Task<int> RunSshCheckAsync(string[] args)
    {
        var options = ParseOptions(args, ["--user", "--host"]);
        if (options is null ||
            !options.TryGetValue("--user", out var user) ||
            !options.TryGetValue("--host", out var host))
        {
            return UsageError("ssh-check requires --user USER and --host HOST.");
        }

        var check = await new SshExecutionPath(
                new LocalCommandExecutor(new SystemProcessRunner()))
            .ProbeAsync(user!, host!);
        RenderCheck(check);
        return check.Status == CheckStatus.Pass ? 0 : 1;
    }

    private static async Task<int> RunRepairAsync(string[] args)
    {
        var options = ParseOptions(args, ["--key"]);
        if (options is null || !options.TryGetValue("--key", out var keyPath))
        {
            return UsageError("repair-key requires --key PATH and always requires an interactive per-action confirmation.");
        }

        var repair = new SshKeyPermissionRepair();
        RepairProposal proposal;
        try
        {
            proposal = repair.CreateProposal(keyPath!) ??
                throw new InvalidOperationException("The selected file already has owner-only permissions; no repair is needed.");
        }
        catch (Exception exception)
        {
            AnsiConsole.MarkupLine($"[red]Repair unavailable:[/] {Markup.Escape(exception.Message)}");
            return 1;
        }

        var coordinator = new RepairCoordinator(new ConsoleRepairApproval(), repair);
        var result = await coordinator.ApplyAsync(proposal);
        var color = result.Outcome == RepairOutcome.Applied ? "green" :
            result.Outcome == RepairOutcome.NotApproved ? "yellow" : "red";
        AnsiConsole.MarkupLine($"[{color}]{result.Outcome}[/]: {Markup.Escape(result.Evidence)}");
        return result.Outcome == RepairOutcome.Applied ? 0 :
            result.Outcome == RepairOutcome.NotApproved ? 1 : 2;
    }

    private static Dictionary<string, string?>? ParseOptions(string[] args, string[] allowed)
    {
        var options = new Dictionary<string, string?>(StringComparer.Ordinal);
        for (var index = 0; index < args.Length; index++)
        {
            var option = args[index];
            if (!allowed.Contains(option, StringComparer.Ordinal))
            {
                UsageError($"Unknown option '{option}'.");
                return null;
            }

            if (option == "--json")
            {
                if (!options.TryAdd(option, null))
                {
                    UsageError($"Option '{option}' may only be specified once.");
                    return null;
                }

                continue;
            }

            if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                UsageError($"Option '{option}' requires a value.");
                return null;
            }

            if (!options.TryAdd(option, args[++index]))
            {
                UsageError($"Option '{option}' may only be specified once.");
                return null;
            }
        }

        return options;
    }

    private static void RenderRun(ReadinessRun run)
    {
        AnsiConsole.Write(new Rule("[bold]SysRepairCli host readiness evidence[/]").RuleStyle("grey"));
        AnsiConsole.MarkupLine($"Overall: {Colorize(StatusName(run.OverallStatus), run.OverallStatus == OverallStatus.ReadyForReview ? "green" : run.OverallStatus == OverallStatus.NeedsAttention ? "red" : "yellow")}");
        AnsiConsole.MarkupLine("[dim]Readiness for operator review only; not Vast.ai acceptance or certification.[/]");

        foreach (var check in run.Checks)
        {
            var color = check.Status switch
            {
                CheckStatus.Pass => "green",
                CheckStatus.NeedsAttention => "red",
                CheckStatus.Unsupported => "grey",
                _ => "yellow"
            };
            AnsiConsole.MarkupLine($"[{color}]{Markup.Escape(StatusName(check.Status))}[/] [bold]{Markup.Escape(check.DisplayName)}[/] — {Markup.Escape(check.Evidence)}");
            if (!string.IsNullOrWhiteSpace(check.Limitation))
            {
                AnsiConsole.MarkupLine($"  [dim]Limit: {Markup.Escape(check.Limitation)}[/]");
            }

            if (!string.IsNullOrWhiteSpace(check.NextStep))
            {
                AnsiConsole.MarkupLine($"  Next: {Markup.Escape(check.NextStep)}");
            }
        }

        AnsiConsole.WriteLine();
    }

    private static void RenderCheck(CheckResult check)
    {
        var color = check.Status switch
        {
            CheckStatus.Pass => "green",
            CheckStatus.NeedsAttention => "red",
            CheckStatus.Unsupported => "grey",
            _ => "yellow"
        };
        AnsiConsole.MarkupLine($"[{color}]{Markup.Escape(StatusName(check.Status))}[/] — {Markup.Escape(check.DisplayName)}");
        AnsiConsole.MarkupLine(Markup.Escape(check.Evidence));
        if (!string.IsNullOrWhiteSpace(check.Limitation))
        {
            AnsiConsole.MarkupLine($"[dim]{Markup.Escape(check.Limitation)}[/]");
        }
    }

    private static string Colorize(string text, string color) => $"[{color}]{Markup.Escape(text)}[/]";

    private static string StatusName(CheckStatus status) => status switch
    {
        CheckStatus.NeedsAttention => "Needs attention",
        CheckStatus.NotVerified => "Not verified",
        _ => status.ToString()
    };

    private static string StatusName(OverallStatus status) => status switch
    {
        OverallStatus.ReadyForReview => "Ready for review",
        OverallStatus.NeedsAttention => "Needs attention",
        _ => "Insufficient evidence"
    };

    private static int UsageError(string message)
    {
        AnsiConsole.MarkupLine($"[red]Usage error:[/] {Markup.Escape(message)}");
        AnsiConsole.MarkupLine("[dim]Run 'SysRepairCli --help' for usage.[/]");
        return 2;
    }

    private static void WriteHelp()
    {
        AnsiConsole.MarkupLine("[bold]SysRepairCli[/] — evidence-based host readiness checks");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("  [cyan]check[/]");
        AnsiConsole.WriteLine("    [--json] [--throughput HTTP(S)-URL]");
        AnsiConsole.MarkupLine("  [cyan]ssh-check[/] --user USER --host HOST");
        AnsiConsole.MarkupLine("  [cyan]repair-key[/] --key PATH");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("Checks are bounded and read-only. SSH probes only run fixed read-only remote content.");
        AnsiConsole.MarkupLine("repair-key only changes the selected key to owner read/write (0600) after one interactive confirmation; no --yes or batch mode exists.");
        AnsiConsole.MarkupLine("Exit codes: 0 ready/action applied; 1 needs attention, insufficient evidence, or declined repair; 2 invocation/repair failure; 130 cancelled.");
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    private sealed class ConsoleRepairApproval : IRepairApproval
    {
        public Task<bool> IsApprovedAsync(
            RepairProposal proposal,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AnsiConsole.Write(new Rule("[bold yellow]Review one proposed repair[/]").RuleStyle("yellow"));
            AnsiConsole.MarkupLine($"Target: [bold]{Markup.Escape(proposal.Target)}[/]");
            AnsiConsole.MarkupLine($"Current permissions: [bold]{Markup.Escape(proposal.CurrentState)}[/]");
            AnsiConsole.MarkupLine($"Proposed permissions: [bold]{Markup.Escape(proposal.ProposedState)}[/]");
            AnsiConsole.MarkupLine($"Exact effect: {Markup.Escape(proposal.Effect)}");
            AnsiConsole.MarkupLine("[yellow]No sudo/elevation is used. Declining makes no change.[/]");

            if (Console.IsInputRedirected || Console.IsOutputRedirected)
            {
                AnsiConsole.MarkupLine("[yellow]Interactive confirmation is unavailable; repair was not applied.[/]");
                return Task.FromResult(false);
            }

            var approved = AnsiConsole.Confirm("Apply this action now?", defaultValue: false);
            return Task.FromResult(approved);
        }
    }
}
