using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Serilog;

namespace AiQuotaTray.Usage;

/// <summary>
/// Reads Codex CLI rate limits by talking JSON-RPC to a short-lived
/// <c>codex app-server</c> child process over stdio, the same protocol the
/// Codex CLI's own client speaks. There is no REST endpoint for this data;
/// the app-server is the only supported source.
/// </summary>
public sealed class CodexUsageProvider : IUsageProvider
{
    private static readonly ILogger Logger = Log.ForContext<CodexUsageProvider>();
    private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan ExitGrace = TimeSpan.FromSeconds(3);
    private const int MaxAttempts = 2;

    public string ProviderId => "codex";

    public async Task<UsageResult> GetUsageAsync(CancellationToken cancellationToken)
    {
        var codexPath = await ResolveCodexPathAsync(cancellationToken).ConfigureAwait(false);
        Logger.Debug("where codex -> {CodexPath}", codexPath ?? "(not found)");

        if (codexPath is null)
        {
            return UsageResult.Failure(ProviderId, "codex-app-server", "Codex CLI is not installed or is not available in PATH.");
        }

        RpcSession session = new(null, null, null, false, string.Empty);
        try
        {
            for (var attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                session = await RunAppServerAsync(codexPath, attempt, cancellationToken).ConfigureAwait(false);

                // A cold `codex app-server` runs a marketplace refresh before it answers
                // anything; that can outlast one attempt's timeout. Only retry when we
                // never got a response at all — a response with a null account is a real
                // "not logged in", not worth retrying.
                if (session.AccountResponded || attempt == MaxAttempts)
                {
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "codex app-server run failed");
            return UsageResult.Failure(ProviderId, "codex-app-server", ex.Message);
        }

        if (session.Limits is null || session.Limits["rateLimits"] is null)
        {
            if (!session.AccountResponded)
            {
                return UsageResult.Failure(ProviderId, "codex-app-server",
                    "Codex app-server did not send an account/read response. See the diagnostics log (tray menu) for the raw output.");
            }

            if (session.Account is null)
            {
                return UsageResult.Failure(ProviderId, "codex-app-server", "Codex CLI is not authenticated. Run: codex login");
            }

            var errorMessage = session.LimitError?["message"]?.GetValue<string?>()
                ?? "Codex app-server did not return account rate limits.";
            return UsageResult.Failure(ProviderId, "codex-app-server", errorMessage);
        }

        return BuildResult(session.Account, session.Limits);
    }

    private static UsageResult BuildResult(JsonNode? account, JsonNode limits)
    {
        var rateLimits = limits["rateLimits"];

        var accountEmail = account?["email"]?.GetValue<string?>() ?? "Codex account";
        var loginMethod = account?["planType"]?.GetValue<string?>()
            ?? rateLimits?["planType"]?.GetValue<string?>()
            ?? account?["type"]?.GetValue<string?>()
            ?? "unknown";

        return new UsageResult(
            Provider: "codex",
            Source: "codex-app-server",
            Success: true,
            ErrorMessage: null,
            AccountLabel: $"{accountEmail} · {loginMethod}",
            Primary: ParseWindow(rateLimits?["primary"]),
            Secondary: ParseWindow(rateLimits?["secondary"]),
            Tertiary: null,
            UpdatedAt: DateTimeOffset.Now);
    }

    private static UsageWindow? ParseWindow(JsonNode? value)
    {
        if (value is null)
        {
            return null;
        }

        var usedPercent = value["usedPercent"]?.GetValue<double?>() ?? 0;
        var windowMinutes = value["windowDurationMins"]?.GetValue<int?>();
        var resetsAtSeconds = value["resetsAt"]?.GetValue<double?>();
        var resetsAt = resetsAtSeconds is null
            ? (DateTimeOffset?)null
            : DateTimeOffset.FromUnixTimeSeconds((long)resetsAtSeconds.Value);

        return new UsageWindow(usedPercent, windowMinutes, resetsAt, WindowLabel(windowMinutes));
    }

    /// <summary>Mirrors the window-naming convention Codex's own tooling uses (300 min = one session, 10080 min = one week).</summary>
    private static string WindowLabel(int? minutes) => minutes switch
    {
        null => "Quota",
        300 => "Session (5h)",
        10080 => "Weekly",
        _ when minutes >= 10080 && minutes % 10080 == 0 => Counted(minutes.Value / 10080, "week"),
        _ when minutes >= 1440 && minutes % 1440 == 0 => Counted(minutes.Value / 1440, "day"),
        _ when minutes >= 60 && minutes % 60 == 0 => Counted(minutes.Value / 60, "hour"),
        _ => Counted(minutes.Value, "minute"),
    };

    private static string Counted(int count, string unit) => $"{count} {unit}{(count == 1 ? "" : "s")}";

    /// <summary>
    /// Resolves the exact executable/script `codex` refers to via the real
    /// <c>where.exe</c>, instead of guessing — this is also what tells us
    /// whether to exec it directly or route it through cmd.exe (see below).
    /// </summary>
    private static async Task<string?> ResolveCodexPathAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "where.exe",
                    ArgumentList = { "codex" },
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };
            process.Start();

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            using var exitCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await process.WaitForExitAsync(exitCts.Token).ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                return null;
            }

            // `where` can list several matches (e.g. both codex.cmd and codex.ps1); the
            // first line is the one PATHEXT/cmd.exe would actually run.
            return output
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static async Task<RpcSession> RunAppServerAsync(string codexPath, int attempt, CancellationToken cancellationToken)
    {
        var startInfo = BuildStartInfo(codexPath);
        Logger.Debug("Launching codex app-server, attempt {Attempt}: {FileName} {Arguments}",
            attempt, startInfo.FileName, startInfo.Arguments);

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        var version = typeof(CodexUsageProvider).Assembly.GetName().Version?.ToString() ?? "0.0.0";
        var initializeRequest = "{\"method\":\"initialize\",\"id\":0,\"params\":{\"clientInfo\":{\"name\":\"ai_quota_tray\",\"title\":\"AiQuotaTray\",\"version\":\""
            + version + "\"}}}";
        string[] requests =
        [
            initializeRequest,
            """{"method":"initialized","params":{}}""",
            """{"method":"account/read","id":1,"params":{"refreshToken":false}}""",
            """{"method":"account/rateLimits/read","id":2,"params":{}}""",
        ];

        foreach (var request in requests)
        {
            await process.StandardInput.WriteLineAsync(request.AsMemory(), cancellationToken).ConfigureAwait(false);
        }
        await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);

        JsonNode? account = null;
        JsonNode? limits = null;
        JsonNode? limitError = null;
        var accountSeen = false;
        var limitsSeen = false;
        var rawLines = new StringBuilder();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(ResponseTimeout);

        try
        {
            while (!accountSeen || !limitsSeen)
            {
                var line = await process.StandardOutput.ReadLineAsync(timeoutCts.Token).ConfigureAwait(false);
                if (line is null)
                {
                    rawLines.AppendLine("(stdout closed)");
                    break;
                }
                rawLines.AppendLine(line);
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                JsonNode? node;
                try
                {
                    node = JsonNode.Parse(line);
                }
                catch (JsonException)
                {
                    continue;
                }

                var id = node?["id"]?.GetValue<int?>();
                if (id == 1)
                {
                    account = node!["result"]?["account"];
                    accountSeen = true;
                }
                else if (id == 2)
                {
                    limits = node!["result"];
                    limitError = node["error"];
                    limitsSeen = true;
                }
            }
        }
        catch (OperationCanceledException)
        {
            rawLines.AppendLine("(timed out waiting for a response)");
        }

        // Only close stdin now — codex app-server exits as soon as it sees EOF on
        // its input, even if it hasn't answered every queued request yet. Closing
        // this earlier (right after writing) starved account/read and
        // account/rateLimits/read of a response before they were ever handled.
        try
        {
            process.StandardInput.Close();
        }
        catch
        {
            // Process may already have exited.
        }

        await StopProcessAsync(process).ConfigureAwait(false);

        var stdErr = await SafeReadStdErrAsync(stdErrTask).ConfigureAwait(false);
        Logger.Debug("codex app-server stdout:\n{Stdout}", rawLines.Length > 0 ? rawLines.ToString() : "(no output)");
        if (!string.IsNullOrWhiteSpace(stdErr))
        {
            Logger.Warning("codex app-server stderr:\n{Stderr}", stdErr);
        }

        return new RpcSession(account, limits, limitError, accountSeen, stdErr);
    }

    /// <summary>
    /// `where codex` may resolve to a real .exe or to an npm-style .cmd/.ps1
    /// shim. A .exe can be launched directly — one less process hop between
    /// us and its stdio. A script still needs cmd.exe to interpret it.
    /// </summary>
    private static ProcessStartInfo BuildStartInfo(string codexPath)
    {
        var isDirectlyExecutable = codexPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            || codexPath.EndsWith(".com", StringComparison.OrdinalIgnoreCase);

        if (isDirectlyExecutable)
        {
            var info = new ProcessStartInfo
            {
                FileName = codexPath,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            info.ArgumentList.Add("app-server");
            return info;
        }

        return new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/d /c \"{codexPath}\" app-server",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
    }

    private static async Task<string> SafeReadStdErrAsync(Task<string> stdErrTask)
    {
        try
        {
            return await stdErrTask.ConfigureAwait(false);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static async Task StopProcessAsync(Process process)
    {
        try
        {
            using var exitCts = new CancellationTokenSource(ExitGrace);
            await process.WaitForExitAsync(exitCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Already exited.
        }
    }

    private sealed record RpcSession(JsonNode? Account, JsonNode? Limits, JsonNode? LimitError, bool AccountResponded, string StdErr);
}
