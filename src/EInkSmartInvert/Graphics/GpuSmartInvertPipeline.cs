using System.Drawing.Imaging;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using EInkSmartInvert.Config;
using EInkSmartInvert.Core;
using static EInkSmartInvert.Graphics.D3D11Interop;

namespace EInkSmartInvert.Graphics;

public sealed unsafe class GpuSmartInvertPipeline : IDisposable
{
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct StatsParamsCB
    {
        public Vector2 SrcSize;
        public Vector2 GridSize;
        public uint Rotation;
        public uint SrcIsLinear;
        public Vector2 Pad;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct SmartInvertParamsCB
    {
        public Vector2 ScreenSize;
        public uint Rotation;
        public uint Mode;

        public float PaperWhiteKnee;
        public float InkBlackKnee;
        public float SaturationBoost;
        public float ContrastGamma;

        public uint KeepLightWindows;
        public uint ProtectPhotos;
        public uint PreserveHue;
        public uint EnhanceLightRegions;

        public uint WindowCount;
        public uint ProtectedCount;
        public uint DebugView;
        public uint SrcIsLinear;

        public uint UseHardwareHolePunch;
        public uint UncoveredDesktopPassthrough;
        public Vector2 Pad0;

        public fixed float WindowRects[16 * 4];
        public fixed float WindowFlags[16 * 4];
        public fixed float ProtectedRects[8 * 4];
        public fixed float ProtectedFlags[8 * 4];
    }

    private readonly IntPtr _device;
    private readonly IntPtr _context;

    private IntPtr _vsFullscreen;
    private IntPtr _psDownsample;
    private IntPtr _psSmartInvert;
    private IntPtr _samplerPoint;

    private IntPtr _cbStats;
    private IntPtr _cbSmartInvert;

    // Tiny 80x45 = 14.4 KB stats surface & staging readback
    private IntPtr _statsTex;
    private IntPtr _statsRtv;
    private IntPtr _statsStaging;

    private readonly ClassifiedWindow[] _classifiedWins = new ClassifiedWindow[16];
    private readonly ProtectedRect[] _protectedRects = new ProtectedRect[8];
    public int LastWindowCount { get; private set; }
    public int LastProtectedCount { get; private set; }
    public ReadOnlySpan<ClassifiedWindow> LastWindows => _classifiedWins.AsSpan(0, LastWindowCount);
    public ReadOnlySpan<ProtectedRect> LastProtected => _protectedRects.AsSpan(0, LastProtectedCount);

    public GpuSmartInvertPipeline(IntPtr device, IntPtr context)
    {
        _device = device;
        _context = context;
        InitializeResources();
    }

    private void InitializeResources()
    {
        string downsampleSrc = LoadEmbeddedShader("EInkSmartInvert.Graphics.Shaders.DownsampleStats.hlsl");
        string smartInvertSrc = LoadEmbeddedShader("EInkSmartInvert.Graphics.Shaders.SmartInvert.hlsl");

        CompileAndCreateVs(downsampleSrc, "VS_Fullscreen", out _vsFullscreen);
        CompileAndCreatePs(downsampleSrc, "PS_DownsampleStats", out _psDownsample);
        CompileAndCreatePs(smartInvertSrc, "PS_SmartInvert", out _psSmartInvert);

        var pointDesc = new D3D11_SAMPLER_DESC
        {
            Filter = D3D11_FILTER_MIN_MAG_MIP_POINT,
            AddressU = D3D11_TEXTURE_ADDRESS_CLAMP,
            AddressV = D3D11_TEXTURE_ADDRESS_CLAMP,
            AddressW = D3D11_TEXTURE_ADDRESS_CLAMP,
            MaxAnisotropy = 1,
            MaxLOD = float.MaxValue
        };
        CheckHr(CreateSamplerState(_device, pointDesc, out _samplerPoint), "CreateSamplerState(Point)");

        CreateConstBuffer((uint)Marshal.SizeOf<StatsParamsCB>(), out _cbStats);
        CreateConstBuffer((uint)Marshal.SizeOf<SmartInvertParamsCB>(), out _cbSmartInvert);

        var statsDesc = new D3D11_TEXTURE2D_DESC
        {
            Width = GeometricRegionTracker.GridW,
            Height = GeometricRegionTracker.GridH,
            MipLevels = 1,
            ArraySize = 1,
            Format = DXGI_FORMAT_R8G8B8A8_UNORM,
            SampleDesc = new DXGI_SAMPLE_DESC { Count = 1, Quality = 0 },
            Usage = D3D11_USAGE_DEFAULT,
            BindFlags = D3D11_BIND_RENDER_TARGET
        };
        CheckHr(CreateTexture2D(_device, statsDesc, IntPtr.Zero, out _statsTex), "CreateTexture2D(Stats80x45)");
        CheckHr(CreateRenderTargetView(_device, _statsTex, IntPtr.Zero, out _statsRtv), "CreateRTV(Stats80x45)");

        var stageDesc = statsDesc;
        stageDesc.Usage = D3D11_USAGE_STAGING;
        stageDesc.BindFlags = 0;
        stageDesc.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
        CheckHr(CreateTexture2D(_device, stageDesc, IntPtr.Zero, out _statsStaging), "CreateTexture2D(StatsStaging14KB)");
    }

    private void CreateConstBuffer(uint byteWidth, out IntPtr buf)
    {
        var desc = new D3D11_BUFFER_DESC
        {
            ByteWidth = (byteWidth + 15u) & ~15u,
            Usage = D3D11_USAGE_DEFAULT,
            BindFlags = D3D11_BIND_CONSTANT_BUFFER
        };
        CheckHr(CreateBuffer(_device, desc, IntPtr.Zero, out buf), "CreateBuffer(CB)");
    }

    public void ProcessFrame(
        IntPtr srcSrv,
        IntPtr targetRtv,
        int screenWidth,
        int screenHeight,
        uint rotation,
        ReadOnlySpan<TrackedWindow> windows,
        AppSettings settings,
        GeometricRegionTracker tracker,
        bool srcIsLinear = false,
        bool useHardwareHolePunch = true,
        bool uncoveredDesktopPassthrough = false)
    {
        IASetPrimitiveTopology(_context, D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        VSSetShader(_context, _vsFullscreen);

        IntPtr smp = _samplerPoint;
        PSSetSamplers(_context, 0, 1, &smp);

        IntPtr nullPtr = IntPtr.Zero;

        bool needsSmartAnalysis = settings.Mode == FilterMode.SmartAuto;
        if (!needsSmartAnalysis)
        {
            for (int i = 0; i < windows.Length; i++)
            {
                if (!windows[i].IsPassthroughHole && windows[i].EffectiveMode == FilterMode.SmartAuto)
                {
                    needsSmartAnalysis = true;
                    break;
                }
            }
        }

        if (needsSmartAnalysis)
        {
            var statsCb = new StatsParamsCB
            {
                SrcSize = new Vector2(screenWidth, screenHeight),
                GridSize = new Vector2(GeometricRegionTracker.GridW, GeometricRegionTracker.GridH),
                Rotation = rotation,
                SrcIsLinear = srcIsLinear ? 1u : 0u
            };
            UpdateSubresource(_context, _cbStats, 0, IntPtr.Zero, (IntPtr)(&statsCb), 0, 0);

            IntPtr rtv1 = _statsRtv;
            IntPtr cb1 = _cbStats;
            IntPtr srv1 = srcSrv;

            OMSetRenderTargets(_context, 1, &rtv1, IntPtr.Zero);
            RSSetViewports(_context, 1, new D3D11_VIEWPORT { Width = GeometricRegionTracker.GridW, Height = GeometricRegionTracker.GridH, MaxDepth = 1f });
            PSSetShader(_context, _psDownsample);
            PSSetConstantBuffers(_context, 0, 1, &cb1);
            PSSetShaderResources(_context, 0, 1, &srv1);
            Draw(_context, 3, 0);

            OMSetRenderTargets(_context, 1, &nullPtr, IntPtr.Zero);
            PSSetShaderResources(_context, 0, 1, &nullPtr);

            CopyResource(_context, _statsStaging, _statsTex);
            if (Map(_context, _statsStaging, 0, D3D11_MAP_READ, 0, out var mapped) >= 0)
            {
                try
                {
                    tracker.Analyze(
                        mapped.pData,
                        (int)mapped.RowPitch,
                        screenWidth,
                        screenHeight,
                        windows,
                        settings,
                        _classifiedWins,
                        out int wCnt,
                        _protectedRects,
                        out int pCnt);
                    LastWindowCount = wCnt;
                    LastProtectedCount = pCnt;
                }
                finally
                {
                    Unmap(_context, _statsStaging, 0);
                }
            }
        }
        else if (windows.Length > 0)
        {
            int wCnt = Math.Min(windows.Length, 16);
            for (int i = 0; i < wCnt; i++)
            {
                var tw = windows[i];
                bool isDark = !tw.IsPassthroughHole && tw.EffectiveMode != FilterMode.CrispLightOnly;
                _classifiedWins[i] = new ClassifiedWindow(tw.Hwnd, tw.LocalBounds, isDark, tw.IsPassthroughHole, tw.EffectiveMode, tw.ProcessName);
            }
            LastWindowCount = wCnt;
            LastProtectedCount = 0;
        }
        else
        {
            LastWindowCount = 0;
            LastProtectedCount = 0;
        }

        var mainCb = new SmartInvertParamsCB
        {
            ScreenSize = new Vector2(screenWidth, screenHeight),
            Rotation = rotation,
            Mode = (uint)settings.Mode,
            PaperWhiteKnee = settings.PaperWhiteKnee,
            InkBlackKnee = settings.InkBlackKnee,
            SaturationBoost = settings.SaturationBoost,
            ContrastGamma = settings.ContrastGamma,
            KeepLightWindows = settings.KeepLightWindows ? 1u : 0u,
            ProtectPhotos = settings.ProtectPhotos ? 1u : 0u,
            PreserveHue = settings.PreserveHue ? 1u : 0u,
            EnhanceLightRegions = settings.EnhanceLightRegions ? 1u : 0u,
            WindowCount = (uint)LastWindowCount,
            ProtectedCount = (uint)LastProtectedCount,
            DebugView = settings.DebugMaskOverlay ? 1u : 0u,
            SrcIsLinear = srcIsLinear ? 1u : 0u,
            UseHardwareHolePunch = useHardwareHolePunch ? 1u : 0u,
            UncoveredDesktopPassthrough = uncoveredDesktopPassthrough ? 1u : 0u
        };

        for (int i = 0; i < LastWindowCount; i++)
        {
            var b = _classifiedWins[i].LocalBounds;
            mainCb.WindowRects[i * 4 + 0] = b.Left;
            mainCb.WindowRects[i * 4 + 1] = b.Top;
            mainCb.WindowRects[i * 4 + 2] = b.Right;
            mainCb.WindowRects[i * 4 + 3] = b.Bottom;
            mainCb.WindowFlags[i * 4 + 0] = _classifiedWins[i].IsDark ? 1.0f : 0.0f;
            mainCb.WindowFlags[i * 4 + 1] = _classifiedWins[i].IsPassthroughHole ? 1.0f : 0.0f;
            mainCb.WindowFlags[i * 4 + 2] = (float)(uint)_classifiedWins[i].EffectiveMode;
        }

        for (int k = 0; k < LastProtectedCount; k++)
        {
            var pr = _protectedRects[k].LocalBounds;
            mainCb.ProtectedRects[k * 4 + 0] = pr.Left;
            mainCb.ProtectedRects[k * 4 + 1] = pr.Top;
            mainCb.ProtectedRects[k * 4 + 2] = pr.Right;
            mainCb.ProtectedRects[k * 4 + 3] = pr.Bottom;
            mainCb.ProtectedFlags[k * 4 + 0] = _protectedRects[k].IsMedia ? 1.0f : 0.0f;
        }

        UpdateSubresource(_context, _cbSmartInvert, 0, IntPtr.Zero, (IntPtr)(&mainCb), 0, 0);

        IntPtr outRtv = targetRtv;
        IntPtr cbMain = _cbSmartInvert;
        IntPtr srvMain = srcSrv;

        OMSetRenderTargets(_context, 1, &outRtv, IntPtr.Zero);
        RSSetViewports(_context, 1, new D3D11_VIEWPORT { Width = screenWidth, Height = screenHeight, MaxDepth = 1f });
        PSSetShader(_context, _psSmartInvert);
        PSSetConstantBuffers(_context, 0, 1, &cbMain);
        PSSetShaderResources(_context, 0, 1, &srvMain);

        Draw(_context, 3, 0);

        PSSetShaderResources(_context, 0, 1, &nullPtr);
    }

    public string ExportDiagnosticSnapshot(
        IntPtr srcSrv,
        int screenWidth,
        int screenHeight,
        uint rotation,
        ReadOnlySpan<TrackedWindow> windows,
        AppSettings settings,
        GeometricRegionTracker tracker,
        bool srcIsLinear,
        bool uncoveredDesktopPassthrough,
        string snapshotDir)
    {
        Directory.CreateDirectory(snapshotDir);
        IntPtr snapTex = IntPtr.Zero;
        IntPtr snapRtv = IntPtr.Zero;
        IntPtr snapStage = IntPtr.Zero;

        try
        {
            var desc = new D3D11_TEXTURE2D_DESC
            {
                Width = (uint)screenWidth,
                Height = (uint)screenHeight,
                MipLevels = 1,
                ArraySize = 1,
                Format = DXGI_FORMAT_B8G8R8A8_UNORM,
                SampleDesc = new DXGI_SAMPLE_DESC { Count = 1, Quality = 0 },
                Usage = D3D11_USAGE_DEFAULT,
                BindFlags = D3D11_BIND_RENDER_TARGET
            };
            CheckHr(CreateTexture2D(_device, desc, IntPtr.Zero, out snapTex), "SnapTex");
            CheckHr(CreateRenderTargetView(_device, snapTex, IntPtr.Zero, out snapRtv), "SnapRTV");

            var stDesc = desc;
            stDesc.Usage = D3D11_USAGE_STAGING;
            stDesc.BindFlags = 0;
            stDesc.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
            CheckHr(CreateTexture2D(_device, stDesc, IntPtr.Zero, out snapStage), "SnapStage");

            // 1. Render Raw Input (Mode = Off, UseHardwareHolePunch = false)
            var rawSettings = settings.Clone();
            rawSettings.Mode = FilterMode.Off;
            rawSettings.DebugMaskOverlay = false;
            ProcessFrame(srcSrv, snapRtv, screenWidth, screenHeight, rotation, windows, rawSettings, tracker, srcIsLinear, useHardwareHolePunch: false, uncoveredDesktopPassthrough);
            Flush(_context);
            using (var rawBmp = ReadbackToBitmap(snapTex, snapStage, screenWidth, screenHeight))
            {
                rawBmp.Save(Path.Combine(snapshotDir, "snap_1_raw_input.png"), ImageFormat.Png);
            }

            // 2. Render Exact Simulated Screen Output (useHardwareHolePunch = false composites transparent holes over src!)
            var visSettings = settings.Clone();
            visSettings.DebugMaskOverlay = false;
            ProcessFrame(srcSrv, snapRtv, screenWidth, screenHeight, rotation, windows, visSettings, tracker, srcIsLinear, useHardwareHolePunch: false, uncoveredDesktopPassthrough);
            Flush(_context);
            using (var outBmp = ReadbackToBitmap(snapTex, snapStage, screenWidth, screenHeight))
            {
                outBmp.Save(Path.Combine(snapshotDir, "snap_2_what_you_see.png"), ImageFormat.Png);
            }

            // 3. Render Debug Region Overlay (shows cyan=inverted, green=light-window, orange=video/photo box)
            var dbgSettings = settings.Clone();
            dbgSettings.DebugMaskOverlay = true;
            ProcessFrame(srcSrv, snapRtv, screenWidth, screenHeight, rotation, windows, dbgSettings, tracker, srcIsLinear, useHardwareHolePunch: false, uncoveredDesktopPassthrough);
            Flush(_context);
            using (var dbgBmp = ReadbackToBitmap(snapTex, snapStage, screenWidth, screenHeight))
            {
                dbgBmp.Save(Path.Combine(snapshotDir, "snap_3_debug_regions.png"), ImageFormat.Png);
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Screen: {screenWidth}x{screenHeight}, Mode={settings.Mode}, OnlyListedApps={settings.OnlyInvertListedApps}, SrcLinear={srcIsLinear}");
            sb.AppendLine($"Detected Windows ({LastWindowCount}):");
            for (int i = 0; i < LastWindowCount; i++)
                sb.AppendLine($"  [{i}] HWND=0x{_classifiedWins[i].Hwnd:X} Proc='{_classifiedWins[i].ProcessName}' Mode={_classifiedWins[i].EffectiveMode} Bounds={_classifiedWins[i].LocalBounds} IsDark={_classifiedWins[i].IsDark} PassthroughHole={_classifiedWins[i].IsPassthroughHole}");
            sb.AppendLine($"Protected Clean Rectangles ({LastProtectedCount}):");
            for (int k = 0; k < LastProtectedCount; k++)
                sb.AppendLine($"  [{k}] Bounds={_protectedRects[k].LocalBounds} IsMedia={_protectedRects[k].IsMedia}");

            File.WriteAllText(Path.Combine(snapshotDir, "snap_report.txt"), sb.ToString());
            return snapshotDir;
        }
        finally
        {
            SafeRelease(ref snapStage);
            SafeRelease(ref snapRtv);
            SafeRelease(ref snapTex);
        }
    }

    private Bitmap ReadbackToBitmap(IntPtr gpuTex, IntPtr stagingTex, int w, int h)
    {
        CopyResource(_context, stagingTex, gpuTex);
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        if (Map(_context, stagingTex, 0, D3D11_MAP_READ, 0, out var mapped) >= 0)
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
                Unmap(_context, stagingTex, 0);
            }
        }
        return bmp;
    }

    private void CompileAndCreateVs(string src, string entry, out IntPtr vs)
    {
        IntPtr blob = CompileShader(src, entry, "vs_5_0", entry);
        try { CheckHr(CreateVertexShader(_device, BlobGetBufferPointer(blob), BlobGetBufferSize(blob), out vs), $"CreateVS({entry})"); }
        finally { SafeRelease(ref blob); }
    }

    private void CompileAndCreatePs(string src, string entry, out IntPtr ps)
    {
        IntPtr blob = CompileShader(src, entry, "ps_5_0", entry);
        try { CheckHr(CreatePixelShader(_device, BlobGetBufferPointer(blob), BlobGetBufferSize(blob), out ps), $"CreatePS({entry})"); }
        finally { SafeRelease(ref blob); }
    }

    private static string LoadEmbeddedShader(string resourceName)
    {
        var asm = Assembly.GetExecutingAssembly();
        using Stream? stream = asm.GetManifestResourceStream(resourceName);
        if (stream == null)
            throw new InvalidOperationException($"Embedded shader '{resourceName}' not found.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static IntPtr CompileShader(string source, string entryPoint, string profile, string sourceName)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(source);
        fixed (byte* pSrc = utf8)
        {
            int hr = D3DCompile(
                (IntPtr)pSrc,
                (nuint)utf8.Length,
                sourceName,
                IntPtr.Zero,
                IntPtr.Zero,
                entryPoint,
                profile,
                1u << 15,
                0,
                out IntPtr codeBlob,
                out IntPtr errBlob);

            if (hr < 0)
            {
                string errMsg = $"HRESULT 0x{hr:X8}";
                if (errBlob != IntPtr.Zero)
                {
                    errMsg = Marshal.PtrToStringAnsi(BlobGetBufferPointer(errBlob), (int)BlobGetBufferSize(errBlob)) ?? errMsg;
                    SafeRelease(ref errBlob);
                }
                throw new InvalidOperationException($"HLSL compile failed ({sourceName}:{entryPoint}): {errMsg}");
            }

            SafeRelease(ref errBlob);
            return codeBlob;
        }
    }

    private static void CheckHr(int hr, string op)
    {
        if (hr < 0)
            throw new InvalidOperationException($"{op} failed with HRESULT 0x{hr:X8}");
    }

    public void Dispose()
    {
        SafeRelease(ref _statsStaging);
        SafeRelease(ref _statsRtv);
        SafeRelease(ref _statsTex);
        SafeRelease(ref _cbSmartInvert);
        SafeRelease(ref _cbStats);
        SafeRelease(ref _samplerPoint);
        SafeRelease(ref _psSmartInvert);
        SafeRelease(ref _psDownsample);
        SafeRelease(ref _vsFullscreen);
    }
}
