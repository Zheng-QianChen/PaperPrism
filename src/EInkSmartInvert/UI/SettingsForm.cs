using System.Runtime.InteropServices;
using EInkSmartInvert.Config;
using EInkSmartInvert.Core;

namespace EInkSmartInvert.UI;

public sealed class SettingsForm : Form
{
    private const float BaseLogicalWidth = 640f;
    private const float BaseLogicalHeight = 570f;

    private const int WM_DPICHANGED = 0x02E0;
    private const int WM_NCHITTEST  = 0x0084;
    private const int HTLEFT        = 10;
    private const int HTRIGHT       = 11;
    private const int HTTOP         = 12;
    private const int HTTOPLEFT     = 13;
    private const int HTTOPRIGHT    = 14;
    private const int HTBOTTOM      = 15;
    private const int HTBOTTOMLEFT  = 16;
    private const int HTBOTTOMRIGHT = 17;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    private readonly AppSettings _settings;
    private readonly Action<AppSettings> _onChanged;
    private readonly OverlayEngine _engine;
    private bool _suppressEvents;
    private bool _uiBuilt;
    private float _uiScale = 1f;
    private Icon? _windowIcon;
    private FilterMode _lastNonOffMode = FilterMode.SmartAuto;

    private EditorialMasthead _masthead = null!;
    private EditorialChapterBar _chapterBar = null!;
    private Panel _footer = null!;
    private Panel _pageHost = null!;
    private Panel _pagePrism = null!;
    private Panel _pageApps = null!;
    private Panel _pageTuning = null!;

    // Section headers (100% owner-drawn, zero WinForms Label red-[X] or AutoScale bugs!)
    private EditorialSectionHeader _epigraph1 = null!;
    private EditorialSectionHeader _epigraph2 = null!;
    private EditorialSectionHeader _epigraph3 = null!;

    // Page I: Prism Overview
    private EditorialStampButton _btnMonitorPicker = null!;
    private EditorialModeCards _modeCards = null!;
    private Panel _whitelistBox = null!;
    private EditorialCheckBox _chkWhitelistOverview = null!;
    private EditorialStampButton _btnJumpApps = null!;
    private EditorialStampButton _btnKaleido = null!;
    private EditorialStampButton _btnCarta = null!;
    private EditorialStampButton _btnSnap = null!;
    private readonly List<(string Label, string Device)> _monitorEntries = new();

    // Page II: Dramatis Personae (App Roster)
    private EditorialCheckBox _chkWhitelistApps = null!;
    private EditorialStampButton _btnPickWin = null!;
    private EditorialStampButton _btnAddManual = null!;
    private EditorialStampButton _btnResetApps = null!;
    private Panel _rosterFrame = null!;
    private EditorialAppRoster _appRoster = null!;

    // Page III: Darkroom & Optics
    private Panel _sliderSheet = null!;
    private EditorialSliderRow _sldWhite = null!;
    private EditorialSliderRow _sldBlack = null!;
    private EditorialSliderRow _sldSat = null!;
    private EditorialSliderRow _sldPhoto = null!;
    private EditorialSliderRow _sldFps = null!;

    private Panel _optSheet = null!;
    private EditorialCheckBox _chkVirtual = null!;
    private EditorialCheckBox _chkKeepLight = null!;
    private EditorialCheckBox _chkProtectPhotos = null!;
    private EditorialCheckBox _chkPreserveHue = null!;
    private EditorialCheckBox _chkEnhanceLight = null!;
    private EditorialCheckBox _chkDebugMask = null!;

    private int S(float px) => EditorialPalette.ScalePx(px, _uiScale);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    public SettingsForm(AppSettings currentSettings, OverlayEngine engine, Action<AppSettings> onChanged, int initialChapter = 0)
    {
        _settings = currentSettings.Clone();
        if (_settings.Mode != FilterMode.Off)
            _lastNonOffMode = _settings.Mode;
        _engine = engine;
        _onChanged = onChanged;

        AutoScaleMode = AutoScaleMode.None;
        Text = UiStrings.AppTitle;
        BackColor = EditorialPalette.PaperBg;
        FormBorderStyle = FormBorderStyle.None;
        MaximizeBox = false;
        ShowInTaskbar = true;
        StartPosition = FormStartPosition.Manual;

        _windowIcon = AppIcon.CreateWindowIcon(64);
        Icon = _windowIcon;

        BuildBookUi();
        _uiBuilt = true;

        PositionAndScaleForScreen(Screen.FromPoint(Cursor.Position));
        PopulateMonitors();
        LoadValuesToControls();
        ShowChapter(Math.Clamp(initialChapter, 0, 2));

        _engine.StatusChanged += OnEngineStatusChanged;
        FormClosed += (_, _) =>
        {
            _engine.StatusChanged -= OnEngineStatusChanged;
            _windowIcon?.Dispose();
        };
    }

