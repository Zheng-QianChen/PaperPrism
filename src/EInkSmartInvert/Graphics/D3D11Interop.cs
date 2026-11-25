using System.Numerics;
using System.Runtime.InteropServices;

namespace EInkSmartInvert.Graphics;

internal static unsafe class D3D11Interop
{
    public static readonly Guid IID_IDXGIDevice          = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
    public static readonly Guid IID_IDXGIDevice1         = new("77db970f-6276-48ba-ba28-070143b4392c");
    public static readonly Guid IID_IDXGIFactory1        = new("770aae78-f26f-4dba-a829-253c83d1b387");
    public static readonly Guid IID_IDXGIFactory2        = new("50c83a1c-e072-4c48-87b0-3630fa36a6d0");
    public static readonly Guid IID_IDXGIOutput1         = new("00cddea8-939b-4b83-a340-a685226666cc");
    public static readonly Guid IID_ID3D11Texture2D      = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
    public static readonly Guid IID_IDCompositionDevice  = new("c37ea93a-e7aa-450d-b16f-9746cb0407f3");

    public const int DXGI_FORMAT_R16G16B16A16_FLOAT = 10;
    public const int DXGI_FORMAT_R8G8B8A8_UNORM     = 28;
    public const int DXGI_FORMAT_B8G8R8A8_UNORM     = 87;
    public const int DXGI_FORMAT_R8G8_UNORM         = 49;

    public const int D3D11_USAGE_DEFAULT   = 0;
    public const int D3D11_USAGE_IMMUTABLE = 1;
    public const int D3D11_USAGE_DYNAMIC   = 2;
    public const int D3D11_USAGE_STAGING   = 3;

    public const uint D3D11_BIND_VERTEX_BUFFER   = 0x1;
    public const uint D3D11_BIND_CONSTANT_BUFFER = 0x4;
    public const uint D3D11_BIND_SHADER_RESOURCE = 0x8;
    public const uint D3D11_BIND_RENDER_TARGET   = 0x20;

    public const uint D3D11_CPU_ACCESS_WRITE = 0x10000;
    public const uint D3D11_CPU_ACCESS_READ  = 0x20000;

    public const int D3D11_MAP_READ          = 1;
    public const int D3D11_MAP_WRITE_DISCARD = 4;

    public const int D3D11_FILTER_MIN_MAG_MIP_POINT  = 0;
    public const int D3D11_FILTER_MIN_MAG_MIP_LINEAR = 0x15;
    public const int D3D11_TEXTURE_ADDRESS_CLAMP     = 3;
    public const int D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST = 4;

    public const uint DXGI_USAGE_RENDER_TARGET_OUTPUT = 0x20;
    public const int  DXGI_SCALING_STRETCH            = 0;
    public const int  DXGI_SCALING_NONE               = 1;
    public const int  DXGI_SWAP_EFFECT_FLIP_DISCARD   = 4;
    public const int  DXGI_ALPHA_MODE_PREMULTIPLIED   = 1;
    public const int  DXGI_ALPHA_MODE_IGNORE          = 3;
    public const uint DXGI_MWA_NO_ALT_ENTER           = 0x2;
    public const uint DXGI_PRESENT_DO_NOT_WAIT        = 0x00000008;

    public const int D3D11_BLEND_ZERO          = 1;
    public const int D3D11_BLEND_ONE           = 2;
    public const int D3D11_BLEND_SRC_ALPHA     = 5;
    public const int D3D11_BLEND_INV_SRC_ALPHA = 6;
    public const int D3D11_BLEND_OP_ADD        = 1;
    public const byte D3D11_COLOR_WRITE_ENABLE_ALL = 0x0F;

    public const int DXGI_ERROR_NOT_FOUND         = unchecked((int)0x887A0002);
    public const int DXGI_ERROR_UNSUPPORTED       = unchecked((int)0x887A0004);
    public const int DXGI_ERROR_DEVICE_REMOVED    = unchecked((int)0x887A0005);
    public const int DXGI_ERROR_DEVICE_RESET      = unchecked((int)0x887A0007);
    public const int DXGI_ERROR_WAS_STILL_DRAWING = unchecked((int)0x887A000A);
    public const int DXGI_ERROR_WAIT_TIMEOUT      = unchecked((int)0x887A0027);
    public const int DXGI_ERROR_ACCESS_LOST       = unchecked((int)0x887A0026);
    public const int E_ACCESSDENIED               = unchecked((int)0x80070005);

