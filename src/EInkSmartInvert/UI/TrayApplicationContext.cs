using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using EInkSmartInvert.Config;
using EInkSmartInvert.Core;

namespace EInkSmartInvert.UI;

public sealed class TrayApplicationContext : ApplicationContext
{
    private sealed class HotkeyMessageWindow : NativeWindow, IDisposable
    {
        private const int WM_HOTKEY = 0x0312;
        private readonly Action<int> _onHotkey;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const uint MOD_ALT = 0x0001;
        private const uint MOD_WIN = 0x0008;
        private const uint MOD_NOREPEAT = 0x4000;

        public HotkeyMessageWindow(Action<int> onHotkey)
        {
            _onHotkey = onHotkey;
            CreateHandle(new CreateParams());

            // Win+Alt+I (0x49): Toggle On/Off
            RegisterHotKey(Handle, 1, MOD_WIN | MOD_ALT | MOD_NOREPEAT, 0x49);
            // Win+Alt+M (0x4D): Cycle Mode
            RegisterHotKey(Handle, 2, MOD_WIN | MOD_ALT | MOD_NOREPEAT, 0x4D);
            // Win+Alt+S (0x53): Open Settings
            RegisterHotKey(Handle, 3, MOD_WIN | MOD_ALT | MOD_NOREPEAT, 0x53);
            // Win+Alt+D (0x44): Export Diagnostic Snapshot
            RegisterHotKey(Handle, 4, MOD_WIN | MOD_ALT | MOD_NOREPEAT, 0x44);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY)
            {
                _onHotkey((int)m.WParam);
            }
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            if (Handle != IntPtr.Zero)
            {
                UnregisterHotKey(Handle, 1);
                UnregisterHotKey(Handle, 2);
                UnregisterHotKey(Handle, 3);
                UnregisterHotKey(Handle, 4);
                DestroyHandle();
            }
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private AppSettings _settings;
    private readonly OverlayEngine _engine;
    private readonly NotifyIcon _trayIcon;
    private readonly HotkeyMessageWindow _hotkeyWindow;
    private SettingsForm? _settingsForm;
    private FilterMode _lastActiveMode = FilterMode.SmartAuto;
    private Icon? _currentIcon;

    private ToolStripMenuItem _miSmartAuto = null!;
    private ToolStripMenuItem _miForceInvert = null!;
    private ToolStripMenuItem _miCrispOnly = null!;
    private ToolStripMenuItem _miOff = null!;
    private ToolStripMenuItem _miOnlyListedApps = null!;
    private ToolStripMenuItem _miStatus = null!;

    private SynchronizationContext _uiSync = null!;
    private RegisteredWaitHandle? _exitWaitReg;

    public TrayApplicationContext(AppSettings initialSettings, EventWaitHandle? externalExitEvent = null, bool openUiOnStart = false)
    {
        _settings = initialSettings;
        if (_settings.Mode != FilterMode.Off)
            _lastActiveMode = _settings.Mode;

        _engine = new OverlayEngine(_settings);
        _hotkeyWindow = new HotkeyMessageWindow(HandleHotkey);

        _trayIcon = new NotifyIcon
        {
            Text = UiStrings.TrayTooltip,
            ContextMenuStrip = BuildContextMenu()
        };
        UpdateTrayState();
        _trayIcon.Visible = true;

        _trayIcon.DoubleClick += (_, _) => OpenSettingsWindow();
        _engine.StatusChanged += status =>
        {
            if (_trayIcon.ContextMenuStrip is { IsDisposed: false } cms && cms.InvokeRequired)
                cms.BeginInvoke(() => _miStatus.Text = status);
            else
                _miStatus.Text = status;
        };

        _engine.Start();

        // Capture real WindowsFormsSynchronizationContext AFTER OverlayHostForm handle creation installed it
        _uiSync = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

        if (externalExitEvent != null)
        {
            _exitWaitReg = ThreadPool.RegisterWaitForSingleObject(
                externalExitEvent,
                (_, timedOut) =>
                {
                    if (!timedOut)
                        _uiSync.Post(_ => ExitThread(), null);
                },
                null,
                -1,
                executeOnlyOnce: true);
        }

        _trayIcon.ShowBalloonTip(
            2500,
            UiStrings.TrayStartedTitle,
            UiStrings.TrayStartedBody,
            ToolTipIcon.Info);

        if (openUiOnStart)
        {
            var openTimer = new System.Windows.Forms.Timer { Interval = 80 };
            openTimer.Tick += (_, _) =>
            {
                openTimer.Stop();
                openTimer.Dispose();
                OpenSettingsWindow();
            };
            openTimer.Start();
        }
    }