    private void PositionAndScaleForScreen(Screen screen)
    {
        uint winDpi = IsHandleCreated ? GetDpiForWindow(Handle) : 0;
        float dpiScale = winDpi > 0 ? (winDpi / 96f) : (DeviceDpi > 0 ? DeviceDpi / 96f : 1f);

        // Also infer visual scale if monitor is ultra-high-res even at lower Windows DPI setting
        var work = screen.WorkingArea;
        int targetW = (int)MathF.Round(BaseLogicalWidth * dpiScale);
        int targetH = (int)MathF.Round(BaseLogicalHeight * dpiScale);

        // Clamp cleanly within 88% of screen working area while preserving exact aspect ratio!
        int maxW = (int)(work.Width * 0.88f);
        int maxH = (int)(work.Height * 0.88f);
        if (targetW > maxW || targetH > maxH)
        {
            float fit = MathF.Min((float)maxW / BaseLogicalWidth, (float)maxH / BaseLogicalHeight);
            targetW = (int)MathF.Round(BaseLogicalWidth * fit);
            targetH = (int)MathF.Round(BaseLogicalHeight * fit);
        }

        targetW = Math.Max(480, targetW);
        targetH = Math.Max(430, targetH);

        int x = work.Left + (work.Width - targetW) / 2;
        int y = work.Top + (work.Height - targetH) / 2;
        Bounds = new Rectangle(x, y, targetW, targetH);
        ApplyResponsiveLayout();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int cornerPref = 2; // DWMWCP_ROUND
        int borderColor = 0x00423E3A;
        DwmSetWindowAttribute(Handle, 33, ref cornerPref, sizeof(int));
        DwmSetWindowAttribute(Handle, 34, ref borderColor, sizeof(int));
    }