    [StructLayout(LayoutKind.Sequential)]
    public struct DXGI_SAMPLE_DESC
    {
        public uint Count;
        public uint Quality;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DXGI_SWAP_CHAIN_DESC1
    {
        public uint Width;
        public uint Height;
        public int Format;
        public int Stereo;
        public DXGI_SAMPLE_DESC SampleDesc;
        public uint BufferUsage;
        public uint BufferCount;
        public int Scaling;
        public int SwapEffect;
        public int AlphaMode;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DXGI_ADAPTER_DESC1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
        public uint VendorId, DeviceId, SubSysId, Revision;
        public nuint DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory;
        public long AdapterLuid;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DXGI_OUTPUT_DESC
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
        public Core.MonitorManager.RECT DesktopCoordinates;
        public int AttachedToDesktop;
        public int Rotation;
        public IntPtr Monitor;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DXGI_MODE_DESC
    {
        public uint Width;
        public uint Height;
        public uint RefreshRateNumerator;
        public uint RefreshRateDenominator;
        public int Format;
        public int ScanlineOrdering;
        public int Scaling;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DXGI_OUTDUPL_DESC
    {
        public DXGI_MODE_DESC ModeDesc;
        public int Rotation;
        public int DesktopImageInSystemMemory;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DXGI_OUTDUPL_POINTER_POSITION
    {
        public int X;
        public int Y;
        public int Visible;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DXGI_OUTDUPL_FRAME_INFO
    {
        public long LastPresentTime;
        public long LastMouseUpdateTime;
        public uint AccumulatedFrames;
        public int RectsCoalesced;
        public int ProtectedContentMaskedOut;
        public DXGI_OUTDUPL_POINTER_POSITION PointerPosition;
        public uint TotalMetadataBufferSize;
        public uint PointerShapeBufferSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D3D11_TEXTURE2D_DESC
    {
        public uint Width;
        public uint Height;
        public uint MipLevels;
        public uint ArraySize;
        public int Format;
        public DXGI_SAMPLE_DESC SampleDesc;
        public int Usage;
        public uint BindFlags;
        public uint CPUAccessFlags;
        public uint MiscFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D3D11_BUFFER_DESC
    {
        public uint ByteWidth;
        public int Usage;
        public uint BindFlags;
        public uint CPUAccessFlags;
        public uint MiscFlags;
        public uint StructureByteStride;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D3D11_SUBRESOURCE_DATA
    {
        public IntPtr pSysMem;
        public uint SysMemPitch;
        public uint SysMemSlicePitch;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D3D11_MAPPED_SUBRESOURCE
    {
        public IntPtr pData;
        public uint RowPitch;
        public uint DepthPitch;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct D3D11_SAMPLER_DESC
    {
        public int Filter;
        public int AddressU;
        public int AddressV;
        public int AddressW;
        public float MipLODBias;
        public uint MaxAnisotropy;
        public int ComparisonFunc;
        public fixed float BorderColor[4];
        public float MinLOD;
        public float MaxLOD;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D3D11_VIEWPORT
    {
        public float TopLeftX;
        public float TopLeftY;
        public float Width;
        public float Height;
        public float MinDepth;
        public float MaxDepth;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D3D11_RENDER_TARGET_BLEND_DESC
    {
        public int BlendEnable;
        public int SrcBlend;
        public int DestBlend;
        public int BlendOp;
        public int SrcBlendAlpha;
        public int DestBlendAlpha;
        public int BlendOpAlpha;
        public byte RenderTargetWriteMask;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D3D11_BLEND_DESC
    {
        public int AlphaToCoverageEnable;
        public int IndependentBlendEnable;
        public D3D11_RENDER_TARGET_BLEND_DESC RT0;
        public D3D11_RENDER_TARGET_BLEND_DESC RT1;
        public D3D11_RENDER_TARGET_BLEND_DESC RT2;
        public D3D11_RENDER_TARGET_BLEND_DESC RT3;
        public D3D11_RENDER_TARGET_BLEND_DESC RT4;
        public D3D11_RENDER_TARGET_BLEND_DESC RT5;
        public D3D11_RENDER_TARGET_BLEND_DESC RT6;
        public D3D11_RENDER_TARGET_BLEND_DESC RT7;
    }

    [DllImport("dxgi.dll", ExactSpelling = true)]
    public static extern int CreateDXGIFactory1(in Guid riid, out IntPtr ppFactory);

    [DllImport("dcomp.dll", ExactSpelling = true)]
    public static extern int DCompositionCreateDevice(IntPtr dxgiDevice, in Guid iid, out IntPtr dcompositionDevice);

    [DllImport("d3d11.dll", ExactSpelling = true)]
    public static extern int D3D11CreateDevice(
        IntPtr pAdapter,
        int driverType,
        IntPtr software,
        uint flags,
        IntPtr pFeatureLevels,
        uint featureLevels,
        uint sdkVersion,
        out IntPtr ppDevice,
        out int pFeatureLevel,
        out IntPtr ppImmediateContext);

    [DllImport("d3dcompiler_47.dll", ExactSpelling = true)]
    public static extern int D3DCompile(
        IntPtr pSrcData,
        nuint srcDataSize,
        [MarshalAs(UnmanagedType.LPStr)] string? pSourceName,
        IntPtr pDefines,
        IntPtr pInclude,
        [MarshalAs(UnmanagedType.LPStr)] string pEntrypoint,
        [MarshalAs(UnmanagedType.LPStr)] string pTarget,
        uint flags1,
        uint flags2,
        out IntPtr ppCode,
        out IntPtr ppErrorMsgs);

    private static void* VSlot(IntPtr obj, int index) => (*(void***)obj)[index];

    public static int QueryInterface(IntPtr obj, in Guid iid, out IntPtr ppv)
    {
        fixed (Guid* pIid = &iid)
        {
            IntPtr outPtr = IntPtr.Zero;
            int hr = ((delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)VSlot(obj, 0))(obj, pIid, &outPtr);
            ppv = outPtr;
            return hr;
        }
    }

    public static void SafeRelease(ref IntPtr obj)
    {
        if (obj != IntPtr.Zero)
        {
            ((delegate* unmanaged[Stdcall]<IntPtr, uint>)VSlot(obj, 2))(obj);
            obj = IntPtr.Zero;
        }
    }

    public static IntPtr BlobGetBufferPointer(IntPtr blob) =>
        ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr>)VSlot(blob, 3))(blob);

    public static nuint BlobGetBufferSize(IntPtr blob) =>
        ((delegate* unmanaged[Stdcall]<IntPtr, nuint>)VSlot(blob, 4))(blob);

    // IDXGIFactory1 / IDXGIFactory2
    public static int FactoryMakeWindowAssociation(IntPtr factory, IntPtr hwnd, uint flags) =>
        ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, int>)VSlot(factory, 8))(factory, hwnd, flags);

    public static int FactoryEnumAdapters1(IntPtr factory, uint idx, out IntPtr adapter)
    {
        IntPtr p = IntPtr.Zero;
        int hr = ((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)VSlot(factory, 12))(factory, idx, &p);
        adapter = p;
        return hr;
    }

    public static int FactoryCreateSwapChainForComposition(
        IntPtr factory2, IntPtr device, in DXGI_SWAP_CHAIN_DESC1 desc, out IntPtr swapChain)
    {
        fixed (DXGI_SWAP_CHAIN_DESC1* pDesc = &desc)
        {
            IntPtr sc = IntPtr.Zero;
            int hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, DXGI_SWAP_CHAIN_DESC1*, IntPtr, IntPtr*, int>)
                VSlot(factory2, 24))(factory2, device, pDesc, IntPtr.Zero, &sc);
            swapChain = sc;
            return hr;
        }
    }

    // DirectComposition (IDCompositionDevice, IDCompositionTarget, IDCompositionVisual)
    public static int DCompCommit(IntPtr dcompDevice) =>
        ((delegate* unmanaged[Stdcall]<IntPtr, int>)VSlot(dcompDevice, 3))(dcompDevice);

    public static int DCompCreateTargetForHwnd(IntPtr dcompDevice, IntPtr hwnd, int topmost, out IntPtr target)
    {
        IntPtr t = IntPtr.Zero;
        int hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int, IntPtr*, int>)
            VSlot(dcompDevice, 6))(dcompDevice, hwnd, topmost, &t);
        target = t;
        return hr;
    }

    public static int DCompCreateVisual(IntPtr dcompDevice, out IntPtr visual)
    {
        IntPtr v = IntPtr.Zero;
        int hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)
            VSlot(dcompDevice, 7))(dcompDevice, &v);
        visual = v;
        return hr;
    }

    public static int DCompTargetSetRoot(IntPtr target, IntPtr visual) =>
        ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)VSlot(target, 3))(target, visual);