    private ContextMenuStrip BuildContextMenu()
    {
        var menu = new ContextMenuStrip();

        _miStatus = new ToolStripMenuItem(UiStrings.IsZh ? "初始化中..." : "Initializing...") { Enabled = false };
        menu.Items.Add(_miStatus);
        menu.Items.Add(new ToolStripSeparator());

        _miSmartAuto = new ToolStripMenuItem(UiStrings.TrayMenuSmart, null, (_, _) => SetMode(FilterMode.SmartAuto));
        _miForceInvert = new ToolStripMenuItem(UiStrings.TrayMenuForce, null, (_, _) => SetMode(FilterMode.ForceInvertAll));
        _miCrispOnly = new ToolStripMenuItem(UiStrings.TrayMenuCrisp, null, (_, _) => SetMode(FilterMode.CrispLightOnly));
        _miOff = new ToolStripMenuItem(UiStrings.TrayMenuOff, null, (_, _) => SetMode(FilterMode.Off));

        menu.Items.AddRange(new ToolStripItem[] { _miSmartAuto, _miForceInvert, _miCrispOnly, _miOff });
        menu.Items.Add(new ToolStripSeparator());

        _miOnlyListedApps = new ToolStripMenuItem(UiStrings.TrayMenuWhitelist, null, (_, _) =>
        {
            _settings.OnlyInvertListedApps = !_settings.OnlyInvertListedApps;
            _settings.Save();
            _engine.UpdateSettings(_settings);
            UpdateTrayState();
            if (_settingsForm != null && !_settingsForm.IsDisposed)
                _settingsForm.RefreshFromExternalSettings(_settings);
        });
        var miAppRules = new ToolStripMenuItem(UiStrings.TrayMenuRoster, null, (_, _) => OpenSettingsWindow(initialChapter: 1));
        menu.Items.Add(_miOnlyListedApps);
        menu.Items.Add(miAppRules);
        menu.Items.Add(new ToolStripSeparator());

        var miSettings = new ToolStripMenuItem(UiStrings.TrayMenuConsole, null, (_, _) => OpenSettingsWindow(initialChapter: 0));
        var miExit = new ToolStripMenuItem(UiStrings.TrayMenuExit, null, (_, _) => ExitThread());

        menu.Items.Add(miSettings);
        menu.Items.Add(miExit);
        return menu;
    }

    private void HandleHotkey(int id)
    {
        switch (id)
        {
            case 1: // Toggle On/Off
                if (_settings.Mode == FilterMode.Off)
                    SetMode(_lastActiveMode);
                else
                    SetMode(FilterMode.Off);
                break;

            case 2: // Cycle Mode
                FilterMode next = _settings.Mode switch
                {
                    FilterMode.SmartAuto => FilterMode.ForceInvertAll,
                    FilterMode.ForceInvertAll => FilterMode.CrispLightOnly,
                    FilterMode.CrispLightOnly => FilterMode.Off,
                    _ => FilterMode.SmartAuto
                };
                SetMode(next);
                break;

            case 3: // Open Settings
                OpenSettingsWindow();
                break;

            case 4: // Diagnostic Snapshot
                _engine.RequestDiagnosticSnapshot();
                _trayIcon.ShowBalloonTip(1800, UiStrings.TraySnapTitle, UiStrings.TraySnapBody, ToolTipIcon.Info);
                break;
        }
    }

    private void SetMode(FilterMode mode)
    {
        if (mode != FilterMode.Off)
            _lastActiveMode = mode;

        _settings.Mode = mode;
        _settings.Save();
        _engine.UpdateSettings(_settings);
        UpdateTrayState();
    }

    private void UpdateTrayState()
    {
        _miSmartAuto.Checked = _settings.Mode == FilterMode.SmartAuto;
        _miForceInvert.Checked = _settings.Mode == FilterMode.ForceInvertAll;
        _miCrispOnly.Checked = _settings.Mode == FilterMode.CrispLightOnly;
        _miOff.Checked = _settings.Mode == FilterMode.Off;
        if (_miOnlyListedApps != null)
            _miOnlyListedApps.Checked = _settings.OnlyInvertListedApps;

        var newIcon = AppIcon.CreateTrayIcon(_settings.Mode != FilterMode.Off, 32);
        _trayIcon.Icon = newIcon;
        _currentIcon?.Dispose();
        _currentIcon = newIcon;
    }

    private void OpenSettingsWindow(int initialChapter = 0)
    {
        if (_settingsForm != null && !_settingsForm.IsDisposed)
        {
            _settingsForm.ShowChapter(initialChapter);
            _settingsForm.Activate();
            return;
        }

        _settingsForm = new SettingsForm(_settings, _engine, updated =>
        {
            _settings = updated;
            if (_settings.Mode != FilterMode.Off)
                _lastActiveMode = _settings.Mode;
            _engine.UpdateSettings(_settings);
            UpdateTrayState();
        }, initialChapter);
        _settingsForm.Show();
    }

    protected override void ExitThreadCore()
    {
        _exitWaitReg?.Unregister(null);
        _settingsForm?.Close();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _currentIcon?.Dispose();
        _hotkeyWindow.Dispose();
        _engine.Dispose();
        base.ExitThreadCore();
    }
}
