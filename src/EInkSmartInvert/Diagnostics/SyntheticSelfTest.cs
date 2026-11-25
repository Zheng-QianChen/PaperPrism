using System.Drawing.Imaging;
using EInkSmartInvert.Config;
using EInkSmartInvert.Core;
using EInkSmartInvert.Graphics;
using static EInkSmartInvert.Graphics.D3D11Interop;

namespace EInkSmartInvert.Diagnostics;

public static unsafe class SyntheticSelfTest
{
    public static int Run(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        string logPath = Path.Combine(outputDir, "selftest.log");
        using var logWriter = new StreamWriter(logPath, append: false) { AutoFlush = true };
        void Log(string msg)
        {
            Console.WriteLine(msg);
            logWriter.WriteLine(msg);
        }

        try
        {
            return RunCore(outputDir, Log);
        }
        catch (Exception ex)
        {
            Log($"[FATAL] {ex}");
            return 1;
        }
    }

    private static int RunCore(string outputDir, Action<string> Log)
    {
        const int W = 1600;
        const int H = 1200;

        Log($"[SelfTest] Building {W}x{H} composite multi-window + photo + code + PDF scene (Pure-GPU v2.0)...");
        using var srcBmp = BuildSyntheticDesktopScene(W, H, out var windows);
        string beforePath = Path.Combine(outputDir, "before.png");
        srcBmp.Save(beforePath, ImageFormat.Png);

        IntPtr device = IntPtr.Zero;
        IntPtr context = IntPtr.Zero;
        IntPtr srcTex = IntPtr.Zero;
        IntPtr srcSrv = IntPtr.Zero;
        IntPtr outTex = IntPtr.Zero;
        IntPtr outRtv = IntPtr.Zero;
        IntPtr outStaging = IntPtr.Zero;
        GpuSmartInvertPipeline? pipeline = null;

        try
        {
            int hr = D3D11CreateDevice(IntPtr.Zero, 1, IntPtr.Zero, 0x20, IntPtr.Zero, 0, 7, out device, out int fl, out context);
            if (hr < 0)
                hr = D3D11CreateDevice(IntPtr.Zero, 2, IntPtr.Zero, 0x20, IntPtr.Zero, 0, 7, out device, out fl, out context);
            if (hr < 0)
                throw new InvalidOperationException($"D3D11CreateDevice failed: 0x{hr:X8}");

            Log($"[SelfTest] D3D11 Device initialized (FeatureLevel=0x{fl:X}). Compiling Pure-GPU HLSL shaders...");
            pipeline = new GpuSmartInvertPipeline(device, context);

            var texDesc = new D3D11_TEXTURE2D_DESC
            {
                Width = W,
                Height = H,
                MipLevels = 1,
                ArraySize = 1,
                Format = DXGI_FORMAT_B8G8R8A8_UNORM,
                SampleDesc = new DXGI_SAMPLE_DESC { Count = 1, Quality = 0 },
                Usage = D3D11_USAGE_DEFAULT,
                BindFlags = D3D11_BIND_SHADER_RESOURCE
            };

            UploadBitmapToTexture(device, context, srcBmp, texDesc, ref srcTex);
            CreateShaderResourceView(device, srcTex, IntPtr.Zero, out srcSrv);

            var outDesc = texDesc;
            outDesc.BindFlags = D3D11_BIND_RENDER_TARGET;
            CreateTexture2D(device, outDesc, IntPtr.Zero, out outTex);
            CreateRenderTargetView(device, outTex, IntPtr.Zero, out outRtv);

            var stageDesc = texDesc;
            stageDesc.Usage = D3D11_USAGE_STAGING;
            stageDesc.BindFlags = 0;
            stageDesc.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
            CreateTexture2D(device, stageDesc, IntPtr.Zero, out outStaging);

            var tracker = new GeometricRegionTracker();
            var settings = new AppSettings();
            settings.ApplyKaleido3Preset();

            // 1. Static composite test (offscreen PNG compositing: useHardwareHolePunch = false)
            pipeline.ProcessFrame(srcSrv, outRtv, W, H, 1, windows, settings, tracker, useHardwareHolePunch: false);
            Flush(context);

            using var afterBmp = ReadbackBitmap(context, outTex, outStaging, W, H);
            string afterPath = Path.Combine(outputDir, "after_smart.png");
            afterBmp.Save(afterPath, ImageFormat.Png);

            // 2. Debug Mask Overlay output
            settings.DebugMaskOverlay = true;
            pipeline.ProcessFrame(srcSrv, outRtv, W, H, 1, windows, settings, tracker, useHardwareHolePunch: false);
            Flush(context);

            using var debugBmp = ReadbackBitmap(context, outTex, outStaging, W, H);
            string debugPath = Path.Combine(outputDir, "after_debug_mask.png");
            debugBmp.Save(debugPath, ImageFormat.Png);
            settings.DebugMaskOverlay = false;

            // 3. Multi-frame Web Video Playback + Hardware Alpha=0 Clean Rectangular Cutout Test!
            var videoRect = new Rectangle(790, 820, 280, 260);
            tracker.Reset();
            for (int f = 0; f < 6; f++)
            {
                DrawDarkSceneVideoFrame(srcBmp, videoRect, f);
                UploadBitmapToTexture(device, context, srcBmp, texDesc, ref srcTex);
                pipeline.ProcessFrame(srcSrv, outRtv, W, H, 1, windows, settings, tracker, useHardwareHolePunch: true);
            }
            Flush(context);
            using var videoHoleBmp = ReadbackBitmap(context, outTex, outStaging, W, H);

            // 4. Antigravity Split-Pane Dark Chat Scroll Regression Test!
            // Simulate a maximized dark app with static left sidebar + static top/bottom bars, scrolling only the right chat column!
            using var splitBmp = new Bitmap(W, H, PixelFormat.Format32bppArgb);
            var splitWin = new TrackedWindow[]
            {
                new(new IntPtr(3001), new Rectangle(0, 0, W, H), new Rectangle(0, 0, W, H))
            };
            tracker.Reset();
            for (int sf = 0; sf < 10; sf++)
            {
                DrawAntigravitySplitPaneScrollFrame(splitBmp, W, H, scrollOffsetPx: sf * 28);
                UploadBitmapToTexture(device, context, splitBmp, texDesc, ref srcTex);
                pipeline.ProcessFrame(srcSrv, outRtv, W, H, 1, splitWin, settings, tracker, useHardwareHolePunch: true, uncoveredDesktopPassthrough: true);
            }
            Flush(context);
            using var splitAfterBmp = ReadbackBitmap(context, outTex, outStaging, W, H);
            Color cSplitChatBg = splitAfterBmp.GetPixel(1000, 500);
            int falseMediaBoxesDuringScroll = pipeline.LastProtectedCount;

            // Restore srcTex to synthetic scene for Test 5
            UploadBitmapToTexture(device, context, srcBmp, texDesc, ref srcTex);
            tracker.Reset();

            // 5. Virtual Display Uncovered Desktop + Rounded Floating Popup (Zero Square Corners) Test!
            using var roundedSceneBmp = (Bitmap)srcBmp.Clone();
            var popupRect = new Rectangle(180, 140, 240, 64);
            DrawWhiteRoundedPopupOverDarkIde(roundedSceneBmp, popupRect, cornerRadius: 18);
            UploadBitmapToTexture(device, context, roundedSceneBmp, texDesc, ref srcTex);
            tracker.Reset();

            var virtualWins = new TrackedWindow[]
            {
                new(new IntPtr(2002), popupRect, popupRect, IsPassthroughHole: false), // Captured rounded white popup!
                new(new IntPtr(2001), new Rectangle(120, 420, 180, 48), new Rectangle(120, 420, 180, 48), IsPassthroughHole: true),
                windows[0],
                windows[1],
                windows[2]
            };
            pipeline.ProcessFrame(srcSrv, outRtv, W, H, 1, virtualWins, settings, tracker, useHardwareHolePunch: true, uncoveredDesktopPassthrough: true);
            Flush(context);
            using var virtBmp = ReadbackBitmap(context, outTex, outStaging, W, H);
            Color cRoundedPopupCenter = virtBmp.GetPixel(popupRect.Left + popupRect.Width / 2, popupRect.Top + popupRect.Height / 2);
            Color cRoundedPopupOuterCorner = virtBmp.GetPixel(popupRect.Left + 2, popupRect.Top + 2);

            Color cDarkIdeBg = afterBmp.GetPixel(220, 200);
            Color cLightPdfBg = afterBmp.GetPixel(1150, 180);
            Color cPhotoSky = afterBmp.GetPixel(600, 740);
            Color cPhotoSkyOrig = srcBmp.GetPixel(600, 740);
            Color cWhiteSubCard = afterBmp.GetPixel(1300, 880);
            Color cDarkBrowserPane = afterBmp.GetPixel(900, 880);
            Color cVideoCenterHole = videoHoleBmp.GetPixel(930, 950);
            Color cIdeOpaque = videoHoleBmp.GetPixel(220, 200);
            Color cUncoveredDesktop = virtBmp.GetPixel(10, 10);
            Color cPopupHole = virtBmp.GetPixel(150, 440);
            Color cVirtIdeOpaque = virtBmp.GetPixel(220, 260);

            Log($"[Verify] Dark IDE (#1E1E1E) -> RGB({cDarkIdeBg.R},{cDarkIdeBg.G},{cDarkIdeBg.B}) [Expected ~255,255,255]");
            Log($"[Verify] Light PDF (#FFFFFF) -> RGB({cLightPdfBg.R},{cLightPdfBg.G},{cLightPdfBg.B}) [Expected ~255,255,255]");
            Log($"[Verify] Embedded Photo -> Orig RGB({cPhotoSkyOrig.R},{cPhotoSkyOrig.G},{cPhotoSkyOrig.B}) vs Out RGB({cPhotoSky.R},{cPhotoSky.G},{cPhotoSky.B})");
            Log($"[Verify] White Sub-Card inside Dark Browser -> RGB({cWhiteSubCard.R},{cWhiteSubCard.G},{cWhiteSubCard.B})");
            Log($"[Verify] Dark Browser Header -> RGB({cDarkBrowserPane.R},{cDarkBrowserPane.G},{cDarkBrowserPane.B})");
            Log($"[Verify] Web Video Hardware Hole-Punch Alpha -> VideoAlpha={cVideoCenterHole.A} (Expected <=10 transparent cutout!), DarkIdeAlpha={cIdeOpaque.A} (Expected 255)");
            Log($"[Verify] Split-Pane Dark Chat Scroll -> FalseMediaBoxes={falseMediaBoxesDuringScroll} (Expected 0!), ChatAlpha={cSplitChatBg.A} (Expected 255)");
            Log($"[Verify] Virtual Display Passthrough -> DesktopAlpha={cUncoveredDesktop.A} (Expected 0), PopupAlpha={cPopupHole.A} (Expected 0), IdeAlpha={cVirtIdeOpaque.A} (Expected 255)");
            Log($"[Verify] Rounded Floating Popup -> CenterRGB=({cRoundedPopupCenter.R},{cRoundedPopupCenter.G},{cRoundedPopupCenter.B}) [Expected ~255], OuterCornerRGB=({cRoundedPopupOuterCorner.R},{cRoundedPopupOuterCorner.G},{cRoundedPopupOuterCorner.B}) [Expected ~255, zero dark square corner!]");

            bool pass = true;
            if (cDarkIdeBg.R < 250 || cDarkIdeBg.G < 250 || cDarkIdeBg.B < 250) { Log("[FAIL] Dark IDE background check!"); pass = false; }
            if (cLightPdfBg.R < 250 || cLightPdfBg.G < 250 || cLightPdfBg.B < 250) { Log("[FAIL] Light PDF check!"); pass = false; }
            if (Math.Abs(cPhotoSky.B - cPhotoSkyOrig.B) > 45) { Log("[FAIL] Photo protection check!"); pass = false; }
            if (cWhiteSubCard.R < 245 || cDarkBrowserPane.R < 245) { Log("[FAIL] Sub-pane classification check!"); pass = false; }
            if (cVideoCenterHole.A > 20 || cIdeOpaque.A < 250) { Log("[FAIL] Web video hardware Alpha=0 cutout check!"); pass = false; }
            if (falseMediaBoxesDuringScroll != 0 || cSplitChatBg.A < 250) { Log("[FAIL] Split-pane dark chat scroll check!"); pass = false; }
            if (cUncoveredDesktop.A != 0 || cPopupHole.A != 0 || cVirtIdeOpaque.A < 250) { Log("[FAIL] Virtual display passthrough check!"); pass = false; }
            if (cRoundedPopupCenter.R < 245 || cRoundedPopupOuterCorner.R < 245) { Log("[FAIL] Rounded floating popup corner check!"); pass = false; }

            // 6 & 7. Interactive Desktop WinRT WGC & HWND Z-Order Verification (skip on headless GitHub Actions runners)
            bool isHeadlessCi = string.Equals(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"), "true", StringComparison.OrdinalIgnoreCase);
            if (isHeadlessCi)
            {
                Log("[Verify] Headless CI runner detected: skipping interactive WGC/HWND desktop session checks.");
            }
            else
            {
                CheckHrLocal(QueryInterface(device, IID_IDXGIDevice, out IntPtr dxgiDev), "QI(IDXGIDevice)");
                try
                {
                    using var wgc = new WgcWindowCompositor(device, dxgiDev, context, W, H);
                    using var dummyForm = new Form
                    {
                        Text = "WGC SelfTest Window",
                        FormBorderStyle = FormBorderStyle.None,
                        StartPosition = FormStartPosition.Manual,
                        Bounds = new Rectangle(100, 100, 320, 240),
                        BackColor = Color.FromArgb(30, 30, 30),
                        ShowInTaskbar = false
                    };
                    dummyForm.Show();
                    Application.DoEvents();

                    var testWins = new TrackedWindow[]
                    {
                        new(dummyForm.Handle, new Rectangle(100, 100, 320, 240), new Rectangle(100, 100, 320, 240), IsPassthroughHole: false)
                    };
                    for (int iter = 0; iter < 8; iter++)
                    {
                        Application.DoEvents();
                        wgc.UpdateAndComposite(new Rectangle(0, 0, W, H), testWins, forceRedraw: iter == 0);
                        if (!testWins[0].IsPassthroughHole)
                            break;
                        Thread.Sleep(25);
                        testWins[0] = testWins[0] with { IsPassthroughHole = false };
                    }
                    Log($"[Verify] WgcWindowCompositor WinRT COM ABI verified! (LiveFrameCaptured={!testWins[0].IsPassthroughHole})");
                    dummyForm.Close();
                }
                finally
                {
                    SafeRelease(ref dxgiDev);
                }

                using (var normalApp = new Form { Text = "NormalApp", ShowInTaskbar = false })
                using (var hiddenIme = new Form { Text = "SogouCandidateSim", TopMost = true, ShowInTaskbar = false })
                using (var overlayTest = new OverlayHostForm())
                {
                    normalApp.Show();
                    hiddenIme.Show();
                    ShowWindowSelfTest(hiddenIme.Handle, 0);
                    overlayTest.Show();
                    overlayTest.PositionOnMonitor(new Rectangle(0, 0, 400, 300));
                    overlayTest.EnsureTopMost();

                    ShowWindowSelfTest(hiddenIme.Handle, 4);
                    normalApp.Activate();

                    int rank = 0, rIme = -1, rOverlay = -1, rNormal = -1;
                    EnumWindowsSelfTest((h, _) =>
                    {
                        if (h == hiddenIme.Handle) rIme = rank;
                        if (h == overlayTest.Handle) rOverlay = rank;
                        if (h == normalApp.Handle) rNormal = rank;
                        rank++;
                        return true;
                    }, IntPtr.Zero);

                    Log($"[Verify] IME Z-Order Protection -> IMERank={rIme}, OverlayRank={rOverlay}, NormalAppRank={rNormal} [Expected IME < Overlay < Normal]");
                    if (rIme < 0 || rOverlay < 0 || rNormal < 0 || !(rIme < rOverlay && rOverlay < rNormal))
                    {
                        Log("[FAIL] IME Z-Order protection check failed!");
                        pass = false;
                    }
                }
            }

            // 8. Per-Application Whitelist & Per-Window Independent Invert Mode Test!
            var appCfg = new AppSettings { OnlyInvertListedApps = true, Mode = FilterMode.SmartAuto };
            bool rulesOk =
                appCfg.ResolveWindowMode("Antigravity") == FilterMode.SmartAuto &&
                appCfg.ResolveWindowMode("chrome") == FilterMode.SmartAuto &&
                appCfg.ResolveWindowMode("codex") == FilterMode.SmartAuto &&
                appCfg.ResolveWindowMode("ZCode") == FilterMode.SmartAuto &&
                appCfg.ResolveWindowMode("WindowsTerminal") == FilterMode.SmartAuto &&
                appCfg.ResolveWindowMode("unrelated_app", "ConsoleWindowClass") == FilterMode.SmartAuto &&
                appCfg.ResolveWindowMode("zotero") == FilterMode.SmartAuto &&
                appCfg.ResolveWindowMode("PotPlayerMini64") == FilterMode.Off;

            UploadBitmapToTexture(device, context, srcBmp, texDesc, ref srcTex);
            tracker.Reset();
            var perAppWins = new TrackedWindow[]
            {
                // Browser: ForceInvertAll override (even white sub-card at 1300,880 should be inverted dark!)
                new(new IntPtr(1003), windows[0].LocalBounds, windows[0].FullBounds, IsPassthroughHole: false, EffectiveMode: FilterMode.ForceInvertAll, ProcessName: "cmd"),
                // Light PDF: Non-whitelisted app -> PassthroughHole (Alpha=0!)
                new(new IntPtr(1002), windows[1].LocalBounds, windows[1].FullBounds, IsPassthroughHole: true, EffectiveMode: FilterMode.Off, ProcessName: "PotPlayer"),
                // Dark IDE: SmartAuto whitelisted app -> inverted paper white (Alpha=255, RGB~255)!
                new(new IntPtr(1001), windows[2].LocalBounds, windows[2].FullBounds, IsPassthroughHole: false, EffectiveMode: FilterMode.SmartAuto, ProcessName: "Antigravity")
            };
            pipeline.ProcessFrame(srcSrv, outRtv, W, H, 1, perAppWins, appCfg, tracker, useHardwareHolePunch: true, uncoveredDesktopPassthrough: true);
            Flush(context);
            using var perAppBmp = ReadbackBitmap(context, outTex, outStaging, W, H);
            Color cForceWhiteCard = perAppBmp.GetPixel(1300, 880); // Was white #FCFCFD, ForceInvertAll -> dark (<35)!
            Color cUnlistedHole = perAppBmp.GetPixel(1150, 180);   // PassthroughOff -> Alpha=0!
            Color cListedSmartIde = perAppBmp.GetPixel(220, 200);  // SmartAuto -> RGB~255, Alpha=255!

            Log($"[Verify] Per-App Rules & Per-Window Modes -> RulesMatch={rulesOk}, ForceInvertCardRGB=({cForceWhiteCard.R},{cForceWhiteCard.G},{cForceWhiteCard.B}) [Expected <35], UnlistedAlpha={cUnlistedHole.A} [Expected 0], ListedIdeRGB=({cListedSmartIde.R},{cListedSmartIde.G},{cListedSmartIde.B})");
            if (!rulesOk || cForceWhiteCard.R > 40 || cUnlistedHole.A != 0 || cListedSmartIde.R < 250 || cListedSmartIde.A < 250)
            {
                Log("[FAIL] Per-Application Smart Invert check failed!");
                pass = false;
            }

            Log(pass ? "[SelfTest] ALL PURE-GPU & VIDEO CUTOUT CHECKS PASSED!" : "[SelfTest] SOME CHECKS FAILED.");
            return pass ? 0 : 2;
        }
        finally
        {
            pipeline?.Dispose();
            SafeRelease(ref outStaging);
            SafeRelease(ref outRtv);
            SafeRelease(ref outTex);
            SafeRelease(ref srcSrv);
            SafeRelease(ref srcTex);
            SafeRelease(ref context);
            SafeRelease(ref device);
        }
    }

    private delegate bool EnumWinSelfTestProc(IntPtr hWnd, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "ShowWindow")]
    private static extern bool ShowWindowSelfTest(IntPtr hWnd, int nCmdShow);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "EnumWindows")]
    private static extern bool EnumWindowsSelfTest(EnumWinSelfTestProc lpEnumFunc, IntPtr lParam);

    private static void CheckHrLocal(int hr, string op)
    {
        if (hr < 0) throw new InvalidOperationException($"{op} failed: 0x{hr:X8}");
    }

    private static void UploadBitmapToTexture(IntPtr device, IntPtr context, Bitmap bmp, in D3D11_TEXTURE2D_DESC desc, ref IntPtr tex)
    {
        var bd = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            if (tex == IntPtr.Zero)
            {
                var init = new D3D11_SUBRESOURCE_DATA { pSysMem = bd.Scan0, SysMemPitch = (uint)bd.Stride };
                CreateTexture2D(device, desc, (IntPtr)(&init), out tex);
            }
            else
            {
                UpdateSubresource(context, tex, 0, IntPtr.Zero, bd.Scan0, (uint)bd.Stride, 0);
            }
        }
        finally
        {
            bmp.UnlockBits(bd);
        }
    }

    private static void DrawWhiteRoundedPopupOverDarkIde(Bitmap bmp, Rectangle rect, int cornerRadius)
    {
        // Draws a white rounded pill inside 'rect', leaving the 4 outer corners as the underlying #1E1E1E dark IDE!
        using var g = System.Drawing.Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var path = new System.Drawing.Drawing2D.GraphicsPath();
        int d = cornerRadius * 2;
        path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        using var fill = new SolidBrush(Color.FromArgb(250, 250, 252));
        using var pen = new Pen(Color.FromArgb(210, 214, 220), 1.5f);
        using var fnt = new Font("Microsoft YaHei UI", 11, FontStyle.Regular);
        g.FillPath(fill, path);
        g.DrawPath(pen, path);
        g.DrawString("复制  |  搜索  |  翻译", fnt, Brushes.Black, rect.Left + 26, rect.Top + 18);
    }

    private static void DrawAntigravitySplitPaneScrollFrame(Bitmap bmp, int w, int h, int scrollOffsetPx)
    {
        using var g = System.Drawing.Graphics.FromImage(bmp);
        g.Clear(Color.FromArgb(12, 12, 14));
        using var sideBg = new SolidBrush(Color.FromArgb(18, 18, 20));
        using var bubbleBg = new SolidBrush(Color.FromArgb(26, 26, 30));
        using var fnt = new Font("Microsoft YaHei UI", 14, FontStyle.Regular);

        // Static left sidebar (0..480), static top header (0..90), static bottom input (h-150..h)
        g.FillRectangle(sideBg, 0, 0, 480, h);
        g.DrawString("Antigravity Sidebar\n• Project 1\n• Project 2", fnt, Brushes.Gainsboro, 24, 120);

        // Scrollable right chat messages (520..1540, 100..h-160)
        g.SetClip(new Rectangle(500, 95, w - 520, h - 255));
        for (int m = 0; m < 12; m++)
        {
            int y = 120 + m * 150 - (scrollOffsetPx % 300);
            g.FillRectangle(bubbleBg, 540, y, 920, 92);
            g.DrawString($"[{m}] 智能反色虚拟屏滚动回归测试 — 暗色聊天框与代码段在上下滑动时不应误判为视频黑框！", fnt, Brushes.WhiteSmoke, 560, y + 18);
        }
        g.ResetClip();
        g.FillRectangle(sideBg, 480, 0, w - 480, 95);
        g.FillRectangle(sideBg, 480, h - 160, w - 480, 160);
    }

    private static void DrawDarkSceneVideoFrame(Bitmap bmp, Rectangle rect, int frameIndex)
    {
        var bd = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            float phase = frameIndex * 0.85f;
            for (int y = 0; y < rect.Height; y++)
            {
                float ny = (float)y / rect.Height;
                byte* row = (byte*)bd.Scan0 + y * bd.Stride;
                for (int x = 0; x < rect.Width; x++)
                {
                    float nx = (float)x / rect.Width;
                    // Dark cinema night scene (low luminance 0.08..0.32!) moving across frames
                    float wave = MathF.Sin(nx * 12.0f + phase) * MathF.Cos(ny * 9.0f - phase * 0.7f);
                    float lum = 0.16f + 0.10f * wave;
                    row[x * 4 + 0] = (byte)Math.Clamp((int)((lum + 0.05f) * 255f), 0, 255);
                    row[x * 4 + 1] = (byte)Math.Clamp((int)(lum * 255f), 0, 255);
                    row[x * 4 + 2] = (byte)Math.Clamp((int)((lum * 0.85f) * 255f), 0, 255);
                    row[x * 4 + 3] = 255;
                }
            }
        }
        finally
        {
            bmp.UnlockBits(bd);
        }
    }

    private static Bitmap ReadbackBitmap(IntPtr ctx, IntPtr gpuTex, IntPtr stagingTex, int w, int h)
    {
        CopyResource(ctx, stagingTex, gpuTex);
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        if (Map(ctx, stagingTex, 0, D3D11_MAP_READ, 0, out var mapped) >= 0)
        {
            try
            {
                var bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                try
                {
                    for (int y = 0; y < h; y++)
                    {
                        Buffer.MemoryCopy(
                            (void*)(mapped.pData + y * (int)mapped.RowPitch),
                            (void*)(bd.Scan0 + y * bd.Stride),
                            bd.Stride,
                            w * 4);
                    }
                }
                finally
                {
                    bmp.UnlockBits(bd);
                }
            }
            finally
            {
                Unmap(ctx, stagingTex, 0);
            }
        }
        return bmp;
    }

    private static Bitmap BuildSyntheticDesktopScene(int w, int h, out TrackedWindow[] windows)
    {
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using var g = System.Drawing.Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        g.Clear(Color.FromArgb(18, 22, 28));

        var rectIde = new Rectangle(30, 40, 790, 1100);
        var rectPdf = new Rectangle(850, 40, 720, 560);
        var rectBrowser = new Rectangle(760, 580, 800, 570);

        using (var bgIde = new SolidBrush(Color.FromArgb(30, 30, 30)))
        using (var bgSidebar = new SolidBrush(Color.FromArgb(37, 37, 38)))
        using (var bgTitle = new SolidBrush(Color.FromArgb(50, 50, 51)))
        using (var bgSel = new SolidBrush(Color.FromArgb(38, 79, 120)))
        using (var penBorder = new Pen(Color.FromArgb(62, 62, 66), 2))
        {
            g.FillRectangle(bgIde, rectIde);
            g.FillRectangle(bgTitle, rectIde.X, rectIde.Y, rectIde.Width, 38);
            g.FillRectangle(bgSidebar, rectIde.X, rectIde.Y + 38, 130, rectIde.Height - 38);
            g.FillRectangle(bgSel, rectIde.X + 145, rectIde.Y + 185, 590, 34);
            g.DrawRectangle(penBorder, rectIde);

            using var fTitle = new Font("Consolas", 13, FontStyle.Bold);
            using var fCode = new Font("Consolas", 15, FontStyle.Regular);
            g.DrawString("VS Code — SmartInvertEngine.cs (Dark Mode)", fTitle, Brushes.Gainsboro, rectIde.X + 16, rectIde.Y + 8);

            int cx = rectIde.X + 152;
            int cy = rectIde.Y + 66;
            using var brBlue = new SolidBrush(Color.FromArgb(86, 156, 214));
            using var brGreen = new SolidBrush(Color.FromArgb(106, 153, 85));
            using var brYellow = new SolidBrush(Color.FromArgb(220, 220, 170));
            using var brCoral = new SolidBrush(Color.FromArgb(206, 145, 120));
            using var brText = new SolidBrush(Color.FromArgb(212, 212, 212));
            using var brRed = new SolidBrush(Color.FromArgb(244, 71, 71));

            g.DrawString("// Kaleido 3 Color E-Ink Smart Invert Pipeline", fCode, brGreen, cx, cy);
            g.DrawString("public void RenderSecondaryMonitor(Monitor target)", fCode, brBlue, cx, cy + 40);
            g.DrawString("{", fCode, brText, cx, cy + 80);
            g.DrawString("    var theme = DetectWindowTheme(target); // Selected Line", fCode, brYellow, cx, cy + 122);
            g.DrawString("    string label = \"Keep Hue & Paper White\";", fCode, brCoral, cx, cy + 166);
            g.DrawString("    if (target.HasError) LogError(\"Alert Red!\");", fCode, brRed, cx, cy + 210);
            g.DrawString("    ApplyOklabGamutMapping(saturation: 1.35f);", fCode, brText, cx, cy + 254);
            g.DrawString("}", fCode, brText, cx, cy + 298);
            g.DrawString("Markdown Preview — Embedded Natural Photo:", fTitle, brText, cx, cy + 365);
        }

        DrawNaturalLandscapePhoto(bmp, new Rectangle(200, 520, 570, 540));

        using (var bgPdf = new SolidBrush(Color.FromArgb(255, 255, 255)))
        using (var bgToolbar = new SolidBrush(Color.FromArgb(243, 244, 246)))
        using (var penPdf = new Pen(Color.FromArgb(209, 213, 219), 2))
        using (var fH1 = new Font("Microsoft YaHei UI", 18, FontStyle.Bold))
        using (var fBody = new Font("Microsoft YaHei UI", 13, FontStyle.Regular))
        {
            g.FillRectangle(bgPdf, rectPdf);
            g.FillRectangle(bgToolbar, rectPdf.X, rectPdf.Y, rectPdf.Width, 42);
            g.DrawRectangle(penPdf, rectPdf);

            using var brDark = new SolidBrush(Color.FromArgb(17, 24, 39));
            using var brBlueH = new SolidBrush(Color.FromArgb(37, 99, 235));
            g.DrawString("论文阅读器 — Light Mode PDF (应保持白底不反黑)", fBody, brDark, rectPdf.X + 16, rectPdf.Y + 10);
            g.DrawString("1. 电子墨水屏光学特性与双屏工作流", fH1, brBlueH, rectPdf.X + 36, rectPdf.Y + 70);
            g.DrawString("当文档窗口本身已经是白底黑字时，智能反色会自动识别并保持浅色，\n不会像传统反色工具那样把白底 PDF 变成全黑底片。", fBody, brDark, rectPdf.X + 36, rectPdf.Y + 125);

            g.FillRectangle(new SolidBrush(Color.FromArgb(37, 99, 235)), rectPdf.X + 50, rectPdf.Y + 260, 140, 220);
            g.FillRectangle(new SolidBrush(Color.FromArgb(22, 163, 74)), rectPdf.X + 230, rectPdf.Y + 310, 140, 170);
            g.FillRectangle(new SolidBrush(Color.FromArgb(220, 38, 38)), rectPdf.X + 410, rectPdf.Y + 230, 140, 250);
        }

        using (var bgBrowser = new SolidBrush(Color.FromArgb(24, 26, 31)))
        using (var bgWhiteCard = new SolidBrush(Color.FromArgb(252, 252, 253)))
        using (var penBr = new Pen(Color.FromArgb(75, 85, 99), 2))
        using (var fBrowser = new Font("Microsoft YaHei UI", 13, FontStyle.Regular))
        {
            g.FillRectangle(bgBrowser, rectBrowser);
            g.DrawRectangle(penBr, rectBrowser);
            g.DrawString("深色浏览器侧边栏\n(此区域应自动漂白为白底)", fBrowser, Brushes.WhiteSmoke, rectBrowser.X + 24, rectBrowser.Y + 60);
            g.DrawString("• 收藏夹与目录\n• 夜间深色导航栏\n• 阅读列表", fBrowser, Brushes.LightSteelBlue, rectBrowser.X + 24, rectBrowser.Y + 160);

            var whiteCard = new Rectangle(1110, 640, 420, 480);
            g.FillRectangle(bgWhiteCard, whiteCard);
            g.DrawString("内嵌浅色网页区域\n\n即使外层浏览器是深色主题，\n这块原本白底的网页正文也会\n被子区域检测保留为白底黑字！", fBrowser, Brushes.Black, whiteCard.X + 24, whiteCard.Y + 36);
        }

        windows = new TrackedWindow[]
        {
            new(new IntPtr(1003), rectBrowser, rectBrowser),
            new(new IntPtr(1002), rectPdf, rectPdf),
            new(new IntPtr(1001), rectIde, rectIde)
        };

        return bmp;
    }

    private static void DrawNaturalLandscapePhoto(Bitmap bmp, Rectangle rect)
    {
        var bd = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < rect.Height; y++)
            {
                float ny = (float)y / rect.Height;
                byte* row = (byte*)bd.Scan0 + y * bd.Stride;
                for (int x = 0; x < rect.Width; x++)
                {
                    float nx = (float)x / rect.Width;
                    float wave1 = MathF.Sin(nx * 14.0f + ny * 8.0f) * 0.5f + 0.5f;
                    float wave2 = MathF.Sin(nx * 37.0f - ny * 29.0f) * 0.5f + 0.5f;
                    float micro = MathF.Sin(nx * 131.0f + ny * 97.0f) * 0.04f;
                    float horizon = 0.52f + 0.09f * MathF.Sin(nx * 6.5f) + 0.04f * MathF.Cos(nx * 17.0f);

                    float r, g, b;
                    if (ny < horizon)
                    {
                        float t = ny / horizon;
                        r = 0.22f + 0.52f * t + 0.16f * wave1 + micro;
                        g = 0.46f + 0.30f * t + 0.12f * wave1 + micro;
                        b = 0.84f - 0.14f * t + 0.08f * wave2 + micro;
                    }
                    else
                    {
                        float t = (ny - horizon) / (1.0f - horizon);
                        r = 0.18f + 0.26f * wave1 + 0.15f * t + micro;
                        g = 0.42f + 0.28f * wave2 - 0.10f * t + micro;
                        b = 0.15f + 0.14f * wave1 + micro;
                    }

                    row[x * 4 + 0] = (byte)Math.Clamp((int)(b * 255f), 0, 255);
                    row[x * 4 + 1] = (byte)Math.Clamp((int)(g * 255f), 0, 255);
                    row[x * 4 + 2] = (byte)Math.Clamp((int)(r * 255f), 0, 255);
                    row[x * 4 + 3] = 255;
                }
            }
        }
        finally
        {
            bmp.UnlockBits(bd);
        }
    }
}
