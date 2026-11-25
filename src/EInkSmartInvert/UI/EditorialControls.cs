using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using EInkSmartInvert.Config;

namespace EInkSmartInvert.UI;

internal static class EditorialPalette
{
    // Harmonized with V1 Counterchange Prism Icon:
    // Warm Vintage Paper (#F5F1E8) + Deep Printmaker Charcoal (#1C1F24) + Serene Slate-Ink Hierarchy
    public static readonly Color PaperBg      = Color.FromArgb(245, 241, 232); // #F5F1E8
    public static readonly Color PaperSheet   = Color.FromArgb(251, 249, 244); // #FBF9F4
    public static readonly Color PaperHover   = Color.FromArgb(235, 229, 218); // #EBE5DA
    public static readonly Color Ink          = Color.FromArgb(28, 31, 36);    // #1C1F24
    public static readonly Color InkSoft      = Color.FromArgb(72, 75, 82);    // #484B52
    public static readonly Color InkMuted     = Color.FromArgb(122, 119, 112); // #7A7770
    public static readonly Color RuleHairline = Color.FromArgb(214, 208, 196); // #D6D0C4
    public static readonly Color Crimson      = Color.FromArgb(52, 84, 92);    // #34545C
    public static readonly Color CrimsonDark  = Color.FromArgb(36, 60, 66);    // #243C42
    public static readonly Color CrimsonTint  = Color.FromArgb(228, 236, 238); // #E4ECEE

    public static void ConfigureHighQuality(System.Drawing.Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
    }

    /// <summary>
    /// Creates a pixel-unit font scaled directly by the window's live scale factor (never stuck at primary 96-DPI Point size!).
    /// </summary>
    public static Font FontPx(string family, float basePxAt1x, float scale, FontStyle style = FontStyle.Regular)
    {
        float px = MathF.Max(9f, basePxAt1x * scale);
        return new Font(family, px, style, GraphicsUnit.Pixel);
    }

    public static int ScalePx(float logicalPx, float scale) => Math.Max(1, (int)MathF.Round(logicalPx * scale));
}

internal interface IScalableEditorialControl
{
    float UiScale { get; set; }
}

/// <summary>
/// Pure owner-drawn section header (replaces WinForms Label completely — zero disposed-Font red-[X] or AutoScale bugs!).
/// </summary>
internal sealed class EditorialSectionHeader : Control, IScalableEditorialControl
{
    private float _uiScale = 1f;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float UiScale
    {
        get => _uiScale;
        set { _uiScale = MathF.Max(0.75f, value); Invalidate(); }
    }

    public EditorialSectionHeader()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        EditorialPalette.ConfigureHighQuality(g);
        g.Clear(BackColor);

        string fontName = UiStrings.IsZh ? "Microsoft YaHei UI" : "Georgia";
        using var f = EditorialPalette.FontPx(fontName, 11.8f, _uiScale, FontStyle.Bold);
        TextRenderer.DrawText(g, Text, f, ClientRectangle, EditorialPalette.InkMuted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
    }
}

