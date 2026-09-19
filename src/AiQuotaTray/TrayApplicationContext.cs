using AiQuotaTray.Logging;
using AiQuotaTray.Ui;
using AiQuotaTray.Usage;

namespace AiQuotaTray;

/// <summary>Owns the tray icon, its context menu, the polling timer, and the details flyout.</summary>
internal sealed class TrayApplicationContext : ApplicationContext
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(5);

    private readonly IReadOnlyList<IUsageProvider> _providers =
    [
        new CodexUsageProvider(),
        new CopilotUsageProvider(),
    ];

    private readonly NotifyIcon _notifyIcon;
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private readonly ToolStripMenuItem _startupMenuItem;
    private readonly UsageDetailsForm _detailsForm = new();

    private IReadOnlyList<UsageResult> _lastResults = [];
    private bool _refreshing;

    public TrayApplicationContext()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Refresh now", null, async (_, _) => await RefreshAsync());
        menu.Items.Add(new ToolStripSeparator());
        _startupMenuItem = new ToolStripMenuItem("Start with Windows") { CheckOnClick = true, Checked = StartupManager.IsEnabled() };
        _startupMenuItem.Click += (_, _) => StartupManager.SetEnabled(_startupMenuItem.Checked);
        menu.Items.Add(_startupMenuItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Open diagnostics log", null, (_, _) => AppLog.OpenLogFolder());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitThread());

        _notifyIcon = new NotifyIcon
        {
            Icon = TrayIconRenderer.Render(null, null, null),
            Text = "AI Quota Tray — checking…",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _notifyIcon.MouseClick += OnTrayIconClick;

        _refreshTimer = new System.Windows.Forms.Timer { Interval = (int)RefreshInterval.TotalMilliseconds };
        _refreshTimer.Tick += async (_, _) => await RefreshAsync();
        _refreshTimer.Start();

        _ = RefreshAsync();
    }

    private void OnTrayIconClick(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        if (_detailsForm.Visible)
        {
            _detailsForm.Hide();
            return;
        }

        _detailsForm.Render(_lastResults);
        _detailsForm.ShowNear(GetTrayIconBounds());
    }

    private async Task RefreshAsync()
    {
        if (_refreshing)
        {
            return;
        }
        _refreshing = true;

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var results = await Task.WhenAll(_providers.Select(p => GetSafelyAsync(p, cts.Token)));

            _lastResults = results;
            UpdateIcon(results);

            if (_detailsForm.Visible)
            {
                _detailsForm.Render(results);
            }
        }
        finally
        {
            _refreshing = false;
        }
    }

    private static async Task<UsageResult> GetSafelyAsync(IUsageProvider provider, CancellationToken cancellationToken)
    {
        try
        {
            return await provider.GetUsageAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            return UsageResult.Failure(provider.ProviderId, provider.ProviderId, ex.Message);
        }
    }

    private void UpdateIcon(IReadOnlyList<UsageResult> results)
    {
        var codex = results.FirstOrDefault(r => r.Provider == "codex");
        var copilot = results.FirstOrDefault(r => r.Provider == "copilot");

        var codexSessionPercent = WindowPercent(codex, r => r.Primary);
        var codexWeeklyPercent = WindowPercent(codex, r => r.Secondary);
        var copilotPercent = WindowPercent(copilot, r => r.Primary);

        var oldIcon = _notifyIcon.Icon;
        _notifyIcon.Icon = TrayIconRenderer.Render(codexSessionPercent, codexWeeklyPercent, copilotPercent);
        oldIcon?.Dispose();

        _notifyIcon.Text = Truncate(BuildTooltip(codex, copilot), 127);
    }

    private static double? WindowPercent(UsageResult? result, Func<UsageResult, UsageWindow?> select)
    {
        if (result is null || !result.Success)
        {
            return null;
        }
        return select(result)?.UsedPercent;
    }

    private static string BuildTooltip(UsageResult? codex, UsageResult? copilot)
    {
        var lines = new List<string> { "AI Quota Tray" };
        lines.Add(Summarize("Codex", codex));
        lines.Add(Summarize("Copilot", copilot));
        return string.Join('\n', lines);
    }

    private static string Summarize(string name, UsageResult? result)
    {
        if (result is null)
        {
            return $"{name}: no data";
        }
        if (!result.Success)
        {
            return $"{name}: error";
        }

        var parts = new List<string>();
        if (result.Primary is { } primary)
        {
            parts.Add($"{primary.ResetDescription} {primary.UsedPercent:0}%");
        }
        if (result.Secondary is { } secondary)
        {
            parts.Add($"{secondary.ResetDescription} {secondary.UsedPercent:0}%");
        }
        return parts.Count > 0 ? $"{name}: {string.Join(", ", parts)}" : $"{name}: ok";
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..(maxLength - 1)] + "…";

    private Rectangle GetTrayIconBounds()
    {
        // NotifyIcon exposes no geometry; the cursor position at click time is
        // the closest reliable proxy for "where the tray icon is".
        var cursor = Cursor.Position;
        return new Rectangle(cursor.X, cursor.Y, 1, 1);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _refreshTimer.Dispose();
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _detailsForm.Dispose();
        }
        base.Dispose(disposing);
    }
}
