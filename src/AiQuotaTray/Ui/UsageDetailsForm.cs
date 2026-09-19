using System.Drawing.Text;
using AiQuotaTray.Usage;

namespace AiQuotaTray.Ui;

/// <summary>
/// Borderless flyout showing a detailed breakdown for every provider, opened
/// next to the tray icon. Styled to match native Windows 11 flyouts (Volume,
/// Wi-Fi, Battery): DWM-rounded corners, dark/light theme colors read from
/// the registry, and a best-effort acrylic backdrop.
/// </summary>
internal sealed class UsageDetailsForm : Form
{
    private const int FlyoutWidth = 340;
    private const int ContentPadding = 16;

    // Quick escape hatch: acrylic-over-WinForms is inherently a bit of a hack
    // (transparency-key compositing, not a first-class WinForms feature) and
    // this was never run on a real Windows 11 box during development. If it
    // ever renders oddly on some GPU/driver combination, this environment
    // variable falls back to the always-correct flat themed background
    // without needing a code change.
    private static readonly bool AcrylicDisabledByUser =
        Environment.GetEnvironmentVariable("AIQUOTATRAY_DISABLE_ACRYLIC") == "1";

    private static readonly string UiFontFamily = ResolveUiFontFamily();

    private readonly FlowLayoutPanel _root;
    private ThemePalette _palette = ThemePalette.Resolve();
    private bool _acrylicActive;

    public UsageDetailsForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        AutoSize = false;
        Width = FlyoutWidth;
        DoubleBuffered = true;

