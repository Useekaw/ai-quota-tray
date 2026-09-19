using AiQuotaTray.Usage;

namespace AiQuotaTray.Ui;

/// <summary>Borderless flyout showing a detailed breakdown for every provider, opened next to the tray icon.</summary>
internal sealed class UsageDetailsForm : Form
{
    private const int FlyoutWidth = 320;

    private readonly FlowLayoutPanel _root;

    public UsageDetailsForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.White;
        Padding = new Padding(1);
        AutoSize = false;
        Width = FlyoutWidth;

        var border = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(220, 220, 220), Padding = new Padding(1) };
        _root = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.White,
            Padding = new Padding(12),
        };
        border.Controls.Add(_root);
        Controls.Add(border);

        Deactivate += (_, _) => Hide();
    }

    public void Render(IReadOnlyList<UsageResult> results)
    {
        _root.SuspendLayout();
        _root.Controls.Clear();

        _root.Controls.Add(Label("AI Quota", bold: true, sizeDelta: 2));
        _root.Controls.Add(Spacer());

        foreach (var result in results)
        {
            RenderProvider(result);
            _root.Controls.Add(Spacer());
        }

        _root.Controls.Add(Label($"Last checked {DateTime.Now:HH:mm:ss}", bold: false, color: Color.Gray, sizeDelta: -1));

        _root.ResumeLayout(true);
        Height = _root.PreferredSize.Height + 4;
    }

    private void RenderProvider(UsageResult result)
    {
        _root.Controls.Add(Label(DisplayName(result.Provider), bold: true));

        if (!result.Success)
        {
            _root.Controls.Add(Label(result.ErrorMessage ?? "Unknown error", color: Color.FromArgb(198, 40, 40)));
            return;
        }

        if (result.AccountLabel is not null)
        {
            _root.Controls.Add(Label(result.AccountLabel, color: Color.DimGray, sizeDelta: -1));
        }

        foreach (var window in new[] { result.Primary, result.Secondary, result.Tertiary })
        {
            if (window is null)
            {
                continue;
            }
            _root.Controls.Add(WindowRow(window));
        }
    }

    private Control WindowRow(UsageWindow window)
    {
        var panel = new TableLayoutPanel
        {
            Width = FlyoutWidth - 30,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 3,
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));

        panel.Controls.Add(Label(window.ResetDescription), 0, 0);
        panel.Controls.Add(Label($"{window.UsedPercent:0}%", align: ContentAlignment.MiddleRight), 1, 0);

        var bar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = Math.Clamp((int)Math.Round(window.UsedPercent), 0, 100),
            Width = panel.Width,
            Height = 10,
        };
        panel.SetColumnSpan(bar, 2);
        panel.Controls.Add(bar, 0, 1);

        var detail = window.DisplayValue ?? ResetSummary(window.ResetsAt);
        var detailLabel = Label(detail, color: Color.DimGray, sizeDelta: -1);
        panel.SetColumnSpan(detailLabel, 2);
        panel.Controls.Add(detailLabel, 0, 2);

        return panel;
    }

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

    private static Label Label(string text, bool bold = false, Color? color = null, int sizeDelta = 0, ContentAlignment align = ContentAlignment.MiddleLeft) => new()
    {
        Text = text,
        AutoSize = true,
        Width = FlyoutWidth - 30,
        Font = new Font(SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont, bold ? FontStyle.Bold : FontStyle.Regular)
            .Adjust(sizeDelta),
        ForeColor = color ?? Color.Black,
        TextAlign = align,
    };

    private static Control Spacer() => new Panel { Height = 6, Width = 1 };

    /// <summary>Positions the flyout above/right of the tray icon, flipping so it always stays on-screen.</summary>
    public void ShowNear(Rectangle iconBounds)
    {
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

internal static class FontExtensions
{
    public static Font Adjust(this Font font, int sizeDelta)
    {
        if (sizeDelta == 0)
        {
            return font;
        }
        return new Font(font.FontFamily, Math.Max(6, font.Size + sizeDelta), font.Style);
    }
}