    public static int DCompVisualSetContent(IntPtr visual, IntPtr content) =>
        ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)VSlot(visual, 15))(visual, content);

    // IDXGIAdapter1 & IDXGIOutput1
    public static int AdapterEnumOutputs(IntPtr adapter, uint idx, out IntPtr output)
    {
        IntPtr p = IntPtr.Zero;
        int hr = ((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)VSlot(adapter, 7))(adapter, idx, &p);
        output = p;
        return hr;
    }

    public static int OutputGetDesc(IntPtr output, out DXGI_OUTPUT_DESC desc)
    {
        desc = default;
        int cb = Marshal.SizeOf<DXGI_OUTPUT_DESC>();
        IntPtr mem = Marshal.AllocHGlobal(cb);
        try
        {
            int hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)VSlot(output, 7))(output, mem);
            if (hr >= 0) desc = Marshal.PtrToStructure<DXGI_OUTPUT_DESC>(mem);
            return hr;
        }
        finally
        {
            Marshal.FreeHGlobal(mem);
        }
    }

    public static int Output1DuplicateOutput(IntPtr output1, IntPtr device, out IntPtr duplication)
    {
        IntPtr p = IntPtr.Zero;
        int hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr*, int>)VSlot(output1, 22))(output1, device, &p);
        duplication = p;
        return hr;
    }

    // IDXGIOutputDuplication
    public static void OutDuplGetDesc(IntPtr dupl, out DXGI_OUTDUPL_DESC desc)
    {
        fixed (DXGI_OUTDUPL_DESC* p = &desc)
        {
            ((delegate* unmanaged[Stdcall]<IntPtr, DXGI_OUTDUPL_DESC*, void>)VSlot(dupl, 7))(dupl, p);
        }
    }

    public static int OutDuplAcquireNextFrame(IntPtr dupl, uint timeoutMs, out DXGI_OUTDUPL_FRAME_INFO info, out IntPtr desktopResource)
    {
        fixed (DXGI_OUTDUPL_FRAME_INFO* pInfo = &info)
        {
            IntPtr res = IntPtr.Zero;
            int hr = ((delegate* unmanaged[Stdcall]<IntPtr, uint, DXGI_OUTDUPL_FRAME_INFO*, IntPtr*, int>)
                VSlot(dupl, 8))(dupl, timeoutMs, pInfo, &res);
            desktopResource = res;
            return hr;
        }
    }

    public static int OutDuplReleaseFrame(IntPtr dupl) =>
        ((delegate* unmanaged[Stdcall]<IntPtr, int>)VSlot(dupl, 14))(dupl);

    // IDXGIDevice1
    public static int DXGIDevice1SetMaximumFrameLatency(IntPtr dxgiDevice1, uint maxLatency) =>
        ((delegate* unmanaged[Stdcall]<IntPtr, uint, int>)VSlot(dxgiDevice1, 12))(dxgiDevice1, maxLatency);

    // IDXGISwapChain1
    public static int SwapChainPresent(IntPtr swapChain, uint syncInterval, uint flags) =>
        ((delegate* unmanaged[Stdcall]<IntPtr, uint, uint, int>)VSlot(swapChain, 8))(swapChain, syncInterval, flags);

    public static int SwapChainGetBuffer(IntPtr swapChain, uint buffer, in Guid riid, out IntPtr surface)
    {
        fixed (Guid* pIid = &riid)
        {
            IntPtr s = IntPtr.Zero;
            int hr = ((delegate* unmanaged[Stdcall]<IntPtr, uint, Guid*, IntPtr*, int>)VSlot(swapChain, 9))(swapChain, buffer, pIid, &s);
            surface = s;
            return hr;
        }
    }

    // ID3D11Device
    public static int CreateBuffer(IntPtr dev, in D3D11_BUFFER_DESC desc, IntPtr initialData, out IntPtr buffer)
    {
        fixed (D3D11_BUFFER_DESC* pDesc = &desc)
        {
            IntPtr b = IntPtr.Zero;
            int hr = ((delegate* unmanaged[Stdcall]<IntPtr, D3D11_BUFFER_DESC*, IntPtr, IntPtr*, int>)
                VSlot(dev, 3))(dev, pDesc, initialData, &b);
            buffer = b;
            return hr;
        }
    }

    public static int CreateTexture2D(IntPtr dev, in D3D11_TEXTURE2D_DESC desc, IntPtr initialData, out IntPtr tex)
    {
        fixed (D3D11_TEXTURE2D_DESC* pDesc = &desc)
        {
            IntPtr t = IntPtr.Zero;
            int hr = ((delegate* unmanaged[Stdcall]<IntPtr, D3D11_TEXTURE2D_DESC*, IntPtr, IntPtr*, int>)
                VSlot(dev, 5))(dev, pDesc, initialData, &t);
            tex = t;
            return hr;
        }
    }

    public static int CreateShaderResourceView(IntPtr dev, IntPtr resource, IntPtr desc, out IntPtr srv)
    {
        IntPtr s = IntPtr.Zero;
        int hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, IntPtr*, int>)
            VSlot(dev, 7))(dev, resource, desc, &s);
        srv = s;
        return hr;
    }

    public static int CreateRenderTargetView(IntPtr dev, IntPtr resource, IntPtr desc, out IntPtr rtv)
    {
        IntPtr r = IntPtr.Zero;
        int hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, IntPtr*, int>)
            VSlot(dev, 9))(dev, resource, desc, &r);
        rtv = r;
        return hr;
    }

    public static int CreateVertexShader(IntPtr dev, IntPtr bytecode, nuint len, out IntPtr vs)
    {
        IntPtr v = IntPtr.Zero;
        int hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, nuint, IntPtr, IntPtr*, int>)
            VSlot(dev, 12))(dev, bytecode, len, IntPtr.Zero, &v);
        vs = v;
        return hr;
    }

    public static int CreatePixelShader(IntPtr dev, IntPtr bytecode, nuint len, out IntPtr ps)
    {
        IntPtr p = IntPtr.Zero;
        int hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, nuint, IntPtr, IntPtr*, int>)
            VSlot(dev, 15))(dev, bytecode, len, IntPtr.Zero, &p);
        ps = p;
        return hr;
    }

    public static int CreateBlendState(IntPtr dev, in D3D11_BLEND_DESC desc, out IntPtr blendState)
    {
        fixed (D3D11_BLEND_DESC* pDesc = &desc)
        {
            IntPtr bs = IntPtr.Zero;
            int hr = ((delegate* unmanaged[Stdcall]<IntPtr, D3D11_BLEND_DESC*, IntPtr*, int>)
                VSlot(dev, 20))(dev, pDesc, &bs);
            blendState = bs;
            return hr;
        }
    }

    public static int CreateSamplerState(IntPtr dev, in D3D11_SAMPLER_DESC desc, out IntPtr sampler)
    {
        fixed (D3D11_SAMPLER_DESC* pDesc = &desc)
        {
            IntPtr s = IntPtr.Zero;
            int hr = ((delegate* unmanaged[Stdcall]<IntPtr, D3D11_SAMPLER_DESC*, IntPtr*, int>)
                VSlot(dev, 23))(dev, pDesc, &s);
            sampler = s;
            return hr;
        }
    }

    // ID3D11Texture2D::GetDesc (ID3D11Resource inherits ID3D11DeviceChild(3..6), ID3D11Resource(7..9), ID3D11Texture2D::GetDesc = 10)
    public static void Texture2DGetDesc(IntPtr tex, out D3D11_TEXTURE2D_DESC desc)
    {
        fixed (D3D11_TEXTURE2D_DESC* p = &desc)
        {
            ((delegate* unmanaged[Stdcall]<IntPtr, D3D11_TEXTURE2D_DESC*, void>)VSlot(tex, 10))(tex, p);
        }
    }

    // ID3D11DeviceContext
    public static void PSSetShaderResources(IntPtr ctx, uint startSlot, uint numViews, IntPtr* srvs) =>
        ((delegate* unmanaged[Stdcall]<IntPtr, uint, uint, IntPtr*, void>)VSlot(ctx, 8))(ctx, startSlot, numViews, srvs);

    public static void PSSetShader(IntPtr ctx, IntPtr ps) =>
        ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, uint, void>)VSlot(ctx, 9))(ctx, ps, IntPtr.Zero, 0);

    public static void PSSetSamplers(IntPtr ctx, uint startSlot, uint numSamplers, IntPtr* samplers) =>
        ((delegate* unmanaged[Stdcall]<IntPtr, uint, uint, IntPtr*, void>)VSlot(ctx, 10))(ctx, startSlot, numSamplers, samplers);

    public static void VSSetShader(IntPtr ctx, IntPtr vs) =>
        ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, uint, void>)VSlot(ctx, 11))(ctx, vs, IntPtr.Zero, 0);

    public static void Draw(IntPtr ctx, uint vertexCount, uint startVertexLocation) =>
        ((delegate* unmanaged[Stdcall]<IntPtr, uint, uint, void>)VSlot(ctx, 13))(ctx, vertexCount, startVertexLocation);

    public static int Map(IntPtr ctx, IntPtr resource, uint subresource, int mapType, uint mapFlags, out D3D11_MAPPED_SUBRESOURCE mapped)
    {
        fixed (D3D11_MAPPED_SUBRESOURCE* p = &mapped)
        {
            return ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, int, uint, D3D11_MAPPED_SUBRESOURCE*, int>)
                VSlot(ctx, 14))(ctx, resource, subresource, mapType, mapFlags, p);
        }
    }

    public static void Unmap(IntPtr ctx, IntPtr resource, uint subresource) =>
        ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, void>)VSlot(ctx, 15))(ctx, resource, subresource);

    public static void PSSetConstantBuffers(IntPtr ctx, uint startSlot, uint numBuffers, IntPtr* buffers) =>
        ((delegate* unmanaged[Stdcall]<IntPtr, uint, uint, IntPtr*, void>)VSlot(ctx, 16))(ctx, startSlot, numBuffers, buffers);

    public static void IASetPrimitiveTopology(IntPtr ctx, int topology) =>
        ((delegate* unmanaged[Stdcall]<IntPtr, int, void>)VSlot(ctx, 24))(ctx, topology);

    public static void OMSetRenderTargets(IntPtr ctx, uint numViews, IntPtr* rtvs, IntPtr dsv) =>
        ((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, IntPtr, void>)VSlot(ctx, 33))(ctx, numViews, rtvs, dsv);

    public static void OMSetBlendState(IntPtr ctx, IntPtr blendState, float* blendFactor, uint sampleMask) =>
        ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, float*, uint, void>)VSlot(ctx, 35))(ctx, blendState, blendFactor, sampleMask);

    public static void RSSetViewports(IntPtr ctx, uint numViewports, in D3D11_VIEWPORT vp)
    {
        fixed (D3D11_VIEWPORT* p = &vp)
        {
            ((delegate* unmanaged[Stdcall]<IntPtr, uint, D3D11_VIEWPORT*, void>)VSlot(ctx, 44))(ctx, numViewports, p);
        }
    }

    public static void CopyResource(IntPtr ctx, IntPtr dst, IntPtr src) =>
        ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, void>)VSlot(ctx, 47))(ctx, dst, src);

    public static void UpdateSubresource(IntPtr ctx, IntPtr dst, uint dstSubresource, IntPtr dstBox, IntPtr srcData, uint srcRowPitch, uint srcDepthPitch) =>
        ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, IntPtr, IntPtr, uint, uint, void>)
            VSlot(ctx, 48))(ctx, dst, dstSubresource, dstBox, srcData, srcRowPitch, srcDepthPitch);

    public static void ClearRenderTargetView(IntPtr ctx, IntPtr rtv, float* colorRgba) =>
        ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, float*, void>)VSlot(ctx, 50))(ctx, rtv, colorRgba);

    public static void Flush(IntPtr ctx) =>
        ((delegate* unmanaged[Stdcall]<IntPtr, void>)VSlot(ctx, 111))(ctx);
}
