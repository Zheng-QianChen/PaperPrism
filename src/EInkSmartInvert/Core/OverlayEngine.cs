using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using EInkSmartInvert.Config;
using EInkSmartInvert.Graphics;
using static EInkSmartInvert.Graphics.D3D11Interop;

namespace EInkSmartInvert.Core;

public sealed class OverlayHostForm : Form
{
    private const int WS_EX_TOPMOST             = 0x00000008;
    private const int WS_EX_TRANSPARENT         = 0x00000020;
    private const int WS_EX_TOOLWINDOW          = 0x00000080;
    private const int WS_EX_LAYERED             = 0x00080000;
    private const int WS_EX_NOREDIRECTIONBITMAP = 0x00200000;
    private const int WS_EX_NOACTIVATE          = 0x08000000;

    private const uint WDA_NONE               = 0x00000000;
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const uint SWP_NOMOVE     = 0x0002;
    private const uint SWP_NOSIZE     = 0x0001;
    private const int  SW_SHOWNOACTIVATE = 4;
    private const int  WM_DPICHANGED  = 0x02E0;

    private Rectangle _targetPhysicalBounds;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool CaptureExclusionActive { get; private set; }

    public OverlayHostForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        Text = "PaperPrism Overlay";
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOPMOST | WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_NOREDIRECTIONBITMAP;
            return cp;
        }
    }

    protected override void OnPaintBackground(PaintEventArgs e) { }

    protected override void WndProc(ref Message m)
    {
        // Prevent WinForms PerMonitorV2 from rescaling our 1:1 physical-pixel overlay back to 300x300
        // when moving across 100% DPI primary monitor -> 300% DPI virtual E-Ink monitor!
        if (m.Msg == WM_DPICHANGED)
        {
            m.Result = IntPtr.Zero;
            return;
        }
        base.WndProc(ref m);
    }

    public bool ApplyCaptureExclusion(bool excludeFromCapture)
    {
        if (!IsHandleCreated) return false;
        uint flag = excludeFromCapture ? WDA_EXCLUDEFROMCAPTURE : WDA_NONE;
        bool ok = SetWindowDisplayAffinity(Handle, flag);
        CaptureExclusionActive = excludeFromCapture && ok;
        return ok;
    }

    public void PositionOnMonitor(Rectangle bounds)
    {
        _targetPhysicalBounds = bounds;
        if (IsHandleCreated)
        {
            ShowWindow(Handle, SW_SHOWNOACTIVATE);
            IntPtr lowestOther = WindowTracker.FindLowestOtherTopMostInSameBand(Handle, out _);
            IntPtr insertAfter = lowestOther != IntPtr.Zero ? lowestOther : HWND_TOPMOST;
            if (!SetWindowPos(Handle, insertAfter, bounds.X, bounds.Y, bounds.Width, bounds.Height, SWP_NOACTIVATE | SWP_SHOWWINDOW))
                SetWindowPos(Handle, HWND_TOPMOST, bounds.X, bounds.Y, bounds.Width, bounds.Height, SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }
    }

    public void EnsureTopMost()
    {
        if (IsHandleCreated && Visible)
        {
            IntPtr lowestOther = WindowTracker.FindLowestOtherTopMostInSameBand(Handle, out bool alreadyAtFloor);
            IntPtr insertAfter = lowestOther != IntPtr.Zero ? lowestOther : HWND_TOPMOST;

            if (_targetPhysicalBounds.Width > 0 && Bounds != _targetPhysicalBounds)
            {
                if (!SetWindowPos(Handle, insertAfter, _targetPhysicalBounds.X, _targetPhysicalBounds.Y, _targetPhysicalBounds.Width, _targetPhysicalBounds.Height, SWP_NOACTIVATE | SWP_SHOWWINDOW))
                    SetWindowPos(Handle, HWND_TOPMOST, _targetPhysicalBounds.X, _targetPhysicalBounds.Y, _targetPhysicalBounds.Width, _targetPhysicalBounds.Height, SWP_NOACTIVATE | SWP_SHOWWINDOW);
            }
            else if (!alreadyAtFloor)
            {
                if (!SetWindowPos(Handle, insertAfter, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE))
                    SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            }
        }
    }
}

