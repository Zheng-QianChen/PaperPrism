using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using EInkSmartInvert.Core;
using static EInkSmartInvert.Graphics.D3D11Interop;

namespace EInkSmartInvert.Graphics;

/// <summary>
/// Zero-copy GPU VRAM per-window compositor powered by Windows.Graphics.Capture (CreateForWindow).
/// Because it captures individual HWNDs directly from DWM rather than the composited monitor output,
/// our overlay window does NOT need WDA_EXCLUDEFROMCAPTURE — making it 100% visible to virtual display
/// streamers (Sunshine, Apollo, Moonlight, SuperDisplay, Spacedesk) with ZERO infinite-mirror feedback!
/// </summary>
public sealed unsafe class WgcWindowCompositor : IDisposable
{
    private static readonly Guid IID_IGraphicsCaptureItemInterop       = new("3628e81b-3cac-4c60-b7f4-23ce0e0c3356");
    private static readonly Guid IID_IGraphicsCaptureItem              = new("79c3f95b-31f7-4ec2-a464-632ef5d30760");
    private static readonly Guid IID_IDirect3D11CaptureFramePoolStatics2 = new("589b103f-6bbc-5df5-a991-02e28b3b66d5");
    private static readonly Guid IID_IGraphicsCaptureSession2          = new("2c39ae40-7d2e-5044-804e-8b6799d4cf9e");
    private static readonly Guid IID_IGraphicsCaptureSession3          = new("f2cdd966-22ae-5ea1-9596-3a289344c3be");
    private static readonly Guid IID_IDirect3DDxgiInterfaceAccess      = new("a9b3d012-3df2-4ee3-b8d1-8695f457d3c1");
    private static readonly Guid IID_IClosable                         = new("30d5a829-7fa4-4026-83bb-d75bae4ea99e");

    [StructLayout(LayoutKind.Sequential)]
    private struct SizeInt32
    {
        public int Width;
        public int Height;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct BlitParamsCB
    {
        public Vector2 UvScale;
        public Vector2 Pad;
    }

    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int RoInitialize(int initType);