/// <summary>
/// Dual-world split masthead with pixel-scaled typography, borderless drag, Refresh + Power buttons, and Win11 caption controls.
/// </summary>
internal sealed class EditorialMasthead : Control, IScalableEditorialControl
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern nint SendMessage(IntPtr hWnd, int msg, nint wParam, nint lParam);

    private const int WM_NCLBUTTONDOWN = 0x00A1;
    private const int HTCAPTION = 2;

    private Bitmap? _logoMaster;
    private float _uiScale = 1f;
    private bool _hoverPower;
    private bool _hoverRefresh;
    private bool _hoverMin;
    private bool _hoverClose;
    private Rectangle _powerRect;
    private Rectangle _refreshRect;
    private Rectangle _minRect;
    private Rectangle _closeRect;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float UiScale
    {
        get => _uiScale;
        set { _uiScale = MathF.Max(0.75f, value); Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string StatusText { get; set; } = "STANDBY";

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool IsFilterActive { get; set; } = true;

    public event EventHandler? ToggleActiveClicked;
    public event EventHandler? RefreshClicked;
    public event EventHandler? MinimizeClicked;
    public event EventHandler? CloseClicked;

    public EditorialMasthead()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Default;
    }

    public void SetLogoMaster(Bitmap bmp)
    {
        _logoMaster?.Dispose();
        _logoMaster = bmp;
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _logoMaster?.Dispose();
        base.Dispose(disposing);
    }

    private int S(float px) => EditorialPalette.ScalePx(px, _uiScale);

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        bool hPow = _powerRect.Contains(e.Location);
        bool hRef = _refreshRect.Contains(e.Location);
        bool hMin = _minRect.Contains(e.Location);
        bool hCls = _closeRect.Contains(e.Location);
        if (hPow != _hoverPower || hRef != _hoverRefresh || hMin != _hoverMin || hCls != _hoverClose)
        {
            _hoverPower = hPow;
            _hoverRefresh = hRef;
            _hoverMin = hMin;
            _hoverClose = hCls;
            Cursor = (hPow || hRef || hMin || hCls) ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hoverPower || _hoverRefresh || _hoverMin || _hoverClose)
        {
            _hoverPower = _hoverRefresh = _hoverMin = _hoverClose = false;
            Cursor = Cursors.Default;
            Invalidate();
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left &&
            !_powerRect.Contains(e.Location) &&
            !_refreshRect.Contains(e.Location) &&
            !_minRect.Contains(e.Location) &&
            !_closeRect.Contains(e.Location))
        {
            var host = FindForm();
            if (host != null)
            {
                ReleaseCapture();
                SendMessage(host.Handle, WM_NCLBUTTONDOWN, HTCAPTION, 0);
            }
        }
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button != MouseButtons.Left) return;
        if (_closeRect.Contains(e.Location))
            CloseClicked?.Invoke(this, EventArgs.Empty);
        else if (_minRect.Contains(e.Location))
            MinimizeClicked?.Invoke(this, EventArgs.Empty);
        else if (_refreshRect.Contains(e.Location))
            RefreshClicked?.Invoke(this, EventArgs.Empty);
        else if (_powerRect.Contains(e.Location))
            ToggleActiveClicked?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        EditorialPalette.ConfigureHighQuality(g);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        int w = Width, h = Height;

        g.Clear(EditorialPalette.PaperBg);

        float splitTopX = w * 0.565f;
        float splitBotX = w * 0.495f;

        using (var inkPath = new GraphicsPath())
        {
            inkPath.AddPolygon(new PointF[]
            {
                new(0, 0),
                new(splitTopX, 0),
                new(splitBotX, h),
                new(0, h)
            });
            using var inkBrush = new SolidBrush(EditorialPalette.Ink);
            g.FillPath(inkBrush, inkPath);
        }

        using (var rulePen = new Pen(Color.FromArgb(120, 124, 130), MathF.Max(1.2f, 1.5f * _uiScale)))
        {
            g.DrawLine(rulePen, splitTopX, 0, splitBotX, h);
        }

        // Left side: Crisp scaled Logo + PAPER PRISM serif title + Chinese subtitle
        int padL = S(18);
        int logoSz = S(48);
        int logoY = (h - logoSz) / 2;
        if (_logoMaster != null)
        {
            using var haloPen = new Pen(Color.FromArgb(72, 77, 86), MathF.Max(1f, 1.2f * _uiScale));
            int rad = S(11), d = rad * 2;
            using var haloPath = new GraphicsPath();
            haloPath.AddArc(padL, logoY, d, d, 180, 90);
            haloPath.AddArc(padL + logoSz - d, logoY, d, d, 270, 90);
            haloPath.AddArc(padL + logoSz - d, logoY + logoSz - d, d, d, 0, 90);
            haloPath.AddArc(padL, logoY + logoSz - d, d, d, 90, 90);
            haloPath.CloseFigure();
            g.DrawPath(haloPen, haloPath);
            g.DrawImage(_logoMaster, new Rectangle(padL, logoY, logoSz, logoSz));
        }

        int textX = padL + logoSz + S(14);
        string titleFont = UiStrings.IsZh ? "Microsoft YaHei UI" : "Georgia";
        using var fMain = EditorialPalette.FontPx(titleFont, UiStrings.IsZh ? 21f : 20f, _uiScale, FontStyle.Bold);
        using var fSub = EditorialPalette.FontPx("Microsoft YaHei UI", 12.4f, _uiScale, FontStyle.Regular);
        using var creamBrush = new SolidBrush(EditorialPalette.PaperBg);
        using var mutedCream = new SolidBrush(Color.FromArgb(196, 188, 175));

        g.DrawString(UiStrings.MastheadTitle, fMain, creamBrush, textX, S(20));
        g.DrawString(UiStrings.MastheadSubtitle, fSub, mutedCream, textX + S(1), S(54));

        // Top-Right Native Win11 Style Minimalist Caption Buttons (Minimize '—' & Close '×')
        int capBtnW = S(40);
        int capBtnH = S(28);
        _closeRect = new Rectangle(w - capBtnW, 0, capBtnW, capBtnH);
        _minRect = new Rectangle(w - capBtnW * 2, 0, capBtnW, capBtnH);

        if (_hoverMin)
        {
            using var minBg = new SolidBrush(EditorialPalette.PaperHover);
            g.FillRectangle(minBg, _minRect);
        }
        if (_hoverClose)
        {
            using var clsBg = new SolidBrush(Color.FromArgb(196, 43, 28));
            g.FillRectangle(clsBg, _closeRect);
        }

        float glyphStroke = MathF.Max(1.2f, 1.35f * _uiScale);
        using (var minPen = new Pen(EditorialPalette.InkSoft, glyphStroke))
        {
            int mx1 = _minRect.X + S(15), mx2 = _minRect.Right - S(15), my = _minRect.Y + capBtnH / 2;
            g.DrawLine(minPen, mx1, my, mx2, my);
        }
        using (var clsPen = new Pen(_hoverClose ? Color.White : EditorialPalette.InkSoft, glyphStroke))
        {
            int cx1 = _closeRect.X + S(15), cy1 = _closeRect.Y + S(9);
            int cx2 = _closeRect.Right - S(15), cy2 = _closeRect.Bottom - S(9);
            g.DrawLine(clsPen, cx1, cy1, cx2, cy2);
            g.DrawLine(clsPen, cx2, cy1, cx1, cy2);
        }

        // Right side: Status line + [↻ 刷新] + [折射开启 · ON]
        int rightPad = S(18);
        using var fStatusZh = EditorialPalette.FontPx("Microsoft YaHei UI", 11.6f, _uiScale, FontStyle.Regular);

        string cleanStatus = StatusText
            .Replace("初始化中...", UiStrings.IsZh ? "初始化中..." : "Initializing...")
            .Replace("运行中 — ", "")
            .Replace("运行中 (虚拟屏 WGC 窗口合成) — ", "")
            .Replace("运行中 (物理屏 DXGI 零拷贝) — ", "")
            .Replace("运行中 (自动切换 WGC 窗口合成) — ", "")
            .Replace("已暂停 (原画直通 · 0% GPU)", UiStrings.IsZh ? "已休眠 · 零开销" : "Paused · 0% GPU")
            .Replace("等待副屏连接唤醒中 (0% GPU 待命)", UiStrings.IsZh ? "待命 · 等待副屏唤醒" : "Standby · Waiting for display");
        if (cleanStatus.Length > 26)
            cleanStatus = cleanStatus[..26] + "…";

        Size statusSz = TextRenderer.MeasureText(g, cleanStatus, fStatusZh, new Size(w, h), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        int statusX = w - rightPad - statusSz.Width;
        int statusY = S(32);

        int dotSz = S(7);
        var dotRect = new Rectangle(statusX - dotSz - S(6), statusY + (statusSz.Height - dotSz) / 2, dotSz, dotSz);
        if (IsFilterActive && !StatusText.Contains("等待") && !StatusText.Contains("Standby"))
        {
            using var bDot = new SolidBrush(EditorialPalette.Crimson);
            g.FillEllipse(bDot, dotRect);
        }
        else
        {
            using var pDot = new Pen(EditorialPalette.InkMuted, MathF.Max(1.2f, 1.4f * _uiScale));
            g.DrawEllipse(pDot, dotRect);
        }
        TextRenderer.DrawText(g, cleanStatus, fStatusZh, new Point(statusX, statusY), EditorialPalette.InkSoft, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

        int btnW = S(112);
        int btnH = S(28);
        int btnY = h - S(12) - btnH;
        _powerRect = new Rectangle(w - rightPad - btnW, btnY, btnW, btnH);

        int refW = S(72);
        _refreshRect = new Rectangle(_powerRect.X - S(8) - refW, btnY, refW, btnH);

        float borderW = MathF.Max(1f, 1.1f * _uiScale);
        using (var refBg = new SolidBrush(_hoverRefresh ? EditorialPalette.PaperHover : EditorialPalette.PaperSheet))
        using (var refPen = new Pen(EditorialPalette.Ink, borderW))
        {
            g.FillRectangle(refBg, _refreshRect);
            g.DrawRectangle(refPen, _refreshRect.X, _refreshRect.Y, _refreshRect.Width - 1, _refreshRect.Height - 1);
            TextRenderer.DrawText(g, UiStrings.BtnRefresh, fStatusZh, _refreshRect, EditorialPalette.Ink,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        if (IsFilterActive)
        {
            using var bg = new SolidBrush(_hoverPower ? EditorialPalette.Crimson : EditorialPalette.Ink);
            g.FillRectangle(bg, _powerRect);
            TextRenderer.DrawText(g, UiStrings.BtnActiveOn, fStatusZh, _powerRect, EditorialPalette.PaperBg,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
        else
        {
            using var bg = new SolidBrush(_hoverPower ? EditorialPalette.PaperHover : EditorialPalette.PaperSheet);
            using var pen = new Pen(EditorialPalette.Ink, borderW * 1.2f);
            g.FillRectangle(bg, _powerRect);
            g.DrawRectangle(pen, _powerRect.X, _powerRect.Y, _powerRect.Width - 1, _powerRect.Height - 1);
            TextRenderer.DrawText(g, UiStrings.BtnPausedOff, fStatusZh, _powerRect, EditorialPalette.Ink,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }
}

/// <summary>
/// Book-chapter navigation strip ("I. 棱镜总控  |  II. 登场应用  |  III. 显影调校") with pixel-scaled fonts.
/// </summary>
internal sealed class EditorialChapterBar : Control, IScalableEditorialControl
{
    private readonly string[] _titles = { UiStrings.TabPrism, UiStrings.TabApps(0, false), UiStrings.TabTuning };
    private readonly Rectangle[] _tabRects = new Rectangle[3];
    private int _hoverIdx = -1;
    private float _uiScale = 1f;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float UiScale
    {
        get => _uiScale;
        set { _uiScale = MathF.Max(0.75f, value); Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectedIndex { get; set; }

    public event Action<int>? SelectedIndexChanged;

    public EditorialChapterBar()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
    }

    public void SetAppCountBadge(int enabledCount, bool whitelistOn)
    {
        _titles[1] = UiStrings.TabApps(enabledCount, whitelistOn);
        Invalidate();
    }

    private int S(float px) => EditorialPalette.ScalePx(px, _uiScale);

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int idx = -1;
        for (int i = 0; i < _tabRects.Length; i++)
        {
            if (_tabRects[i].Contains(e.Location)) { idx = i; break; }
        }
        if (idx != _hoverIdx)
        {
            _hoverIdx = idx;
            Cursor = idx >= 0 ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hoverIdx != -1)
        {
            _hoverIdx = -1;
            Cursor = Cursors.Default;
            Invalidate();
        }
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button != MouseButtons.Left) return;
        for (int i = 0; i < _tabRects.Length; i++)
        {
            if (_tabRects[i].Contains(e.Location) && i != SelectedIndex)
            {
                SelectedIndex = i;
                Invalidate();
                SelectedIndexChanged?.Invoke(i);
                break;
            }
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        EditorialPalette.ConfigureHighQuality(g);
        g.Clear(EditorialPalette.PaperSheet);

        int topRuleH = S(2);
        using var topPen = new Pen(EditorialPalette.Ink, topRuleH);
        using var botPen = new Pen(EditorialPalette.Ink, MathF.Max(1f, 1f * _uiScale));
        g.DrawLine(topPen, 0, topRuleH / 2, Width, topRuleH / 2);
        g.DrawLine(botPen, 0, Height - 1, Width, Height - 1);

        int colW = Width / 3;
        using var fActive = EditorialPalette.FontPx("Microsoft YaHei UI", 13.2f, _uiScale, FontStyle.Bold);
        using var fIdle = EditorialPalette.FontPx("Microsoft YaHei UI", 12.8f, _uiScale, FontStyle.Regular);
        using var divPen = new Pen(EditorialPalette.RuleHairline, MathF.Max(1f, 1f * _uiScale));

        for (int i = 0; i < 3; i++)
        {
            int x = i * colW;
            int w = (i == 2) ? (Width - x) : colW;
            _tabRects[i] = new Rectangle(x, topRuleH, w, Height - topRuleH - 1);

            if (i == _hoverIdx && i != SelectedIndex)
            {
                using var hBr = new SolidBrush(EditorialPalette.PaperHover);
                g.FillRectangle(hBr, _tabRects[i]);
            }

            if (i > 0)
                g.DrawLine(divPen, x, S(9), x, Height - S(9));

            bool sel = (i == SelectedIndex);
            TextRenderer.DrawText(
                g,
                _titles[i],
                sel ? fActive : fIdle,
                _tabRects[i],
                sel ? EditorialPalette.Ink : EditorialPalette.InkMuted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            if (sel)
            {
                int barPad = S(28);
                int barH = S(3);
                using var barBr = new SolidBrush(EditorialPalette.Crimson);
                g.FillRectangle(barBr, x + barPad, Height - barH - 1, w - barPad * 2, barH);
            }
        }
    }
}

/// <summary>
/// 2x2 Penguin Bookplate Mode Selector Cards with pixel-scaled fonts.
/// </summary>
internal sealed class EditorialModeCards : Control, IScalableEditorialControl
{
    private static (FilterMode Mode, string Code, string Title, string Desc)[] GetCards() => new[]
    {
        (FilterMode.SmartAuto,      "01", UiStrings.Mode1Title, UiStrings.Mode1Desc),
        (FilterMode.ForceInvertAll, "02", UiStrings.Mode2Title, UiStrings.Mode2Desc),
        (FilterMode.CrispLightOnly, "03", UiStrings.Mode3Title, UiStrings.Mode3Desc),
        (FilterMode.Off,            "04", UiStrings.Mode4Title, UiStrings.Mode4Desc)
    };

    private readonly Rectangle[] _rects = new Rectangle[4];
    private int _hoverIdx = -1;
    private float _uiScale = 1f;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float UiScale
    {
        get => _uiScale;
        set { _uiScale = MathF.Max(0.75f, value); Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public FilterMode SelectedMode { get; set; } = FilterMode.SmartAuto;

    public event Action<FilterMode>? ModeSelected;

    public EditorialModeCards()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
    }

    private int S(float px) => EditorialPalette.ScalePx(px, _uiScale);

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int idx = -1;
        for (int i = 0; i < 4; i++)
        {
            if (_rects[i].Contains(e.Location)) { idx = i; break; }
        }
        if (idx != _hoverIdx)
        {
            _hoverIdx = idx;
            Cursor = idx >= 0 ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hoverIdx != -1) { _hoverIdx = -1; Cursor = Cursors.Default; Invalidate(); }
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button != MouseButtons.Left) return;
        var cards = GetCards();
        for (int i = 0; i < 4; i++)
        {
            if (_rects[i].Contains(e.Location) && cards[i].Mode != SelectedMode)
            {
                SelectedMode = cards[i].Mode;
                Invalidate();
                ModeSelected?.Invoke(SelectedMode);
                break;
            }
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        EditorialPalette.ConfigureHighQuality(g);
        g.Clear(EditorialPalette.PaperBg);
        var cards = GetCards();

        int gap = S(10);
        int cw = (Width - gap) / 2;
        int ch = (Height - gap) / 2;

        using var fCode = EditorialPalette.FontPx("Georgia", 13f, _uiScale, FontStyle.Bold | FontStyle.Italic);
        using var fTitle = EditorialPalette.FontPx("Microsoft YaHei UI", 13.6f, _uiScale, FontStyle.Bold);
        using var fDesc = EditorialPalette.FontPx("Microsoft YaHei UI", 11.5f, _uiScale, FontStyle.Regular);

        for (int i = 0; i < 4; i++)
        {
            int col = i % 2;
            int row = i / 2;
            var rc = new Rectangle(col * (cw + gap), row * (ch + gap), cw, ch);
            _rects[i] = rc;

            bool sel = (cards[i].Mode == SelectedMode);
            bool hov = (i == _hoverIdx);

            Color bg = sel
                ? EditorialPalette.Ink
                : (hov ? EditorialPalette.PaperHover : EditorialPalette.PaperSheet);
            using (var b = new SolidBrush(bg))
                g.FillRectangle(b, rc);

            float bw = MathF.Max(1f, (sel ? 1.5f : 1f) * _uiScale);
            using (var pen = new Pen(sel ? EditorialPalette.Ink : EditorialPalette.InkSoft, bw))
                g.DrawRectangle(pen, rc.X, rc.Y, rc.Width - 1, rc.Height - 1);

            if (sel)
            {
                using var spine = new SolidBrush(EditorialPalette.Crimson);
                g.FillRectangle(spine, rc.X, rc.Y, S(5), rc.Height);
            }

            int padX = rc.X + S(14);
            int topY = rc.Y + S(9);

            using var codeBrush = new SolidBrush(sel ? Color.FromArgb(145, 175, 182) : EditorialPalette.InkMuted);
            g.DrawString(cards[i].Code, fCode, codeBrush, rc.Right - S(32), topY);

            TextRenderer.DrawText(g, cards[i].Title, fTitle, new Point(padX, topY),
                sel ? EditorialPalette.PaperBg : EditorialPalette.Ink, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

            TextRenderer.DrawText(g, cards[i].Desc, fDesc, new Point(padX, topY + S(24)),
                sel ? Color.FromArgb(200, 193, 180) : EditorialPalette.InkMuted, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }
    }
}

/// <summary>
/// Owner-drawn flat bookplate stamp button or dropdown selector with pixel-scaled fonts.
/// </summary>
internal sealed class EditorialStampButton : Control, IScalableEditorialControl
{
    private bool _hover;
    private float _uiScale = 1f;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float UiScale
    {
        get => _uiScale;
        set { _uiScale = MathF.Max(0.75f, value); Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool IsPrimaryCrimson { get; set; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool ShowDropdownArrow { get; set; }

    public EditorialStampButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
    }

    private int S(float px) => EditorialPalette.ScalePx(px, _uiScale);

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        EditorialPalette.ConfigureHighQuality(g);

        Color bg = IsPrimaryCrimson
            ? (_hover ? EditorialPalette.CrimsonDark : EditorialPalette.Crimson)
            : (_hover ? EditorialPalette.PaperHover : EditorialPalette.PaperSheet);
        Color fg = IsPrimaryCrimson ? Color.White : EditorialPalette.Ink;
        Color border = IsPrimaryCrimson ? EditorialPalette.CrimsonDark : EditorialPalette.Ink;

        using (var b = new SolidBrush(bg))
            g.FillRectangle(b, ClientRectangle);
        using (var p = new Pen(border, MathF.Max(1f, 1f * _uiScale)))
            g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);

        using var f = EditorialPalette.FontPx("Microsoft YaHei UI", 12.2f, _uiScale, IsPrimaryCrimson ? FontStyle.Bold : FontStyle.Regular);
        if (ShowDropdownArrow)
        {
            var textRect = new Rectangle(S(12), 0, Width - S(34), Height);
            TextRenderer.DrawText(g, Text, f, textRect, fg, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            int cx = Width - S(16);
            int cy = Height / 2;
            int r = S(4);
            using var triBr = new SolidBrush(EditorialPalette.Crimson);
            g.FillPolygon(triBr, new Point[] { new(cx - r, cy - r / 2), new(cx + r, cy - r / 2), new(cx, cy + r) });
        }
        else
        {
            TextRenderer.DrawText(g, Text, f, ClientRectangle, fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }
}

/// <summary>
/// Minimalist printmaker checkbox with pixel-scaled label.
/// </summary>
internal sealed class EditorialCheckBox : Control, IScalableEditorialControl
{
    private bool _hover;
    private bool _checked;
    private float _uiScale = 1f;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float UiScale
    {
        get => _uiScale;
        set { _uiScale = MathF.Max(0.75f, value); Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Checked
    {
        get => _checked;
        set { if (_checked != value) { _checked = value; Invalidate(); } }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool BoldLabel { get; set; }

    public event EventHandler? CheckedChanged;

    public EditorialCheckBox()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
    }

    private int S(float px) => EditorialPalette.ScalePx(px, _uiScale);

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button == MouseButtons.Left)
        {
            _checked = !_checked;
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        EditorialPalette.ConfigureHighQuality(g);
        g.Clear(BackColor);

        int boxSz = S(16);
        int boxY = (Height - boxSz) / 2;
        var boxRect = new Rectangle(S(2), boxY, boxSz, boxSz);

        if (_checked)
        {
            using var fill = new SolidBrush(_hover ? EditorialPalette.Crimson : EditorialPalette.Ink);
            g.FillRectangle(fill, boxRect);

            using var ckPen = new Pen(EditorialPalette.PaperSheet, MathF.Max(1.6f, 2f * _uiScale));
            g.DrawLines(ckPen, new PointF[]
            {
                new(boxRect.X + boxSz * 0.22f, boxRect.Y + boxSz * 0.52f),
                new(boxRect.X + boxSz * 0.44f, boxRect.Y + boxSz * 0.74f),
                new(boxRect.X + boxSz * 0.80f, boxRect.Y + boxSz * 0.26f)
            });
        }
        else
        {
            using var bg = new SolidBrush(_hover ? EditorialPalette.PaperHover : EditorialPalette.PaperSheet);
            using var pen = new Pen(EditorialPalette.Ink, MathF.Max(1.1f, 1.2f * _uiScale));
            g.FillRectangle(bg, boxRect);
            g.DrawRectangle(pen, boxRect);
        }

        using var f = EditorialPalette.FontPx("Microsoft YaHei UI", 12.4f, _uiScale, BoldLabel ? FontStyle.Bold : FontStyle.Regular);
        var textRect = new Rectangle(boxRect.Right + S(9), 0, Width - boxRect.Right - S(10), Height);
        TextRenderer.DrawText(g, Text, f, textRect, EditorialPalette.Ink, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }
}

/// <summary>
/// Clean printmaker hairline slider with pixel-scaled label and Consolas readout.
/// </summary>
internal sealed class EditorialSliderRow : Control, IScalableEditorialControl
{
    private bool _dragging;
    private bool _hover;
    private int _val;
    private float _uiScale = 1f;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float UiScale
    {
        get => _uiScale;
        set { _uiScale = MathF.Max(0.75f, value); Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Title { get; set; } = "";

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Minimum { get; set; } = 0;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Maximum { get; set; } = 100;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Value
    {
        get => _val;
        set { _val = Math.Clamp(value, Minimum, Maximum); Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<int, string>? ValueFormatter { get; set; }

    public event EventHandler? ValueChanged;

    public EditorialSliderRow()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
    }

    private int S(float px) => EditorialPalette.ScalePx(px, _uiScale);

    private (int TrackL, int TrackR) GetTrackSpan()
    {
        return (S(112), Width - S(82));
    }

    private void UpdateFromMouse(int mouseX)
    {
        var (l, r) = GetTrackSpan();
        if (r <= l) return;
        float t = Math.Clamp((float)(mouseX - l) / (r - l), 0f, 1f);
        int next = Minimum + (int)MathF.Round(t * (Maximum - Minimum));
        if (next != _val)
        {
            _val = next;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left)
        {
            _dragging = true;
            Capture = true;
            UpdateFromMouse(e.X);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging) UpdateFromMouse(e.X);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_dragging)
        {
            _dragging = false;
            Capture = false;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        EditorialPalette.ConfigureHighQuality(g);
        g.Clear(BackColor);

        using var fLabel = EditorialPalette.FontPx("Microsoft YaHei UI", 12.4f, _uiScale, FontStyle.Regular);

        TextRenderer.DrawText(g, Title, fLabel, new Rectangle(0, 0, S(108), Height),
            EditorialPalette.InkSoft, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

        var (trackL, trackR) = GetTrackSpan();
        int cy = Height / 2;
        float frac = (Maximum > Minimum) ? (float)(_val - Minimum) / (Maximum - Minimum) : 0f;
        int thumbX = trackL + (int)MathF.Round(frac * (trackR - trackL));

        float lineW = MathF.Max(1.5f, 2f * _uiScale);
        using (var idlePen = new Pen(EditorialPalette.RuleHairline, lineW))
            g.DrawLine(idlePen, trackL, cy, trackR, cy);

        if (thumbX > trackL)
        {
            using var activePen = new Pen(EditorialPalette.Ink, lineW);
            g.DrawLine(activePen, trackL, cy, thumbX, cy);
        }

        int d = (_dragging || _hover) ? S(7) : S(6);
        using (var redBr = new SolidBrush(EditorialPalette.Crimson))
        {
            g.FillPolygon(redBr, new Point[]
            {
                new(thumbX, cy - d),
                new(thumbX + d, cy),
                new(thumbX, cy + d),
                new(thumbX - d, cy)
            });
        }

        string txt = ValueFormatter != null ? ValueFormatter(_val) : _val.ToString();
        bool hasCjk = txt.Any(ch => ch > 127);
        using var fVal = EditorialPalette.FontPx(hasCjk ? "Microsoft YaHei UI" : "Consolas", hasCjk ? 12.4f : 13.0f, _uiScale, FontStyle.Bold);
        TextRenderer.DrawText(g, txt, fVal, new Rectangle(trackR + S(8), 0, Width - trackR - S(8), Height),
            EditorialPalette.Ink, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }
}

/// <summary>
/// Pixel-scaled "Dramatis Personae" (登场应用名录) manuscript list.
/// </summary>
internal sealed class EditorialAppRoster : ScrollableControl, IScalableEditorialControl
{
    private sealed class RowLayout
    {
        public Rectangle Bounds;
        public Rectangle CheckRect;
        public Rectangle TextRect;
        public Rectangle ModeBtnRect;
        public Rectangle DeleteRect;
    }

    private List<AppInvertRule> _rules = new();
    private RowLayout[] _layouts = Array.Empty<RowLayout>();
    private int _hoverRow = -1;
    private bool _hoverModeBtn;
    private bool _hoverDelBtn;
    private float _uiScale = 1f;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float UiScale
    {
        get => _uiScale;
        set
        {
            float next = MathF.Max(0.75f, value);
            if (MathF.Abs(next - _uiScale) > 0.005f)
            {
                _uiScale = next;
                RecomputeLayout();
                Invalidate();
            }
        }
    }

    public event EventHandler? RulesChanged;

    public EditorialAppRoster()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        AutoScroll = true;
        BackColor = EditorialPalette.PaperSheet;
    }

    private int S(float px) => EditorialPalette.ScalePx(px, _uiScale);

    public void SetRules(List<AppInvertRule> rules)
    {
        _rules = rules.Select(r => r.Clone()).ToList();
        RecomputeLayout();
        Invalidate();
    }

    public List<AppInvertRule> GetRulesClone() => _rules.Select(r => r.Clone()).ToList();

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        RecomputeLayout();
    }

    private void RecomputeLayout()
    {
        if (_rules.Count == 0 || Width <= 40 || !IsHandleCreated && Width <= 0)
        {
            _layouts = Array.Empty<RowLayout>();
            AutoScrollMinSize = Size.Empty;
            return;
        }

        _layouts = new RowLayout[_rules.Count];
        ComputePass(ClientSize.Width);
        if (ClientSize.Width != (_layouts.Length > 0 ? _layouts[0].Bounds.Width : ClientSize.Width))
            ComputePass(ClientSize.Width);
    }

    private void ComputePass(int usableW)
    {
        int modeBtnW = S(108);
        int delBtnW = S(26);
        int checkX = S(14);
        int checkSz = S(16);
        int textX = checkX + checkSz + S(12);
        int rightClusterW = modeBtnW + delBtnW + S(22);
        int textMaxW = Math.Max(S(100), usableW - textX - rightClusterW);

        using var g = CreateGraphics();
        using var fMono = EditorialPalette.FontPx("Consolas", 11.4f, _uiScale, FontStyle.Regular);

        int y = 0;
        for (int i = 0; i < _rules.Count; i++)
        {
            string procsText = FormatProcessList(_rules[i].MatchProcesses);
            Size procSz = TextRenderer.MeasureText(g, procsText, fMono, new Size(textMaxW, S(240)),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);

            int rowH = Math.Max(S(44), S(26) + procSz.Height + S(6));
            int btnH = S(26);
            int modeX = usableW - S(14) - delBtnW - S(6) - modeBtnW;
            int delX = usableW - S(12) - delBtnW;

            _layouts[i] = new RowLayout
            {
                Bounds = new Rectangle(0, y, usableW, rowH),
                CheckRect = new Rectangle(checkX, y + (rowH - checkSz) / 2, checkSz, checkSz),
                TextRect = new Rectangle(textX, y + S(5), textMaxW, rowH - S(8)),
                ModeBtnRect = new Rectangle(modeX, y + (rowH - btnH) / 2, modeBtnW, btnH),
                DeleteRect = new Rectangle(delX, y + (rowH - delBtnW) / 2, delBtnW, delBtnW)
            };
            y += rowH;
        }
        AutoScrollMinSize = new Size(0, y);
    }

    private static string FormatProcessList(string raw)
    {
        var parts = raw.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join(" · ", parts);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Point pt = new(e.X - AutoScrollPosition.X, e.Y - AutoScrollPosition.Y);
        int row = -1;
        bool hMode = false, hDel = false;
        for (int i = 0; i < _layouts.Length; i++)
        {
            if (_layouts[i].Bounds.Contains(pt))
            {
                row = i;
                hMode = _layouts[i].ModeBtnRect.Contains(pt);
                hDel = _layouts[i].DeleteRect.Contains(pt);
                break;
            }
        }
        if (row != _hoverRow || hMode != _hoverModeBtn || hDel != _hoverDelBtn)
        {
            _hoverRow = row;
            _hoverModeBtn = hMode;
            _hoverDelBtn = hDel;
            Cursor = row >= 0 ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hoverRow != -1)
        {
            _hoverRow = -1;
            _hoverModeBtn = false;
            _hoverDelBtn = false;
            Cursor = Cursors.Default;
            Invalidate();
        }
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button != MouseButtons.Left) return;
        Point pt = new(e.X - AutoScrollPosition.X, e.Y - AutoScrollPosition.Y);

        for (int i = 0; i < _layouts.Length; i++)
        {
            var lay = _layouts[i];
            if (!lay.Bounds.Contains(pt)) continue;

            if (lay.DeleteRect.Contains(pt))
            {
                _rules.RemoveAt(i);
                _hoverRow = -1;
                RecomputeLayout();
                Invalidate();
                RulesChanged?.Invoke(this, EventArgs.Empty);
                return;
            }

            if (lay.ModeBtnRect.Contains(pt))
            {
                int rowIdx = i;
                var fullLabels = UiStrings.FullRuleModes;
                var cms = new ContextMenuStrip
                {
                    Font = EditorialPalette.FontPx("Microsoft YaHei UI", 12.4f, _uiScale)
                };
                for (int m = 0; m < fullLabels.Length; m++)
                {
                    int captureM = m;
                    var item = new ToolStripMenuItem(fullLabels[m])
                    {
                        Checked = ((int)_rules[rowIdx].RuleMode == m)
                    };
                    item.Click += (_, _) =>
                    {
                        _rules[rowIdx].RuleMode = (AppRuleMode)captureM;
                        Invalidate();
                        RulesChanged?.Invoke(this, EventArgs.Empty);
                    };
                    cms.Items.Add(item);
                }
                cms.Show(this, new Point(lay.ModeBtnRect.Left + AutoScrollPosition.X, lay.ModeBtnRect.Bottom + AutoScrollPosition.Y));
                return;
            }

            if (lay.CheckRect.Contains(pt) || pt.X < lay.CheckRect.Right + S(8))
            {
                _rules[i].Enabled = !_rules[i].Enabled;
                Invalidate();
                RulesChanged?.Invoke(this, EventArgs.Empty);
                return;
            }

            EditRuleDialog(i);
            return;
        }
    }

    public void AddNewRuleInteractive()
    {
        var nr = new AppInvertRule { Name = UiStrings.NewAppDefault, MatchProcesses = "app_name", Enabled = true, RuleMode = AppRuleMode.FollowGlobal };
        _rules.Add(nr);
        RecomputeLayout();
        Invalidate();
        if (!EditRuleDialog(_rules.Count - 1))
        {
            _rules.RemoveAt(_rules.Count - 1);
            RecomputeLayout();
            Invalidate();
        }
    }

    private bool EditRuleDialog(int index)
    {
        var rule = _rules[index];
        using var fDlg = EditorialPalette.FontPx("Microsoft YaHei UI", 12.6f, _uiScale);
        using var dlg = new Form
        {
            Text = UiStrings.EditDlgTitle,
            AutoScaleMode = AutoScaleMode.None,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            StartPosition = FormStartPosition.CenterParent,
            BackColor = EditorialPalette.PaperBg,
            Font = fDlg,
            ClientSize = new Size(S(420), S(200))
        };

        var lbl1 = new Label { Text = UiStrings.EditDlgName, Location = new Point(S(18), S(16)), AutoSize = true, ForeColor = EditorialPalette.InkSoft };
        var txtName = new TextBox { Text = rule.Name, Location = new Point(S(18), S(42)), Width = S(384), BorderStyle = BorderStyle.FixedSingle };

        var lbl2 = new Label { Text = UiStrings.EditDlgProcs, Location = new Point(S(18), S(80)), AutoSize = true, ForeColor = EditorialPalette.InkSoft };
        var txtProcs = new TextBox { Text = rule.MatchProcesses, Location = new Point(S(18), S(106)), Width = S(384), BorderStyle = BorderStyle.FixedSingle };

        var btnOk = new EditorialStampButton { Text = UiStrings.BtnOk, UiScale = _uiScale, IsPrimaryCrimson = true, Location = new Point(S(226), S(150)), Size = new Size(S(84), S(32)) };
        var btnCancel = new EditorialStampButton { Text = UiStrings.BtnCancel, UiScale = _uiScale, Location = new Point(S(318), S(150)), Size = new Size(S(84), S(32)) };

        bool saved = false;
        btnOk.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(txtProcs.Text))
            {
                rule.Name = string.IsNullOrWhiteSpace(txtName.Text) ? txtProcs.Text.Trim() : txtName.Text.Trim();
                rule.MatchProcesses = txtProcs.Text.Trim();
                saved = true;
                dlg.Close();
            }
        };
        btnCancel.Click += (_, _) => dlg.Close();

        dlg.Controls.AddRange(new Control[] { lbl1, txtName, lbl2, txtProcs, btnOk, btnCancel });
        dlg.ShowDialog(FindForm());

        if (saved)
        {
            RecomputeLayout();
            Invalidate();
            RulesChanged?.Invoke(this, EventArgs.Empty);
        }
        return saved;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (_layouts.Length != _rules.Count)
            RecomputeLayout();

        var g = e.Graphics;
        EditorialPalette.ConfigureHighQuality(g);
        g.Clear(EditorialPalette.PaperSheet);
        g.TranslateTransform(AutoScrollPosition.X, AutoScrollPosition.Y);

        using var fName = EditorialPalette.FontPx("Microsoft YaHei UI", 12.8f, _uiScale, FontStyle.Bold);
        using var fMono = EditorialPalette.FontPx("Consolas", 11.4f, _uiScale, FontStyle.Regular);
        using var fBtn = EditorialPalette.FontPx("Microsoft YaHei UI", 11.7f, _uiScale, FontStyle.Regular);
        using var linePen = new Pen(EditorialPalette.RuleHairline, MathF.Max(1f, 1f * _uiScale));

        for (int i = 0; i < _layouts.Length; i++)
        {
            var r = _rules[i];
            var lay = _layouts[i];
            bool hov = (i == _hoverRow);

            if (hov)
            {
                using var hBr = new SolidBrush(EditorialPalette.PaperHover);
                g.FillRectangle(hBr, lay.Bounds);
            }

            if (i > 0)
                g.DrawLine(linePen, S(14), lay.Bounds.Top, lay.Bounds.Right - S(14), lay.Bounds.Top);

            if (r.Enabled)
            {
                using var fill = new SolidBrush(EditorialPalette.Ink);
                g.FillRectangle(fill, lay.CheckRect);
                using var ckPen = new Pen(EditorialPalette.PaperSheet, MathF.Max(1.6f, 2f * _uiScale));
                g.DrawLines(ckPen, new PointF[]
                {
                    new(lay.CheckRect.X + lay.CheckRect.Width * 0.22f, lay.CheckRect.Y + lay.CheckRect.Height * 0.52f),
                    new(lay.CheckRect.X + lay.CheckRect.Width * 0.44f, lay.CheckRect.Y + lay.CheckRect.Height * 0.74f),
                    new(lay.CheckRect.X + lay.CheckRect.Width * 0.80f, lay.CheckRect.Y + lay.CheckRect.Height * 0.26f)
                });
            }
            else
            {
                using var p = new Pen(EditorialPalette.InkMuted, MathF.Max(1.1f, 1.2f * _uiScale));
                g.DrawRectangle(p, lay.CheckRect);
            }

            Color titleCol = r.Enabled ? EditorialPalette.Ink : EditorialPalette.InkMuted;
            TextRenderer.DrawText(g, r.Name, fName, new Point(lay.TextRect.X, lay.TextRect.Y), titleCol, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

            string procs = FormatProcessList(r.MatchProcesses);
            var procRect = new Rectangle(lay.TextRect.X, lay.TextRect.Y + S(19), lay.TextRect.Width, lay.TextRect.Height - S(17));
            TextRenderer.DrawText(g, procs, fMono, procRect, EditorialPalette.InkMuted, TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

            bool modeHighlight = (hov && _hoverModeBtn);
            float bw = MathF.Max(1f, 1f * _uiScale);
            using (var mBg = new SolidBrush(modeHighlight ? EditorialPalette.Ink : EditorialPalette.PaperBg))
            using (var mPen = new Pen(EditorialPalette.Ink, bw))
            {
                g.FillRectangle(mBg, lay.ModeBtnRect);
                g.DrawRectangle(mPen, lay.ModeBtnRect.X, lay.ModeBtnRect.Y, lay.ModeBtnRect.Width - 1, lay.ModeBtnRect.Height - 1);
            }
            var shortLabels = UiStrings.ShortRuleModes;
            int mIdx = Math.Clamp((int)r.RuleMode, 0, shortLabels.Length - 1);
            TextRenderer.DrawText(g, shortLabels[mIdx] + " ▾", fBtn, lay.ModeBtnRect,
                modeHighlight ? EditorialPalette.PaperBg : EditorialPalette.Ink,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            if (hov)
            {
                if (_hoverDelBtn)
                {
                    using var dBg = new SolidBrush(EditorialPalette.CrimsonTint);
                    g.FillRectangle(dBg, lay.DeleteRect);
                }
                TextRenderer.DrawText(g, "×", fName, lay.DeleteRect,
                    _hoverDelBtn ? EditorialPalette.Crimson : EditorialPalette.InkMuted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }
    }
}