    protected override void WndProc(ref Message m)
    {
        // Seamless cross-monitor DPI transition (e.g. 100% primary <-> 300% E-Ink monitor)
        if (m.Msg == WM_DPICHANGED && m.LParam != IntPtr.Zero)
        {
            var suggested = Marshal.PtrToStructure<RECT>(m.LParam);
            var scr = Screen.FromRectangle(new Rectangle(suggested.Left, suggested.Top, suggested.Right - suggested.Left, suggested.Bottom - suggested.Top));
            var work = scr.WorkingArea;

            int newDpi = (int)(m.WParam.ToInt64() & 0xFFFF);
            float dpiScale = MathF.Max(1f, newDpi / 96f);
            int w = (int)MathF.Round(BaseLogicalWidth * dpiScale);
            int h = (int)MathF.Round(BaseLogicalHeight * dpiScale);

            int maxW = (int)(work.Width * 0.88f);
            int maxH = (int)(work.Height * 0.88f);
            if (w > maxW || h > maxH)
            {
                float fit = MathF.Min((float)maxW / BaseLogicalWidth, (float)maxH / BaseLogicalHeight);
                w = (int)MathF.Round(BaseLogicalWidth * fit);
                h = (int)MathF.Round(BaseLogicalHeight * fit);
            }

            int cx = (suggested.Left + suggested.Right) / 2;
            int cy = (suggested.Top + suggested.Bottom) / 2;
            int nx = Math.Clamp(cx - w / 2, work.Left, Math.Max(work.Left, work.Right - w));
            int ny = Math.Clamp(cy - h / 2, work.Top, Math.Max(work.Top, work.Bottom - h));

            SetWindowPos(Handle, IntPtr.Zero, nx, ny, w, h, 0x0014 /* SWP_NOZORDER | SWP_NOACTIVATE */);
            ApplyResponsiveLayout();
            m.Result = IntPtr.Zero;
            return;
        }

        base.WndProc(ref m);

        // Allow smooth borderless edge & corner dragging to resize the window and zoom fonts freely!
        if (m.Msg == WM_NCHITTEST && (int)m.Result == 1 /* HTCLIENT */)
        {
            Point pt = PointToClient(new Point((int)(short)(m.LParam.ToInt64() & 0xFFFF), (int)(short)((m.LParam.ToInt64() >> 16) & 0xFFFF)));
            int grip = S(8);
            bool left = pt.X < grip;
            bool right = pt.X >= Width - grip;
            bool top = pt.Y < grip;
            bool bottom = pt.Y >= Height - grip;

            if (bottom && right || (pt.X >= Width - S(18) && pt.Y >= Height - S(18))) m.Result = (IntPtr)HTBOTTOMRIGHT;
            else if (bottom && left) m.Result = (IntPtr)HTBOTTOMLEFT;
            else if (top && left) m.Result = (IntPtr)HTTOPLEFT;
            else if (top && right) m.Result = (IntPtr)HTTOPRIGHT;
            else if (left) m.Result = (IntPtr)HTLEFT;
            else if (right) m.Result = (IntPtr)HTRIGHT;
            else if (top) m.Result = (IntPtr)HTTOP;
            else if (bottom) m.Result = (IntPtr)HTBOTTOM;
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (_uiBuilt && WindowState != FormWindowState.Minimized)
            ApplyResponsiveLayout();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        PositionAndScaleForScreen(Screen.FromHandle(Handle));
        ShowWindow(Handle, 9);
        ShowWindow(Handle, 5);
        SetForegroundWindow(Handle);
        Activate();
        try
        {
            string lp = Path.Combine(AppContext.BaseDirectory, "runtime.log");
            File.AppendAllText(lp, $"[{DateTime.Now:HH:mm:ss.fff}] [UI] PaperPrism OnShown Handle=0x{Handle:X}, Bounds={Bounds}, Scale={_uiScale:F2}, IsWindowVisible={IsWindowVisible(Handle)}{Environment.NewLine}");
        }
        catch { }
    }

    public void ShowChapter(int index)
    {
        int idx = Math.Clamp(index, 0, 2);
        _chapterBar.SelectedIndex = idx;
        _chapterBar.Invalidate();
        _pagePrism.Visible = (idx == 0);
        _pageApps.Visible = (idx == 1);
        _pageTuning.Visible = (idx == 2);
    }

    private void OnEngineStatusChanged(string status)
    {
        if (IsDisposed || !IsHandleCreated) return;
        BeginInvoke(() =>
        {
            _masthead.StatusText = status;
            _masthead.Invalidate();
        });
    }

    private void BuildBookUi()
    {
        MinimumSize = new Size(480, 430);

        _masthead = new EditorialMasthead
        {
            StatusText = _engine.StatusMessage,
            IsFilterActive = _settings.Mode != FilterMode.Off
        };
        _masthead.SetLogoMaster(AppIcon.CreateHeaderLogoBitmap(256));
        _masthead.MinimizeClicked += (_, _) => WindowState = FormWindowState.Minimized;
        _masthead.CloseClicked += (_, _) => Close();
        _masthead.RefreshClicked += (_, _) =>
        {
            PopulateMonitors();
            _engine.ForceRefresh();
        };
        _masthead.ToggleActiveClicked += (_, _) =>
        {
            if (_settings.Mode == FilterMode.Off)
                _settings.Mode = _lastNonOffMode;
            else
            {
                _lastNonOffMode = _settings.Mode;
                _settings.Mode = FilterMode.Off;
            }
            LoadValuesToControls();
            SaveAndBroadcast();
        };

        _chapterBar = new EditorialChapterBar();
        _chapterBar.SelectedIndexChanged += ShowChapter;

        _footer = new Panel { BackColor = EditorialPalette.PaperBg };
        _footer.Paint += (s, e) =>
        {
            if (s is not Control c) return;
            EditorialPalette.ConfigureHighQuality(e.Graphics);
            using var p = new Pen(EditorialPalette.RuleHairline, MathF.Max(1f, 1f * _uiScale));
            e.Graphics.DrawLine(p, S(22), 0, c.Width - S(22), 0);
            using var f = EditorialPalette.FontPx("Microsoft YaHei UI", 11.2f, _uiScale, FontStyle.Regular);
            TextRenderer.DrawText(
                e.Graphics,
                UiStrings.FooterShortcuts,
                f,
                c.ClientRectangle,
                EditorialPalette.InkMuted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            // Subtle bottom-right resize corner lines
            using var gripPen = new Pen(EditorialPalette.InkMuted, MathF.Max(1f, 1.1f * _uiScale));
            int gx = c.Width - S(6), gy = c.Height - S(6);
            e.Graphics.DrawLine(gripPen, gx - S(7), gy, gx, gy - S(7));
            e.Graphics.DrawLine(gripPen, gx - S(3), gy, gx, gy - S(3));
        };

        _pageHost = new Panel { BackColor = EditorialPalette.PaperBg };

        _pagePrism = BuildChapterPrism();
        _pageApps = BuildChapterApps();
        _pageTuning = BuildChapterTuning();

        _pageHost.Controls.Add(_pagePrism);
        _pageHost.Controls.Add(_pageApps);
        _pageHost.Controls.Add(_pageTuning);

        Controls.Add(_pageHost);
        Controls.Add(_chapterBar);
        Controls.Add(_masthead);
        Controls.Add(_footer);
    }

    private Panel BuildChapterPrism()
    {
        var page = new Panel { BackColor = EditorialPalette.PaperBg };
        _epigraph1 = CreateEpigraph(UiStrings.SecPrism);

        _btnMonitorPicker = new EditorialStampButton { ShowDropdownArrow = true };
        _btnMonitorPicker.Click += (_, _) => ShowMonitorDropdown();

        _modeCards = new EditorialModeCards();
        _modeCards.ModeSelected += mode =>
        {
            if (mode != FilterMode.Off)
                _lastNonOffMode = mode;
            _settings.Mode = mode;
            LoadValuesToControls();
            SaveAndBroadcast();
        };

        _whitelistBox = CreateFramedSheet();
        _chkWhitelistOverview = new EditorialCheckBox
        {
            Text = UiStrings.WhitelistOverviewCheck,
            BoldLabel = true,
            BackColor = EditorialPalette.PaperSheet
        };
        _chkWhitelistOverview.CheckedChanged += (_, _) =>
        {
            if (_suppressEvents) return;
            _settings.OnlyInvertListedApps = _chkWhitelistOverview.Checked;
            LoadValuesToControls();
            SaveAndBroadcast();
        };

        _btnJumpApps = new EditorialStampButton { Text = UiStrings.JumpAppsBtn(6), IsPrimaryCrimson = true };
        _btnJumpApps.Click += (_, _) => ShowChapter(1);

        _whitelistBox.Controls.Add(_chkWhitelistOverview);
        _whitelistBox.Controls.Add(_btnJumpApps);

        _btnKaleido = new EditorialStampButton { Text = UiStrings.PresetKaleido };
        _btnKaleido.Click += (_, _) => { _settings.ApplyKaleido3Preset(); LoadValuesToControls(); SaveAndBroadcast(); };

        _btnCarta = new EditorialStampButton { Text = UiStrings.PresetCarta };
        _btnCarta.Click += (_, _) => { _settings.ApplyCartaMonochromePreset(); LoadValuesToControls(); SaveAndBroadcast(); };

        _btnSnap = new EditorialStampButton { Text = UiStrings.BtnSnapshot };
        _btnSnap.Click += (_, _) => _engine.RequestDiagnosticSnapshot();

        page.Controls.AddRange(new Control[]
        {
            _epigraph1, _btnMonitorPicker, _modeCards, _whitelistBox, _btnKaleido, _btnCarta, _btnSnap
        });
        return page;
    }

    private Panel BuildChapterApps()
    {
        var page = new Panel { BackColor = EditorialPalette.PaperBg, Visible = false };
        _epigraph2 = CreateEpigraph(UiStrings.SecApps);

        _chkWhitelistApps = new EditorialCheckBox
        {
            Text = UiStrings.WhitelistAppsCheck,
            BoldLabel = true,
            BackColor = EditorialPalette.PaperBg
        };
        _chkWhitelistApps.CheckedChanged += (_, _) =>
        {
            if (_suppressEvents) return;
            _settings.OnlyInvertListedApps = _chkWhitelistApps.Checked;
            LoadValuesToControls();
            SaveAndBroadcast();
        };

        _btnPickWin = new EditorialStampButton { Text = UiStrings.BtnPickWindow, IsPrimaryCrimson = true };
        _btnPickWin.Click += (_, _) => ShowPickRunningWindowMenu(_btnPickWin);

        _btnAddManual = new EditorialStampButton { Text = UiStrings.BtnAddManual };
        _btnAddManual.Click += (_, _) => _appRoster.AddNewRuleInteractive();

        _btnResetApps = new EditorialStampButton { Text = UiStrings.BtnResetList };
        _btnResetApps.Click += (_, _) =>
        {
            _settings.AppRules = AppSettings.CreateDefaultAppRules();
            LoadValuesToControls();
            SaveAndBroadcast();
        };

        _rosterFrame = CreateFramedSheet();
        _appRoster = new EditorialAppRoster { Dock = DockStyle.Fill };
        _appRoster.RulesChanged += (_, _) =>
        {
            _settings.AppRules = _appRoster.GetRulesClone();
            UpdateBadges();
            SaveAndBroadcast();
        };
        _rosterFrame.Controls.Add(_appRoster);

        page.Controls.AddRange(new Control[] { _epigraph2, _chkWhitelistApps, _btnPickWin, _btnAddManual, _btnResetApps, _rosterFrame });
        return page;
    }

    private Panel BuildChapterTuning()
    {
        var page = new Panel { BackColor = EditorialPalette.PaperBg, Visible = false };
        _epigraph3 = CreateEpigraph(UiStrings.SecTuning);

        _sliderSheet = CreateFramedSheet();
        _sldWhite = CreateSlider(_sliderSheet, UiStrings.SldWhite, 60, 95, v => $"{v}%");
        _sldBlack = CreateSlider(_sliderSheet, UiStrings.SldBlack, 2, 38, v => $"{v}%");
        _sldSat   = CreateSlider(_sliderSheet, UiStrings.SldSat,   0, 220, v => v == 0 ? UiStrings.ValMono : $"{v / 100.0:F2}x");
        _sldPhoto = CreateSlider(_sliderSheet, UiStrings.SldPhoto, 15, 95, v => $"{v}%");
        _sldFps   = CreateSlider(_sliderSheet, UiStrings.SldFps,   8, 60, v => UiStrings.ValFps(v));

        _optSheet = CreateFramedSheet();
        _chkVirtual       = CreateGridCheck(_optSheet, UiStrings.OptVirtual);
        _chkKeepLight     = CreateGridCheck(_optSheet, UiStrings.OptKeepLight);
        _chkProtectPhotos = CreateGridCheck(_optSheet, UiStrings.OptProtectMedia);
        _chkPreserveHue   = CreateGridCheck(_optSheet, UiStrings.OptPreserveHue);
        _chkEnhanceLight  = CreateGridCheck(_optSheet, UiStrings.OptEnhanceLight);
        _chkDebugMask     = CreateGridCheck(_optSheet, UiStrings.OptDebugMask);

        page.Controls.AddRange(new Control[] { _epigraph3, _sliderSheet, _optSheet });
        return page;
    }

    /// <summary>
    /// Recomputes every coordinate AND pixel font scale from the current ClientSize —
    /// guaranteeing 100% crisp proportional scaling across 100%..300% DPI monitors and manual window resizing!
    /// </summary>
    public void ApplyResponsiveLayout()
    {
        int W = ClientSize.Width;
        int H = ClientSize.Height;
        if (W <= 80 || H <= 80) return;

        _uiScale = MathF.Min(W / BaseLogicalWidth, H / BaseLogicalHeight);

        SuspendLayout();

        int mastH = S(96);
        int tabH = S(40);
        int footH = S(30);
        int hostY = mastH + tabH;
        int hostH = Math.Max(S(100), H - hostY - footH);

        _masthead.Bounds = new Rectangle(0, 0, W, mastH);
        _masthead.UiScale = _uiScale;

        _chapterBar.Bounds = new Rectangle(0, mastH, W, tabH);
        _chapterBar.UiScale = _uiScale;

        _footer.Bounds = new Rectangle(0, H - footH, W, footH);
        _footer.Invalidate();

        _pageHost.Bounds = new Rectangle(0, hostY, W, hostH);
        _pagePrism.Bounds = new Rectangle(0, 0, W, hostH);
        _pageApps.Bounds = new Rectangle(0, 0, W, hostH);
        _pageTuning.Bounds = new Rectangle(0, 0, W, hostH);

        int padX = S(22);
        int contentW = W - padX * 2;

        // --- Layout Chapter I ---
        int y1 = S(14);
        _epigraph1.Bounds = new Rectangle(padX, y1, contentW, S(20));
        _epigraph1.UiScale = _uiScale;
        y1 += S(23);

        _btnMonitorPicker.Bounds = new Rectangle(padX, y1, contentW, S(34));
        _btnMonitorPicker.UiScale = _uiScale;
        y1 += S(46);

        int remaining1 = hostH - y1 - S(14) - S(46) - S(12) - S(34);
        int cardsH = Math.Max(S(116), remaining1);
        _modeCards.Bounds = new Rectangle(padX, y1, contentW, cardsH);
        _modeCards.UiScale = _uiScale;
        y1 += cardsH + S(13);

        int wlH = S(46);
        _whitelistBox.Bounds = new Rectangle(padX, y1, contentW, wlH);
        _whitelistBox.Padding = new Padding(Math.Max(1, S(1)));
        int jumpW = S(132);
        _chkWhitelistOverview.Bounds = new Rectangle(S(12), S(8), contentW - jumpW - S(28), S(30));
        _chkWhitelistOverview.UiScale = _uiScale;
        _btnJumpApps.Bounds = new Rectangle(contentW - jumpW - S(10), S(8), jumpW, S(30));
        _btnJumpApps.UiScale = _uiScale;
        y1 += wlH + S(12);

        int pGap = S(10);
        int pW = (contentW - pGap * 2) / 3;
        _btnKaleido.Bounds = new Rectangle(padX, y1, pW, S(34));
        _btnCarta.Bounds = new Rectangle(padX + pW + pGap, y1, pW, S(34));
        _btnSnap.Bounds = new Rectangle(padX + (pW + pGap) * 2, y1, contentW - (pW + pGap) * 2, S(34));
        _btnKaleido.UiScale = _btnCarta.UiScale = _btnSnap.UiScale = _uiScale;

        // --- Layout Chapter II ---
        int y2 = S(13);
        _epigraph2.Bounds = new Rectangle(padX, y2, contentW, S(20));
        _epigraph2.UiScale = _uiScale;
        y2 += S(23);

        int bPickW = S(122), bManW = S(84), bRstW = S(78), bGap = S(8);
        int rightBtnsW = bPickW + bManW + bRstW + bGap * 2;
        _chkWhitelistApps.Bounds = new Rectangle(padX, y2, Math.Max(S(160), contentW - rightBtnsW - S(10)), S(30));
        _chkWhitelistApps.UiScale = _uiScale;

        int bx = padX + contentW - rightBtnsW;
        _btnPickWin.Bounds = new Rectangle(bx, y2, bPickW, S(30));
        _btnAddManual.Bounds = new Rectangle(bx + bPickW + bGap, y2, bManW, S(30));
        _btnResetApps.Bounds = new Rectangle(bx + bPickW + bGap + bManW + bGap, y2, bRstW, S(30));
        _btnPickWin.UiScale = _btnAddManual.UiScale = _btnResetApps.UiScale = _uiScale;
        y2 += S(38);

        int rosterH = Math.Max(S(120), hostH - y2 - S(12));
        _rosterFrame.Bounds = new Rectangle(padX, y2, contentW, rosterH);
        _appRoster.UiScale = _uiScale;

        // --- Layout Chapter III ---
        int y3 = S(13);
        _epigraph3.Bounds = new Rectangle(padX, y3, contentW, S(20));
        _epigraph3.UiScale = _uiScale;
        y3 += S(23);

        int sliderSheetH = S(188);
        _sliderSheet.Bounds = new Rectangle(padX, y3, contentW, sliderSheetH);
        int sPad = S(14), sW = contentW - sPad * 2;
        EditorialSliderRow[] sliders = { _sldWhite, _sldBlack, _sldSat, _sldPhoto, _sldFps };
        for (int i = 0; i < sliders.Length; i++)
        {
            sliders[i].Bounds = new Rectangle(sPad, S(8) + i * S(34), sW, S(32));
            sliders[i].UiScale = _uiScale;
        }
        y3 += sliderSheetH + S(12);

        int optH = Math.Max(S(114), hostH - y3 - S(12));
        _optSheet.Bounds = new Rectangle(padX, y3, contentW, optH);
        int colW = (contentW - S(28)) / 2;
        int optRowStep = Math.Max(S(32), (optH - S(18)) / 3);
        EditorialCheckBox[] checks = { _chkVirtual, _chkKeepLight, _chkProtectPhotos, _chkPreserveHue, _chkEnhanceLight, _chkDebugMask };
        for (int i = 0; i < checks.Length; i++)
        {
            int col = i % 2, row = i / 2;
            checks[i].Bounds = new Rectangle(S(14) + col * colW, S(9) + row * optRowStep, colW - S(6), S(30));
            checks[i].UiScale = _uiScale;
        }

        ResumeLayout(true);
        Invalidate(true);
    }

    private static EditorialSectionHeader CreateEpigraph(string text)
    {
        return new EditorialSectionHeader
        {
            Text = text,
            BackColor = EditorialPalette.PaperBg
        };
    }

    private Panel CreateFramedSheet()
    {
        var sheet = new Panel
        {
            BackColor = EditorialPalette.PaperSheet,
            Padding = new Padding(1)
        };
        sheet.Paint += (s, e) =>
        {
            if (s is not Control c) return;
            using var pen = new Pen(EditorialPalette.Ink, MathF.Max(1f, 1f * _uiScale));
            e.Graphics.DrawRectangle(pen, 0, 0, c.Width - 1, c.Height - 1);
        };
        return sheet;
    }

    private EditorialSliderRow CreateSlider(Panel parent, string title, int min, int max, Func<int, string> fmt)
    {
        var s = new EditorialSliderRow
        {
            Title = title,
            Minimum = min,
            Maximum = max,
            ValueFormatter = fmt,
            BackColor = EditorialPalette.PaperSheet
        };
        s.ValueChanged += (_, _) =>
        {
            if (_suppressEvents) return;
            _settings.PaperWhiteKnee = _sldWhite.Value / 100.0f;
            _settings.InkBlackKnee = _sldBlack.Value / 100.0f;
            _settings.SaturationBoost = _sldSat.Value / 100.0f;
            _settings.PhotoSensitivity = _sldPhoto.Value / 100.0f;
            _settings.MaxFps = _sldFps.Value;
            SaveAndBroadcast();
        };
        parent.Controls.Add(s);
        return s;
    }

    private EditorialCheckBox CreateGridCheck(Panel parent, string text)
    {
        var chk = new EditorialCheckBox
        {
            Text = text,
            BackColor = EditorialPalette.PaperSheet
        };
        chk.CheckedChanged += (_, _) =>
        {
            if (_suppressEvents) return;
            _settings.VirtualDisplayMode = _chkVirtual.Checked;
            _settings.KeepLightWindows = _chkKeepLight.Checked;
            _settings.ProtectPhotos = _chkProtectPhotos.Checked;
            _settings.PreserveHue = _chkPreserveHue.Checked;
            _settings.EnhanceLightRegions = _chkEnhanceLight.Checked;
            _settings.DebugMaskOverlay = _chkDebugMask.Checked;
            SaveAndBroadcast();
        };
        parent.Controls.Add(chk);
        return chk;
    }

    private void PopulateMonitors()
    {
        _monitorEntries.Clear();
        _monitorEntries.Add((UiStrings.AutoSecondaryMonitor, "AUTO_SECONDARY"));

        var monitors = MonitorManager.GetAllMonitors();
        foreach (var mon in monitors)
            _monitorEntries.Add((mon.DisplayLabel, mon.DeviceName));

        int idx = _monitorEntries.FindIndex(m => string.Equals(m.Device, _settings.TargetMonitorDevice, StringComparison.OrdinalIgnoreCase));
        if (idx < 0)
        {
            int friendlyIdx = monitors.FindIndex(m => string.Equals(m.FriendlyName, _settings.TargetMonitorDevice, StringComparison.OrdinalIgnoreCase));
            if (friendlyIdx >= 0) idx = friendlyIdx + 1;
        }
        _btnMonitorPicker.Text = idx >= 0
            ? _monitorEntries[idx].Label
            : UiStrings.TargetStandby(_settings.TargetMonitorDevice);
        _btnMonitorPicker.Invalidate();
    }

    private void ShowMonitorDropdown()
    {
        var cms = new ContextMenuStrip { Font = EditorialPalette.FontPx("Microsoft YaHei UI", 12.4f, _uiScale) };
        foreach (var entry in _monitorEntries)
        {
            string dev = entry.Device;
            string lbl = entry.Label;
            var mi = new ToolStripMenuItem(lbl)
            {
                Checked = string.Equals(dev, _settings.TargetMonitorDevice, StringComparison.OrdinalIgnoreCase)
            };
            mi.Click += (_, _) =>
            {
                _settings.TargetMonitorDevice = dev;
                _btnMonitorPicker.Text = lbl;
                _btnMonitorPicker.Invalidate();
                SaveAndBroadcast();
            };
            cms.Items.Add(mi);
        }
        cms.Show(_btnMonitorPicker, new Point(0, _btnMonitorPicker.Height));
    }

    private void ShowPickRunningWindowMenu(Control anchor)
    {
        var menu = new ContextMenuStrip { Font = EditorialPalette.FontPx("Microsoft YaHei UI", 12.4f, _uiScale) };
        var running = WindowTracker.EnumerateRunningDesktopApps();
        var currentRules = _appRoster.GetRulesClone();

        foreach (var app in running)
        {
            bool exists = currentRules.Any(r => r.MatchesProcess(app.ProcessName));
            string label = exists
                ? $"{UiStrings.AlreadyInList}{app.ProcessName}  —  {app.SampleWindowTitle}"
                : $"{app.ProcessName}  —  {app.SampleWindowTitle}";
            var item = new ToolStripMenuItem(label) { Enabled = !exists };
            if (!exists)
            {
                string proc = app.ProcessName;
                string title = app.SampleWindowTitle;
                item.Click += (_, _) =>
                {
                    currentRules.Add(new AppInvertRule
                    {
                        Enabled = true,
                        Name = $"{proc} · {title}",
                        MatchProcesses = proc,
                        RuleMode = AppRuleMode.FollowGlobal
                    });
                    _settings.AppRules = currentRules;
                    LoadValuesToControls();
                    SaveAndBroadcast();
                };
            }
            menu.Items.Add(item);
        }

        if (menu.Items.Count == 0)
            menu.Items.Add(new ToolStripMenuItem(UiStrings.NoOtherApps) { Enabled = false });

        menu.Show(anchor, new Point(0, anchor.Height));
    }

    public void RefreshFromExternalSettings(AppSettings external)
    {
        _settings.Mode = external.Mode;
        if (_settings.Mode != FilterMode.Off)
            _lastNonOffMode = _settings.Mode;
        _settings.OnlyInvertListedApps = external.OnlyInvertListedApps;
        _settings.AppRules = external.AppRules.Select(r => r.Clone()).ToList();
        LoadValuesToControls();
    }

    private void UpdateBadges()
    {
        int enabledCount = _settings.AppRules?.Count(r => r.Enabled) ?? 0;
        _chapterBar.SetAppCountBadge(enabledCount, _settings.OnlyInvertListedApps);
        _btnJumpApps.Text = UiStrings.JumpAppsBtn(enabledCount);
        _btnJumpApps.Invalidate();
    }

    private void LoadValuesToControls()
    {
        _suppressEvents = true;

        _masthead.IsFilterActive = _settings.Mode != FilterMode.Off;
        _masthead.Invalidate();

        _modeCards.SelectedMode = _settings.Mode;
        _modeCards.Invalidate();

        _chkWhitelistOverview.Checked = _settings.OnlyInvertListedApps;
        _chkWhitelistApps.Checked = _settings.OnlyInvertListedApps;
        _appRoster.SetRules(_settings.AppRules ?? AppSettings.CreateDefaultAppRules());
        UpdateBadges();

        _sldWhite.Value = Math.Clamp((int)MathF.Round(_settings.PaperWhiteKnee * 100f), _sldWhite.Minimum, _sldWhite.Maximum);
        _sldBlack.Value = Math.Clamp((int)MathF.Round(_settings.InkBlackKnee * 100f), _sldBlack.Minimum, _sldBlack.Maximum);
        _sldSat.Value = Math.Clamp((int)MathF.Round(_settings.SaturationBoost * 100f), _sldSat.Minimum, _sldSat.Maximum);
        _sldPhoto.Value = Math.Clamp((int)MathF.Round(_settings.PhotoSensitivity * 100f), _sldPhoto.Minimum, _sldPhoto.Maximum);
        _sldFps.Value = Math.Clamp(_settings.MaxFps, _sldFps.Minimum, _sldFps.Maximum);

        _chkVirtual.Checked = _settings.VirtualDisplayMode;
        _chkKeepLight.Checked = _settings.KeepLightWindows;
        _chkProtectPhotos.Checked = _settings.ProtectPhotos;
        _chkPreserveHue.Checked = _settings.PreserveHue;
        _chkEnhanceLight.Checked = _settings.EnhanceLightRegions;
        _chkDebugMask.Checked = _settings.DebugMaskOverlay;

        _suppressEvents = false;
    }

    private void SaveAndBroadcast()
    {
        _settings.Save();
        _onChanged(_settings.Clone());
    }
}