    [DllImport("combase.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int WindowsCreateString(string sourceString, uint length, out IntPtr hstring);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int WindowsDeleteString(IntPtr hstring);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int RoGetActivationFactory(IntPtr activatableClassId, in Guid iid, out IntPtr factory);

    private static void* VSlot(IntPtr obj, int index) => (*(void***)obj)[index];

    private static void CloseWinRtObject(IntPtr obj)
    {
        if (obj == IntPtr.Zero) return;
        if (QueryInterface(obj, IID_IClosable, out IntPtr closable) >= 0 && closable != IntPtr.Zero)
        {
            ((delegate* unmanaged[Stdcall]<IntPtr, int>)VSlot(closable, 6))(closable);
            SafeRelease(ref closable);
        }
    }

    private sealed class WindowCaptureEntry : IDisposable
    {
        public IntPtr Hwnd;
        public IntPtr Item;
        public IntPtr Pool;
        public IntPtr Session;
        public SizeInt32 PoolSize;

        public IntPtr CachedTex;
        public IntPtr CachedSrv;
        public uint TexWidth;
        public uint TexHeight;
        public int TexFormat;
        public Vector2 ValidUvScale = Vector2.One;
        public bool HasFrame;
        public Rectangle LastFullBounds;
        public int FailCooldownFrames;

        public void Dispose()
        {
            SafeRelease(ref CachedSrv);
            SafeRelease(ref CachedTex);
            CloseWinRtObject(Session);
            CloseWinRtObject(Pool);
            SafeRelease(ref Session);
            SafeRelease(ref Pool);
            SafeRelease(ref Item);
        }
    }

    private const string BlitHlsl = @"
Texture2D<float4> WinTex : register(t0);
SamplerState LinearClamp : register(s0);

cbuffer BlitParams : register(b0)
{
    float2 UvScale;
    float2 _pad;
};

struct VSOut
{
    float4 Pos : SV_POSITION;
    float2 UV  : TEXCOORD0;
};

VSOut VS_Blit(uint vid : SV_VertexID)
{
    VSOut o;
    o.UV = float2((vid << 1) & 2, vid & 2);
    o.Pos = float4(o.UV * float2(2.0f, -2.0f) + float2(-1.0f, 1.0f), 0.0f, 1.0f);
    return o;
}

float4 PS_Blit(VSOut pin) : SV_TARGET
{
    float2 uv = saturate(pin.UV * UvScale);
    float4 c = WinTex.SampleLevel(LinearClamp, uv, 0);
    float maxRgb = max(c.r, max(c.g, c.b));
    float effA = (maxRgb > c.a + 0.02f) ? 1.0f : c.a;
    return float4(c.rgb, effA);
}
";

    private readonly IntPtr _device;
    private readonly IntPtr _context;
    private IntPtr _winrtD3dDevice;
    private IntPtr _itemInterop;
    private IntPtr _poolStatics2;

    private IntPtr _vsBlit;
    private IntPtr _psBlit;
    private IntPtr _samplerLinear;
    private IntPtr _blendPremult;
    private IntPtr _cbBlit;

    private IntPtr _compositeTex;
    private IntPtr _compositeRtv;
    private IntPtr _compositeSrv;
    private readonly int _monWidth;
    private readonly int _monHeight;

    private readonly Dictionary<IntPtr, WindowCaptureEntry> _entries = new(16);
    private int _prevWinOrderHash;

    public IntPtr CompositeSrv => _compositeSrv;

    public WgcWindowCompositor(IntPtr device, IntPtr dxgiDevice, IntPtr context, int monWidth, int monHeight)
    {
        _device = device;
        _context = context;
        _monWidth = monWidth;
        _monHeight = monHeight;

        RoInitialize(1); // RO_INIT_MULTITHREADED (ignores RPC_E_CHANGED_MODE if already initialized)
        CheckHr(CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice, out _winrtD3dDevice), "CreateDirect3D11DeviceFromDXGIDevice");
        _itemInterop = GetActivationFactory("Windows.Graphics.Capture.GraphicsCaptureItem", IID_IGraphicsCaptureItemInterop);
        _poolStatics2 = GetActivationFactory("Windows.Graphics.Capture.Direct3D11CaptureFramePool", IID_IDirect3D11CaptureFramePoolStatics2);

        InitializeGpuResources();
    }

    private static IntPtr GetActivationFactory(string className, in Guid iid)
    {
        CheckHr(WindowsCreateString(className, (uint)className.Length, out IntPtr hs), $"HSTRING({className})");
        try
        {
            CheckHr(RoGetActivationFactory(hs, iid, out IntPtr fac), $"RoGetActivationFactory({className})");
            return fac;
        }
        finally
        {
            WindowsDeleteString(hs);
        }
    }

    private void InitializeGpuResources()
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(BlitHlsl);
        fixed (byte* pSrc = utf8)
        {
            CheckHr(D3DCompile((IntPtr)pSrc, (nuint)utf8.Length, "Blit.hlsl", IntPtr.Zero, IntPtr.Zero, "VS_Blit", "vs_5_0", 1u << 15, 0, out IntPtr vsBlob, out IntPtr vsErr), "Compile(VS_Blit)");
            SafeRelease(ref vsErr);
            CheckHr(CreateVertexShader(_device, BlobGetBufferPointer(vsBlob), BlobGetBufferSize(vsBlob), out _vsBlit), "CreateVS(Blit)");
            SafeRelease(ref vsBlob);

            CheckHr(D3DCompile((IntPtr)pSrc, (nuint)utf8.Length, "Blit.hlsl", IntPtr.Zero, IntPtr.Zero, "PS_Blit", "ps_5_0", 1u << 15, 0, out IntPtr psBlob, out IntPtr psErr), "Compile(PS_Blit)");
            SafeRelease(ref psErr);
            CheckHr(CreatePixelShader(_device, BlobGetBufferPointer(psBlob), BlobGetBufferSize(psBlob), out _psBlit), "CreatePS(Blit)");
            SafeRelease(ref psBlob);
        }

        var smpDesc = new D3D11_SAMPLER_DESC
        {
            Filter = D3D11_FILTER_MIN_MAG_MIP_LINEAR,
            AddressU = D3D11_TEXTURE_ADDRESS_CLAMP,
            AddressV = D3D11_TEXTURE_ADDRESS_CLAMP,
            AddressW = D3D11_TEXTURE_ADDRESS_CLAMP,
            MaxAnisotropy = 1,
            MaxLOD = float.MaxValue
        };
        CheckHr(CreateSamplerState(_device, smpDesc, out _samplerLinear), "CreateSampler(Blit)");

        var blendDesc = new D3D11_BLEND_DESC
        {
            AlphaToCoverageEnable = 0,
            IndependentBlendEnable = 0,
            RT0 = new D3D11_RENDER_TARGET_BLEND_DESC
            {
                BlendEnable = 1,
                SrcBlend = D3D11_BLEND_ONE,
                DestBlend = D3D11_BLEND_INV_SRC_ALPHA,
                BlendOp = D3D11_BLEND_OP_ADD,
                SrcBlendAlpha = D3D11_BLEND_ONE,
                DestBlendAlpha = D3D11_BLEND_INV_SRC_ALPHA,
                BlendOpAlpha = D3D11_BLEND_OP_ADD,
                RenderTargetWriteMask = D3D11_COLOR_WRITE_ENABLE_ALL
            }
        };
        CheckHr(CreateBlendState(_device, blendDesc, out _blendPremult), "CreateBlendState(Premult)");

        var cbDesc = new D3D11_BUFFER_DESC
        {
            ByteWidth = 16,
            Usage = D3D11_USAGE_DEFAULT,
            BindFlags = D3D11_BIND_CONSTANT_BUFFER
        };
        CheckHr(CreateBuffer(_device, cbDesc, IntPtr.Zero, out _cbBlit), "CreateCB(Blit)");

        var compDesc = new D3D11_TEXTURE2D_DESC
        {
            Width = (uint)_monWidth,
            Height = (uint)_monHeight,
            MipLevels = 1,
            ArraySize = 1,
            Format = DXGI_FORMAT_B8G8R8A8_UNORM,
            SampleDesc = new DXGI_SAMPLE_DESC { Count = 1, Quality = 0 },
            Usage = D3D11_USAGE_DEFAULT,
            BindFlags = D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE
        };
        CheckHr(CreateTexture2D(_device, compDesc, IntPtr.Zero, out _compositeTex), "CreateTexture2D(WgcComposite)");
        CheckHr(CreateRenderTargetView(_device, _compositeTex, IntPtr.Zero, out _compositeRtv), "CreateRTV(WgcComposite)");
        CheckHr(CreateShaderResourceView(_device, _compositeTex, IntPtr.Zero, out _compositeSrv), "CreateSRV(WgcComposite)");
    }

