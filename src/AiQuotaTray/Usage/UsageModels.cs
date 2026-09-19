namespace AiQuotaTray.Usage;

/// <summary>One rate-limit window (e.g. Codex's 5h session window, or Copilot's premium request budget).</summary>
public sealed record UsageWindow(
    double UsedPercent,
    int? WindowMinutes,
    DateTimeOffset? ResetsAt,
    string ResetDescription,
    string? DisplayValue = null);

/// <summary>Snapshot of one provider's quota state, as reported by its own backend.</summary>
public sealed record UsageResult(
    string Provider,
    string Source,
    bool Success,
    string? ErrorMessage,
    string? AccountLabel,
    UsageWindow? Primary,
    UsageWindow? Secondary,
    UsageWindow? Tertiary,
    DateTimeOffset UpdatedAt)
{
    public static UsageResult Failure(string provider, string source, string message) => new(
        Provider: provider,
        Source: source,
        Success: false,
        ErrorMessage: message,
        AccountLabel: null,
        Primary: null,
        Secondary: null,
        Tertiary: null,
        UpdatedAt: DateTimeOffset.Now);
}

public interface IUsageProvider
{
    string ProviderId { get; }

    Task<UsageResult> GetUsageAsync(CancellationToken cancellationToken);
}
