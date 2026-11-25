using System.Runtime.InteropServices;
using EInkSmartInvert.Config;

namespace EInkSmartInvert.Core;

public readonly record struct TrackedWindow(
    IntPtr Hwnd,
    Rectangle LocalBounds,
    Rectangle FullBounds,
    bool IsPassthroughHole = false,
    FilterMode EffectiveMode = FilterMode.SmartAuto,
    string ProcessName = ""
);

public readonly record struct RunningAppCandidate(
    string ProcessName,
    string SampleWindowTitle
);

public static unsafe class WindowTracker
{
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsHungAppWindow(IntPtr hWnd);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")]
    private static extern nint GetClassLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern bool GetWindowBand(IntPtr hWnd, out uint pdwBand);

    [DllImport("user32.dll")]
    private static extern bool GetLayeredWindowAttributes(IntPtr hwnd, out uint crKey, out byte bAlpha, out uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(IntPtr hWnd, char* lpClassName, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(IntPtr hWnd, char* lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageNameW(IntPtr hProcess, uint dwFlags, char* lpExeName, ref uint lpdwSize);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out MonitorManager.RECT lpRect);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out MonitorManager.RECT pvAttribute, int cbAttribute);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;
    private const int GCL_STYLE = -26;
    private const nint CS_IME = 0x00010000;
    private const nint WS_MAXIMIZE = 0x01000000;
    private const nint WS_EX_TOPMOST = 0x00000008;
    private const nint WS_EX_TRANSPARENT = 0x00000020;
    private const nint WS_EX_TOOLWINDOW = 0x00000080;
    private const nint WS_EX_LAYERED = 0x00080000;
    private const nint WS_EX_NOREDIRECTIONBITMAP = 0x00200000;
    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    private const int DWMWA_CLOAKED = 14;

    private static readonly object _pidCacheLock = new();
    private static readonly Dictionary<uint, string> _pidToProcessName = new(64);
    private static readonly uint _ownPid = (uint)Environment.ProcessId;

    // Zero-allocation state for single-threaded render loop usage
    private static Rectangle _curMonBounds;
    private static IntPtr _curExcludeHwnd;
    private static bool _passedOverlayHwnd;
    private static bool _curIncludePopupsAsHoles;
    private static AppSettings? _curSettings;
    private static TrackedWindow[]? _curTargetBuf;
    private static int _curCount;
    private static int _curMax;
    private static readonly Rectangle[] _occluders = new Rectangle[16];
    private static int _occluderCount;
    private static readonly EnumWindowsProc CachedCallback = EnumCallback;

    // Zero-allocation state for TopMost band floor query (UI thread)
    private static IntPtr _tmQueryOverlayHwnd;
    private static bool _tmHasBand;
    private static uint _tmMyBand;
    private static IntPtr _tmLowestOther;
    private static IntPtr _tmLastInPrefix;
    private static readonly EnumWindowsProc CachedTopMostPrefixCallback = TopMostPrefixCallback;

    public static string GetProcessNameForHwnd(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
            return string.Empty;

        GetWindowThreadProcessId(hWnd, out uint pid);
        if (pid == 0)
            return string.Empty;

        lock (_pidCacheLock)
        {
            if (_pidToProcessName.TryGetValue(pid, out string? cached))
                return cached;
        }

        string resolved = ResolveProcessNameByPid(pid);
        if (!string.IsNullOrEmpty(resolved))
        {
            lock (_pidCacheLock)
            {
                if (_pidToProcessName.Count > 256)
                    _pidToProcessName.Clear();
                _pidToProcessName[pid] = resolved;
            }
        }
        return resolved;
    }

    private static string ResolveProcessNameByPid(uint pid)
    {
        IntPtr hProc = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (hProc == IntPtr.Zero)
            return string.Empty;

        try
        {
            char* pathBuf = stackalloc char[512];
            uint size = 512;
            if (!QueryFullProcessImageNameW(hProc, 0, pathBuf, ref size) || size == 0)
                return string.Empty;

            var span = new ReadOnlySpan<char>(pathBuf, (int)size);
            int slash = span.LastIndexOfAny('\\', '/');
            if (slash >= 0 && slash + 1 < span.Length)
                span = span[(slash + 1)..];
            if (span.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                span = span[..^4];

            return span.ToString();
        }
        finally
        {
            CloseHandle(hProc);
        }
    }

    /// <summary>
    /// Lists visible user desktop applications so the UI can offer 1-click "从当前运行窗口添加".
    /// </summary>
    public static List<RunningAppCandidate> EnumerateRunningDesktopApps()
    {
        var list = new List<RunningAppCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd))
                return true;

            if (DwmGetWindowAttribute(hWnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
                return true;

            nint ex = GetWindowLongPtr(hWnd, GWL_EXSTYLE);
            if ((ex & WS_EX_TOOLWINDOW) != 0)
                return true;

            GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid == 0 || pid == _ownPid)
                return true;

            char* titleBuf = stackalloc char[160];
            int tLen = GetWindowTextW(hWnd, titleBuf, 160);
            if (tLen <= 0)
                return true;

            string proc = GetProcessNameForHwnd(hWnd);
            if (string.IsNullOrWhiteSpace(proc) ||
                proc.Equals("explorer", StringComparison.OrdinalIgnoreCase) ||
                proc.Equals("TextInputHost", StringComparison.OrdinalIgnoreCase) ||
                proc.Equals("SearchHost", StringComparison.OrdinalIgnoreCase) ||
                proc.Equals("ShellExperienceHost", StringComparison.OrdinalIgnoreCase) ||
                proc.Equals("StartMenuExperienceHost", StringComparison.OrdinalIgnoreCase) ||
                proc.Equals("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (seen.Add(proc))
            {
                string title = new string(titleBuf, 0, tLen).Trim();
                if (title.Length > 48)
                    title = title[..48] + "…";
                list.Add(new RunningAppCandidate(proc, title));
            }
            return true;
        }, IntPtr.Zero);

        list.Sort((a, b) => string.Compare(a.ProcessName, b.ProcessName, StringComparison.OrdinalIgnoreCase));
        return list;
    }

    /// <summary>
    /// Finds the lowest other WS_EX_TOPMOST window in the same ZBID band (within the contiguous TopMost prefix
    /// above normal windows, including hidden SW_HIDE IME candidate windows such as Sogou SoPY_Comp).
    /// Placing OverlayHostForm right below this HWND keeps OverlayHostForm 100% ABOVE all normal app windows
    /// while 100% BELOW all TopMost IME candidate bars, status bars, menus, and tooltips!
    /// </summary>
    public static IntPtr FindLowestOtherTopMostInSameBand(IntPtr overlayHwnd, out bool alreadyAtBottomOfTopMostBand)
    {
        _tmQueryOverlayHwnd = overlayHwnd;
        _tmHasBand = GetWindowBand(overlayHwnd, out _tmMyBand);
        _tmLowestOther = IntPtr.Zero;
        _tmLastInPrefix = IntPtr.Zero;

        EnumWindows(CachedTopMostPrefixCallback, IntPtr.Zero);

        nint myEx = GetWindowLongPtr(overlayHwnd, GWL_EXSTYLE);
        alreadyAtBottomOfTopMostBand = ((myEx & WS_EX_TOPMOST) != 0) && (_tmLastInPrefix == overlayHwnd);
        return _tmLowestOther;
    }

    private static bool TopMostPrefixCallback(IntPtr hWnd, IntPtr lParam)
    {
        if (_tmHasBand && GetWindowBand(hWnd, out uint band) && band != _tmMyBand)
            return true; // Skip higher ZBID bands (e.g. ZBID_IMMERSIVE_IHM)

        nint ex = GetWindowLongPtr(hWnd, GWL_EXSTYLE);
        if ((ex & WS_EX_TOPMOST) == 0)
            return false; // Reached boundary into Normal (non-topmost) windows!

        _tmLastInPrefix = hWnd;
        if (hWnd != _tmQueryOverlayHwnd)
            _tmLowestOther = hWnd;
        return true;
    }

    public static int FillVisibleWindowsOnMonitor(
        Rectangle monitorBounds,
        IntPtr excludeOverlayHwnd,
        TrackedWindow[] buffer,
        bool includePopupsAsHoles = false,
        AppSettings? settings = null)
    {
        _curMonBounds = monitorBounds;
        _curExcludeHwnd = excludeOverlayHwnd;
        _passedOverlayHwnd = (excludeOverlayHwnd == IntPtr.Zero) || !IsWindowVisible(excludeOverlayHwnd);
        _curIncludePopupsAsHoles = includePopupsAsHoles;
        _curSettings = settings;
        _curTargetBuf = buffer;
        _curCount = 0;
        _occluderCount = 0;
        _curMax = Math.Min(buffer.Length, 16);

        EnumWindows(CachedCallback, IntPtr.Zero);

        // Safety fallback if excludeOverlayHwnd was not encountered in EnumWindows
        if (!_passedOverlayHwnd)
        {
            _passedOverlayHwnd = true;
            _curCount = 0;
            _occluderCount = 0;
            EnumWindows(CachedCallback, IntPtr.Zero);
        }

        _curSettings = null;
        _curTargetBuf = null;
        return _curCount;
    }

    private static bool IsImeWindowClassName(ReadOnlySpan<char> cls)
    {
        return cls.Equals("IME", StringComparison.OrdinalIgnoreCase) ||
               cls.Equals("MSCTFIME UI", StringComparison.OrdinalIgnoreCase) ||
               cls.Contains("SoPY", StringComparison.OrdinalIgnoreCase) ||
               cls.Contains("Sogou", StringComparison.OrdinalIgnoreCase) ||
               cls.StartsWith("SG_", StringComparison.OrdinalIgnoreCase) ||
               cls.Contains("Candi", StringComparison.OrdinalIgnoreCase) ||
               cls.Contains("Weasel", StringComparison.OrdinalIgnoreCase) ||
               cls.Contains("QQPinyin", StringComparison.OrdinalIgnoreCase) ||
               cls.Contains("Baidu", StringComparison.OrdinalIgnoreCase);
    }

    private static bool EnumCallback(IntPtr hWnd, IntPtr lParam)
    {
        if (hWnd == _curExcludeHwnd)
        {
            _passedOverlayHwnd = true;
            return true;
        }

        // Any window enumerated BEFORE OverlayHostForm is ALREADY physically in front of OverlayHostForm
        // in DWM Z-order (e.g., TopMost IME candidate bars, tooltips, context menus, taskbar)!
        // DWM composites it natively on top of our overlay at 0ms latency without needing capture or hole-punch!
        if (!_passedOverlayHwnd)
            return true;

        if (!IsWindowVisible(hWnd) || IsIconic(hWnd))
            return true;

        bool isHung = IsHungAppWindow(hWnd);
        if (!isHung && DwmGetWindowAttribute(hWnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
            return true;

        bool isShellOrSpriteHole = false;
        bool isImeWindow = ((GetClassLongPtr(hWnd, GCL_STYLE) & CS_IME) != 0);

        char* clsBuf = stackalloc char[64];
        int len = GetClassNameW(hWnd, clsBuf, 64);
        var cls = len > 0 ? new ReadOnlySpan<char>(clsBuf, len) : ReadOnlySpan<char>.Empty;
        if (len > 0)
        {
            if (cls.SequenceEqual("Progman") ||
                cls.SequenceEqual("WorkerW"))
            {
                return true;
            }

            if (IsImeWindowClassName(cls))
                isImeWindow = true;

            if (isImeWindow ||
                cls.SequenceEqual("Windows.UI.Core.CoreWindow") ||
                cls.SequenceEqual("Shell_TrayWnd") ||
                cls.SequenceEqual("Shell_SecondaryTrayWnd"))
            {
                if (!_curIncludePopupsAsHoles)
                    return true;
                isShellOrSpriteHole = true;
            }
        }

        nint exStyle = GetWindowLongPtr(hWnd, GWL_EXSTYLE);
        // Ignore click-through full-screen overlays, EXCEPT owned non-topmost IME candidate bars!
        if (!isImeWindow && (exStyle & (WS_EX_LAYERED | WS_EX_TRANSPARENT)) == (WS_EX_LAYERED | WS_EX_TRANSPARENT))
            return true;

        // Legacy UpdateLayeredWindow GDI sprites (e.g. non-topmost skin popups/IMEs) return false from GetLayeredWindowAttributes;
        // WGC CreateForWindow cannot capture ULW sprites, so mark them as instant Alpha=0 passthrough holes!
        if (!isShellOrSpriteHole && _curIncludePopupsAsHoles &&
            (exStyle & WS_EX_LAYERED) != 0 && (exStyle & WS_EX_NOREDIRECTIONBITMAP) == 0 &&
            !GetLayeredWindowAttributes(hWnd, out _, out _, out _))
        {
            isShellOrSpriteHole = true;
        }

        MonitorManager.RECT rc;
        if (isHung || DwmGetWindowAttribute(hWnd, DWMWA_EXTENDED_FRAME_BOUNDS, out rc, sizeof(MonitorManager.RECT)) != 0)
        {
            if (!GetWindowRect(hWnd, out rc))
                return true;
        }

        int w = rc.Right - rc.Left;
        int h = rc.Bottom - rc.Top;
        int minW = _curIncludePopupsAsHoles ? 16 : 60;
        int minH = _curIncludePopupsAsHoles ? 16 : 60;
        if (w < minW || h < minH)
            return true;

        var fullRect = new Rectangle(rc.Left, rc.Top, w, h);
        var clipped = Rectangle.Intersect(fullRect, _curMonBounds);
        int minClip = _curIncludePopupsAsHoles ? 12 : 32;
        if (clipped.Width < minClip || clipped.Height < minClip)
            return true;

        var localRect = new Rectangle(
            clipped.X - _curMonBounds.X,
            clipped.Y - _curMonBounds.Y,
            clipped.Width,
            clipped.Height);

        // Z-Order Occlusion Culling: skip non-topmost windows 100% buried behind a front opaque window!
        if ((exStyle & WS_EX_TOPMOST) == 0)
        {
            for (int k = 0; k < _occluderCount; k++)
            {
                if (_occluders[k].Contains(localRect))
                    return true;
            }
        }

        string procName = string.Empty;
        FilterMode effectiveMode = FilterMode.SmartAuto;
        bool isPassthroughHole = isShellOrSpriteHole;

        if (!isShellOrSpriteHole)
        {
            procName = GetProcessNameForHwnd(hWnd);
            if (_curSettings != null)
            {
                effectiveMode = _curSettings.ResolveWindowMode(procName, cls);
                if (effectiveMode == FilterMode.Off)
                    isPassthroughHole = true;
            }
        }
        else
        {
            effectiveMode = FilterMode.Off;
        }

        _curTargetBuf![_curCount++] = new TrackedWindow(
            hWnd,
            localRect,
            fullRect,
            isPassthroughHole,
            effectiveMode,
            procName);

        // Any opaque normal window (whether smart-inverted or native passthrough) physically occludes windows behind it!
        if (!isShellOrSpriteHole && (exStyle & WS_EX_LAYERED) == 0 && localRect.Width >= 200 && localRect.Height >= 160 && _occluderCount < _occluders.Length)
        {
            nint style = GetWindowLongPtr(hWnd, GWL_STYLE);
            bool isMaximized = (style & WS_MAXIMIZE) != 0;
            var occRect = isMaximized ? localRect : Rectangle.Inflate(localRect, -12, -12);
            if (occRect.Width > 0 && occRect.Height > 0)
                _occluders[_occluderCount++] = occRect;
        }

        return _curCount < _curMax;
    }
}