        _root = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(ContentPadding),
            BackColor = Color.Transparent,
        };
        Controls.Add(_root);

        Deactivate += (_, _) => Hide();
        Paint += (_, e) => DrawBorder(e.Graphics);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyChrome();
    }

    /// <summary>Re-read the theme and reapply DWM chrome — called on every open, since the user can flip Windows theme while the popup is closed.</summary>
    private void ApplyChrome()
    {
        _palette = ThemePalette.Resolve();
        Win11Window.ApplyFlyoutChrome(Handle, _palette.IsDark);

        _acrylicActive = !AcrylicDisabledByUser && Win11Window.TryApplyAcrylicBackdrop(Handle);
        if (_acrylicActive)
        {
            // The classic "glass sheet" trick: DWM only composites the acrylic
            // material through pixels matching TransparencyKey, so the form and
            // every non-drawing child control paint in that exact key color
            // (or literally Color.Transparent, which WinForms resolves back to
            // whatever the parent last painted — ending up as the same key).
            var glassKey = Color.FromArgb(1, 1, 2);
            BackColor = glassKey;
            TransparencyKey = glassKey;
        }
        else
        {
            BackColor = _palette.WindowBackground;
            TransparencyKey = Color.Empty;
        }

        _root.BackColor = _acrylicActive ? Color.Transparent : _palette.WindowBackground;
    }

    private void DrawBorder(Graphics g)
    {
        using var pen = new Pen(_palette.Border, 1f);
        g.DrawRectangle(pen, new Rectangle(0, 0, Width - 1, Height - 1));
    }

    public void Render(IReadOnlyList<UsageResult> results)
    {
        _root.SuspendLayout();
        _root.Controls.Clear();

        _root.Controls.Add(Label("AI Quota", bold: true, sizeDelta: 3));
        _root.Controls.Add(Spacer(8));

        foreach (var result in results)
        {
            RenderProvider(result);
            _root.Controls.Add(Spacer(14));
        }

        _root.Controls.Add(Label($"Last checked {DateTime.Now:HH:mm:ss}", color: _palette.TextSecondary, sizeDelta: -1));

        _root.ResumeLayout(true);
        Height = _root.PreferredSize.Height;
        Invalidate();
    }

    private void RenderProvider(UsageResult result)
    {
        _root.Controls.Add(Label(DisplayName(result.Provider), bold: true));

        if (!result.Success)
        {
            _root.Controls.Add(Spacer(2));
            _root.Controls.Add(Label(result.ErrorMessage ?? "Unknown error", color: Color.FromArgb(224, 90, 90)));
            return;
        }

        if (result.AccountLabel is not null)
        {
            _root.Controls.Add(Label(result.AccountLabel, color: _palette.TextSecondary, sizeDelta: -1));
        }

        foreach (var window in new[] { result.Primary, result.Secondary, result.Tertiary })
        {
            if (window is null)
            {
                continue;
            }
            _root.Controls.Add(Spacer(8));
            _root.Controls.Add(WindowRow(window));
        }
    }

    private Control WindowRow(UsageWindow window)
    {
        var contentWidth = FlyoutWidth - (ContentPadding * 2);

        var panel = new TableLayoutPanel
        {
            Width = contentWidth,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 3,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));

        panel.Controls.Add(Label(window.ResetDescription), 0, 0);
        panel.Controls.Add(Label($"{window.UsedPercent:0}%", align: ContentAlignment.MiddleRight), 1, 0);

        var bar = new UsageBar
        {
            Width = contentWidth,
            Percent = window.UsedPercent,
            FillColor = ColorFor(window.UsedPercent),
            TrackColor = _palette.BarTrack,
            Margin = new Padding(0, 4, 0, 4),
        };
        panel.SetColumnSpan(bar, 2);
        panel.Controls.Add(bar, 0, 1);

        var detail = window.DisplayValue ?? ResetSummary(window.ResetsAt);
        var detailLabel = Label(detail, color: _palette.TextSecondary, sizeDelta: -1);
        panel.SetColumnSpan(detailLabel, 2);
        panel.Controls.Add(detailLabel, 0, 2);

        return panel;
    }

    private static Color ColorFor(double percent) => percent switch
    {
        < 70 => Color.FromArgb(96, 189, 104),
        < 90 => Color.FromArgb(249, 183, 61),
        _ => Color.FromArgb(224, 90, 90),
    };

    private static string ResetSummary(DateTimeOffset? resetsAt)
    {
        if (resetsAt is null)
        {
            return string.Empty;
        }

        var local = resetsAt.Value.ToLocalTime();
        var remaining = local - DateTimeOffset.Now;
        var relative = remaining <= TimeSpan.Zero
            ? "resetting"
            : remaining.TotalHours < 24
                ? $"in {remaining.Hours}h {remaining.Minutes}m"
                : $"in {(int)remaining.TotalDays}d {remaining.Hours}h";

        return $"Resets {local:ddd HH:mm} ({relative})";
    }

    private static string DisplayName(string provider) => provider switch
    {
        "codex" => "Codex",
        "copilot" => "GitHub Copilot",
        _ => provider,
    };

    private Label Label(string text, bool bold = false, Color? color = null, int sizeDelta = 0, ContentAlignment align = ContentAlignment.MiddleLeft) => new()
    {
        Text = text,
        AutoSize = true,
        MaximumSize = new Size(FlyoutWidth - (ContentPadding * 2), 0),
        Font = new Font(UiFontFamily, 9f + sizeDelta, bold ? FontStyle.Bold : FontStyle.Regular),
        ForeColor = color ?? _palette.TextPrimary,
        BackColor = Color.Transparent,
        // Simulated-transparent labels lose ClearType's assumption of a solid
        // background; GDI+ antialiasing looks correct over the acrylic/glass-key
        // background where ClearType would otherwise fringe.
        UseCompatibleTextRendering = _acrylicActive,
        TextAlign = align,
        Margin = Padding.Empty,
    };

    private static Control Spacer(int height) => new Panel { Height = height, Width = 1, BackColor = Color.Transparent, Margin = Padding.Empty };

    /// <summary>Windows 11 ships "Segoe UI Variable Text" as the default UI font; older Windows falls back to plain Segoe UI.</summary>
    private static string ResolveUiFontFamily()
    {
        const string preferred = "Segoe UI Variable Text";
        try
        {
            using var installed = new InstalledFontCollection();
            if (installed.Families.Any(f => f.Name == preferred))
            {
                return preferred;
            }
        }
        catch
        {
            // Fall through to the safe default.
        }
        return "Segoe UI";
    }

    /// <summary>Positions the flyout above/right of the tray icon, flipping so it always stays on-screen.</summary>
    public void ShowNear(Rectangle iconBounds)
    {
        ApplyChrome();

        var workingArea = Screen.FromRectangle(iconBounds).WorkingArea;

        var x = Math.Min(iconBounds.Right - FlyoutWidth, workingArea.Right - FlyoutWidth);
        x = Math.Max(x, workingArea.Left);

        var y = iconBounds.Top - Height;
        if (y < workingArea.Top)
        {
            y = iconBounds.Bottom;
        }

        Location = new Point(x, y);
        Show();
        Activate();
    }
}