public sealed unsafe class OverlayEngine : IDisposable
{
    [DllImport("psapi.dll")]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr ho);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr hdc, int x, int y, int cx, int cy, IntPtr hdcSrc, int x1, int y1, uint rop);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO pbmi, uint usage, out IntPtr ppvBits, IntPtr hSection, uint offset);

    private delegate void WinEventDelegate(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(
        uint eventMin,
        uint eventMax,
        IntPtr hmodWinEventProc,
        WinEventDelegate lpfnWinEventProc,
        uint idProcess,
        uint idThread,
        uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    private const uint EVENT_OBJECT_SHOW          = 0x8002;
    private const uint EVENT_OBJECT_HIDE          = 0x8003;
    private const uint WINEVENT_SKIPOWNPROCESS    = 0x0002;
    private const uint SRCCOPY = 0x00CC0020;

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        public uint bmiColors;
    }

    private readonly string _logPath;
    private readonly string _agentCmdPath;
    private readonly object _logLock = new();
    private readonly AutoResetEvent _wakeEvent = new(false);
    private readonly WinEventDelegate _winEventProc;
    private IntPtr _winEventHook;
    private OverlayHostForm? _overlayForm;
    private IntPtr _overlayHwnd;
    private Thread? _renderThread;
    private CancellationTokenSource? _cts;

    private volatile AppSettings _settings;
    private volatile bool _settingsDirty = true;
    private volatile bool _sessionRestartRequested = true;
    private volatile bool _snapshotRequested;
    private int _accessDeniedStreak;

    public string StatusMessage { get; private set; } = "初始化中...";
    public event Action<string>? StatusChanged;

    public OverlayEngine(AppSettings initialSettings)
    {
        _settings = initialSettings.Clone();
        _winEventProc = OnWinEvent;
        _logPath = Path.Combine(AppContext.BaseDirectory, "runtime.log");
        _agentCmdPath = Path.Combine(AppContext.BaseDirectory, "agent_cmd.txt");
        LogDiag("=== PaperPrism GPU Engine v1.0.0 initialized ===");
    }

    private long _lastWinEventTick;

    private void OnWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        // Ignore events when overlay is hidden/standby, and throttle to >=60ms to prevent background app HWND spam!
        if (idObject != 0 || idChild != 0 || hwnd == IntPtr.Zero || hwnd == _overlayHwnd)
            return;

        var form = _overlayForm;
        if (form == null || !form.Visible)
            return;

        long now = Environment.TickCount64;
        if (now - _lastWinEventTick < 60)
            return;
        _lastWinEventTick = now;

        form.EnsureTopMost();
        _wakeEvent.Set();
    }

    private void LogDiag(string message)
    {
        try
        {
            lock (_logLock)
            {
                File.AppendAllText(_logPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
            }
        }
        catch { }
    }

    private static void TrimProcessMemory()
    {
        try
        {
            GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            EmptyWorkingSet(Process.GetCurrentProcess().Handle);
        }
        catch { }
    }

    public void Start()
    {
        if (_renderThread != null)
            return;

        _overlayForm = new OverlayHostForm();
        _overlayHwnd = _overlayForm.Handle;
        _winEventHook = SetWinEventHook(
            EVENT_OBJECT_SHOW,
            EVENT_OBJECT_HIDE,
            IntPtr.Zero,
            _winEventProc,
            0,
            0,
            WINEVENT_SKIPOWNPROCESS);
        LogDiag($"Overlay HWND=0x{_overlayHwnd:X}, WinEventHook=0x{_winEventHook:X}");

        _cts = new CancellationTokenSource();
        _renderThread = new Thread(() => RenderLoop(_cts.Token))
        {
            Name = "PaperPrism-RenderLoop",
            IsBackground = true,
            Priority = ThreadPriority.AboveNormal
        };
        _renderThread.Start();
    }

    public void UpdateSettings(AppSettings updated)
    {
        bool needRestart =
            !string.Equals(_settings.TargetMonitorDevice, updated.TargetMonitorDevice, StringComparison.OrdinalIgnoreCase) ||
            _settings.VirtualDisplayMode != updated.VirtualDisplayMode;

        _settings = updated.Clone();
        _settingsDirty = true;
        if (needRestart)
            _sessionRestartRequested = true;
        _wakeEvent.Set();
    }

    public void ForceRefresh()
    {
        _settingsDirty = true;
        _sessionRestartRequested = true;
        _wakeEvent.Set();
    }

    public void RequestDiagnosticSnapshot()
    {
        _snapshotRequested = true;
        _settingsDirty = true;
    }

    private void CheckAgentCommandFile()
    {
        try
        {
            if (!File.Exists(_agentCmdPath))
                return;

            string cmd = File.ReadAllText(_agentCmdPath).Trim();
            File.Delete(_agentCmdPath);
            LogDiag($"Agent command received: '{cmd}'");

            if (cmd.Contains("restart", StringComparison.OrdinalIgnoreCase))
            {
                string exePath = Path.Combine(AppContext.BaseDirectory, "PaperPrism.exe");
                if (!File.Exists(exePath))
                    exePath = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "EInkSmartInvert.exe");
                if (File.Exists(exePath))
                {
                    LogDiag("Self-restarting onto updated binary in interactive desktop...");
                    Process.Start(new ProcessStartInfo(exePath)
                    {
                        UseShellExecute = false,
                        WorkingDirectory = AppContext.BaseDirectory
                    });
                    return;
                }
            }
            if (cmd.Contains("reload", StringComparison.OrdinalIgnoreCase))
            {
                var reloaded = AppSettings.Load();
                UpdateSettings(reloaded);
            }
            if (cmd.Contains("snapshot", StringComparison.OrdinalIgnoreCase))
            {
                RequestDiagnosticSnapshot();
            }
        }
        catch (Exception ex)
        {
            LogDiag($"Agent cmd error: {ex.Message}");
        }
    }

    private void ReportStatus(string msg)
    {
        if (string.Equals(StatusMessage, msg, StringComparison.Ordinal))
            return;

        StatusMessage = msg;
        LogDiag($"Status: {msg}");
        StatusChanged?.Invoke(msg);
    }

    private void ConfigureOverlayWindow(bool visible, bool excludeFromCapture, Rectangle? bounds = null)
    {
        var form = _overlayForm;
        if (form == null || form.IsDisposed || !form.IsHandleCreated)
            return;

        try
        {
            form.Invoke(() =>
            {
                if (form.IsDisposed) return;
                if (visible)
                {
                    form.ApplyCaptureExclusion(excludeFromCapture);
                    if (!form.Visible)
                        form.Show();
                    if (bounds.HasValue)
                        form.PositionOnMonitor(bounds.Value);
                    form.EnsureTopMost();
                }
                else
                {
                    if (form.Visible)
                        form.Hide();
                }
            });
        }
        catch { }
    }

    private void RenderLoop(CancellationToken ct)
    {
        var tracker = new GeometricRegionTracker();
        long lastMaintenanceTick = Environment.TickCount64;
        bool wasPaused = false;

        while (!ct.IsCancellationRequested)
        {
            CheckAgentCommandFile();
            var curSettings = _settings;

            if (curSettings.Mode == FilterMode.Off)
            {
                if (!wasPaused)
                {
                    wasPaused = true;
                    ConfigureOverlayWindow(visible: false, excludeFromCapture: false);
                    ReportStatus("已暂停 (原画直通 · 0% GPU)");
                    TrimProcessMemory();
                }
                Thread.Sleep(250);
                continue;
            }
            wasPaused = false;

            var targetMon = MonitorManager.SelectTargetMonitor(curSettings.TargetMonitorDevice);
            if (targetMon == null)
            {
                if (!wasPaused)
                {
                    wasPaused = true;
                    ConfigureOverlayWindow(visible: false, excludeFromCapture: false);
                    ReportStatus("等待副屏连接唤醒中 (0% GPU 待命)");
                    TrimProcessMemory();
                }
                Thread.Sleep(1500);
                continue;
            }

            _sessionRestartRequested = false;
            tracker.Reset();

            bool excludeCapture = !curSettings.VirtualDisplayMode;
            ConfigureOverlayWindow(visible: true, excludeFromCapture: excludeCapture, bounds: targetMon.Bounds);

            RunMonitorSession(targetMon, _overlayHwnd, curSettings.VirtualDisplayMode, tracker, ref lastMaintenanceTick, ct);
        }
    }

    private static void SaveRealMonitorBitBltPng(Rectangle monBounds, string outFile)
    {
        IntPtr sdc = GetDC(IntPtr.Zero);
        if (sdc == IntPtr.Zero) return;
        IntPtr memDc = IntPtr.Zero;
        IntPtr dib = IntPtr.Zero;
        IntPtr oldObj = IntPtr.Zero;
        try
        {
            int w = monBounds.Width;
            int h = monBounds.Height;
            memDc = CreateCompatibleDC(sdc);
            var bmi = new BITMAPINFO
            {
                bmiHeader = new BITMAPINFOHEADER
                {
                    biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                    biWidth = w,
                    biHeight = -h,
                    biPlanes = 1,
                    biBitCount = 32
                }
            };
            dib = CreateDIBSection(sdc, ref bmi, 0, out IntPtr bits, IntPtr.Zero, 0);
            if (dib == IntPtr.Zero || bits == IntPtr.Zero) return;
            oldObj = SelectObject(memDc, dib);
            if (BitBlt(memDc, 0, 0, w, h, sdc, monBounds.X, monBounds.Y, SRCCOPY))
            {
                using var bmp = new Bitmap(w, h, w * 4, PixelFormat.Format32bppArgb, bits);
                bmp.Save(outFile, ImageFormat.Png);
            }
        }
        finally
        {
            if (memDc != IntPtr.Zero)
            {
                if (oldObj != IntPtr.Zero) SelectObject(memDc, oldObj);
                if (dib != IntPtr.Zero) DeleteObject(dib);
                DeleteDC(memDc);
            }
            ReleaseDC(IntPtr.Zero, sdc);
        }
    }

    private static bool TrySeedTextureViaOneShotBitBlt(IntPtr context, IntPtr dstTex, Rectangle monBounds)
    {
        int w = monBounds.Width;
        int h = monBounds.Height;
        IntPtr sdc = GetDC(IntPtr.Zero);
        if (sdc == IntPtr.Zero) return false;

        IntPtr memDc = IntPtr.Zero;
        IntPtr dib = IntPtr.Zero;
        IntPtr oldObj = IntPtr.Zero;
        try
        {
            memDc = CreateCompatibleDC(sdc);
            var bmi = new BITMAPINFO
            {
                bmiHeader = new BITMAPINFOHEADER
                {
                    biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                    biWidth = w,
                    biHeight = -h,
                    biPlanes = 1,
                    biBitCount = 32
                }
            };
            dib = CreateDIBSection(sdc, ref bmi, 0, out IntPtr bits, IntPtr.Zero, 0);
            if (dib == IntPtr.Zero || bits == IntPtr.Zero) return false;

            oldObj = SelectObject(memDc, dib);
            if (!BitBlt(memDc, 0, 0, w, h, sdc, monBounds.X, monBounds.Y, SRCCOPY))
                return false;

            UpdateSubresource(context, dstTex, 0, IntPtr.Zero, bits, (uint)(w * 4), 0);
            return true;
        }
        finally
        {
            if (memDc != IntPtr.Zero)
            {
                if (oldObj != IntPtr.Zero) SelectObject(memDc, oldObj);
                if (dib != IntPtr.Zero) DeleteObject(dib);
                DeleteDC(memDc);
            }
            ReleaseDC(IntPtr.Zero, sdc);
        }
    }

    private void RunMonitorSession(
        MonitorDescriptor monitor,
        IntPtr overlayHwnd,
        bool virtualDisplayMode,
        GeometricRegionTracker tracker,
        ref long lastMaintenanceTick,
        CancellationToken ct)
    {
        IntPtr factory1 = IntPtr.Zero;
        IntPtr factory2 = IntPtr.Zero;
        IntPtr adapter = IntPtr.Zero;
        IntPtr output = IntPtr.Zero;
        IntPtr output1 = IntPtr.Zero;
        IntPtr device = IntPtr.Zero;
        IntPtr dxgiDevice = IntPtr.Zero;
        IntPtr context = IntPtr.Zero;
        IntPtr swapChain = IntPtr.Zero;
        IntPtr backBufferRtv = IntPtr.Zero;
        IntPtr dcompDevice = IntPtr.Zero;
        IntPtr dcompTarget = IntPtr.Zero;
        IntPtr dcompVisual = IntPtr.Zero;
        IntPtr duplication = IntPtr.Zero;
        IntPtr cachedTex = IntPtr.Zero;
        IntPtr cachedSrv = IntPtr.Zero;
        GpuSmartInvertPipeline? pipeline = null;
        WgcWindowCompositor? wgcCompositor = null;

        int monW = monitor.Bounds.Width;
        int monH = monitor.Bounds.Height;
        var winBuffer = new TrackedWindow[16];

        void EnsureCachedTexture(int w, int h, int dxgiFormat)
        {
            if (cachedTex != IntPtr.Zero)
            {
                Texture2DGetDesc(cachedTex, out var existing);
                if (existing.Width == (uint)w && existing.Height == (uint)h && existing.Format == dxgiFormat)
                    return;
                SafeRelease(ref cachedSrv);
                SafeRelease(ref cachedTex);
            }

            var desc = new D3D11_TEXTURE2D_DESC
            {
                Width = (uint)w,
                Height = (uint)h,
                MipLevels = 1,
                ArraySize = 1,
                Format = dxgiFormat,
                SampleDesc = new DXGI_SAMPLE_DESC { Count = 1, Quality = 0 },
                Usage = D3D11_USAGE_DEFAULT,
                BindFlags = D3D11_BIND_SHADER_RESOURCE
            };
            CheckHr(CreateTexture2D(device, desc, IntPtr.Zero, out cachedTex), "CreateTexture2D(CachedSrc)");
            CheckHr(CreateShaderResourceView(device, cachedTex, IntPtr.Zero, out cachedSrv), "CreateSRV(CachedSrc)");
        }

        try
        {
            LogDiag($"Starting session on {monitor.DeviceName} Bounds={monitor.Bounds} VirtualDisplayMode={virtualDisplayMode}");

            if (CreateDXGIFactory1(IID_IDXGIFactory1, out factory1) < 0)
            {
                ReportStatus("DXGI 初始化失败");
                Thread.Sleep(1000);
                return;
            }

            CheckHr(QueryInterface(factory1, IID_IDXGIFactory2, out factory2), "QueryInterface(IDXGIFactory2)");

            // In VirtualDisplayMode, ALWAYS bind directly to the Primary Hardware GPU (IntPtr.Zero) rather than
            // an IddCx indirect display adapter clone, avoiding cross-adapter DWM/WUDFHost surface marshaling!
            if (!virtualDisplayMode)
            {
                for (uint a = 0; FactoryEnumAdapters1(factory1, a, out IntPtr candAdapter) >= 0; a++)
                {
                    for (uint o = 0; AdapterEnumOutputs(candAdapter, o, out IntPtr candOutput) >= 0; o++)
                    {
                        if (OutputGetDesc(candOutput, out var odesc) >= 0 &&
                            odesc.AttachedToDesktop != 0 &&
                            string.Equals(odesc.DeviceName, monitor.DeviceName, StringComparison.OrdinalIgnoreCase))
                        {
                            adapter = candAdapter;
                            output = candOutput;
                            break;
                        }
                        SafeRelease(ref candOutput);
                    }
                    if (output != IntPtr.Zero)
                        break;
                    SafeRelease(ref candAdapter);
                }
            }

            int driverType = (adapter != IntPtr.Zero) ? 0 : 1;
            int hrDev = D3D11CreateDevice(adapter, driverType, IntPtr.Zero, 0x20, IntPtr.Zero, 0, 7, out device, out int fl, out context);
            if (hrDev < 0)
            {
                ReportStatus($"D3D11 设备创建失败 (0x{hrDev:X8})");
                Thread.Sleep(1000);
                return;
            }

            var scDesc = new DXGI_SWAP_CHAIN_DESC1
            {
                Width = (uint)monW,
                Height = (uint)monH,
                Format = DXGI_FORMAT_B8G8R8A8_UNORM,
                Stereo = 0,
                SampleDesc = new DXGI_SAMPLE_DESC { Count = 1, Quality = 0 },
                BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT,
                BufferCount = 2,
                Scaling = DXGI_SCALING_STRETCH,
                SwapEffect = DXGI_SWAP_EFFECT_FLIP_DISCARD,
                AlphaMode = DXGI_ALPHA_MODE_PREMULTIPLIED,
                Flags = 0
            };

            CheckHr(FactoryCreateSwapChainForComposition(factory2, device, scDesc, out swapChain), "CreateSwapChainForComposition");
            CheckHr(QueryInterface(device, IID_IDXGIDevice, out dxgiDevice), "QueryInterface(IDXGIDevice)");
            if (QueryInterface(device, IID_IDXGIDevice1, out IntPtr dxgiDev1) >= 0 && dxgiDev1 != IntPtr.Zero)
            {
                DXGIDevice1SetMaximumFrameLatency(dxgiDev1, 1);
                SafeRelease(ref dxgiDev1);
            }
            CheckHr(DCompositionCreateDevice(dxgiDevice, IID_IDCompositionDevice, out dcompDevice), "DCompositionCreateDevice");

            CheckHr(DCompCreateTargetForHwnd(dcompDevice, overlayHwnd, 1, out dcompTarget), "DCompCreateTargetForHwnd");
            CheckHr(DCompCreateVisual(dcompDevice, out dcompVisual), "DCompCreateVisual");
            CheckHr(DCompVisualSetContent(dcompVisual, swapChain), "DCompVisualSetContent");
            CheckHr(DCompTargetSetRoot(dcompTarget, dcompVisual), "DCompTargetSetRoot");
            CheckHr(DCompCommit(dcompDevice), "DCompCommit");

            CheckHr(SwapChainGetBuffer(swapChain, 0, IID_ID3D11Texture2D, out IntPtr backBuf), "SwapChainGetBuffer");
            try
            {
                CheckHr(CreateRenderTargetView(device, backBuf, IntPtr.Zero, out backBufferRtv), "CreateRTV(BackBuffer)");
            }
            finally
            {
                SafeRelease(ref backBuf);
            }

            pipeline = new GpuSmartInvertPipeline(device, context);

            uint rotationShaderCode = 1;
            int srcTexFormat = DXGI_FORMAT_B8G8R8A8_UNORM;
            bool hasValidCachedFrame = false;

            if (virtualDisplayMode)
            {
                // Per-Window GPU WGC Compositor (WDA_NONE: 100% visible on virtual displays / streamers!)
                wgcCompositor = new WgcWindowCompositor(device, dxgiDevice, context, monW, monH);

                // Clear initial overlay to full transparent until first window frame arrives (~15ms)
                float* clearZero = stackalloc float[4] { 0f, 0f, 0f, 0f };
                ClearRenderTargetView(context, backBufferRtv, clearZero);
                SwapChainPresent(swapChain, 0, 0);

                string monTitle = !string.IsNullOrWhiteSpace(monitor.FriendlyName) ? monitor.FriendlyName : monitor.DeviceName;
                ReportStatus($"运行中 — {monTitle} · {monW}×{monH}");
            }
            else
            {
                int dupHr = DXGI_ERROR_UNSUPPORTED;
                if (output != IntPtr.Zero && QueryInterface(output, IID_IDXGIOutput1, out output1) >= 0)
                {
                    dupHr = Output1DuplicateOutput(output1, device, out duplication);
                    LogDiag($"DuplicateOutput hr=0x{dupHr:X8}");
                }

                if (dupHr >= 0 && duplication != IntPtr.Zero)
                {
                    _accessDeniedStreak = 0;
                    string monTitle = !string.IsNullOrWhiteSpace(monitor.FriendlyName) ? monitor.FriendlyName : monitor.DeviceName;
                    ReportStatus($"运行中 — {monTitle} · {monW}×{monH}");
                }
                else if (dupHr == E_ACCESSDENIED && ++_accessDeniedStreak < 3)
                {
                    ReportStatus("等待桌面权限恢复 (UAC/锁屏切换中)...");
                    Thread.Sleep(400);
                    return;
                }
                else
                {
                    // Automatic fallback to WGC per-window compositor if DuplicateOutput is unsupported
                    LogDiag($"DuplicateOutput unavailable (0x{dupHr:X8}), switching to WGC Window Compositor...");
                    ConfigureOverlayWindow(visible: true, excludeFromCapture: false, bounds: monitor.Bounds);
                    virtualDisplayMode = true;
                    wgcCompositor = new WgcWindowCompositor(device, dxgiDevice, context, monW, monH);
                    string monTitle = !string.IsNullOrWhiteSpace(monitor.FriendlyName) ? monitor.FriendlyName : monitor.DeviceName;
                    ReportStatus($"运行中 — {monTitle} · {monW}×{monH}");
                }

                if (!virtualDisplayMode)
                {
                    EnsureCachedTexture(monW, monH, DXGI_FORMAT_B8G8R8A8_UNORM);
                    if (TrySeedTextureViaOneShotBitBlt(context, cachedTex, monitor.Bounds))
                    {
                        hasValidCachedFrame = true;
                        int initWins = WindowTracker.FillVisibleWindowsOnMonitor(monitor.Bounds, overlayHwnd, winBuffer, settings: _settings);
                        pipeline.ProcessFrame(cachedSrv, backBufferRtv, monW, monH, 1, winBuffer.AsSpan(0, initWins), _settings, tracker, srcIsLinear: false, useHardwareHolePunch: true, uncoveredDesktopPassthrough: false);
                        SwapChainPresent(swapChain, 0, DXGI_PRESENT_DO_NOT_WAIT);
                    }
                }
            }

            TrimProcessMemory();

            var swFrame = Stopwatch.StartNew();
            long lastCmdPollTick = Environment.TickCount64;

            while (!ct.IsCancellationRequested && !_sessionRestartRequested)
            {
                var curSettings = _settings;
                if (curSettings.Mode == FilterMode.Off)
                    break;

                long nowTick = Environment.TickCount64;
                if (nowTick - lastCmdPollTick >= 450)
                {
                    lastCmdPollTick = nowTick;
                    CheckAgentCommandFile();
                    curSettings = _settings;
                    if (_sessionRestartRequested || curSettings.Mode == FilterMode.Off)
                        break;
                }

                if (nowTick - lastMaintenanceTick > 2500)
                {
                    lastMaintenanceTick = nowTick;
                    var refreshedMon = MonitorManager.SelectTargetMonitor(curSettings.TargetMonitorDevice);
                    if (refreshedMon == null || refreshedMon.Bounds != monitor.Bounds || refreshedMon.DeviceName != monitor.DeviceName)
                        break;

                    _overlayForm?.BeginInvoke(() => _overlayForm?.EnsureTopMost());
                }

                int targetIntervalMs = 1000 / Math.Clamp(curSettings.MaxFps, 5, 60);
                swFrame.Restart();

                if (virtualDisplayMode && wgcCompositor != null)
                {
                    int winCount = WindowTracker.FillVisibleWindowsOnMonitor(
                        monitor.Bounds,
                        overlayHwnd,
                        winBuffer,
                        includePopupsAsHoles: true,
                        settings: curSettings);
                    bool force = _settingsDirty || _snapshotRequested;
                    _settingsDirty = false;

                    bool changed = wgcCompositor.UpdateAndComposite(monitor.Bounds, winBuffer.AsSpan(0, winCount), force);
                    if (changed)
                    {
                        pipeline.ProcessFrame(
                            wgcCompositor.CompositeSrv,
                            backBufferRtv,
                            monW,
                            monH,
                            1,
                            winBuffer.AsSpan(0, winCount),
                            curSettings,
                            tracker,
                            srcIsLinear: false,
                            useHardwareHolePunch: true,
                            uncoveredDesktopPassthrough: true);

                        int hrPresent = SwapChainPresent(swapChain, 0, DXGI_PRESENT_DO_NOT_WAIT);
                        if (hrPresent == DXGI_ERROR_WAS_STILL_DRAWING)
                        {
                            // Virtual display / WUDFHost busy: never block DWM, yield gracefully!
                            _settingsDirty = true;
                            Thread.Sleep(45);
                            continue;
                        }
                        if (hrPresent == DXGI_ERROR_DEVICE_REMOVED || hrPresent == DXGI_ERROR_DEVICE_RESET)
                        {
                            LogDiag($"SwapChainPresent device reset/removed (0x{hrPresent:X8}); restarting session cleanly...");
                            Thread.Sleep(500);
                            break;
                        }

                        if (_snapshotRequested)
                        {
                            _snapshotRequested = false;
                            Flush(context);
                            Thread.Sleep(35); // Allow DWM to composite the newly presented frame onto DISPLAY42
                            ExportFullDiagnostics(
                                pipeline,
                                wgcCompositor.CompositeSrv,
                                monitor.Bounds,
                                1,
                                winBuffer.AsSpan(0, winCount),
                                curSettings,
                                tracker,
                                srcIsLinear: false,
                                uncoveredDesktopPassthrough: true,
                                captureRealDesktopBitBlt: true);
                        }
                    }

                    int elapsed = (int)swFrame.ElapsedMilliseconds;
                    if (elapsed < targetIntervalMs)
                    {
                        _wakeEvent.WaitOne(targetIntervalMs - elapsed);
                        // Enforce a tiny 6ms minimum frame floor when WinEvent bursts arrive
                        int afterWake = (int)swFrame.ElapsedMilliseconds;
                        if (afterWake < 6)
                            Thread.Sleep(6 - afterWake);
                    }
                }
                else
                {
                    bool shouldRender = false;
                    int hrFrame = OutDuplAcquireNextFrame(duplication, (uint)targetIntervalMs, out var frameInfo, out IntPtr desktopRes);
                    if (hrFrame == DXGI_ERROR_WAIT_TIMEOUT)
                    {
                        if ((_settingsDirty || _snapshotRequested) && hasValidCachedFrame)
                        {
                            _settingsDirty = false;
                            shouldRender = true;
                        }
                    }
                    else if (hrFrame >= 0)
                    {
                        try
                        {
                            if (frameInfo.LastPresentTime != 0 || !hasValidCachedFrame || _settingsDirty || _snapshotRequested)
                            {
                                if (QueryInterface(desktopRes, IID_ID3D11Texture2D, out IntPtr frameTex) >= 0)
                                {
                                    Texture2DGetDesc(frameTex, out var fdesc);
                                    EnsureCachedTexture((int)fdesc.Width, (int)fdesc.Height, fdesc.Format);
                                    srcTexFormat = fdesc.Format;

                                    if ((int)fdesc.Width != monW && (int)fdesc.Width == monH)
                                    {
                                        OutDuplGetDesc(duplication, out var dd);
                                        rotationShaderCode = (uint)dd.Rotation;
                                    }
                                    else
                                    {
                                        rotationShaderCode = 1;
                                    }

                                    CopyResource(context, cachedTex, frameTex);
                                    SafeRelease(ref frameTex);
                                    hasValidCachedFrame = true;
                                    _settingsDirty = false;
                                    shouldRender = true;
                                }
                            }
                        }
                        finally
                        {
                            SafeRelease(ref desktopRes);
                            OutDuplReleaseFrame(duplication);
                        }
                    }
                    else if (hrFrame == DXGI_ERROR_ACCESS_LOST || hrFrame == E_ACCESSDENIED)
                    {
                        Thread.Sleep(200);
                        break;
                    }

                    if (shouldRender && hasValidCachedFrame)
                    {
                        bool srcIsLinear = (srcTexFormat == DXGI_FORMAT_R16G16B16A16_FLOAT);
                        int winCount = WindowTracker.FillVisibleWindowsOnMonitor(monitor.Bounds, overlayHwnd, winBuffer, settings: curSettings);

                        if (_snapshotRequested)
                        {
                            _snapshotRequested = false;
                            ExportFullDiagnostics(
                                pipeline,
                                cachedSrv,
                                monitor.Bounds,
                                rotationShaderCode,
                                winBuffer.AsSpan(0, winCount),
                                curSettings,
                                tracker,
                                srcIsLinear,
                                uncoveredDesktopPassthrough: false,
                                captureRealDesktopBitBlt: false);
                        }

                        pipeline.ProcessFrame(
                            cachedSrv,
                            backBufferRtv,
                            monW,
                            monH,
                            rotationShaderCode,
                            winBuffer.AsSpan(0, winCount),
                            curSettings,
                            tracker,
                            srcIsLinear,
                            useHardwareHolePunch: true,
                            uncoveredDesktopPassthrough: false);

                        int hrPresent = SwapChainPresent(swapChain, 0, DXGI_PRESENT_DO_NOT_WAIT);
                        if (hrPresent == DXGI_ERROR_WAS_STILL_DRAWING)
                        {
                            // Virtual display / WUDFHost busy: never block DWM, yield gracefully!
                            _settingsDirty = true;
                            Thread.Sleep(45);
                            continue;
                        }
                        if (hrPresent == DXGI_ERROR_DEVICE_REMOVED || hrPresent == DXGI_ERROR_DEVICE_RESET)
                        {
                            LogDiag($"SwapChainPresent device reset/removed (0x{hrPresent:X8}); restarting session cleanly...");
                            Thread.Sleep(500);
                            break;
                        }

                        int elapsed = (int)swFrame.ElapsedMilliseconds;
                        if (elapsed < targetIntervalMs)
                            Thread.Sleep(targetIntervalMs - elapsed);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogDiag($"Session error: {ex}");
            ReportStatus($"渲染恢复中: {ex.Message}");
            Thread.Sleep(500);
        }
        finally
        {
            if (dcompTarget != IntPtr.Zero && dcompDevice != IntPtr.Zero)
            {
                DCompTargetSetRoot(dcompTarget, IntPtr.Zero);
                DCompCommit(dcompDevice);
            }
            wgcCompositor?.Dispose();
            pipeline?.Dispose();
            SafeRelease(ref cachedSrv);
            SafeRelease(ref cachedTex);
            SafeRelease(ref duplication);
            SafeRelease(ref dcompVisual);
            SafeRelease(ref dcompTarget);
            SafeRelease(ref dcompDevice);
            SafeRelease(ref backBufferRtv);
            SafeRelease(ref swapChain);
            SafeRelease(ref dxgiDevice);
            SafeRelease(ref context);
            SafeRelease(ref device);
            SafeRelease(ref output1);
            SafeRelease(ref output);
            SafeRelease(ref adapter);
            SafeRelease(ref factory2);
            SafeRelease(ref factory1);
            TrimProcessMemory();
        }
    }

    private void ExportFullDiagnostics(
        GpuSmartInvertPipeline pipeline,
        IntPtr srcSrv,
        Rectangle monBounds,
        uint rotation,
        ReadOnlySpan<TrackedWindow> windows,
        AppSettings settings,
        GeometricRegionTracker tracker,
        bool srcIsLinear,
        bool uncoveredDesktopPassthrough,
        bool captureRealDesktopBitBlt)
    {
        try
        {
            string snapDir = Path.Combine(AppContext.BaseDirectory, "snapshots");
            pipeline.ExportDiagnosticSnapshot(
                srcSrv,
                monBounds.Width,
                monBounds.Height,
                rotation,
                windows,
                settings,
                tracker,
                srcIsLinear,
                uncoveredDesktopPassthrough,
                snapDir);

            if (captureRealDesktopBitBlt)
            {
                SaveRealMonitorBitBltPng(monBounds, Path.Combine(snapDir, "snap_4_real_virtual_screen.png"));
            }

            LogDiag($"Snapshot exported to {snapDir} (windows={windows.Length}, realBitBlt={captureRealDesktopBitBlt})");
            ReportStatus($"已导出画面快照至 snapshots/ ({DateTime.Now:HH:mm:ss})");
        }
        catch (Exception ex)
        {
            LogDiag($"Snapshot export failed: {ex.Message}");
        }
    }

    private static void CheckHr(int hr, string op)
    {
        if (hr < 0)
            throw new InvalidOperationException($"{op} failed with HRESULT 0x{hr:X8}");
    }

    public void Dispose()
    {
        if (_winEventHook != IntPtr.Zero)
        {
            UnhookWinEvent(_winEventHook);
            _winEventHook = IntPtr.Zero;
        }
        _cts?.Cancel();
        _wakeEvent.Set();
        _renderThread?.Join(1500);
        _cts?.Dispose();
        _wakeEvent.Dispose();
        if (_overlayForm != null && !_overlayForm.IsDisposed)
        {
            _overlayForm.Close();
            _overlayForm.Dispose();
        }
    }
}
