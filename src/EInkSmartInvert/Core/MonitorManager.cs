using System.Runtime.InteropServices;

namespace EInkSmartInvert.Core;

public sealed class MonitorDescriptor
{
    public IntPtr Handle { get; init; }
    public string DeviceName { get; init; } = "";
    public string FriendlyName { get; init; } = "";
    public bool IsPrimary { get; init; }
    public Rectangle Bounds { get; init; }

    public string DisplayLabel
    {
        get
        {
            bool zh = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("zh", StringComparison.OrdinalIgnoreCase);
            string role = IsPrimary ? (zh ? "主屏" : "Primary") : (zh ? "副屏" : "Secondary");
            string name = string.IsNullOrWhiteSpace(FriendlyName) ? DeviceName.Replace(@"\\.\", "") : FriendlyName;
            return $"{name}  ·  {Bounds.Width}×{Bounds.Height}  ·  {role}";
        }
    }
}

public static class MonitorManager
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEXW
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFOEXW lpmi);

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID { public uint LowPart; public int HighPart; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RATIONAL { public uint Num, Den; }

    [StructLayout(LayoutKind.Sequential)]
    private struct PATH_SOURCE_INFO { public LUID adapterId; public uint id, modeInfoIdx, statusFlags; }

    [StructLayout(LayoutKind.Sequential)]
    private struct PATH_TARGET_INFO
    {
        public LUID adapterId; public uint id, modeInfoIdx, outputTechnology, rotation, scaling;
        public RATIONAL refreshRate; public uint scanLineOrdering; public int targetAvailable; public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PATH_INFO { public PATH_SOURCE_INFO sourceInfo; public PATH_TARGET_INFO targetInfo; public uint flags; }

    [StructLayout(LayoutKind.Sequential, Size = 64)]
    private struct MODE_INFO { }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SOURCE_DEVICE_NAME
    {
        public uint type, size;
        public LUID adapterId;
        public uint id;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string viewGdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TARGET_DEVICE_NAME
    {
        public uint type, size;
        public LUID adapterId;
        public uint id, flags, outputTechnology;
        public ushort edidManufactureId, edidProductCodeId;
        public uint connectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string monitorFriendlyDeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string monitorDevicePath;
    }

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPath, out uint numMode);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(uint flags, ref uint numPath, [Out] PATH_INFO[] paths, ref uint numMode, [Out] MODE_INFO[] modes, IntPtr topo);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref SOURCE_DEVICE_NAME req);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref TARGET_DEVICE_NAME req);

    private static Dictionary<string, string> QueryMonitorFriendlyNames()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            const uint QDC_ONLY_ACTIVE_PATHS = 2;
            if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out uint np, out uint nm) == 0 && np > 0)
            {
                var paths = new PATH_INFO[np];
                var modes = new MODE_INFO[nm];
                if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref np, paths, ref nm, modes, IntPtr.Zero) == 0)
                {
                    for (int i = 0; i < np; i++)
                    {
                        var sn = new SOURCE_DEVICE_NAME
                        {
                            type = 1,
                            size = (uint)Marshal.SizeOf<SOURCE_DEVICE_NAME>(),
                            adapterId = paths[i].sourceInfo.adapterId,
                            id = paths[i].sourceInfo.id
                        };
                        var tn = new TARGET_DEVICE_NAME
                        {
                            type = 2,
                            size = (uint)Marshal.SizeOf<TARGET_DEVICE_NAME>(),
                            adapterId = paths[i].targetInfo.adapterId,
                            id = paths[i].targetInfo.id
                        };
                        if (DisplayConfigGetDeviceInfo(ref sn) == 0 &&
                            DisplayConfigGetDeviceInfo(ref tn) == 0 &&
                            !string.IsNullOrWhiteSpace(sn.viewGdiDeviceName) &&
                            !string.IsNullOrWhiteSpace(tn.monitorFriendlyDeviceName))
                        {
                            map[sn.viewGdiDeviceName] = tn.monitorFriendlyDeviceName.Trim();
                        }
                    }
                }
            }
        }
        catch { }
        return map;
    }

    private static Dictionary<string, string> _cachedFriendlyNames = new(StringComparer.OrdinalIgnoreCase);
    private static string _lastTopologySignature = "";

    public static List<MonitorDescriptor> GetAllMonitors(bool forceRefreshFriendlyNames = false)
    {
        var rawList = new List<(IntPtr Handle, string DeviceName, bool IsPrimary, Rectangle Bounds)>(4);
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMon, IntPtr hdc, ref RECT rc, IntPtr data) =>
        {
            var mi = new MONITORINFOEXW { cbSize = Marshal.SizeOf<MONITORINFOEXW>() };
            if (GetMonitorInfoW(hMon, ref mi))
            {
                int w = mi.rcMonitor.Right - mi.rcMonitor.Left;
                int h = mi.rcMonitor.Bottom - mi.rcMonitor.Top;
                if (w > 0 && h > 0)
                {
                    rawList.Add((hMon, mi.szDevice, (mi.dwFlags & 1u) != 0, new Rectangle(mi.rcMonitor.Left, mi.rcMonitor.Top, w, h)));
                }
            }
            return true;
        }, IntPtr.Zero);

        // IMPORTANT: QueryDisplayConfig acquires a global win32k/dxgkrnl display lock in DWM!
        // NEVER call QueryMonitorFriendlyNames() unless the monitor topology actually changed or UI requested refresh!
        string sig = string.Join("|", rawList.Select(r => $"{r.DeviceName}:{r.Bounds.Width}x{r.Bounds.Height}"));
        if (forceRefreshFriendlyNames || !string.Equals(sig, _lastTopologySignature, StringComparison.Ordinal))
        {
            _lastTopologySignature = sig;
            _cachedFriendlyNames = QueryMonitorFriendlyNames();
        }

        var list = new List<MonitorDescriptor>(rawList.Count);
        foreach (var r in rawList)
        {
            _cachedFriendlyNames.TryGetValue(r.DeviceName, out string? fn);
            list.Add(new MonitorDescriptor
            {
                Handle = r.Handle,
                DeviceName = r.DeviceName,
                FriendlyName = fn ?? "",
                IsPrimary = r.IsPrimary,
                Bounds = r.Bounds
            });
        }
        return list;
    }

    public static MonitorDescriptor? SelectTargetMonitor(string targetDeviceSetting)
    {
        var monitors = GetAllMonitors();
        if (monitors.Count == 0)
            return null;

        if (!string.IsNullOrWhiteSpace(targetDeviceSetting) &&
            !string.Equals(targetDeviceSetting, "AUTO_SECONDARY", StringComparison.OrdinalIgnoreCase))
        {
            var exact = monitors.FirstOrDefault(m =>
                string.Equals(m.DeviceName, targetDeviceSetting, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(m.FriendlyName, targetDeviceSetting, StringComparison.OrdinalIgnoreCase));
            if (exact != null)
                return exact;

            // Never fall back to primary monitor when a secondary stream screen is temporarily disconnected!
            return monitors.FirstOrDefault(m => !m.IsPrimary);
        }

        // Prefer first non-primary monitor; return null (0% GPU standby) if all secondaries are disconnected
        return monitors.FirstOrDefault(m => !m.IsPrimary);
    }
}