    /// <summary>
    /// Updates per-window WGC frames and composites them onto CompositeSrv if any window changed or moved.
    /// Any window without a captured frame yet is marked IsPassthroughHole = true for clean Alpha=0 passthrough.
    /// Returns true if anything changed and the overlay should re-render.
    /// </summary>
    public bool UpdateAndComposite(Rectangle monitorBounds, Span<TrackedWindow> windows, bool forceRedraw)
    {
        bool anyChange = forceRedraw;
        int orderHash = windows.Length;

        for (int i = 0; i < windows.Length; i++)
        {
            var tw = windows[i];
            orderHash = HashCode.Combine(orderHash, tw.Hwnd, tw.FullBounds, tw.IsPassthroughHole, tw.EffectiveMode);

            if (tw.IsPassthroughHole)
                continue;

            if (!_entries.TryGetValue(tw.Hwnd, out var entry))
            {
                entry = new WindowCaptureEntry { Hwnd = tw.Hwnd };
                _entries[tw.Hwnd] = entry;
                anyChange = true;
            }

            if (entry.LastFullBounds != tw.FullBounds)
            {
                entry.LastFullBounds = tw.FullBounds;
                anyChange = true;
            }

            if (entry.Pool == IntPtr.Zero)
            {
                if (entry.FailCooldownFrames > 0)
                {
                    entry.FailCooldownFrames--;
                }
                else if (!TryStartWindowCapture(entry, tw.Hwnd))
                {
                    entry.FailCooldownFrames = 48;
                }
            }

            if (entry.Pool != IntPtr.Zero && PollLatestWindowFrame(entry))
                anyChange = true;

            if (!entry.HasFrame)
                windows[i] = tw with { IsPassthroughHole = true };
        }

        if (orderHash != _prevWinOrderHash)
        {
            _prevWinOrderHash = orderHash;
            anyChange = true;
        }

        // Prune closed or moved-away windows immediately to release VRAM
        if (_entries.Count > 0)
        {
            Span<IntPtr> stale = stackalloc IntPtr[Math.Min(_entries.Count, 32)];
            int staleCount = 0;
            foreach (var kv in _entries)
            {
                bool active = false;
                for (int i = 0; i < windows.Length; i++)
                {
                    if (windows[i].Hwnd == kv.Key)
                    {
                        active = true;
                        break;
                    }
                }
                if (!active && staleCount < stale.Length)
                    stale[staleCount++] = kv.Key;
            }
            for (int s = 0; s < staleCount; s++)
            {
                if (_entries.Remove(stale[s], out var removed))
                {
                    removed.Dispose();
                    anyChange = true;
                }
            }
        }

        if (!anyChange)
            return false;

        // Render all captured windows from back-to-front onto _compositeRtv with Premultiplied Alpha!
        // Clearing to RGBA(0,0,0,0) preserves exact Alpha=0 outside rounded window corners!
        float* clearColor = stackalloc float[4] { 0.0f, 0.0f, 0.0f, 0.0f };
        ClearRenderTargetView(_context, _compositeRtv, clearColor);

        IntPtr rtv = _compositeRtv;
        OMSetRenderTargets(_context, 1, &rtv, IntPtr.Zero);
        OMSetBlendState(_context, _blendPremult, null, 0xFFFFFFFFu);
        IASetPrimitiveTopology(_context, D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        VSSetShader(_context, _vsBlit);
        PSSetShader(_context, _psBlit);

        IntPtr smp = _samplerLinear;
        IntPtr cb = _cbBlit;
        PSSetSamplers(_context, 0, 1, &smp);
        PSSetConstantBuffers(_context, 0, 1, &cb);

        for (int i = windows.Length - 1; i >= 0; i--)
        {
            var tw = windows[i];
            if (tw.IsPassthroughHole || !_entries.TryGetValue(tw.Hwnd, out var entry) || !entry.HasFrame || entry.CachedSrv == IntPtr.Zero)
                continue;

            var blitCb = new BlitParamsCB { UvScale = entry.ValidUvScale };
            UpdateSubresource(_context, _cbBlit, 0, IntPtr.Zero, (IntPtr)(&blitCb), 0, 0);

            var vp = new D3D11_VIEWPORT
            {
                TopLeftX = tw.FullBounds.X - monitorBounds.X,
                TopLeftY = tw.FullBounds.Y - monitorBounds.Y,
                Width = Math.Max(1, tw.FullBounds.Width),
                Height = Math.Max(1, tw.FullBounds.Height),
                MinDepth = 0f,
                MaxDepth = 1f
            };
            RSSetViewports(_context, 1, vp);

            IntPtr srv = entry.CachedSrv;
            PSSetShaderResources(_context, 0, 1, &srv);
            Draw(_context, 3, 0);
        }

        OMSetBlendState(_context, IntPtr.Zero, null, 0xFFFFFFFFu);
        IntPtr nullPtr = IntPtr.Zero;
        PSSetShaderResources(_context, 0, 1, &nullPtr);
        OMSetRenderTargets(_context, 1, &nullPtr, IntPtr.Zero);
        return true;
    }

    private bool TryStartWindowCapture(WindowCaptureEntry entry, IntPtr hwnd)
    {
        Guid iidItem = IID_IGraphicsCaptureItem;
        IntPtr item = IntPtr.Zero;
        int hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, Guid*, IntPtr*, int>)VSlot(_itemInterop, 3))(
            _itemInterop, hwnd, &iidItem, &item);
        if (hr < 0 || item == IntPtr.Zero)
            return false;

