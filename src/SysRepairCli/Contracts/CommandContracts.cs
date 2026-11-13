using System.Diagnostics;
using System.Text;

namespace SysRepairCli;

public sealed record ProcessInvocation(
    string Executable,
    IReadOnlyList<string> Arguments,
    TimeSpan Timeout,
    bool UseShellExecute,
    string? StandardInput = null,
    int MaximumOutputCharacters = 65_536);

public sealed record CommandResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut = false,
    TimeSpan? Elapsed = null)
{
    public bool Succeeded => ExitCode == 0 && !TimedOut;
}

public interface IProcessRunner
{
    Task<CommandResult> RunAsync(
        ProcessInvocation invocation,
        CancellationToken cancellationToken);
}

public sealed class SystemProcessRunner : IProcessRunner
{
    public async Task<CommandResult> RunAsync(
        ProcessInvocation invocation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        if (string.IsNullOrWhiteSpace(invocation.Executable) ||
            invocation.Executable.Contains('\0'))
        {
            throw new ArgumentException("A valid executable is required.", nameof(invocation));
        }

        if (invocation.Timeout <= TimeSpan.Zero || invocation.Timeout > TimeSpan.FromMinutes(2))
        {
            throw new ArgumentOutOfRangeException(nameof(invocation), "Process timeout must be between zero and two minutes.");
        }

        if (invocation.MaximumOutputCharacters < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(invocation), "Output limit must be positive.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (invocation.Arguments.Any(argument => argument is null || argument.Contains('\0')))
        {
            throw new ArgumentException("Arguments must not be null or contain NUL characters.", nameof(invocation));
        }

        if (invocation.UseShellExecute)
        {
            throw new ArgumentException("Shell execution is not supported.", nameof(invocation));
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = invocation.Executable,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = invocation.StandardInput is not null,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in invocation.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var stopwatch = Stopwatch.StartNew();
        if (!process.Start())
        {
            throw new InvalidOperationException($"Could not start executable '{invocation.Executable}'.");
        }

        var stdoutTask = ReadBoundedAsync(process.StandardOutput, invocation.MaximumOutputCharacters);
        var stderrTask = ReadBoundedAsync(process.StandardError, invocation.MaximumOutputCharacters);
        using var timeout = new CancellationTokenSource(invocation.Timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            if (invocation.StandardInput is not null)
            {
                try
                {
                    await process.StandardInput.WriteAsync(invocation.StandardInput.AsMemory(), linked.Token);
                }
                catch (IOException)
                {
                }
                finally
                {
                    process.StandardInput.Close();
                }
            }

            await process.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException)
        {
            if (invocation.StandardInput is not null)
            {
                process.StandardInput.Close();
            }

            TryKill(process);
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(stdoutTask, stderrTask);
            cancellationToken.ThrowIfCancellationRequested();
            stopwatch.Stop();
            return new CommandResult(
                -1,
                stdoutTask.Result,
                stderrTask.Result,
                TimedOut: true,
                Elapsed: stopwatch.Elapsed);
        }

        var output = await stdoutTask;
        var error = await stderrTask;
        stopwatch.Stop();
        return new CommandResult(process.ExitCode, output, error, Elapsed: stopwatch.Elapsed);
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, int maximumCharacters)
    {
        var output = new StringBuilder(Math.Min(maximumCharacters, 4096));
        var buffer = new char[4096];
        var truncated = false;
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory())) > 0)
        {
            var remaining = maximumCharacters - output.Length;
            if (remaining > 0)
            {
                output.Append(buffer, 0, Math.Min(read, remaining));
            }

            truncated |= read > remaining;
        }

        if (truncated && output.Length >= 3)
        {
            output.Length -= 3;
            output.Append("...");
        }

        return output.ToString();
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }
}

public interface ICommandExecutor
{
    Task<CommandResult> ExecuteAsync(
        string executable,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken = default,
        string? standardInput = null);
}

public sealed class LocalCommandExecutor : ICommandExecutor
{
    private readonly IProcessRunner processRunner;

    public LocalCommandExecutor(IProcessRunner processRunner)
    {
        this.processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
    }

    public Task<CommandResult> ExecuteAsync(
        string executable,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken = default,
        string? standardInput = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentNullException.ThrowIfNull(arguments);
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(2))
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be between zero and two minutes.");
        }

        return processRunner.RunAsync(
            new ProcessInvocation(
                executable,
                arguments.ToArray(),
                timeout,
                UseShellExecute: false,
                StandardInput: standardInput),
            cancellationToken);
    }
}
