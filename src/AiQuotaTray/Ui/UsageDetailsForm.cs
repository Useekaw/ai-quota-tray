using System.Drawing.Text;
using System.Runtime.InteropServices;
using AiQuotaTray.Usage;

namespace AiQuotaTray.Ui;

/// <summary>
/// Borderless flyout showing a detailed breakdown for every provider, opened
/// next to the tray icon. Styled to match native Windows 11 flyouts (Volume,
/// Wi-Fi, Battery): DWM-rounded corners and dark/light theme colors read
/// from the registry.
/// </summary>
internal sealed class UsageDetailsForm : Form
{
    // Sizing is scaled ~1.5x over the original design (flat 9pt / 340px felt
    // cramped in practice) — kept as one multiplier so it stays proportional
    // if it needs tuning again.
    private const float UiScale = 1.5f;
    private const int FlyoutWidth = (int)(340 * UiScale);
    private const int ContentPadding = (int)(16 * UiScale);

    private static readonly string UiFontFamily = ResolveUiFontFamily();

    private readonly FlowLayoutPanel _root;
    private ThemePalette _palette = ThemePalette.Resolve();

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

    // Composites all child-control painting off-screen before blitting to the
    // window — without this, tearing down and rebuilding the label/bar tree
    // on every refresh (see Render) is visible as a brief blink even with
    // WM_SETREDRAW suppression on the form itself.
    protected override CreateParams CreateParams
    {
        get
        {
            const int WsExComposited = 0x02000000;
            var cp = base.CreateParams;
            cp.ExStyle |= WsExComposited;
            return cp;
        }
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
        BackColor = _palette.WindowBackground;
        _root.BackColor = Color.Transparent;
    }

    private void DrawBorder(Graphics g)
    {
        using var pen = new Pen(_palette.Border, 1f);
        g.DrawRectangle(pen, new Rectangle(0, 0, Width - 1, Height - 1));
    }

    public void Render(IReadOnlyList<UsageResult> results)
    {
        // Rebuilding the control tree while the popup is on-screen (a
        // background refresh landing while it's open) is otherwise visible as
        // a single blink: the old controls disappear on Clear(), then the new
        // ones paint in on the next frame. WM_SETREDRAW makes both happen
        // inside one suppressed frame instead.
        var suspendRedraw = Visible && IsHandleCreated;
        if (suspendRedraw)
        {
            SendMessage(Handle, WmSetRedraw, false, IntPtr.Zero);
        }

        try
        {
            _root.SuspendLayout();
            _root.Controls.Clear();

            _root.Controls.Add(HeaderRow());
            _root.Controls.Add(Spacer(10));
            _root.Controls.Add(Divider());
            _root.Controls.Add(Spacer(14));

            for (var i = 0; i < results.Count; i++)
            {
                RenderProvider(results[i]);
                if (i < results.Count - 1)
                {
                    _root.Controls.Add(Spacer(14));
                    _root.Controls.Add(Divider());
                    _root.Controls.Add(Spacer(14));
                }
            }

            // A trailing Spacer, not just _root.Padding.Bottom: an AutoSize
            // FlowLayoutPanel's PreferredSize has been observed not to include
            // its own bottom padding when the last child is a TableLayoutPanel
            // (WindowRow) — the popup ended up with no visible bottom margin at
            // all. A real flowed control's height is always counted.
            _root.Controls.Add(Spacer(ContentPadding));

            _root.ResumeLayout(true);
            Height = _root.PreferredSize.Height;
        }
        finally
        {
            if (suspendRedraw)
            {
                SendMessage(Handle, WmSetRedraw, true, IntPtr.Zero);
            }
        }

        Invalidate(true);
    }

    private const int WmSetRedraw = 0x000B;

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, bool wParam, IntPtr lParam);

    private void RenderProvider(UsageResult result)
    {
        _root.Controls.Add(Label(DisplayName(result.Provider), bold: true));

        if (!result.Success)
        {
            _root.Controls.Add(Spacer(3));
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
            _root.Controls.Add(Spacer(10));
            _root.Controls.Add(WindowRow(window));
        }
    }

    private Control HeaderRow()
    {
        var panel = new TableLayoutPanel
        {
            Width = FlyoutWidth - (ContentPadding * 2),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));

        panel.Controls.Add(Label("AI Quota", bold: true, sizeDelta: 3), 0, 0);
        panel.Controls.Add(
            Label($"Last checked {DateTime.Now:HH:mm}", color: _palette.TextSecondary, sizeDelta: -2,
                align: ContentAlignment.MiddleRight, anchor: AnchorStyles.Top | AnchorStyles.Right),
            1, 0);

        return panel;
    }

    private Control Divider() => new Panel
    {
        Height = 1,
        Width = FlyoutWidth - (ContentPadding * 2),
        BackColor = _palette.Border,
        Margin = Padding.Empty,
    };

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
        panel.Controls.Add(
            Label($"{window.UsedPercent:0}%", align: ContentAlignment.MiddleRight, anchor: AnchorStyles.Top | AnchorStyles.Right),
            1, 0);

        var bar = new UsageBar
        {
            Width = contentWidth,
            Height = (int)(6 * UiScale),
            Percent = window.UsedPercent,
            FillColor = ColorFor(window.UsedPercent),
            TrackColor = _palette.BarTrack,
            Margin = new Padding(0, (int)(4 * UiScale), 0, (int)(4 * UiScale)),
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

    private Label Label(string text, bool bold = false, Color? color = null, int sizeDelta = 0, ContentAlignment align = ContentAlignment.MiddleLeft, AnchorStyles? anchor = null) => new()
    {
        Text = text,
        AutoSize = true,
        MaximumSize = new Size(FlyoutWidth - (ContentPadding * 2), 0),
        Font = new Font(UiFontFamily, (9f + sizeDelta) * UiScale, bold ? FontStyle.Bold : FontStyle.Regular),
        ForeColor = color ?? _palette.TextPrimary,
        BackColor = Color.Transparent,
        TextAlign = align,
        // AutoSize shrinks the label to its text, so inside a TableLayoutPanel
        // cell TextAlign alone can't push it to the cell's right edge — an
        // explicit Anchor is what actually moves the control, not just its text.
        Anchor = anchor ?? (AnchorStyles.Top | AnchorStyles.Left),
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