        SizeInt32 sz = default;
        hr = ((delegate* unmanaged[Stdcall]<IntPtr, SizeInt32*, int>)VSlot(item, 7))(item, &sz);
        if (hr < 0 || sz.Width <= 4 || sz.Height <= 4)
        {
            SafeRelease(ref item);
            return false;
        }

        IntPtr pool = IntPtr.Zero;
        hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int, int, SizeInt32, IntPtr*, int>)VSlot(_poolStatics2, 6))(
            _poolStatics2, _winrtD3dDevice, DXGI_FORMAT_B8G8R8A8_UNORM, 2, sz, &pool);
        if (hr < 0 || pool == IntPtr.Zero)
        {
            SafeRelease(ref item);
            return false;
        }

        IntPtr session = IntPtr.Zero;
        hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr*, int>)VSlot(pool, 10))(pool, item, &session);
        if (hr < 0 || session == IntPtr.Zero)
        {
            CloseWinRtObject(pool);
            SafeRelease(ref pool);
            SafeRelease(ref item);
            return false;
        }

        // Disable mouse cursor & yellow capture border (Windows 11)
        if (QueryInterface(session, IID_IGraphicsCaptureSession2, out IntPtr s2) >= 0 && s2 != IntPtr.Zero)
        {
            ((delegate* unmanaged[Stdcall]<IntPtr, byte, int>)VSlot(s2, 7))(s2, 0);
            SafeRelease(ref s2);
        }
        if (QueryInterface(session, IID_IGraphicsCaptureSession3, out IntPtr s3) >= 0 && s3 != IntPtr.Zero)
        {
            ((delegate* unmanaged[Stdcall]<IntPtr, byte, int>)VSlot(s3, 7))(s3, 0);
            SafeRelease(ref s3);
        }

        hr = ((delegate* unmanaged[Stdcall]<IntPtr, int>)VSlot(session, 6))(session);
        if (hr < 0)
        {
            CloseWinRtObject(session);
            CloseWinRtObject(pool);
            SafeRelease(ref session);
            SafeRelease(ref pool);
            SafeRelease(ref item);
            return false;
        }

        entry.Item = item;
        entry.Pool = pool;
        entry.Session = session;
        entry.PoolSize = sz;
        return true;
    }

    private bool PollLatestWindowFrame(WindowCaptureEntry entry)
    {
        IntPtr frame = IntPtr.Zero;
        int hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)VSlot(entry.Pool, 7))(entry.Pool, &frame);
        if (hr < 0 || frame == IntPtr.Zero)
            return false;

        // Drain queued older frame if a newer one already arrived in the 2-buffer pool
        IntPtr newerFrame = IntPtr.Zero;
        if (((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)VSlot(entry.Pool, 7))(entry.Pool, &newerFrame) >= 0 && newerFrame != IntPtr.Zero)
        {
            CloseWinRtObject(frame);
            SafeRelease(ref frame);
            frame = newerFrame;
        }

        bool updated = false;
        try
        {
            SizeInt32 contentSize = default;
            ((delegate* unmanaged[Stdcall]<IntPtr, SizeInt32*, int>)VSlot(frame, 8))(frame, &contentSize);

            IntPtr surface = IntPtr.Zero;
            if (((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)VSlot(frame, 6))(frame, &surface) >= 0 && surface != IntPtr.Zero)
            {
                try
                {
                    if (QueryInterface(surface, IID_IDirect3DDxgiInterfaceAccess, out IntPtr access) >= 0 && access != IntPtr.Zero)
                    {
                        try
                        {
                            Guid iidTex = IID_ID3D11Texture2D;
                            IntPtr frameTex = IntPtr.Zero;
                            if (((delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)VSlot(access, 3))(access, &iidTex, &frameTex) >= 0 && frameTex != IntPtr.Zero)
                            {
                                try
                                {
                                    Texture2DGetDesc(frameTex, out var fdesc);
                                    EnsureEntryCache(entry, fdesc.Width, fdesc.Height, fdesc.Format);
                                    CopyResource(_context, entry.CachedTex, frameTex);

                                    int cw = contentSize.Width > 0 ? contentSize.Width : (int)fdesc.Width;
                                    int ch = contentSize.Height > 0 ? contentSize.Height : (int)fdesc.Height;
                                    entry.ValidUvScale = new Vector2(
                                        Math.Clamp((float)cw / Math.Max(1u, fdesc.Width), 0.01f, 1.0f),
                                        Math.Clamp((float)ch / Math.Max(1u, fdesc.Height), 0.01f, 1.0f));
                                    entry.HasFrame = true;
                                    updated = true;

                                    // Recreate frame pool if window resized significantly
                                    if (contentSize.Width > 4 && contentSize.Height > 4 &&
                                        (contentSize.Width != entry.PoolSize.Width || contentSize.Height != entry.PoolSize.Height))
                                    {
                                        ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int, int, SizeInt32, int>)VSlot(entry.Pool, 6))(
                                            entry.Pool, _winrtD3dDevice, DXGI_FORMAT_B8G8R8A8_UNORM, 2, contentSize);
                                        entry.PoolSize = contentSize;
                                    }
                                }
                                finally
                                {
                                    SafeRelease(ref frameTex);
                                }
                            }
                        }
                        finally
                        {
                            SafeRelease(ref access);
                        }
                    }
                }
                finally
                {
                    SafeRelease(ref surface);
                }
            }
        }
        finally
        {
            CloseWinRtObject(frame);
            SafeRelease(ref frame);
        }

        return updated;
    }

    private void EnsureEntryCache(WindowCaptureEntry entry, uint w, uint h, int fmt)
    {
        if (entry.CachedTex != IntPtr.Zero && entry.TexWidth == w && entry.TexHeight == h && entry.TexFormat == fmt)
            return;

        SafeRelease(ref entry.CachedSrv);
        SafeRelease(ref entry.CachedTex);

        var desc = new D3D11_TEXTURE2D_DESC
        {
            Width = w,
            Height = h,
            MipLevels = 1,
            ArraySize = 1,
            Format = fmt,
            SampleDesc = new DXGI_SAMPLE_DESC { Count = 1, Quality = 0 },
            Usage = D3D11_USAGE_DEFAULT,
            BindFlags = D3D11_BIND_SHADER_RESOURCE
        };
        CheckHr(CreateTexture2D(_device, desc, IntPtr.Zero, out entry.CachedTex), "CreateTexture2D(WinCache)");
        CheckHr(CreateShaderResourceView(_device, entry.CachedTex, IntPtr.Zero, out entry.CachedSrv), "CreateSRV(WinCache)");
        entry.TexWidth = w;
        entry.TexHeight = h;
        entry.TexFormat = fmt;
    }

    private static void CheckHr(int hr, string op)
    {
        if (hr < 0)
            throw new InvalidOperationException($"{op} failed with HRESULT 0x{hr:X8}");
    }

    public void Dispose()
    {
        foreach (var kv in _entries)
            kv.Value.Dispose();
        _entries.Clear();

        SafeRelease(ref _compositeSrv);
        SafeRelease(ref _compositeRtv);
        SafeRelease(ref _compositeTex);
        SafeRelease(ref _cbBlit);
        SafeRelease(ref _blendPremult);
        SafeRelease(ref _samplerLinear);
        SafeRelease(ref _psBlit);
        SafeRelease(ref _vsBlit);
        SafeRelease(ref _poolStatics2);
        SafeRelease(ref _itemInterop);
        SafeRelease(ref _winrtD3dDevice);
    }
}
