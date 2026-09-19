using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AiQuotaTray.Usage;

/// <summary>
/// Reads Codex CLI rate limits by talking JSON-RPC to a short-lived
/// <c>codex app-server</c> child process over stdio, the same protocol the
/// Codex CLI's own client speaks. There is no REST endpoint for this data;
/// the app-server is the only supported source.
/// </summary>
public sealed class CodexUsageProvider : IUsageProvider
{
    private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan ExitGrace = TimeSpan.FromSeconds(3);

    public string ProviderId => "codex";

    public async Task<UsageResult> GetUsageAsync(CancellationToken cancellationToken)
    {
        RpcSession session;
        try
        {
            session = await RunAppServerAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return UsageResult.Failure(ProviderId, "codex-app-server", ex.Message);
        }

        if (session.Limits is null || session.Limits["rateLimits"] is null)
        {
            if (session.Account is null)
            {
                var missingCli = session.StdErr.Contains("not recognized", StringComparison.OrdinalIgnoreCase)
                    || session.StdErr.Contains("No such file", StringComparison.OrdinalIgnoreCase)
                    || session.StdErr.Contains("cannot find the file", StringComparison.OrdinalIgnoreCase);

                var message = missingCli
                    ? "Codex CLI is not installed or is not available in PATH."
                    : "Codex CLI is not authenticated. Run: codex login";
                return UsageResult.Failure(ProviderId, "codex-app-server", message);
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

    private static async Task<RpcSession> RunAppServerAsync(CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                // Routed through cmd.exe so PATH/PATHEXT resolution finds codex.cmd
                // (npm global installs) as well as a standalone codex.exe.
                Arguments = "/d /c codex app-server",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };

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
        process.StandardInput.Close();

        JsonNode? account = null;
        JsonNode? limits = null;
        JsonNode? limitError = null;
        var accountSeen = false;
        var limitsSeen = false;

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(ResponseTimeout);

        try
        {
            while (!accountSeen || !limitsSeen)
            {
                var line = await process.StandardOutput.ReadLineAsync(timeoutCts.Token).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }
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
            // No response within the timeout; fall through with whatever we collected.
        }

        await StopProcessAsync(process).ConfigureAwait(false);

        var stdErr = await SafeReadStdErrAsync(stdErrTask).ConfigureAwait(false);

        return new RpcSession(account, limits, limitError, stdErr);
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

    private sealed record RpcSession(JsonNode? Account, JsonNode? Limits, JsonNode? LimitError, string StdErr);
}
