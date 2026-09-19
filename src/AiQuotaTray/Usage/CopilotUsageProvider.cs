using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AiQuotaTray.Usage;

/// <summary>
/// Reads GitHub Copilot's quota via the same internal endpoint the VS Code
/// Copilot Chat extension itself calls (<c>copilot_internal/user</c>). It is
/// undocumented and unversioned, so a GitHub-side response shape change can
/// break parsing without notice; there is no public/documented alternative today.
/// </summary>
public sealed class CopilotUsageProvider : IUsageProvider
{
    private const string ApiUrl = "https://api.github.com/copilot_internal/user";

    // Values a real VS Code + Copilot Chat install would send; the endpoint
    // is gated on looking like that client.
    private const string EditorVersion = "vscode/1.105.0";
    private const string CopilotUserAgent = "GitHubCopilotChat/0.32.0";
    private const string GitHubApiVersion = "2026-04-01";

    private static readonly HttpClient HttpClient = CreateHttpClient();

    public string ProviderId => "copilot";

    public async Task<UsageResult> GetUsageAsync(CancellationToken cancellationToken)
    {
        var token = await ResolveTokenAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(token))
        {
            return UsageResult.Failure(ProviderId, "github-copilot-api",
                "GitHub token not found. Run: gh auth login (or set GH_TOKEN / COPILOT_GITHUB_TOKEN).");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, ApiUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("token", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("Editor-Version", EditorVersion);
        request.Headers.Add("X-GitHub-Api-Version", GitHubApiVersion);
        request.Headers.UserAgent.ParseAdd(CopilotUserAgent);

        HttpResponseMessage response;
        try
        {
            response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return UsageResult.Failure(ProviderId, "github-copilot-api", $"GitHub Copilot usage request failed: {ex.Message}");
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var message = TryGetErrorMessage(body) ?? $"GitHub Copilot usage request failed with HTTP {(int)response.StatusCode}.";
            return UsageResult.Failure(ProviderId, "github-copilot-api", message);
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(body);
        }
        catch (JsonException)
        {
            return UsageResult.Failure(ProviderId, "github-copilot-api", "GitHub Copilot returned invalid JSON.");
        }

        if (root is null)
        {
            return UsageResult.Failure(ProviderId, "github-copilot-api", "GitHub Copilot returned an empty response.");
        }

        return BuildResult(root);
    }

    private static UsageResult BuildResult(JsonNode root)
    {
        var quotaSnapshots = root["quota_snapshots"];
        var premium = new QuotaSnapshot(quotaSnapshots?["premium_interactions"]);
        var chat = new QuotaSnapshot(quotaSnapshots?["chat"]);
        var completions = new QuotaSnapshot(quotaSnapshots?["completions"]);

        var resetAt = ParseResetDate(root["quota_reset_date_utc"]?.GetValue<string?>() ?? root["quota_reset_date"]?.GetValue<string?>());
        var planLabel = PlanLabel(root);
        var login = root["login"]?.GetValue<string?>() ?? "GitHub Copilot";
        var accountLabel = planLabel.Length > 0 ? $"{login} · {planLabel}" : login;
        var tokenBasedBilling = root["token_based_billing"]?.GetValue<bool?>() ?? false;
        var premiumLabel = tokenBasedBilling ? "Premium requests · AI credits" : "Premium requests";

        var primary = new UsageWindow(
            UsedPercent: Math.Clamp(premium.UsedPercent, 0, 100),
            WindowMinutes: 43200,
            ResetsAt: resetAt,
            ResetDescription: premiumLabel,
            DisplayValue: premium.Display());

        var secondary = IsSignificant(chat)
            ? new UsageWindow(chat.UsedPercent, 43200, resetAt, "Chat", chat.Display())
            : null;

        var tertiary = IsSignificant(completions)
            ? new UsageWindow(completions.UsedPercent, 43200, resetAt, "Completions", completions.Display())
            : null;

        return new UsageResult(
            Provider: "copilot",
            Source: "github-copilot-api",
            Success: true,
            ErrorMessage: null,
            AccountLabel: accountLabel,
            Primary: primary,
            Secondary: secondary,
            Tertiary: tertiary,
            UpdatedAt: DateTimeOffset.Now);

        // Unlimited-with-no-entitlement windows carry no signal for the popup.
        static bool IsSignificant(QuotaSnapshot snapshot) => !(snapshot.Unlimited && snapshot.Entitlement == 0);
    }

    private static string PlanLabel(JsonNode root)
    {
        var sku = (root["access_type_sku"]?.GetValue<string?>() ?? string.Empty).ToLowerInvariant();
        var plan = (root["copilot_plan"]?.GetValue<string?>() ?? string.Empty).ToLowerInvariant();

        if (sku.Contains("educational") || sku.Contains("student"))
        {
            return "Education";
        }
        if (sku.Contains("pro_plus") || plan.Contains("pro_plus") || plan.Contains("individual_pro"))
        {
            return "Pro+";
        }
        if (sku.StartsWith("free") || plan == "free")
        {
            return "Free";
        }
        if (plan == "business")
        {
            return "Business";
        }
        if (plan == "enterprise")
        {
            return "Enterprise";
        }
        if (plan == "individual")
        {
            return "Pro";
        }
        return plan.Replace('_', ' ');
    }

    private static DateTimeOffset? ParseResetDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        return DateTimeOffset.TryParse(
            value,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : null;
    }

    private static string? TryGetErrorMessage(string body)
    {
        try
        {
            var node = JsonNode.Parse(body);
            return node?["message"]?.GetValue<string?>() ?? node?["error_details"]?["message"]?.GetValue<string?>();
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string?> ResolveTokenAsync(CancellationToken cancellationToken)
    {
        var fromGhCli = await TryGetGhCliTokenAsync(cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(fromGhCli))
        {
            return fromGhCli;
        }

        return Environment.GetEnvironmentVariable("COPILOT_GITHUB_TOKEN")
            ?? Environment.GetEnvironmentVariable("GH_TOKEN")
            ?? Environment.GetEnvironmentVariable("GITHUB_TOKEN");
    }

    private static async Task<string?> TryGetGhCliTokenAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/d /c gh auth token",
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

            return process.ExitCode == 0 ? output.Trim() : null;
        }
        catch
        {
            return null;
        }
    }

    private static HttpClient CreateHttpClient() => new()
    {
        Timeout = TimeSpan.FromSeconds(12),
    };

    /// <summary>One entry of the API's <c>quota_snapshots</c> map.</summary>
    private readonly struct QuotaSnapshot
    {
        public QuotaSnapshot(JsonNode? node)
        {
            Unlimited = node?["unlimited"]?.GetValue<bool?>() ?? false;
            HasQuota = node?["has_quota"]?.GetValue<bool?>() ?? true;
            PercentRemaining = node?["percent_remaining"]?.GetValue<double?>();
            Entitlement = node?["entitlement"]?.GetValue<double?>();
            Remaining = node?["remaining"]?.GetValue<double?>();
            OverageCount = node?["overage_count"]?.GetValue<double?>() ?? 0;
        }

        public bool Unlimited { get; }
        public bool HasQuota { get; }
        public double? PercentRemaining { get; }
        public double? Entitlement { get; }
        public double? Remaining { get; }
        public double OverageCount { get; }

        public double UsedPercent
        {
            get
            {
                if (Unlimited)
                {
                    return 0;
                }
                if (!HasQuota)
                {
                    return 100;
                }
                if (PercentRemaining is { } percentRemaining)
                {
                    return 100 - percentRemaining;
                }
                if (Entitlement is > 0)
                {
                    return (Entitlement.Value - (Remaining ?? 0)) / Entitlement.Value * 100;
                }
                return 0;
            }
        }

        public string Display()
        {
            if (Unlimited)
            {
                return "Unlimited";
            }
            if (Remaining is not null && Entitlement is > 0)
            {
                var overage = OverageCount > 0 ? $" (+{OverageCount:0} overage)" : string.Empty;
                return $"{Math.Max(Remaining.Value, 0):0} / {Entitlement:0} remaining{overage}";
            }
            if (PercentRemaining is { } percentRemaining)
            {
                return $"{percentRemaining:0}% remaining";
            }
            return "Usage available";
        }
    }
}
