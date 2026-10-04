// ============================================================================
//  MonitorTray — выключение/включение отдельных мониторов из системного трея.
//  Использует Windows Display Config API (QueryDisplayConfig / SetDisplayConfig),
//  т.е. тот же механизм, что и «Параметры экрана» Windows — работает без DDC/CI.
//  Совместимо с компилятором C# 5 (.NET Framework 4.x).
// ============================================================================
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using System.Management;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

using System.Reflection;
[assembly: AssemblyTitle("MonitorTray")]
[assembly: AssemblyDescription("Turn individual monitors on/off from the Windows tray (no DDC/CI needed)")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("MonitorTray project")]
[assembly: AssemblyProduct("MonitorTray")]
[assembly: AssemblyCopyright("Copyright (c) 2026 MonitorTray contributors (MIT)")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]
[assembly: AssemblyVersion("1.3.0.0")]
[assembly: AssemblyFileVersion("1.3.0.0")]

namespace MonitorTray
{
    // ------------------------------------------------------------------ native
    internal static class Native
    {
        public const uint QDC_ALL_PATHS = 0x1;
        public const uint QDC_ONLY_ACTIVE_PATHS = 0x2;

        public const uint SDC_TOPOLOGY_INTERNAL = 0x1;
        public const uint SDC_TOPOLOGY_CLONE = 0x2;
        public const uint SDC_TOPOLOGY_EXTEND = 0x4;
        public const uint SDC_TOPOLOGY_EXTERNAL = 0x8;

        public const uint SDC_USE_SUPPLIED_DISPLAY_CONFIG = 0x20;
        public const uint SDC_APPLY = 0x80;
        public const uint SDC_NO_OPTIMIZATION = 0x100;
        public const uint SDC_SAVE_TO_DATABASE = 0x200;
        public const uint SDC_ALLOW_PATH_ORDER_CHANGES = 0x1000;
        public const uint SDC_VIRTUAL_MODE_AWARE = 0x2000;

        public const uint MODE_IDX_INVALID = 0xFFFFFFFF;

        [StructLayout(LayoutKind.Sequential)]
        public struct LUID { public uint LowPart; public int HighPart; }

        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_RATIONAL { public uint Numerator; public uint Denominator; }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINTL { public int x; public int y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_PATH_SOURCE_INFO
        {
            public LUID adapterId;
            public uint id;
            public uint modeInfoIdx;
            public uint statusFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_PATH_TARGET_INFO
        {
            public LUID adapterId;
            public uint id;
            public uint modeInfoIdx;
            public uint outputTechnology;
            public uint rotation;
            public uint scaling;
            public DISPLAYCONFIG_RATIONAL refreshRate;
            public uint scanLineOrdering;
            public int targetAvailable;
            public uint statusFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_PATH_INFO
        {
            public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
            public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
            public uint flags;
        }

        // объединение source/target mode передаётся «как есть» (48 байт)
        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_MODE_INFO
        {
            public uint infoType;
            public uint id;
            public LUID adapterId;
            public UInt64 u0, u1, u2, u3, u4, u5;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_DEVICE_INFO_HEADER
        {
            public uint type;   // 1 = GET_SOURCE_NAME, 2 = GET_TARGET_NAME, 4 = GET_ADAPTER_NAME
            public uint size;
            public LUID adapterId;
            public uint id;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct DISPLAYCONFIG_SOURCE_NAME
        {
            public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string sourceGdiDeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string viewGdiSourceDeviceName;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct DISPLAYCONFIG_TARGET_NAME
        {
            public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string monitorFriendlyDeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string monitorDevicePath;
            public uint outputTechnology;
            public ushort edidManufactureId;
            public ushort edidProductCodeId;
            public uint connectorInstance;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct DISPLAYCONFIG_ADAPTER_NAME
        {
            public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string adapterDeviceName;
        }

        [DllImport("user32.dll")]
        public static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

        [DllImport("user32.dll")]
        public static extern int QueryDisplayConfig(uint flags, ref uint numPathArrayElements,
            [In, Out] DISPLAYCONFIG_PATH_INFO[] pathArray, ref uint numModeInfoArrayElements,
            [In, Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray, out uint topologyId);

        // На некоторых системах QueryDisplayConfig возвращает ERROR_INVALID_PARAMETER,
        // если topologyId != NULL — поэтому всегда передаём NULL.
        [DllImport("user32.dll", EntryPoint = "QueryDisplayConfig")]
        public static extern int QueryDisplayConfigNoTopo(uint flags, ref uint numPathArrayElements,
            [In, Out] DISPLAYCONFIG_PATH_INFO[] pathArray, ref uint numModeInfoArrayElements,
            [In, Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray, IntPtr topologyId);

        [DllImport("user32.dll")]
        public static extern int SetDisplayConfig(uint numPathArrayElements,
            DISPLAYCONFIG_PATH_INFO[] pathArray, uint numModeInfoArrayElements,
            DISPLAYCONFIG_MODE_INFO[] modeInfoArray, uint flags);

        [DllImport("user32.dll")]
        public static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SOURCE_NAME requestPacket);

        [DllImport("user32.dll")]
        public static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_TARGET_NAME requestPacket);

        [DllImport("user32.dll")]
        public static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_ADAPTER_NAME requestPacket);

        [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
        public static extern int DisplayConfigGetDeviceInfoRaw(IntPtr requestPacket);

        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam,
            uint fuFlags, uint uTimeout, out IntPtr result);

        // ---- ChangeDisplaySettingsEx (запасной вариант) ----
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public ushort dmSpecVersion;
            public ushort dmDriverVersion;
            public ushort dmSize;
            public ushort dmDriverExtra;
            public uint dmFields;
            public POINTL dmPosition;
            public uint dmDisplayOrientation;
            public uint dmDisplayFixedOutput;
            public short dmColor;
            public short dmDuplex;
            public short dmYResolution;
            public short dmTTOption;
            public short dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public ushort dmLogPixels;
            public uint dmBitsPerPel;
            public uint dmPelsWidth;
            public uint dmPelsHeight;
            public uint dmDisplayFlags;
            public uint dmDisplayFrequency;
            public uint dmICMMethod;
            public uint dmICMIntent;
            public uint dmMediaType;
            public uint dmDitherType;
            public uint dmReserved1;
            public uint dmReserved2;
            public uint dmPanningWidth;
            public uint dmPanningHeight;
        }

        public const uint ENUM_CURRENT_SETTINGS = 0xFFFFFFFF;
        public const uint ENUM_REGISTRY_SETTINGS = 0xFFFFFFFE;
        public const uint CDS_UPDATEREGISTRY = 0x1;
        public const uint CDS_NORESET = 0x1000000;
        public const uint DM_POSITION = 0x20;
        public const uint DM_PELSWIDTH = 0x80000;
        public const uint DM_PELSHEIGHT = 0x100000;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern bool EnumDisplaySettingsEx(string lpszDeviceName, uint iModeNum, ref DEVMODE lpDevMode, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int ChangeDisplaySettingsEx(string lpszDeviceName, ref DEVMODE lpDevMode, IntPtr hwnd, uint dwflags, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int ChangeDisplaySettingsEx(string lpszDeviceName, IntPtr lpDevMode, IntPtr hwnd, uint dwflags, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct DISPLAY_DEVICE
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
            public uint StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
        }

        public const uint DISPLAY_DEVICE_MIRRORING_DRIVER = 0x8;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern bool EnumDisplayDevices(string lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

        // ---- окна: запоминание/восстановление позиций при вкл/выкл мониторов ----
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        public struct WINDOWPLACEMENT
        {
            public int length;
            public int flags;
            public int showCmd;            // 2 = свёрнуто, 3 = развёрнуто
            public POINTL ptMinPosition;
            public POINTL ptMaxPosition;
            public RECT rcNormalPosition;
        }

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern uint GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        public static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

        [DllImport("user32.dll")]
        public static extern bool SetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("dwmapi.dll")]
        public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int attrValue, int attrSize);

        // ---- яркость мониторов через DDC/CI (Windows Monitor Configuration API) ----
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct PHYSICAL_MONITOR
        {
            public IntPtr hPhysicalMonitor;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szPhysicalMonitorDescription;
        }

        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromPoint(Point pt, uint dwFlags);

        [DllImport("dxva2.dll", CharSet = CharSet.Unicode)]
        public static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, out uint pdwNumberOfPhysicalMonitors);

        [DllImport("dxva2.dll", CharSet = CharSet.Unicode)]
        public static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, uint dwPhysicalMonitorArraySize, [Out] PHYSICAL_MONITOR[] lpPhysicalMonitorArray);

        [DllImport("dxva2.dll")]
        public static extern bool GetMonitorBrightness(IntPtr hMonitor, out uint pdwMinimumBrightness, out uint pdwCurrentBrightness, out uint pdwMaximumBrightness);

        [DllImport("dxva2.dll")]
        public static extern bool SetMonitorBrightness(IntPtr hMonitor, uint dwNewBrightness);

        [DllImport("dxva2.dll")]
        public static extern bool DestroyPhysicalMonitor(IntPtr hMonitor);

        public static int Query(uint flags, out DISPLAYCONFIG_PATH_INFO[] paths, out DISPLAYCONFIG_MODE_INFO[] modes)
        {
            paths = new DISPLAYCONFIG_PATH_INFO[0];
            modes = new DISPLAYCONFIG_MODE_INFO[0];
            uint np, nm;
            int hr = GetDisplayConfigBufferSizes(flags, out np, out nm);
            if (hr != 0) return hr;
            for (int attempt = 0; attempt < 6; attempt++)
            {
                DISPLAYCONFIG_PATH_INFO[] p = new DISPLAYCONFIG_PATH_INFO[np];
                DISPLAYCONFIG_MODE_INFO[] m = new DISPLAYCONFIG_MODE_INFO[nm];
                uint pc = np, mc = nm;
                hr = QueryDisplayConfigNoTopo(flags, ref pc, p, ref mc, m, IntPtr.Zero);
                if (hr == 0)
                {
                    DISPLAYCONFIG_PATH_INFO[] pt = new DISPLAYCONFIG_PATH_INFO[pc];
                    Array.Copy(p, pt, (int)pc);
                    DISPLAYCONFIG_MODE_INFO[] mt = new DISPLAYCONFIG_MODE_INFO[mc];
                    Array.Copy(m, mt, (int)mc);
                    paths = pt; modes = mt;
                    return 0;
                }
                if (hr == 122) { np *= 2; nm *= 2; continue; } // ERROR_INSUFFICIENT_BUFFER
                return hr;
            }
            return hr;
        }

        public static void DpmsOffAll()
        {
            IntPtr r;
            SendMessageTimeout((IntPtr)0xFFFF /*HWND_BROADCAST*/, 0x0112 /*WM_SYSCOMMAND*/,
                (IntPtr)0xF170 /*SC_MONITORPOWER*/, (IntPtr)2, 2 /*SMTO_ABORTIFHUNG*/, 1500, out r);
        }
    }

    // ------------------------------------------------------------------ модель
    internal class Mon
    {
        public Native.LUID AdapterId;
        public uint TargetId;
        public uint SourceId;
        public string Name = "";
        public string DevicePath = "";
        public string GdiName = "";
        public bool Active;
        public bool Primary;
        public Rectangle Bounds = Rectangle.Empty;
        public int PosX = int.MinValue;
        public int PosY = int.MinValue;
        public int PosX2 = int.MinValue;
        public int PosY2 = int.MinValue;
    }

    // ------------------------------------------------------------------ сервис
    internal static class Svc
    {
        public static string LastError = "";

        // Windows 10 2004+ / WDDM 2.7+: драйверы работают в режиме виртуальных режимов.
        // Без SDC_VIRTUAL_MODE_AWARE QueryDisplayConfig возвращает «переведённые» пути,
        // по которым DisplayConfigGetDeviceInfo возвращает ERROR_INVALID_PARAMETER.
        static bool _virtual;
        static bool _probed;

        public static void Probe()
        {
            Native.DISPLAYCONFIG_PATH_INFO[] p;
            Native.DISPLAYCONFIG_MODE_INFO[] m;
            _virtual = Native.Query(Native.QDC_ONLY_ACTIVE_PATHS | Native.SDC_VIRTUAL_MODE_AWARE, out p, out m) == 0;
            _probed = true;
        }

        static int QueryEx(uint flags, out Native.DISPLAYCONFIG_PATH_INFO[] paths, out Native.DISPLAYCONFIG_MODE_INFO[] modes)
        {
            if (_virtual)
                return Native.Query(flags | Native.SDC_VIRTUAL_MODE_AWARE, out paths, out modes);
            return Native.Query(flags, out paths, out modes);
        }

        public static bool IsVirtual { get { return _virtual; } }

        static string Key(Native.DISPLAYCONFIG_PATH_INFO p)
        {
            return p.targetInfo.adapterId.LowPart + "|" + p.targetInfo.adapterId.HighPart + "|" + p.targetInfo.id;
        }

        static bool SameLuid(Native.LUID a, Native.LUID b)
        {
            return a.LowPart == b.LowPart && a.HighPart == b.HighPart;
        }

        static bool SameTarget(Native.DISPLAYCONFIG_PATH_INFO p, Mon m)
        {
            return SameLuid(p.targetInfo.adapterId, m.AdapterId) && p.targetInfo.id == m.TargetId;
        }

        static string GetTargetName(Native.LUID adapter, uint targetId, out string devicePath)
        {
            devicePath = null;
            try
            {
                Native.DISPLAYCONFIG_TARGET_NAME tn = new Native.DISPLAYCONFIG_TARGET_NAME();
                tn.header.type = 2; // DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME
                tn.header.size = (uint)Marshal.SizeOf(typeof(Native.DISPLAYCONFIG_TARGET_NAME));
                tn.header.adapterId = adapter;
                tn.header.id = targetId;
                if (Native.DisplayConfigGetDeviceInfo(ref tn) != 0) return null;
                devicePath = tn.monitorDevicePath;
                string n = tn.monitorFriendlyDeviceName;
                if (n != null) n = n.Trim();
                return n;
            }
            catch { return null; }
        }

        static string GetSourceName(Native.LUID adapter, uint sourceId)
        {
            try
            {
                Native.DISPLAYCONFIG_SOURCE_NAME sn = new Native.DISPLAYCONFIG_SOURCE_NAME();
                sn.header.type = 1; // DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME
                sn.header.size = (uint)Marshal.SizeOf(typeof(Native.DISPLAYCONFIG_SOURCE_NAME));
                sn.header.adapterId = adapter;
                sn.header.id = sourceId;
                if (Native.DisplayConfigGetDeviceInfo(ref sn) != 0) return null;
                return sn.sourceGdiDeviceName;
            }
            catch { return null; }
        }

        static int GdiNum(string gdiName)
        {
            if (string.IsNullOrEmpty(gdiName)) return 999;
            int k = gdiName.LastIndexOf('Y');
            int n;
            if (k >= 0 && int.TryParse(gdiName.Substring(k + 1), out n)) return n;
            return 999;
        }

        // Имена мониторов через WMI (WmiMonitorID): InstanceName содержит UID<targetId>,
        // по нему имя сопоставляется с CCD-таргетом. Работает и с выключенными мониторами.
        static Dictionary<string, string> LoadWmiNames()
        {
            Dictionary<string, string> res = new Dictionary<string, string>();
            try
            {
                using (ManagementObjectSearcher s = new ManagementObjectSearcher(
                    @"root\wmi", "SELECT InstanceName, UserFriendlyName FROM WmiMonitorID"))
                using (ManagementObjectCollection results = s.Get())
                {
                    foreach (ManagementBaseObject mo in results)
                    {
                        string inst = mo["InstanceName"] as string;
                        object uf = mo["UserFriendlyName"];
                        if (inst == null || !(uf is ushort[])) continue;
                        StringBuilder sb = new StringBuilder();
                        foreach (ushort c in (ushort[])uf) { if (c == 0) break; sb.Append((char)c); }
                        string name = sb.ToString().Trim();
                        if (name.Length == 0) continue;
                        int k = inst.IndexOf("UID", StringComparison.OrdinalIgnoreCase);
                        if (k < 0) continue;
                        string uid = inst.Substring(k + 3);
                        int us = uid.IndexOf('_');
                        if (us >= 0) uid = uid.Substring(0, us);
                        res[uid] = name;
                    }
                }
            }
            catch { }
            return res;
        }

        // Список мониторов: сначала активные (по номеру \\.\DISPLAYn), затем выключенные
        public static List<Mon> List()
        {
            List<Mon> list = new List<Mon>();
            Native.DISPLAYCONFIG_PATH_INFO[] ap, allp;
            Native.DISPLAYCONFIG_MODE_INFO[] am, allm;
            if (!_probed) { Probe(); _probed = true; }
            int qhr = QueryEx(Native.QDC_ONLY_ACTIVE_PATHS, out ap, out am);
            if (qhr != 0) { LastError = "QueryDisplayConfig(active) -> 0x" + qhr.ToString("X"); return list; }
            QueryEx(Native.QDC_ALL_PATHS, out allp, out allm);

            HashSet<string> activeKeys = new HashSet<string>();
            foreach (Native.DISPLAYCONFIG_PATH_INFO p in ap)
            {
                Mon m = new Mon();
                m.AdapterId = p.targetInfo.adapterId;
                m.TargetId = p.targetInfo.id;
                m.SourceId = p.sourceInfo.id;
                m.Active = true;
                string path;
                m.Name = GetTargetName(m.AdapterId, m.TargetId, out path);
                m.DevicePath = path != null ? path : "";
                m.GdiName = GetSourceName(p.sourceInfo.adapterId, p.sourceInfo.id);
                if (m.GdiName == null) m.GdiName = "";
                // Позиция исходного режима (для сопоставления с GDI-экранами).
                // Наблюдаемая раскладка SOURCE-режима: size(8), pixelFormat(4), pos.x(4), pos.y(4);
                // классическая — position(8) первой. Пробуем обе.
                uint sidx = _virtual ? (p.sourceInfo.modeInfoIdx & 0xFFFF) : p.sourceInfo.modeInfoIdx;
                if (sidx != Native.MODE_IDX_INVALID && sidx < am.Length && am[sidx].infoType == 1)
                {
                    m.PosX = (int)(am[sidx].u1 >> 32);
                    m.PosY = (int)(am[sidx].u2 & 0xFFFFFFFF);
                    m.PosX2 = (int)(am[sidx].u0 & 0xFFFFFFFF);
                    m.PosY2 = (int)(am[sidx].u0 >> 32);
                }
                activeKeys.Add(Key(p));
                list.Add(m);
            }

            if (allp != null)
            {
                foreach (Native.DISPLAYCONFIG_PATH_INFO p in allp)
                {
                    if (activeKeys.Contains(Key(p))) continue;
                    if (p.targetInfo.targetAvailable == 0) continue; // физически не подключён
                    bool dup = false;
                    foreach (Mon x in list)
                        if (SameLuid(x.AdapterId, p.targetInfo.adapterId) && x.TargetId == p.targetInfo.id) dup = true;
                    if (dup) continue;

                    Mon m = new Mon();
                    m.AdapterId = p.targetInfo.adapterId;
                    m.TargetId = p.targetInfo.id;
                    m.SourceId = p.sourceInfo.id;
                    m.Active = false;
                    string path;
                    m.Name = GetTargetName(m.AdapterId, m.TargetId, out path);
                    m.DevicePath = path != null ? path : "";
                    m.GdiName = GetSourceName(p.sourceInfo.adapterId, p.sourceInfo.id);
                    if (m.GdiName == null) m.GdiName = "";
                    list.Add(m);
                }
            }

            // Сопоставление с GDI-экранами: по позиции исходного режима (два варианта раскладки)
            foreach (Mon m in list)
            {
                if (!m.Active) continue;
                foreach (Screen s in Screen.AllScreens)
                {
                    bool hitA = s.Bounds.X == m.PosX && s.Bounds.Y == m.PosY;
                    bool hitB = s.Bounds.X == m.PosX2 && s.Bounds.Y == m.PosY2;
                    if (hitA || hitB)
                    {
                        m.GdiName = s.DeviceName;
                        m.Primary = s.Primary;
                        m.Bounds = s.Bounds;
                        break;
                    }
                }
            }

            // Выключенным мониторам — оставшиеся GDI-устройства (по остаточному принципу)
            List<GdiEntry> gdis = EnumGdi();
            HashSet<string> used = new HashSet<string>();
            foreach (Mon m in list) if (m.GdiName.Length > 0) used.Add(m.GdiName);
            List<GdiEntry> leftovers = new List<GdiEntry>();
            foreach (GdiEntry d in gdis)
                if (!used.Contains(d.Dev) && (d.Flags & 0x08000000) == 0) leftovers.Add(d);
            List<Mon> disabled = list.FindAll(delegate(Mon x) { return !x.Active && x.GdiName.Length == 0; });
            if (leftovers.Count == disabled.Count)
                for (int i = 0; i < disabled.Count; i++)
                    disabled[i].GdiName = leftovers[i].Dev;

            // выключенным мониторам, которых не нашли выше — GDI-имя из сохранённой карты
            Dictionary<string, string> monMap = LoadMonMap();
            foreach (Mon m2 in list)
            {
                if (m2.Active || m2.GdiName.Length > 0) continue;
                string g;
                if (monMap.TryGetValue(MapKey(m2), out g) && g.Length > 0) m2.GdiName = g;
            }

            // Человеческие имена мониторов: реестр (EDID) либо DeviceString
            foreach (GdiEntry d in gdis)
            {
                if (!d.HasMonitor) continue;
                string friendly = FriendlyFromDeviceId(d.MonId);
                if (friendly == null || friendly.Length == 0)
                {
                    string ds = d.MonStr != null ? d.MonStr.Trim() : "";
                    bool generic = ds.Length == 0 || ds.StartsWith("Generic") || ds.StartsWith("Универсальный");
                    if (!generic) friendly = ds;
                }
                if (friendly == null || friendly.Length == 0) continue;
                foreach (Mon m in list)
                    if (m.GdiName == d.Dev && (m.Name == null || m.Name.Trim().Length == 0))
                        m.Name = friendly;
            }

            // Имена из WMI (по UID = targetId)
            Dictionary<string, string> wmiNames = LoadWmiNames();
            foreach (Mon m in list)
            {
                if (m.Name != null && m.Name.Trim().Length > 0) continue;
                string wn;
                if (wmiNames.TryGetValue(m.TargetId.ToString(), out wn) && wn.Length > 0) m.Name = wn;
            }

            list.Sort(delegate(Mon a, Mon b)
            {
                if (a.Active != b.Active) return a.Active ? -1 : 1;
                return GdiNum(a.GdiName).CompareTo(GdiNum(b.GdiName));
            });

            int seq = 0;
            foreach (Mon m in list)
            {
                if (m.Name == null || m.Name.Trim().Length == 0)
                {
                    int n = GdiNum(m.GdiName);
                    m.Name = string.Format(Loc.Get("monitor_n"), n != 999 ? n.ToString() : (++seq).ToString());
                }
            }
            return list;
        }

        public class GdiEntry
        {
            public string Dev;
            public uint Flags;
            public bool HasMonitor;
            public string MonId;
            public string MonStr;
            public string MonKey;
        }

        public static List<GdiEntry> EnumGdi()
        {
            List<GdiEntry> res = new List<GdiEntry>();
            Native.DISPLAY_DEVICE dd = new Native.DISPLAY_DEVICE();
            dd.cb = Marshal.SizeOf(typeof(Native.DISPLAY_DEVICE));
            for (uint i = 0; i < 32; i++)
            {
                if (!Native.EnumDisplayDevices(null, i, ref dd, 0)) break;
                if ((dd.StateFlags & Native.DISPLAY_DEVICE_MIRRORING_DRIVER) != 0) { dd.cb = Marshal.SizeOf(typeof(Native.DISPLAY_DEVICE)); continue; }
                GdiEntry e = new GdiEntry();
                e.Dev = dd.DeviceName;
                e.Flags = dd.StateFlags;
                Native.DISPLAY_DEVICE mon = new Native.DISPLAY_DEVICE();
                mon.cb = Marshal.SizeOf(typeof(Native.DISPLAY_DEVICE));
                if (Native.EnumDisplayDevices(dd.DeviceName, 0, ref mon, 0))
                {
                    e.HasMonitor = true;
                    e.MonId = mon.DeviceID;
                    e.MonStr = mon.DeviceString;
                    e.MonKey = mon.DeviceKey;
                }
                res.Add(e);
                dd.cb = Marshal.SizeOf(typeof(Native.DISPLAY_DEVICE));
            }
            return res;
        }

        // DeviceID вида MONITOR\DEL41D9\{GUID}\0012 -> читаемое имя из HKLM\Enum\...
        static string FriendlyFromDeviceId(string deviceId)
        {
            if (deviceId == null || deviceId.Length < 8) return null;
            try
            {
                string keyPath = @"SYSTEM\CurrentControlSet\Enum\" + deviceId;
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(keyPath))
                {
                    if (k == null) return null;
                    string fn = k.GetValue("FriendlyName") as string;
                    if (fn != null && fn.Trim().Length > 0) return fn.Trim();
                    string dd = k.GetValue("DeviceDesc") as string;
                    if (dd != null)
                    {
                        int sp = dd.LastIndexOf(';');
                        if (sp >= 0 && sp + 1 < dd.Length) return dd.Substring(sp + 1).Trim();
                        return dd.Trim();
                    }
                }
            }
            catch { }
            return null;
        }

        static uint Offset16(uint v, uint offset, bool offsetHighToo)
        {
            uint low = v & 0xFFFF;
            uint high = (v >> 16) & 0xFFFF;
            if (low != 0xFFFF) low += offset;
            if (offsetHighToo && high != 0xFFFF) high += offset;
            return (high << 16) | low;
        }

        static int TrySet(Native.DISPLAYCONFIG_PATH_INFO[] paths, Native.DISPLAYCONFIG_MODE_INFO[] modes, bool verbose)
        {
            uint vf = _virtual ? Native.SDC_VIRTUAL_MODE_AWARE : 0;
            // Комбинации с SDC_NO_OPTIMIZATION идут первыми: Windows вносит
            // минимальные изменения и не пере-применяет нетронутые выходы,
            // поэтому оставшийся монитор не мигает.
            uint[] flagSets = new uint[] {
                Native.SDC_APPLY | Native.SDC_USE_SUPPLIED_DISPLAY_CONFIG | Native.SDC_NO_OPTIMIZATION | Native.SDC_ALLOW_PATH_ORDER_CHANGES | vf,
                Native.SDC_APPLY | Native.SDC_NO_OPTIMIZATION | Native.SDC_ALLOW_PATH_ORDER_CHANGES | vf,
                Native.SDC_APPLY | Native.SDC_USE_SUPPLIED_DISPLAY_CONFIG | vf,
                Native.SDC_APPLY | Native.SDC_USE_SUPPLIED_DISPLAY_CONFIG | Native.SDC_SAVE_TO_DATABASE | vf,
                Native.SDC_APPLY | Native.SDC_NO_OPTIMIZATION | vf
            };
            int hr = -1;
            foreach (uint f in flagSets)
            {
                hr = Native.SetDisplayConfig((uint)paths.Length, paths, (uint)modes.Length, modes, f);
                if (verbose) Console.Error.WriteLine("  SetDisplayConfig(flags=0x" + f.ToString("X") + ") -> 0x" + hr.ToString("X"));
                if (hr == 0) return 0;
            }
            return hr;
        }

        public static int Disable(Mon m, bool verbose)
        {
            LastError = "";
            if (!_probed) Probe();
            SaveCurrentMode(m); // запомнить текущий режим, чтобы вернуть его при включении
            SaveMonMap(m);       // запомнить GDI-имя монитора (после выключения его сложно вычислить)
            WinSnap.Snapshot(); // запомнить позиции окон, чтобы вернуть их при включении
            Native.DISPLAYCONFIG_PATH_INFO[] ap;
            Native.DISPLAYCONFIG_MODE_INFO[] am;
            int hr = QueryEx(Native.QDC_ONLY_ACTIVE_PATHS, out ap, out am);
            if (hr != 0) { LastError = "QueryDisplayConfig: 0x" + hr.ToString("X"); return hr; }

            if (ap.Length <= 1) { LastError = "нельзя выключить единственный активный монитор"; return -1; }

            int idx = -1;
            for (int i = 0; i < ap.Length; i++) if (SameTarget(ap[i], m)) idx = i;
            if (idx < 0) { LastError = "монитор уже выключен или не найден"; return -1; }

            // Важно: массив режимов передаём без изменений (без переиндексации) —
            // на некоторых системах переиндексированные режимы отвергаются с 0x57.
            Native.DISPLAYCONFIG_PATH_INFO[] np = new Native.DISPLAYCONFIG_PATH_INFO[ap.Length - 1];
            int k = 0;
            for (int i = 0; i < ap.Length; i++) if (i != idx) np[k++] = ap[i];

            // Если выключаем основной монитор — оставшийся должен встать в (0,0).
            // Раскладка SOURCE-режима: u0=размер, u1.lo=формат пикселей, u1.hi=x, u2.lo=y.
            if (m.Primary)
            {
                for (int i = 0; i < np.Length; i++)
                {
                    uint sidx = _virtual ? (np[i].sourceInfo.modeInfoIdx & 0xFFFF) : np[i].sourceInfo.modeInfoIdx;
                    if (sidx != Native.MODE_IDX_INVALID && sidx < am.Length && am[sidx].infoType == 1)
                    {
                        am[sidx].u1 &= 0xFFFFFFFF;                 // x = 0
                        am[sidx].u2 &= 0xFFFFFFFF00000000;         // y = 0
                        break;
                    }
                }
            }
            hr = TrySet(np, am, verbose);

            if (hr != 0 && m.GdiName.Length > 0)
            {
                if (verbose) Console.Error.WriteLine("  CCD failed, trying ChangeDisplaySettingsEx detach…");
                hr = DisableCds(m.GdiName);
            }
            if (hr != 0) LastError = "SetDisplayConfig: 0x" + hr.ToString("X");
            return hr;
        }

        // ---- соответствие монитор (adapter|target) -> GDI-имя, живёт в файле ----
        static string MonMapFile()
        {
            return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MonitorTray_monmap.txt");
        }

        static string MapKey(Mon m)
        {
            return m.AdapterId.LowPart + "|" + m.AdapterId.HighPart + "|" + m.TargetId;
        }

        static Dictionary<string, string> LoadMonMap()
        {
            Dictionary<string, string> map = new Dictionary<string, string>();
            try
            {
                if (!System.IO.File.Exists(MonMapFile())) return map;
                foreach (string line in System.IO.File.ReadAllLines(MonMapFile()))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    map[line.Substring(0, eq)] = line.Substring(eq + 1);
                }
            }
            catch { }
            return map;
        }

        static void SaveMonMap(Mon m)
        {
            try
            {
                if (m == null || m.GdiName == null || m.GdiName.Length == 0) return;
                Dictionary<string, string> map = LoadMonMap();
                map[MapKey(m)] = m.GdiName;
                List<string> lines = new List<string>();
                foreach (KeyValuePair<string, string> kv in map) lines.Add(kv.Key + "=" + kv.Value);
                System.IO.File.WriteAllLines(MonMapFile(), lines.ToArray());
            }
            catch { }
        }

        // ---- синтез режимов: новые драйверы не хранят режимы выключенных мониторов ----
        // TARGET-режим собирается по стандарту CVT-RB из сохранённого разрешения;
        // Windows сверит запрос с EDID и подставит точный режим монитора.
        static void GetSavedModeSize(string gdi, out uint w, out uint h, out uint f)
        {
            w = 1920; h = 1080; f = 60;
            try
            {
                string v;
                if (gdi != null && LoadModeFile().TryGetValue(gdi, out v))
                {
                    string[] pp = v.Split('|');
                    uint pw, ph, pf;
                    if (pp.Length >= 3 && uint.TryParse(pp[0], out pw) && uint.TryParse(pp[1], out ph) && uint.TryParse(pp[2], out pf)
                        && pw >= 640 && ph >= 480 && pf >= 24 && pw <= 16384 && ph <= 16384)
                    { w = pw; h = ph; f = pf; }
                }
            }
            catch { }
        }

        static Native.DISPLAYCONFIG_MODE_INFO MakeTargetMode(uint targetId, Native.LUID adapter, uint w, uint h, uint f)
        {
            uint htotal = w + 160;          // CVT-RB: горизонтальное гашение 160
            uint vtotal = h + 40;           // CVT-RB: вертикальное гашение ~40
            ulong pc = (ulong)htotal * vtotal * f;
            pc = pc / 10000 * 10000;        // кратность 10 кГц
            Native.DISPLAYCONFIG_MODE_INFO mi = new Native.DISPLAYCONFIG_MODE_INFO();
            mi.infoType = 2;                // TARGET
            mi.id = targetId;
            mi.adapterId = adapter;
            mi.u0 = pc;                                              // pixelRate
            mi.u1 = ((ulong)(uint)pc << 32) | htotal;                // hSync: num/den
            mi.u2 = ((ulong)1000 << 32) | (f * 1000);                // vSync: num/den
            mi.u3 = ((ulong)h << 32) | w;                            // activeSize
            mi.u4 = ((ulong)vtotal << 32) | htotal;                  // totalSize
            mi.u5 = ((ulong)1 << 32);                                // progressive
            return mi;
        }

        // SOURCE-режим: наблюдаемая раскладка — u0=размер, u1.lo=формат(4=32bpp), u1.hi=x, u2.lo=y
        static Native.DISPLAYCONFIG_MODE_INFO MakeSourceMode(uint sourceId, Native.LUID adapter, uint w, uint h, int x, int y)
        {
            Native.DISPLAYCONFIG_MODE_INFO mi = new Native.DISPLAYCONFIG_MODE_INFO();
            mi.infoType = 1;                // SOURCE
            mi.id = sourceId;
            mi.adapterId = adapter;
            mi.u0 = ((ulong)h << 32) | w;
            mi.u1 = ((ulong)(uint)x << 32) | 4;
            mi.u2 = (ulong)(uint)y;
            return mi;
        }

        static bool TargetNowActive(Mon m)
        {
            Native.DISPLAYCONFIG_PATH_INFO[] ap;
            Native.DISPLAYCONFIG_MODE_INFO[] am;
            if (QueryEx(Native.QDC_ONLY_ACTIVE_PATHS, out ap, out am) != 0) return false;
            foreach (Native.DISPLAYCONFIG_PATH_INFO p in ap)
                if (SameTarget(p, m)) return true;
            return false;
        }

        // ---- запоминание/восстановление разрешения монитора ----
        static string ModeFile()
        {
            return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MonitorTray_modes.txt");
        }

        static void SaveCurrentMode(Mon m)
        {
            try
            {
                if (m.GdiName == null || m.GdiName.Length == 0) return;
                Native.DEVMODE dm;
                if (!GetDevMode(m.GdiName, Native.ENUM_CURRENT_SETTINGS, out dm)) return;
                Dictionary<string, string> all = LoadModeFile();
                all[m.GdiName] = dm.dmPelsWidth + "|" + dm.dmPelsHeight + "|" + dm.dmDisplayFrequency;
                List<string> lines = new List<string>();
                foreach (KeyValuePair<string, string> kv in all)
                    lines.Add(kv.Key + "=" + kv.Value);
                System.IO.File.WriteAllLines(ModeFile(), lines.ToArray());
            }
            catch { }
        }

        static Dictionary<string, string> LoadModeFile()
        {
            Dictionary<string, string> all = new Dictionary<string, string>();
            try
            {
                if (!System.IO.File.Exists(ModeFile())) return all;
                foreach (string line in System.IO.File.ReadAllLines(ModeFile()))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    all[line.Substring(0, eq)] = line.Substring(eq + 1);
                }
            }
            catch { }
            return all;
        }

        static void RestoreSavedMode(Mon m)
        {
            try
            {
                if (m.GdiName == null || m.GdiName.Length == 0) return;
                string v;
                if (!LoadModeFile().TryGetValue(m.GdiName, out v)) return;
                string[] p = v.Split('|');
                if (p.Length < 3) return;
                uint w, h, f;
                if (!uint.TryParse(p[0], out w) || !uint.TryParse(p[1], out h) || !uint.TryParse(p[2], out f)) return;
                Native.DEVMODE dm;
                if (!GetDevMode(m.GdiName, Native.ENUM_CURRENT_SETTINGS, out dm)) return;
                if (dm.dmPelsWidth == w && dm.dmPelsHeight == h && dm.dmDisplayFrequency == f) return;
                dm.dmFields = Native.DM_PELSWIDTH | Native.DM_PELSHEIGHT | 0x400000; // + DM_DISPLAYFREQUENCY
                dm.dmPelsWidth = w; dm.dmPelsHeight = h; dm.dmDisplayFrequency = f;
                Native.ChangeDisplaySettingsEx(m.GdiName, ref dm, IntPtr.Zero, 0x1 /*CDS_UPDATEREGISTRY*/, IntPtr.Zero);
            }
            catch { }
        }

        public static int Enable(Mon m, bool verbose)
        {
            int hr = EnableInner(m, verbose);
            if (hr == 0)
            {
                RestoreSavedMode(m);
                // окна вернуть на вернувшийся монитор — после того как рабочий стол перестроится
                string gdi = m.GdiName;
                if (GuiMode)
                {
                    Thread t = new Thread(delegate()
                    {
                        try { Thread.Sleep(1500); WinSnap.RestoreFor(gdi, verbose); }
                        catch { }
                    });
                    t.IsBackground = true;
                    t.Start();
                }
                else
                {
                    // в консольном режиме процесс живёт недолго — восстанавливаем сразу
                    try { Thread.Sleep(1500); WinSnap.RestoreFor(gdi, verbose); }
                    catch { }
                }
            }
            return hr;
        }

        public static bool GuiMode; // выставляется Program.Main перед запуском трея

        static int EnableInner(Mon m, bool verbose)
        {
            LastError = "";
            if (!_probed) Probe();
            Native.DISPLAYCONFIG_PATH_INFO[] ap, allp;
            Native.DISPLAYCONFIG_MODE_INFO[] am, allm;
            int hr = QueryEx(Native.QDC_ONLY_ACTIVE_PATHS, out ap, out am);
            if (hr != 0) { LastError = "QueryDisplayConfig: 0x" + hr.ToString("X"); return hr; }

            foreach (Native.DISPLAYCONFIG_PATH_INFO p in ap)
                if (SameTarget(p, m)) { if (verbose) Console.Error.WriteLine("  already active"); return 0; }

            // 1) Самый надёжный способ — подключить через ChangeDisplaySettingsEx
            if (m.GdiName != null && m.GdiName.Length > 0)
            {
                int hrC = EnableCds(m.GdiName);
                if (verbose) Console.Error.WriteLine("  EnableCds -> 0x" + hrC.ToString("X"));
                if (hrC == 0 && TargetNowActive(m)) return 0;
            }

            // 2) CCD: добавить путь выключенного монитора к активным
            QueryEx(Native.QDC_ALL_PATHS, out allp, out allm);
            if (allp == null) { LastError = "QueryDisplayConfig(ALL_PATHS) failed"; return -1; }

            HashSet<string> activeKeys = new HashSet<string>();
            HashSet<string> activeSources = new HashSet<string>();
            foreach (Native.DISPLAYCONFIG_PATH_INFO p in ap)
            {
                activeKeys.Add(Key(p));
                activeSources.Add(p.sourceInfo.adapterId.LowPart + "|" + p.sourceInfo.adapterId.HighPart + "|" + p.sourceInfo.id);
            }

            // кандидаты: неактивные пути с нашим таргетом; сначала те, чей source свободен
            List<int> cands = new List<int>();
            List<int> cands2 = new List<int>();
            for (int i = 0; i < allp.Length; i++)
            {
                Native.DISPLAYCONFIG_PATH_INFO p = allp[i];
                if (activeKeys.Contains(Key(p))) continue;
                if (!SameTarget(p, m)) continue;
                if (p.targetInfo.targetAvailable == 0) continue;
                string sk = p.sourceInfo.adapterId.LowPart + "|" + p.sourceInfo.adapterId.HighPart + "|" + p.sourceInfo.id;
                if (activeSources.Contains(sk)) cands2.Add(i); else cands.Add(i);
            }
            cands.AddRange(cands2);
            if (cands.Count == 0) { LastError = "путь монитора не найден"; return -1; }

            hr = -1;
            foreach (int ci in cands)
            {
                Native.DISPLAYCONFIG_PATH_INFO cand = allp[ci];
                // собрать режимы, на которые ссылается кандидат (в исходном виде)
                List<uint> need = new List<uint>();
                if (_virtual)
                {
                    uint sLow = cand.sourceInfo.modeInfoIdx & 0xFFFF;
                    uint tLow = cand.targetInfo.modeInfoIdx & 0xFFFF;
                    uint tHigh = (cand.targetInfo.modeInfoIdx >> 16) & 0xFFFF;
                    if (sLow != 0xFFFF && sLow < allm.Length) need.Add(sLow);
                    if (tLow != 0xFFFF && tLow < allm.Length) need.Add(tLow);
                    if (tHigh != 0xFFFF && tHigh < allm.Length) need.Add(tHigh);
                }
                else
                {
                    if (cand.sourceInfo.modeInfoIdx != Native.MODE_IDX_INVALID && cand.sourceInfo.modeInfoIdx < allm.Length)
                        need.Add(cand.sourceInfo.modeInfoIdx);
                    if (cand.targetInfo.modeInfoIdx != Native.MODE_IDX_INVALID && cand.targetInfo.modeInfoIdx < allm.Length)
                        need.Add(cand.targetInfo.modeInfoIdx);
                }
                List<uint> uniqNeed = new List<uint>();
                HashSet<uint> seenNeed = new HashSet<uint>();
                foreach (uint u in need) if (seenNeed.Add(u)) uniqNeed.Add(u);

                // Чего не хватает кандидату: новые драйверы не хранят режимы
                // выключенных мониторов — синтезируем их сами.
                bool srcMissing = _virtual
                    ? (cand.sourceInfo.modeInfoIdx & 0xFFFF) == 0xFFFF
                    : cand.sourceInfo.modeInfoIdx == Native.MODE_IDX_INVALID;
                bool tgtMissing = _virtual
                    ? (cand.targetInfo.modeInfoIdx & 0xFFFF) == 0xFFFF
                    : cand.targetInfo.modeInfoIdx == Native.MODE_IDX_INVALID;

                List<Native.DISPLAYCONFIG_MODE_INFO> synth = new List<Native.DISPLAYCONFIG_MODE_INFO>();
                uint sw, sh, sf;
                GetSavedModeSize(m.GdiName, out sw, out sh, out sf);
                System.Drawing.Rectangle vs0 = SystemInformation.VirtualScreen;

                Native.DISPLAYCONFIG_PATH_INFO[] mergedP = new Native.DISPLAYCONFIG_PATH_INFO[ap.Length + 1];
                Native.DISPLAYCONFIG_MODE_INFO[] mergedM = new Native.DISPLAYCONFIG_MODE_INFO[
                    am.Length + uniqNeed.Count + (srcMissing ? 1 : 0) + (tgtMissing ? 1 : 0)];
                Array.Copy(ap, mergedP, ap.Length);
                Array.Copy(am, mergedM, am.Length);
                uint off = (uint)am.Length;
                if (_virtual)
                {
                    cand.sourceInfo.modeInfoIdx = Offset16(cand.sourceInfo.modeInfoIdx, off, false);
                    cand.targetInfo.modeInfoIdx = Offset16(cand.targetInfo.modeInfoIdx, off, true);
                }
                else
                {
                    if (cand.sourceInfo.modeInfoIdx != Native.MODE_IDX_INVALID) cand.sourceInfo.modeInfoIdx += off;
                    if (cand.targetInfo.modeInfoIdx != Native.MODE_IDX_INVALID) cand.targetInfo.modeInfoIdx += off;
                }
                for (int j = 0; j < uniqNeed.Count; j++)
                    mergedM[am.Length + j] = allm[uniqNeed[j]];
                uint extraBase = (uint)(am.Length + uniqNeed.Count);

                if (srcMissing)
                {
                    synth.Add(MakeSourceMode(cand.sourceInfo.id, cand.sourceInfo.adapterId, sw, sh, vs0.Right, 0));
                    if (_virtual)
                        cand.sourceInfo.modeInfoIdx = (uint)((int)(cand.sourceInfo.modeInfoIdx & 0xFFFF0000) | (int)extraBase);
                    else
                        cand.sourceInfo.modeInfoIdx = extraBase;
                }
                if (tgtMissing)
                {
                    synth.Add(MakeTargetMode(cand.targetInfo.id, cand.targetInfo.adapterId, sw, sh, sf));
                    uint tgtIdx = extraBase + (uint)(synth.Count - 1);
                    if (_virtual)
                        cand.targetInfo.modeInfoIdx = (uint)((int)((cand.targetInfo.modeInfoIdx >> 16) << 16) | (int)tgtIdx);
                    else
                        cand.targetInfo.modeInfoIdx = tgtIdx;
                }
                for (int j = 0; j < synth.Count; j++)
                    mergedM[(int)extraBase + j] = synth[j];

                mergedP[ap.Length] = cand;

                hr = TrySet(mergedP, mergedM, verbose);
                if (hr == 0 && TargetNowActive(m)) return 0;
            }

            // Последний шанс: новые драйверы NVIDIA не сохраняют режимы выключенного
            // монитора и игнорируют пути с переиспользованными источниками. Помогает
            // путь со СВЕЖИМ source-id и полностью синтезированными режимами
            // (проверено на Win10 22H2 + драйвер NVIDIA 580.x).
            if (!TargetNowActive(m))
            {
                uint sw2, sh2, sf2;
                GetSavedModeSize(m.GdiName, out sw2, out sh2, out sf2);
                System.Drawing.Rectangle vs1 = SystemInformation.VirtualScreen;
                foreach (int ci in cands)
                {
                    Native.DISPLAYCONFIG_PATH_INFO base1 = allp[ci];

                    Native.DISPLAYCONFIG_PATH_INFO np2 = new Native.DISPLAYCONFIG_PATH_INFO();
                    np2.sourceInfo.adapterId = base1.targetInfo.adapterId;
                    np2.sourceInfo.id = base1.targetInfo.id; // свежий id источника
                    np2.targetInfo = base1.targetInfo;
                    np2.targetInfo.modeInfoIdx = Native.MODE_IDX_INVALID;
                    np2.flags = 1;

                    Native.DISPLAYCONFIG_PATH_INFO[] mergedP2 = new Native.DISPLAYCONFIG_PATH_INFO[ap.Length + 1];
                    Array.Copy(ap, mergedP2, ap.Length);
                    Native.DISPLAYCONFIG_MODE_INFO[] mergedM2 = new Native.DISPLAYCONFIG_MODE_INFO[am.Length + 2];
                    Array.Copy(am, mergedM2, am.Length);
                    mergedM2[am.Length] = MakeSourceMode(np2.sourceInfo.id, np2.sourceInfo.adapterId, sw2, sh2, vs1.Right, 0);
                    mergedM2[am.Length + 1] = MakeTargetMode(np2.targetInfo.id, np2.targetInfo.adapterId, sw2, sh2, sf2);
                    uint base2 = (uint)am.Length;
                    if (_virtual)
                    {
                        np2.sourceInfo.modeInfoIdx = base2;
                        np2.targetInfo.modeInfoIdx = ((0xFFFFu) << 16) | (base2 + 1);
                    }
                    else
                    {
                        np2.sourceInfo.modeInfoIdx = base2;
                        np2.targetInfo.modeInfoIdx = base2 + 1;
                    }
                    mergedP2[ap.Length] = np2;

                    if (verbose) Console.Error.WriteLine("  [enable] trying fresh-source fallback…");
                    hr = TrySet(mergedP2, mergedM2, verbose);
                    if (hr == 0 && TargetNowActive(m)) return 0;
                }
            }

            if (!TargetNowActive(m))
            {
                RestoreAll(); // страховка: вернуть сохранённую в БД топологию
                if (TargetNowActive(m)) { LastError = "включено через восстановление топологии"; return 0; }
                LastError = "не удалось включить монитор";
                return -1;
            }
            return 0;
        }

        public static int RestoreAll()
        {
            return Native.SetDisplayConfig(0, null, 0, null,
                Native.SDC_APPLY | Native.SDC_TOPOLOGY_INTERNAL | Native.SDC_TOPOLOGY_CLONE |
                Native.SDC_TOPOLOGY_EXTEND | Native.SDC_TOPOLOGY_EXTERNAL);
        }

        // ---- запасной вариант через ChangeDisplaySettingsEx ----
        static bool GetDevMode(string gdi, uint mode, out Native.DEVMODE dm)
        {
            dm = new Native.DEVMODE();
            dm.dmSize = (ushort)Marshal.SizeOf(typeof(Native.DEVMODE));
            return Native.EnumDisplaySettingsEx(gdi, mode, ref dm, 0);
        }

        static int DisableCds(string gdi)
        {
            Native.DEVMODE dm;
            if (!GetDevMode(gdi, Native.ENUM_CURRENT_SETTINGS, out dm) &&
                !GetDevMode(gdi, Native.ENUM_REGISTRY_SETTINGS, out dm))
                return -1;
            dm.dmFields = Native.DM_POSITION;
            dm.dmPosition.x = 30000;
            dm.dmPosition.y = 30000;
            int r1 = Native.ChangeDisplaySettingsEx(gdi, ref dm, IntPtr.Zero,
                Native.CDS_UPDATEREGISTRY | Native.CDS_NORESET, IntPtr.Zero);
            if (r1 != 0) return r1; // при неудаче не дёргаем все выходы понапрасну
            return Native.ChangeDisplaySettingsEx(null, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero);
        }

        static int EnableCds(string gdi)
        {
            Native.DEVMODE dm;
            if (!GetDevMode(gdi, Native.ENUM_REGISTRY_SETTINGS, out dm) &&
                !GetDevMode(gdi, Native.ENUM_CURRENT_SETTINGS, out dm))
                return -1;
            dm.dmFields = Native.DM_POSITION | Native.DM_PELSWIDTH | Native.DM_PELSHEIGHT;
            Rectangle vs = SystemInformation.VirtualScreen;
            dm.dmPosition.x = vs.Right;
            dm.dmPosition.y = vs.Top;
            int r1 = Native.ChangeDisplaySettingsEx(gdi, ref dm, IntPtr.Zero,
                Native.CDS_UPDATEREGISTRY | Native.CDS_NORESET, IntPtr.Zero);
            if (r1 != 0) return r1; // при неудаче не дёргаем все выходы понапрасну
            return Native.ChangeDisplaySettingsEx(null, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero);
        }
    }

    // ------------------------------------------------- окна: запоминание позиций
    // Перед выключением монитора снимаем позиции всех обычных окон,
    // после включения возвращаем окна, жившие на вернувшемся мониторе.
    internal static class WinSnap
    {
        class SnapWin
        {
            public IntPtr Hwnd;
            public Native.WINDOWPLACEMENT Pl;
            public string Gdi;
        }

        static readonly List<SnapWin> _saved = new List<SnapWin>();

        // снимок храним и в памяти, и в файле: файл переживает перезапуск
        // программы и работает между отдельными консольными командами
        static string SnapFile()
        {
            return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MonitorTray_windows.txt");
        }

        static void SaveFile()
        {
            try
            {
                List<string> lines = new List<string>();
                foreach (SnapWin w in _saved)
                {
                    Native.RECT r = w.Pl.rcNormalPosition;
                    lines.Add(w.Hwnd.ToInt64() + "|" + w.Gdi + "|" + w.Pl.showCmd +
                        "|" + r.Left + "|" + r.Top + "|" + r.Right + "|" + r.Bottom);
                }
                System.IO.File.WriteAllLines(SnapFile(), lines.ToArray());
            }
            catch { }
        }

        static void LoadFileIfEmpty()
        {
            if (_saved.Count > 0) return;
            try
            {
                if (!System.IO.File.Exists(SnapFile())) return;
                foreach (string line in System.IO.File.ReadAllLines(SnapFile()))
                {
                    string[] p = line.Split('|');
                    if (p.Length < 7) continue;
                    SnapWin w = new SnapWin();
                    w.Hwnd = new IntPtr(long.Parse(p[0]));
                    w.Gdi = p[1];
                    w.Pl = new Native.WINDOWPLACEMENT();
                    w.Pl.showCmd = int.Parse(p[2]);
                    w.Pl.rcNormalPosition = new Native.RECT();
                    w.Pl.rcNormalPosition.Left = int.Parse(p[3]);
                    w.Pl.rcNormalPosition.Top = int.Parse(p[4]);
                    w.Pl.rcNormalPosition.Right = int.Parse(p[5]);
                    w.Pl.rcNormalPosition.Bottom = int.Parse(p[6]);
                    _saved.Add(w);
                }
            }
            catch { }
        }

        public static void Snapshot()
        {
            lock (_saved)
            {
                _saved.Clear();
                Native.EnumWindows(delegate(IntPtr h, IntPtr lp)
                {
                    try
                    {
                        if (!Native.IsWindowVisible(h)) return true;
                        if (Native.GetWindowTextLength(h) == 0) return true; // безымянные/служебные
                        uint ex = Native.GetWindowLong(h, -20 /*GWL_EXSTYLE*/);
                        if ((ex & 0x80) != 0) return true; // WS_EX_TOOLWINDOW
                        int cloaked;
                        if (Native.DwmGetWindowAttribute(h, 14 /*DWMWA_CLOAKED*/, out cloaked, 4) == 0 && cloaked != 0)
                            return true; // скрытые UWP
                        Native.WINDOWPLACEMENT pl = new Native.WINDOWPLACEMENT();
                        pl.length = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.WINDOWPLACEMENT));
                        if (!Native.GetWindowPlacement(h, ref pl)) return true;
                        Native.RECT r = pl.rcNormalPosition;
                        if (r.Right - r.Left <= 0 || r.Bottom - r.Top <= 0) return true;
                        int cx = (r.Left + r.Right) / 2;
                        int cy = (r.Top + r.Bottom) / 2;
                        string gdi = null;
                        foreach (Screen s in Screen.AllScreens)
                            if (s.Bounds.Contains(cx, cy)) { gdi = s.DeviceName; break; }
                        if (gdi == null) return true;
                        SnapWin w = new SnapWin();
                        w.Hwnd = h; w.Pl = pl; w.Gdi = gdi;
                        _saved.Add(w);
                    }
                    catch { }
                    return true;
                }, IntPtr.Zero);
                SaveFile();
            }
        }

        // отладка: что сейчас в снимке
        public static string Dump()
        {
            lock (_saved)
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.Append("winsnap count=").Append(_saved.Count).AppendLine();
                foreach (SnapWin w in _saved)
                    sb.Append("  hwnd=").Append(w.Hwnd).Append(" gdi=").Append(w.Gdi)
                      .Append(" rc=").Append(w.Pl.rcNormalPosition.Left).Append(",").Append(w.Pl.rcNormalPosition.Top)
                      .Append(" showCmd=").Append(w.Pl.showCmd).AppendLine();
                return sb.ToString();
            }
        }

        // вернуть окна, которые жили на мониторе gdiName
        public static void RestoreFor(string gdiName, bool verbose)
        {
            if (gdiName == null || gdiName.Length == 0) return;
            lock (_saved)
            {
                LoadFileIfEmpty();
                if (verbose) Console.Error.WriteLine("  [winsnap] restore for " + gdiName + ", entries=" + _saved.Count);
                foreach (SnapWin w in _saved)
                {
                    try
                    {
                        if (w.Gdi != gdiName) continue;
                        if (!Native.IsWindow(w.Hwnd))
                        {
                            if (verbose) Console.Error.WriteLine("  [winsnap] hwnd " + w.Hwnd + " closed");
                            continue;
                        }
                        Native.WINDOWPLACEMENT cur = new Native.WINDOWPLACEMENT();
                        cur.length = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.WINDOWPLACEMENT));
                        if (!Native.GetWindowPlacement(w.Hwnd, ref cur)) continue;
                        Native.RECT a = cur.rcNormalPosition, b = w.Pl.rcNormalPosition;
                        if (a.Left == b.Left && a.Top == b.Top && a.Right == b.Right && a.Bottom == b.Bottom
                            && cur.showCmd == w.Pl.showCmd)
                        {
                            if (verbose) Console.Error.WriteLine("  [winsnap] hwnd " + w.Hwnd + " already in place");
                            continue;
                        }
                        Native.WINDOWPLACEMENT np = w.Pl;
                        np.length = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.WINDOWPLACEMENT));
                        if (w.Pl.showCmd == 3)
                        {
                            // развёрнутое: сначала нормальное состояние в сохранённой позиции…
                            np.showCmd = 1;
                            Native.SetWindowPlacement(w.Hwnd, ref np);
                            // …потом разворот заново — уже на нужном мониторе
                            Native.ShowWindow(w.Hwnd, 3);
                        }
                        else
                        {
                            // нормальное или свёрнутое: позиция применяется как есть
                            bool ok = Native.SetWindowPlacement(w.Hwnd, ref np);
                            if (verbose) Console.Error.WriteLine("  [winsnap] hwnd " + w.Hwnd + " -> " +
                                b.Left + "," + b.Top + " SetWindowPlacement=" + ok);
                        }
                    }
                    catch (Exception ex)
                    {
                        if (verbose) Console.Error.WriteLine("  [winsnap] error: " + ex.Message);
                    }
                }
            }
        }
    }

    // ---------------------------------------------------- яркость (DDC/CI)
    internal class BrightEntry
    {
        public IntPtr Handle;
        public string Gdi;
        public uint Min, Cur, Max;   // сырой диапазон DDC
        public int? Pending;         // 0..100, применить отложенно
        public int Value;            // 0..100, что показывает ползунок
    }

    internal static class Bright
    {
        public static List<BrightEntry> Open()
        {
            List<BrightEntry> res = new List<BrightEntry>();
            foreach (Screen s in Screen.AllScreens)
            {
                IntPtr hMon = Native.MonitorFromPoint(
                    new Point(s.Bounds.X + s.Bounds.Width / 2, s.Bounds.Y + s.Bounds.Height / 2), 2 /*NEAREST*/);
                if (hMon == IntPtr.Zero) continue;
                uint cnt;
                if (!Native.GetNumberOfPhysicalMonitorsFromHMONITOR(hMon, out cnt) || cnt == 0) continue;
                Native.PHYSICAL_MONITOR[] pm = new Native.PHYSICAL_MONITOR[cnt];
                if (!Native.GetPhysicalMonitorsFromHMONITOR(hMon, cnt, pm)) continue;
                foreach (Native.PHYSICAL_MONITOR p in pm)
                {
                    uint mn, cur, mx;
                    if (Native.GetMonitorBrightness(p.hPhysicalMonitor, out mn, out cur, out mx) && mx > mn)
                    {
                        BrightEntry e = new BrightEntry();
                        e.Handle = p.hPhysicalMonitor;
                        e.Gdi = s.DeviceName;
                        e.Min = mn; e.Cur = cur; e.Max = mx;
                        res.Add(e);
                    }
                    else
                    {
                        Native.DestroyPhysicalMonitor(p.hPhysicalMonitor); // яркость не поддерживается
                    }
                }
            }
            return res;
        }

        public static void Close(List<BrightEntry> list)
        {
            if (list == null) return;
            foreach (BrightEntry e in list)
            {
                try { Native.DestroyPhysicalMonitor(e.Handle); } catch { }
            }
        }

        public static int ToPercent(BrightEntry e)
        {
            return (int)((e.Cur - e.Min) * 100 / (e.Max - e.Min));
        }

        public static void Apply(BrightEntry e, int percent)
        {
            try
            {
                uint v = e.Min + (uint)((e.Max - e.Min) * percent / 100);
                Native.SetMonitorBrightness(e.Handle, v);
                e.Cur = v;
            }
            catch { }
        }
    }

    // ------------------------------------------------------------------ иконка
    // Значок в трее в стиле Windows 11: цветной монитор на скруглённой плитке, плитка — по теме
    // программы (светлая в светлой теме, тёмная — в тёмной). Плитка со своим фоном
    // хорошо видна на любой панели задач, светлой или тёмной.
    internal static class AppIcon
    {
        [DllImport("user32.dll")]
        static extern bool DestroyIcon(IntPtr hIcon);

        static GraphicsPath Tile(float x, float y, float w, float h, float d)
        {
            GraphicsPath p = new GraphicsPath();
            p.AddArc(x, y, d, d, 180, 90);
            p.AddArc(x + w - d, y, d, d, 270, 90);
            p.AddArc(x + w - d, y + h - d, d, d, 0, 90);
            p.AddArc(x, y + h - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static Icon Create(bool dark)
        {
            using (Bitmap bmp = Render(Math.Max(16, SystemInformation.SmallIconSize.Width), dark))
            {
                IntPtr hIcon = bmp.GetHicon();
                Icon icon = (Icon)Icon.FromHandle(hIcon).Clone();
                DestroyIcon(hIcon);
                return icon;
            }
        }

        // картинка значка любого размера (из неё же собран MonitorTray.ico)
        public static Bitmap Render(int size, bool dark)
        {
            // рисуем в 4 раза крупнее и плавно уменьшаем — гладкие края без «лесенки»
            int ss = size * 4;
            Bitmap bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (Bitmap big = new Bitmap(ss, ss, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(big))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.Clear(Color.Transparent);
                    g.ScaleTransform(ss / 32f, ss / 32f); // сетка 32×32
                    RectangleF tr = new RectangleF(0.5f, 0.5f, 31, 31);
                    using (GraphicsPath p = Tile(tr.X, tr.Y, tr.Width, tr.Height, 15))
                    {
                        using (LinearGradientBrush b = new LinearGradientBrush(tr,
                            dark ? Color.FromArgb(62, 62, 68) : Color.White,
                            dark ? Color.FromArgb(36, 36, 40) : Color.FromArgb(232, 236, 243), 90f))
                            g.FillPath(b, p);
                        using (Pen pen = new Pen(dark ? Color.FromArgb(80, 255, 255, 255) : Color.FromArgb(50, 0, 0, 0), 1.4f))
                            g.DrawPath(pen, p);
                    }
                    // тот же цветной монитор, что и в окне программы
                    Popup.MonitorIcon(g, new RectangleF(3, 4.5f, 26, 23), 0, true);
                }
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.CompositingQuality = CompositingQuality.HighQuality;
                    g.Clear(Color.Transparent);
                    g.DrawImage(big, new Rectangle(0, 0, size, size));
                }
            }
            return bmp;
        }
    }

    // ------------------------------------------------------------------ автозапуск
    internal static class Autostart
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ValueName = "MonitorTray";

        public static bool IsEnabled()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey))
                {
                    if (k == null) return false;
                    return k.GetValue(ValueName) != null;
                }
            }
            catch { return false; }
        }

        public static void Set(bool on)
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (on) k.SetValue(ValueName, "\"" + Application.ExecutablePath + "\"");
                else k.DeleteValue(ValueName, false);
            }
        }
    }

    // ------------------------------------------------------------------ локализация
    // Язык по умолчанию — английский; переключается в меню (Language) и сохраняется
    // в %APPDATA%\MonitorTray_lang.txt ("en" или "ru").
    internal static class Loc
    {
        static string _lang = "en";

        public static string Lang { get { return _lang; } }

        static string LangFile()
        {
            return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MonitorTray_lang.txt");
        }

        public static void Load()
        {
            try
            {
                if (System.IO.File.Exists(LangFile()))
                {
                    string s = System.IO.File.ReadAllText(LangFile()).Trim().ToLowerInvariant();
                    if (s == "ru" || s == "en") _lang = s;
                }
            }
            catch { }
        }

        public static void Set(string lang)
        {
            _lang = lang;
            try { System.IO.File.WriteAllText(LangFile(), lang); } catch { }
        }

        public static string Get(string key)
        {
            bool ru = _lang == "ru";
            switch (key)
            {
                case "tray_title": return ru ? "Мониторы" : "Monitors";
                case "no_monitors": return ru ? "Мониторы не найдены" : "No monitors found";
                case "sec_monitors": return ru ? "Мониторы" : "Monitors";
                case "sec_bright": return ru ? "Управление яркостью" : "Brightness";
                case "sec_actions": return ru ? "Действия и настройки" : "Actions & settings";
                case "count_on": return ru ? "вкл. {0} из {1}" : "{0} of {1} on";
                case "badge_primary": return ru ? "Основной" : "Primary";
                case "badge_off": return ru ? "Выключен" : "Off";
                case "bright_all": return ru ? "Общая яркость (все экраны)" : "All screens";
                case "sleep_title": return ru ? "Погасить все экраны" : "Put all screens to sleep";
                case "sleep_hint": return ru ? "до первого движения мыши" : "until the mouse moves";
                case "language": return ru ? "Язык" : "Language";
                case "tip_hide": return ru ? "Свернуть в трей" : "Hide to tray";
                case "menu_open": return ru ? "Открыть MonitorTray" : "Open MonitorTray";
                case "menu_exit": return ru ? "Выход" : "Exit";
                case "tip_theme": return ru ? "Светлая / тёмная тема" : "Light / dark theme";
                case "per_monitor": return ru ? "По мониторам" : "Per monitor";
                case "afk_title": return ru ? "AFK-режим" : "AFK mode";
                case "afk_sub": return ru ? "Гасить монитор без курсора: {0}" : "Turn off an idle monitor after {0}";
                case "afk_after": return ru ? "Через" : "After";
                case "afk_min": return ru ? "мин" : "min";
                case "afk_hour": return ru ? "ч" : "h";
                case "badge_afk": return "AFK";
                case "afk_off_hint": return ru
                    ? "{0} выключен: курсор давно не заходил на него. Чтобы включить, толкните мышь в край экрана в его сторону или нажмите на него в окне MonitorTray."
                    : "{0} was turned off: the cursor hasn't been there for a while. To turn it back on, push the mouse against the screen edge on its side or click it in the MonitorTray window.";
                case "upd_check": return ru ? "Проверять обновления" : "Check for updates";
                case "upd_title": return ru ? "Доступна версия {0}" : "Version {0} is available";
                case "upd_sub": return ru ? "Новый релиз на GitHub" : "New release on GitHub";
                case "upd_button": return ru ? "Обновить" : "Update";
                case "upd_open": return ru ? "Открыть" : "Open";
                case "upd_progress": return ru ? "Загрузка… {0}%" : "Downloading… {0}%";
                case "upd_failed": return ru ? "Не удалось обновить" : "Update failed";
                case "upd_nofile": return ru ? "в релизе нет нужного файла" : "the release has no matching file";
                case "upd_badhash": return ru ? "файл повреждён (не совпала контрольная сумма)" : "the file is damaged (checksum mismatch)";
                case "upd_balloon": return ru ? "Доступна MonitorTray {0}. Нажмите, чтобы обновить." : "MonitorTray {0} is available. Click to update.";
                case "upd_done": return ru ? "MonitorTray обновлена до версии {0}" : "MonitorTray has been updated to {0}";
                case "autostart": return ru ? "Запускать при входе в Windows" : "Start with Windows";
                case "about": return ru ? "О программе" : "About";
                case "tooltip": return ru ? "Мониторы: {0} из {1} вкл." : "Monitors: {0} of {1} on";
                case "off_done": return ru ? "{0} — выключен" : "{0} — turned off";
                case "on_done": return ru ? "{0} — включён" : "{0} — turned on";
                case "failed": return ru ? "Не удалось переключить {0}. {1}" : "Failed to toggle {0}. {1}";
                case "monitor_n": return ru ? "Монитор {0}" : "Monitor {0}";
                case "already_running": return ru
                    ? "MonitorTray уже запущен — значок есть в области уведомлений (возможно, под стрелкой «^»)."
                    : "MonitorTray is already running — the icon is in the notification area (possibly under the \"^\" arrow).";
                case "about_text": return ru
                    ? "MonitorTray 1.3\n\nВключение и выключение отдельных мониторов прямо из трея —\nтем же способом, что и «Параметры экрана» Windows (без DDC/CI).\n\nНажмите на значок в трее и щёлкните по монитору,\nчтобы выключить или включить его.\n\nУдаление: Параметры Windows → Приложения → MonitorTray."
                    : "MonitorTray 1.3\n\nTurn individual monitors on and off right from the tray —\nthe same way Windows Display Settings does it (no DDC/CI needed).\n\nClick the tray icon, then click a monitor\nto turn it off or on.\n\nUninstall: Windows Settings → Apps → MonitorTray.";
                default: return key;
            }
        }
    }

    // ------------------------------------------------------------------ стиль
    // Палитра окна в духе Windows 11 (переключается тёмная/светлая)
    internal static class Theme
    {
        public static Color Bg, Surface, SurfaceHover, Stroke, Divider, Text, TextDim,
            Accent, AccentText, Track, Hover, Edge, Off;

        public static void Apply(bool dark)
        {
            if (dark)
            {
                Bg = Color.FromArgb(32, 32, 35); Surface = Color.FromArgb(45, 45, 49);
                SurfaceHover = Color.FromArgb(52, 52, 57); Stroke = Color.FromArgb(26, 255, 255, 255);
                Divider = Color.FromArgb(50, 51, 55); Text = Color.FromArgb(242, 242, 245);
                TextDim = Color.FromArgb(166, 168, 176); Accent = Color.FromArgb(96, 205, 255);
                AccentText = Color.FromArgb(10, 20, 30); Track = Color.FromArgb(100, 255, 255, 255);
                Hover = Color.FromArgb(14, 255, 255, 255); Edge = Color.FromArgb(46, 255, 255, 255);
                Off = Color.FromArgb(120, 122, 128);
            }
            else
            {
                Bg = Color.FromArgb(243, 244, 248); Surface = Color.FromArgb(252, 252, 254);
                SurfaceHover = Color.FromArgb(246, 247, 250); Stroke = Color.FromArgb(24, 0, 0, 0);
                Divider = Color.FromArgb(225, 228, 234); Text = Color.FromArgb(27, 27, 31);
                TextDim = Color.FromArgb(98, 102, 112); Accent = Color.FromArgb(0, 95, 184);
                AccentText = Color.White; Track = Color.FromArgb(80, 0, 0, 0);
                Hover = Color.FromArgb(10, 0, 0, 0); Edge = Color.FromArgb(40, 0, 0, 0);
                Off = Color.FromArgb(150, 154, 162);
            }
        }
    }

    // Шрифты: Manrope (встроен в exe, сжат gzip) + системный шрифт значков Windows
    internal static class Fonts
    {
        public const int Regular = 0, Medium = 1, SemiBold = 2, Icons = 3;
        static PrivateFontCollection _pfc;
        static FontFamily[] _fam = new FontFamily[4];
        static Dictionary<string, Font> _cache = new Dictionary<string, Font>();

        public static void Load()
        {
            try
            {
                _pfc = new PrivateFontCollection();
                Assembly asm = Assembly.GetExecutingAssembly();
                foreach (string n in asm.GetManifestResourceNames())
                {
                    if (!n.EndsWith(".ttf.gz", StringComparison.OrdinalIgnoreCase)) continue;
                    byte[] data;
                    using (Stream st = asm.GetManifestResourceStream(n))
                    using (GZipStream gz = new GZipStream(st, CompressionMode.Decompress))
                    using (MemoryStream ms = new MemoryStream())
                    {
                        gz.CopyTo(ms);
                        data = ms.ToArray();
                    }
                    IntPtr mem = Marshal.AllocCoTaskMem(data.Length); // живёт до конца процесса
                    Marshal.Copy(data, 0, mem, data.Length);
                    _pfc.AddMemoryFont(mem, data.Length);
                }
                foreach (FontFamily f in _pfc.Families)
                {
                    // семейства встроенных шрифтов переименованы в «<Имя> MT <Начертание>»
                    if (f.Name.EndsWith(" MT Regular")) _fam[Regular] = f;
                    else if (f.Name.EndsWith(" MT Medium")) _fam[Medium] = f;
                    else if (f.Name.EndsWith(" MT SemiBold")) _fam[SemiBold] = f;
                }
            }
            catch { }
            // запасной вариант — системный Segoe UI
            if (_fam[Regular] == null) _fam[Regular] = Family("Segoe UI") ?? FontFamily.GenericSansSerif;
            if (_fam[Medium] == null) _fam[Medium] = _fam[Regular];
            if (_fam[SemiBold] == null) _fam[SemiBold] = Family("Segoe UI Semibold") ?? _fam[Regular];
            _fam[Icons] = Family("Segoe Fluent Icons") ?? Family("Segoe MDL2 Assets") ?? _fam[Regular];
        }

        static FontFamily Family(string name)
        {
            try { return new FontFamily(name); } catch { return null; }
        }

        public static Font Get(int kind, float px)
        {
            string key = kind + ":" + px;
            Font f;
            if (!_cache.TryGetValue(key, out f))
            {
                f = new Font(_fam[kind], px, FontStyle.Regular, GraphicsUnit.Pixel);
                _cache[key] = f;
            }
            return f;
        }
    }

    // Сохраняемые настройки: тема, раскрытость разделов, AFK-режим, автообновление
    internal static class Ui
    {
        public static bool DarkTheme = false;
        public static bool SlidersExpanded = false;
        public static bool SettingsExpanded = true;
        public static bool AfkEnabled = false;      // AFK-режим по умолчанию выключен
        public static int AfkMinutes = 30;          // своё время, 1..240 мин
        public static bool AfkHintShown = false;    // подсказку «как включить обратно» показали
        public static bool AutoUpdate = true;
        // монитор → участвует ли в AFK-режиме (кого нет в списке: все, кроме основного)
        public static Dictionary<string, bool> AfkMonitors = new Dictionary<string, bool>();

        // постоянный ключ монитора (путь устройства не меняется между перезагрузками)
        public static string MonKey(Mon m) { return m.DevicePath.Length > 0 ? m.DevicePath : m.Name; }

        public static bool AfkIncluded(Mon m)
        {
            bool v;
            return AfkMonitors.TryGetValue(MonKey(m), out v) ? v : !m.Primary;
        }

        static string UiFile()
        {
            return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MonitorTray_ui.txt");
        }

        public static void Load()
        {
            try
            {
                if (System.IO.File.Exists(UiFile()))
                {
                    foreach (string line in System.IO.File.ReadAllLines(UiFile()))
                    {
                        if (line == "theme=light") DarkTheme = false;
                        else if (line == "theme=dark") DarkTheme = true;
                        else if (line == "sliders=1") SlidersExpanded = true;
                        else if (line == "settings=1") SettingsExpanded = true;
                        else if (line == "settings=0") SettingsExpanded = false;
                        else if (line == "afk=1") AfkEnabled = true;
                        else if (line == "afk=0") AfkEnabled = false;
                        else if (line == "afk_hint=1") AfkHintShown = true;
                        else if (line == "update=1") AutoUpdate = true;
                        else if (line == "update=0") AutoUpdate = false;
                        else if (line.StartsWith("afk_mon=") && line.Length > 10)
                            AfkMonitors[line.Substring(10)] = line[8] == '1';
                        else if (line.StartsWith("afk_min="))
                        {
                            int v;
                            if (int.TryParse(line.Substring(8), out v) && v >= 1 && v <= 240) AfkMinutes = v;
                        }
                        else if (line == "sliders=0") SlidersExpanded = false;
                    }
                }
            }
            catch { }
            Theme.Apply(DarkTheme);
        }

        public static void Save()
        {
            try
            {
                List<string> lines = new List<string>
                {
                    DarkTheme ? "theme=dark" : "theme=light",
                    SlidersExpanded ? "sliders=1" : "sliders=0",
                    SettingsExpanded ? "settings=1" : "settings=0",
                    AfkEnabled ? "afk=1" : "afk=0",
                    "afk_min=" + AfkMinutes,
                    AfkHintShown ? "afk_hint=1" : "afk_hint=0",
                    AutoUpdate ? "update=1" : "update=0"
                };
                foreach (KeyValuePair<string, bool> kv in AfkMonitors)
                    lines.Add("afk_mon=" + (kv.Value ? "1" : "0") + "|" + kv.Key);
                System.IO.File.WriteAllLines(UiFile(), lines.ToArray());
            }
            catch { }
        }
    }

    // WinAPI для полупрозрачного окна (скруглённые углы и тень на Windows 10 и 11)
    internal static class Layered
    {
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref Point pptDst, ref Size psize,
            IntPtr hdcSrc, ref Point pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hDC);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr hObject);
        [DllImport("shcore.dll")] public static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

        public static void Push(IntPtr hwnd, Bitmap bmp, Point pos)
        {
            IntPtr screenDc = GetDC(IntPtr.Zero);
            IntPtr memDc = CreateCompatibleDC(screenDc);
            IntPtr hBmp = bmp.GetHbitmap(Color.FromArgb(0));
            IntPtr old = SelectObject(memDc, hBmp);
            try
            {
                Size size = bmp.Size;
                Point src = Point.Empty;
                BLENDFUNCTION blend = new BLENDFUNCTION();
                blend.BlendOp = 0;               // AC_SRC_OVER
                blend.SourceConstantAlpha = 255;
                blend.AlphaFormat = 1;           // AC_SRC_ALPHA
                UpdateLayeredWindow(hwnd, screenDc, ref pos, ref size, memDc, ref src, 0, ref blend, 2 /*ULW_ALPHA*/);
            }
            finally
            {
                SelectObject(memDc, old);
                DeleteObject(hBmp);
                DeleteDC(memDc);
                ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        public static float ScaleAt(Point p)
        {
            try
            {
                uint dx, dy;
                IntPtr mon = Native.MonitorFromPoint(p, 2 /*NEAREST*/);
                if (GetDpiForMonitor(mon, 0 /*EFFECTIVE*/, out dx, out dy) == 0 && dx > 0) return dx / 96f;
            }
            catch { }
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) return g.DpiX / 96f;
        }
    }

    // ------------------------------------------------------------------ AFK-режим
    // Монитор, на который курсор не заходил N минут, выключается (как кнопкой в окне).
    // Включается обратно: «толчком» мыши в край экрана, за которым он был, кликом в окне,
    // а если выключился, пока за ПК никого не было, — при первом же движении или нажатии.
    // Мониторы выбираются в окне (по умолчанию — все, кроме основного). Не гасит монитор с курсором,
    // последний включённый, монитор с полноэкранным окном и не срабатывает, пока какая-нибудь
    // программа (видеоплеер, браузер с видео) просит не гасить экран.
    internal class AfkWatcher : NativeWindow
    {
        class Sleeper { public string Key, Name, Gdi; public Rectangle Bounds; public bool WakeOnInput; public int At; }

        [StructLayout(LayoutKind.Sequential)] struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        struct RAWINPUTDEVICE { public ushort usUsagePage, usUsage; public uint dwFlags; public IntPtr hwndTarget; }

        [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);
        [DllImport("powrprof.dll")] static extern uint CallNtPowerInformation(int level, IntPtr inBuf, uint inLen, out uint outBuf, uint outLen);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hWnd, StringBuilder s, int n);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll")] static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] d, uint n, uint size);
        [DllImport("user32.dll")] static extern uint GetRawInputData(IntPtr h, uint cmd, IntPtr data, ref uint size, uint headerSize);

        const int PushNeeded = 300;      // сколько «отсчётов» мыши упереть в край, чтобы разбудить монитор

        readonly TrayApp _app;
        readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer();
        readonly Dictionary<string, int> _seen = new Dictionary<string, int>(); // экран → когда там был курсор
        readonly List<Sleeper> _sleepers = new List<Sleeper>();
        readonly uint _pid = (uint)Process.GetCurrentProcess().Id;
        Sleeper _pushTarget;
        int _pushDir, _pushSum, _pushAt;
        bool _raw;

        public AfkWatcher(TrayApp app)
        {
            _app = app;
            CreateHandle(new CreateParams()); // невидимое окно — получатель сырого ввода мыши
            _timer.Tick += delegate { Tick(); };
            Update();
        }

        public static string Key(Mon m) { return m.AdapterId.LowPart + ":" + m.AdapterId.HighPart + ":" + m.TargetId; }

        public bool IsSleeping(Mon m)
        {
            string k = Key(m);
            foreach (Sleeper s in _sleepers) if (s.Key == k) return true;
            return false;
        }

        // монитор включили вручную — он больше не «спит»
        public void Forget(Mon m)
        {
            string k = Key(m);
            _sleepers.RemoveAll(s => s.Key == k);
            Update();
        }

        // настройка поменялась — запустить или остановить слежение
        public void Update()
        {
            bool need = Ui.AfkEnabled || _sleepers.Count > 0;
            if (need && !_timer.Enabled) { _seen.Clear(); _timer.Start(); }
            if (!need) _timer.Stop();
            _timer.Interval = _sleepers.Count > 0 ? 250 : 1000;
            SetRaw(_sleepers.Count > 0);
        }

        // при выходе из программы спящие мониторы включаются
        public void WakeAll()
        {
            foreach (Sleeper s in _sleepers.ToArray()) Wake(s, false);
            _timer.Stop();
            SetRaw(false);
            DestroyHandle();
        }

        void Tick()
        {
            int now = Environment.TickCount;
            Screen[] screens = Screen.AllScreens;
            int idle = IdleMs();

            // монитор включили другим способом — забыть о нём (Windows обновляет список
            // экранов не мгновенно, поэтому первые секунды после выключения не проверяем)
            _sleepers.RemoveAll(s => unchecked(now - s.At) > 5000 && Array.Exists(screens, sc => sc.DeviceName == s.Gdi));
            // вернулись к ПК — включить то, что выключилось, пока никого не было
            if (idle < 1000)
                foreach (Sleeper s in _sleepers.ToArray()) if (s.WakeOnInput) Wake(s, true);

            if (Ui.AfkEnabled)
            {
                Point p = Cursor.Position;
                bool hold = _app.PopupBusy || DisplayRequired();
                int limit = Ui.AfkMinutes * 60000;
                foreach (Screen sc in screens)
                {
                    int t;
                    if (hold || sc.Bounds.Contains(p) || !_seen.TryGetValue(sc.DeviceName, out t))
                    {
                        _seen[sc.DeviceName] = now;
                        continue;
                    }
                    if (unchecked(now - t) < limit) continue;
                    _seen[sc.DeviceName] = now;
                    if (HasFullscreen(sc.Bounds)) continue; // игра или фильм на весь экран
                    Sleep(sc, idle >= limit);
                    break; // не больше одного монитора за раз
                }
            }
            Update();
        }

        void Sleep(Screen sc, bool wakeOnInput)
        {
            List<Mon> mons = Svc.List();
            Mon m = mons.Find(x => x.Active && x.GdiName == sc.DeviceName);
            if (m == null || !Ui.AfkIncluded(m)) return;
            if (mons.FindAll(x => x.Active).Count < 2) return; // последний включённый не гасим
            if (Svc.Disable(m, false) != 0) return;
            Sleeper s = new Sleeper();
            s.Key = Key(m); s.Name = m.Name; s.Gdi = sc.DeviceName; s.Bounds = sc.Bounds; s.WakeOnInput = wakeOnInput;
            s.At = Environment.TickCount;
            _sleepers.Add(s);
            _app.OnAfkChanged(m.Name, true);
        }

        void Wake(Sleeper s, bool notify)
        {
            _sleepers.Remove(s);
            string k = s.Key;
            Mon m = Svc.List().Find(x => !x.Active && Key(x) == k);
            if (m != null) Svc.Enable(m, false);
            _seen.Clear(); // отсчёт простоя — заново
            if (notify) _app.OnAfkChanged(s.Name, false);
        }

        static int IdleMs()
        {
            LASTINPUTINFO li = new LASTINPUTINFO();
            li.cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO));
            if (!GetLastInputInfo(ref li)) return 0;
            return (int)unchecked((uint)Environment.TickCount - li.dwTime);
        }

        // кто-то просит не гасить экран (ES_DISPLAY_REQUIRED) — так делают плееры и браузеры с видео
        static bool DisplayRequired()
        {
            try
            {
                uint state;
                return CallNtPowerInformation(16 /*SystemExecutionState*/, IntPtr.Zero, 0, out state, 4) == 0 && (state & 0x2) != 0;
            }
            catch { return false; }
        }

        // окно на весь монитор (не развёрнутое, а именно полноэкранное: игра, видео, F11)
        bool HasFullscreen(Rectangle b)
        {
            bool found = false;
            Native.EnumWindows(delegate(IntPtr h, IntPtr lp)
            {
                if (!Native.IsWindowVisible(h) || IsIconic(h) || IsZoomed(h)) return true;
                int cloaked;
                if (Native.DwmGetWindowAttribute(h, 14 /*DWMWA_CLOAKED*/, out cloaked, 4) == 0 && cloaked != 0) return true;
                RECT r;
                if (!GetWindowRect(h, out r)) return true;
                if (r.Left > b.Left || r.Top > b.Top || r.Right < b.Right || r.Bottom < b.Bottom) return true;
                uint pid;
                GetWindowThreadProcessId(h, out pid);
                if (pid == _pid) return true;
                StringBuilder cls = new StringBuilder(64);
                GetClassName(h, cls, 64);
                string c = cls.ToString();
                if (c == "Progman" || c == "WorkerW" || c.StartsWith("Shell_")) return true; // рабочий стол, панель задач
                found = true;
                return false;
            }, IntPtr.Zero);
            return found;
        }

        // ---- «толчок» мыши в край экрана: сырой ввод мыши приходит, даже когда курсор упёрся
        void SetRaw(bool on)
        {
            if (on == _raw || (on && Handle == IntPtr.Zero)) return;
            RAWINPUTDEVICE[] d = new RAWINPUTDEVICE[1];
            d[0].usUsagePage = 1; d[0].usUsage = 2; // мышь
            d[0].dwFlags = on ? 0x100u /*RIDEV_INPUTSINK*/ : 0x1u /*RIDEV_REMOVE*/;
            d[0].hwndTarget = on ? Handle : IntPtr.Zero;
            if (RegisterRawInputDevices(d, 1, (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE)))) _raw = on;
        }

        protected override void WndProc(ref Message msg)
        {
            if (msg.Msg == 0x00FF /*WM_INPUT*/ && _sleepers.Count > 0) OnRawInput(msg.LParam);
            base.WndProc(ref msg);
        }

        void OnRawInput(IntPtr hRaw)
        {
            int hs = 8 + 2 * IntPtr.Size; // RAWINPUTHEADER
            uint size = 0;
            GetRawInputData(hRaw, 0x10000003 /*RID_INPUT*/, IntPtr.Zero, ref size, (uint)hs);
            if (size == 0 || size > 1024) return;
            IntPtr buf = Marshal.AllocHGlobal((int)size);
            try
            {
                if (GetRawInputData(hRaw, 0x10000003, buf, ref size, (uint)hs) != size) return;
                if (Marshal.ReadInt32(buf, 0) != 0 /*RIM_TYPEMOUSE*/) return;
                if ((Marshal.ReadInt16(buf, hs) & 1) != 0) return; // абсолютные координаты (планшет, RDP)
                Push(Marshal.ReadInt32(buf, hs + 12), Marshal.ReadInt32(buf, hs + 16));
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        void Push(int dx, int dy)
        {
            Point p = Cursor.Position;
            Rectangle b = Screen.FromPoint(p).Bounds;
            int dir = 0, amount = 0;
            Point q = p;
            if (dx > 0 && p.X >= b.Right - 1) { dir = 1; amount = dx; q = new Point(b.Right, p.Y); }
            else if (dx < 0 && p.X <= b.Left) { dir = 2; amount = -dx; q = new Point(b.Left - 1, p.Y); }
            else if (dy > 0 && p.Y >= b.Bottom - 1) { dir = 3; amount = dy; q = new Point(p.X, b.Bottom); }
            else if (dy < 0 && p.Y <= b.Top) { dir = 4; amount = -dy; q = new Point(p.X, b.Top - 1); }

            Sleeper target = null;
            if (dir != 0)
                foreach (Sleeper s in _sleepers)
                {
                    Rectangle r = s.Bounds;
                    r.Inflate(2, 2);
                    if (r.Contains(q)) target = s;
                }
            int now = Environment.TickCount;
            if (target == null || target != _pushTarget || dir != _pushDir || unchecked(now - _pushAt) > 400) _pushSum = 0;
            _pushTarget = target; _pushDir = dir; _pushAt = now;
            if (target == null) return;
            _pushSum += amount;
            if (_pushSum < PushNeeded) return;
            _pushSum = 0;
            _pushTarget = null;
            Wake(target, true);
            Update();
        }
    }

    // ------------------------------------------------------------------ автообновление
    // Раз в сутки спрашивает GitHub о последнем релизе. Новая версия ставится только по
    // кнопке «Обновить»: скачивается, сверяется с SHA256SUMS.txt из релиза и заменяет программу
    // (портативную — подменой exe, установленную — тихим запуском нового установщика).
    internal class Updater
    {
        internal static string ApiUrl = "https://api.github.com/repos/zhevniak/MonitorTray/releases/latest";
        public const string ReleasesPage = "https://github.com/zhevniak/MonitorTray/releases/latest";

        public Version Available;        // новая версия, если нашлась
        public bool Downloading;
        public int Progress;             // 0..100
        public string Error;             // текст ошибки последней попытки

        readonly TrayApp _app;
        readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer();
        Dictionary<string, string> _assets = new Dictionary<string, string>(); // имя файла → ссылка

        public Updater(TrayApp app)
        {
            _app = app;
            _timer.Interval = 20000; // первая проверка — через 20 с после запуска
            _timer.Tick += delegate { _timer.Interval = 24 * 3600 * 1000; Check(); };
            _timer.Start();
        }

        public static Version Current { get { return Norm(Assembly.GetExecutingAssembly().GetName().Version); } }

        // «1.4» или «1.4.1» — для подписей
        public static string Pretty(Version v) { return v.Build > 0 ? v.ToString(3) : v.ToString(2); }

        static Version Norm(Version v)
        {
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision));
        }

        public void Check()
        {
            if (!Ui.AutoUpdate || Downloading) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    string json = Get(ApiUrl);
                    Match t = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"v?([0-9]+(?:\\.[0-9]+){1,3})\"");
                    if (!t.Success) return;
                    Version v = Norm(new Version(t.Groups[1].Value));
                    Dictionary<string, string> assets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (Match a in Regex.Matches(json, "\"browser_download_url\"\\s*:\\s*\"([^\"]+)\""))
                    {
                        string url = a.Groups[1].Value;
                        assets[url.Substring(url.LastIndexOf('/') + 1)] = url;
                    }
                    if (v > Current)
                        _app.Post(delegate { Available = v; _assets = assets; _app.OnUpdateChanged(true); });
                }
                catch { } // нет сети — проверим в следующий раз
            });
        }

        static bool IsInstalled()
        {
            string inst = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "MonitorTray");
            string dir = Path.GetDirectoryName(Application.ExecutablePath);
            return string.Equals(Path.GetFullPath(dir).TrimEnd('\\'), Path.GetFullPath(inst).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }

        public void Start()
        {
            if (Available == null || Downloading) return;
            Downloading = true; Progress = 0; Error = null;
            _app.OnUpdateChanged(false);
            bool installed = IsInstalled();
            string name = installed ? "MonitorTraySetup.exe" : "MonitorTray.exe";
            Version ver = Available;
            Dictionary<string, string> assets = _assets;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string file = null;
                try
                {
                    string url, sumsUrl;
                    if (!assets.TryGetValue(name, out url) || !assets.TryGetValue("SHA256SUMS.txt", out sumsUrl))
                        throw new Exception(Loc.Get("upd_nofile"));
                    Match sm = Regex.Match(Get(sumsUrl), "([0-9a-fA-F]{64})\\s+\\*?" + Regex.Escape(name) + "\\s*$", RegexOptions.Multiline);
                    if (!sm.Success) throw new Exception(Loc.Get("upd_nofile"));
                    file = installed
                        ? Path.Combine(Path.GetTempPath(), "MonitorTraySetup-" + ver + ".exe")
                        : Application.ExecutablePath + ".new";
                    Download(url, file);
                    if (!string.Equals(Sha256(file), sm.Groups[1].Value, StringComparison.OrdinalIgnoreCase))
                        throw new Exception(Loc.Get("upd_badhash"));
                    string ready = file;
                    _app.Post(delegate { Finish(installed, ready, ver); });
                }
                catch (Exception ex)
                {
                    if (file != null) try { File.Delete(file); } catch { }
                    string msg = ex.Message;
                    _app.Post(delegate { Downloading = false; Error = msg; _app.OnUpdateChanged(false); });
                }
            });
        }

        void Finish(bool installed, string file, Version ver)
        {
            try
            {
                if (installed)
                {
                    Process.Start(file, "update"); // установщик сам закроет программу и запустит новую
                    _app.ExitApp();
                    return;
                }
                if (Norm(AssemblyName.GetAssemblyName(file).Version) != ver) throw new Exception(Loc.Get("upd_badhash"));
                string exe = Application.ExecutablePath, old = exe + ".old";
                if (File.Exists(old)) File.Delete(old);
                File.Move(exe, old); // запущенный exe переименовать можно, удалить — нет
                try { File.Move(file, exe); }
                catch { File.Move(old, exe); throw; }
                Process.Start(exe, "--updated " + Process.GetCurrentProcess().Id);
                _app.ExitApp();
            }
            catch (Exception ex)
            {
                try { File.Delete(file); } catch { }
                Downloading = false; Error = ex.Message;
                _app.OnUpdateChanged(false);
            }
        }

        static HttpWebRequest Request(string url)
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; // TLS 1.2 для GitHub
            HttpWebRequest r = (HttpWebRequest)WebRequest.Create(url);
            r.UserAgent = "MonitorTray/" + Current;
            r.Accept = "application/vnd.github+json, */*";
            r.Timeout = 20000;
            return r;
        }

        static string Get(string url)
        {
            using (WebResponse resp = Request(url).GetResponse())
            using (StreamReader rd = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                return rd.ReadToEnd();
        }

        void Download(string url, string to)
        {
            using (WebResponse resp = Request(url).GetResponse())
            using (Stream src = resp.GetResponseStream())
            using (FileStream dst = File.Create(to))
            {
                long total = resp.ContentLength, done = 0;
                int last = -1;
                byte[] buf = new byte[65536];
                int n;
                while ((n = src.Read(buf, 0, buf.Length)) > 0)
                {
                    dst.Write(buf, 0, n);
                    done += n;
                    int pc = total > 0 ? (int)(done * 100 / total) : 0;
                    if (pc / 5 != last / 5)
                    {
                        last = pc;
                        _app.Post(delegate { Progress = pc; _app.OnUpdateChanged(false); });
                    }
                }
            }
        }

        static string Sha256(string file)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream fs = File.OpenRead(file))
                return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "");
        }
    }

    // ------------------------------------------------------------------ GUI: окно
    // Всплывающее окно у трея. Целиком рисуется вручную (одна картинка с альфа-каналом),
    // элементы — прямоугольники-«хиты» с обработчиками.
    internal class Popup : Form
    {
        const float W = 372;     // ширина карточки (логические px, 96 dpi)
        const float SH = 18;     // поле под тень
        const float PAD = 22;    // внутренний отступ
        const float IND = 34;    // отступ содержимого секций (под текст заголовка)

        // значки шрифта Segoe MDL2 Assets / Segoe Fluent Icons
        const string G_MONITOR = "", G_SUN = "", G_GEAR = "", G_MOON = "",
            G_CLOSE = "", G_CHECK = "",
            G_DOWN = "", G_UP = "", G_CLOCK = "\uE823", G_DOWNLOAD = "\uE896";

        class Hit { public RectangleF R; public string Id; public Action Click; }

        readonly TrayApp _app;
        readonly List<Hit> _hits = new List<Hit>();
        readonly Dictionary<string, RectangleF> _tracks = new Dictionary<string, RectangleF>();
        readonly System.Windows.Forms.Timer _brightTimer = new System.Windows.Forms.Timer();
        readonly System.Windows.Forms.Timer _tipTimer = new System.Windows.Forms.Timer();
        readonly ToolTip _tip = new ToolTip();

        List<Mon> _mons = new List<Mon>();
        List<BrightEntry> _brights;          // живёт, пока окно открыто
        int _allValue;
        int _busyIdx = -1;
        string _hover, _press, _drag;

        float _scale = 1f;
        float _cardH = 600;
        Rectangle _wa, _sb;                  // рабочая область и границы экрана у трея

        public bool Busy;                    // идёт переключение монитора — не прятать окно
        public int HiddenAt;

        internal static readonly StringFormat FmtL = MakeFmt(StringAlignment.Near);
        internal static readonly StringFormat FmtC = MakeFmt(StringAlignment.Center);
        static readonly StringFormat FmtR = MakeFmt(StringAlignment.Far);

        static StringFormat MakeFmt(StringAlignment a)
        {
            StringFormat f = (StringFormat)StringFormat.GenericTypographic.Clone();
            f.Alignment = a;
            f.LineAlignment = StringAlignment.Center;
            f.Trimming = StringTrimming.EllipsisCharacter;
            f.FormatFlags |= StringFormatFlags.NoWrap;
            return f;
        }

        public Popup(TrayApp app)
        {
            _app = app;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            KeyPreview = true;
            Text = "MonitorTray";

            _brightTimer.Interval = 120;
            _brightTimer.Tick += delegate { _brightTimer.Stop(); FlushBright(); };

            // подсказки к кнопкам в шапке — с обычной для Windows задержкой
            _tipTimer.Interval = 500;
            _tipTimer.Tick += delegate
            {
                _tipTimer.Stop();
                string t = _hover == "close" ? Loc.Get("tip_hide") : (_hover == "theme" ? Loc.Get("tip_theme") : null);
                if (t != null && Visible)
                {
                    Point c = PointToClient(Cursor.Position);
                    _tip.Show(t, this, c.X, c.Y + 24, 3000);
                }
            };
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x80000 /*WS_EX_LAYERED*/ | 0x80 /*WS_EX_TOOLWINDOW*/;
                return cp;
            }
        }

        // ---------------------------------------------------------------- показ / скрытие
        public void ShowNearTray()
        {
            IntPtr h = Handle; // окно должно существовать до первой отрисовки
            Reload();
            AnchorAt(Cursor.Position);
            Render();
            Show();
            Activate();
            Native.SetForegroundWindow(Handle);
        }

        public void HidePopup()
        {
            if (!Visible) return;
            _tipTimer.Stop();
            _tip.Hide(this);
            Hide();
            HiddenAt = Environment.TickCount;
            _brightTimer.Stop();
            FlushBright();
            Bright.Close(_brights);
            _brights = null;
            _hover = _press = _drag = null;
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (!Busy) HidePopup();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) HidePopup();
            base.OnKeyDown(e);
        }

        void Reload()
        {
            _mons = Svc.List();
            if (_brights != null) { Bright.Close(_brights); _brights = null; }
            try { _brights = Bright.Open(); } catch { _brights = new List<BrightEntry>(); }
            int sum = 0;
            foreach (BrightEntry b in _brights) { b.Value = Bright.ToPercent(b); sum += b.Value; }
            _allValue = _brights.Count > 0 ? sum / _brights.Count : 0;
        }

        // запомнить экран у курсора: масштаб и рабочую область
        void AnchorAt(Point p)
        {
            Screen s = Screen.FromPoint(p);
            _wa = s.WorkingArea;
            _sb = s.Bounds;
            _scale = Layered.ScaleAt(p);
        }

        void FlushBright()
        {
            if (_brights == null) return;
            foreach (BrightEntry b in _brights)
            {
                if (b.Pending.HasValue)
                {
                    Bright.Apply(b, b.Pending.Value);
                    b.Pending = null;
                }
            }
        }

        // ---------------------------------------------------------------- отрисовка
        void Render()
        {
            // 1-й проход — только узнать высоту
            using (Bitmap tmp = new Bitmap(1, 1))
            using (Graphics g = Graphics.FromImage(tmp))
                _cardH = PaintCard(g);

            // не вылезать за рабочую область на маленьких экранах
            float scale = _scale;
            float fit = (_wa.Height - 8) / (_cardH + 2 * SH - 16);
            if (fit < scale) scale = fit;

            // Содержимое рисуется на непрозрачной подложке: только так GDI+ даёт
            // ClearType (субпиксельный) текст — он одинаково чёткий в тёмной и светлой теме.
            int cw = (int)Math.Ceiling(W * scale), ch = (int)Math.Ceiling(_cardH * scale);
            using (Bitmap card = new Bitmap(cw, ch, System.Drawing.Imaging.PixelFormat.Format24bppRgb))
            {
                using (Graphics g = Graphics.FromImage(card))
                {
                    g.Clear(Theme.Bg);
                    g.ScaleTransform(scale, scale);
                    PaintCard(g);
                }
                using (Bitmap bmp = Compose(card, scale, SH, W, _cardH, 10))
                {
                    _drawScale = scale;
                    Layered.Push(Handle, bmp, Place(bmp.Width, bmp.Height, scale));
                }
            }
        }
        float _drawScale = 1f;

        Point Place(int bw, int bh, float scale)
        {
            int gap = (int)((SH - 10) * scale); // тень может заходить за край, карточка — в 10 px от него
            bool left = _wa.Left > _sb.Left, top = _wa.Top > _sb.Top;
            int x = left ? _wa.Left - gap : _wa.Right - bw + gap;
            int y = top ? _wa.Top - gap : _wa.Bottom - bh + gap;
            return new Point(x, y);
        }

        // Готовая картинка окна (общая для окна и меню трея): мягкая тень, карточка с
        // ClearType-текстом и сглаженными скруглёнными углами пиксель в пиксель, тонкая рамка.
        internal static Bitmap Compose(Bitmap card, float scale, float sh, float cardW, float cardH, float radius)
        {
            int ox = (int)Math.Round(sh * scale);
            Bitmap bmp = new Bitmap(card.Width + 2 * ox, card.Height + 2 * ox, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.ScaleTransform(scale, scale);
                RectangleF c = new RectangleF(sh, sh, cardW, cardH);
                int maxA = Ui.DarkTheme ? 9 : 5;
                for (int i = (int)sh; i >= 1; i--)
                {
                    float k = 1f - i / sh;
                    RectangleF r = c;
                    r.Inflate(i, i);
                    r.Offset(0, 4);
                    using (GraphicsPath p = Round(r, radius + i))
                    using (SolidBrush b = new SolidBrush(Color.FromArgb((int)(maxA * k * k), 0, 0, 0)))
                        g.FillPath(b, p);
                }
                g.ResetTransform();
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                RectangleF cr = new RectangleF(ox, ox, card.Width, card.Height);
                using (TextureBrush tb = new TextureBrush(card, WrapMode.Clamp))
                using (GraphicsPath p = Round(cr, radius * scale))
                {
                    tb.TranslateTransform(ox, ox);
                    g.FillPath(tb, p);
                }
                cr.Inflate(-0.5f, -0.5f);
                using (GraphicsPath p = Round(cr, radius * scale))
                using (Pen pen = new Pen(Theme.Edge, 1f)) g.DrawPath(pen, p);
            }
            return bmp;
        }

        // содержимое карточки; возвращает её высоту
        float PaintCard(Graphics g)
        {
            _hits.Clear();
            _tracks.Clear();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            float x0 = PAD, x1 = W - PAD, cx = x0 + IND;
            float y = 10;

            // --- верхняя строка: название, кнопка темы и крестик (прячет окно в трей;
            //     выход — в меню по правому клику на значке)
            RectangleF rClose = new RectangleF(W - 10 - 34, y, 34, 30);
            RectangleF rTheme = new RectangleF(rClose.X - 36, y, 34, 30);
            Add("theme", rTheme, ToggleTheme);
            Add("close", rClose, HidePopup);
            if (_hover == "theme") Fill(g, rTheme, 6, Theme.Hover);
            if (_hover == "close") Fill(g, rClose, 6, Theme.Hover);
            Glyph(g, Ui.DarkTheme ? G_SUN : G_MOON, 14, Theme.TextDim, rTheme);
            Glyph(g, G_CLOSE, 11, Theme.TextDim, rClose);
            DrawText(g, "MonitorTray", Fonts.Get(Fonts.Medium, 12), Theme.TextDim, new RectangleF(x0, y, 150, 30), FmtL);
            y += 34;

            // --- плашка автообновления
            if (_app.Updates.Available != null) y = UpdateBanner(g, x0, x1, y);

            // --- мониторы
            int active = 0;
            foreach (Mon m in _mons) if (m.Active) active++;
            y = Section(g, G_MONITOR, Loc.Get("sec_monitors"), y,
                _mons.Count > 0 ? string.Format(Loc.Get("count_on"), active, _mons.Count) : null);
            if (_mons.Count == 0)
            {
                DrawText(g, Loc.Get("no_monitors"), Fonts.Get(Fonts.Regular, 13.5f), Theme.TextDim, new RectangleF(cx, y, x1 - cx, 36), FmtL);
                y += 36;
            }
            for (int i = 0; i < _mons.Count; i++)
            {
                y = MonitorRow(g, i, cx, x1, y);
            }
            y = Divider(g, y + 8);

            // --- яркость (DDC/CI), если мониторы поддерживают
            if (_brights != null && _brights.Count > 0)
            {
                float sy = y;
                y = Section(g, G_SUN, Loc.Get("sec_bright"), y, null);
                if (_brights.Count == 1)
                {
                    y = Slider(g, "b0", BrightName(_brights[0]), _brights[0].Value, cx, x1, y);
                }
                else
                {
                    // кнопка «По мониторам»: показать/скрыть ползунки каждого монитора (как в Twinkle Tray)
                    bool ex = Ui.SlidersExpanded;
                    Font fp = Fonts.Get(Fonts.Medium, 12);
                    string pl = Loc.Get("per_monitor");
                    float pw = g.MeasureString(pl, fp, PointF.Empty, FmtL).Width + 40;
                    RectangleF rp = new RectangleF(x1 + 6 - pw, sy + 4, pw, 26);
                    Add("split", rp, ToggleSplit);
                    Color pc = ex ? Theme.Accent : Theme.TextDim;
                    if (ex) Fill(g, rp, 13, Color.FromArgb(Ui.DarkTheme ? 40 : 24, Theme.Accent));
                    else
                        using (GraphicsPath p = Round(rp, 13))
                        using (Pen pen = new Pen(Theme.Stroke, 1f)) g.DrawPath(pen, p);
                    if (_hover == "split") Fill(g, rp, 13, Theme.Hover);
                    DrawText(g, pl, fp, pc, new RectangleF(rp.X + 12, rp.Y, pw - 34, rp.Height), FmtL);
                    Glyph(g, ex ? G_UP : G_DOWN, 9, pc, new RectangleF(rp.Right - 26, rp.Y, 20, rp.Height));

                    y = Slider(g, "all", Loc.Get("bright_all"), _allValue, cx, x1, y);
                    if (ex)
                        for (int i = 0; i < _brights.Count; i++)
                            y = Slider(g, "b" + i, BrightName(_brights[i]), _brights[i].Value, cx, x1, y);
                }
                y = Divider(g, y + 8);
            }

            // --- действия и настройки: клик по заголовку сворачивает / разворачивает раздел
            bool sx = Ui.SettingsExpanded;
            RectangleF rh = new RectangleF(x0 - 10, y, x1 - x0 + 16, 34);
            Add("settings", rh, ToggleSettings);
            if (_hover == "settings") Fill(g, rh, 6, Theme.Hover);
            Glyph(g, sx ? G_UP : G_DOWN, 11, Theme.TextDim, new RectangleF(x1 - 26, y, 26, 34));
            y = Section(g, G_GEAR, Loc.Get("sec_actions"), y, null);
            if (!sx) return y + 6;

            // погасить все экраны — кнопка-карточка
            RectangleF rs = new RectangleF(x0, y, x1 - x0, 60);
            Add("sleep", rs, SleepAll);
            Card(g, rs, _hover == "sleep");
            Glyph(g, G_MOON, 19, Theme.Accent, new RectangleF(rs.X + 6, rs.Y, 44, rs.Height));
            DrawText(g, Loc.Get("sleep_title"), Fonts.Get(Fonts.Medium, 14), Theme.Text, new RectangleF(rs.X + 52, rs.Y + 10, rs.Width - 64, 21), FmtL);
            DrawText(g, Loc.Get("sleep_hint"), Fonts.Get(Fonts.Regular, 11.5f), Theme.TextDim, new RectangleF(rs.X + 52, rs.Y + 31, rs.Width - 64, 18), FmtL);
            y += rs.Height + 6;

            // AFK-режим — карточка с переключателем; когда включён, ниже выбор времени
            y = AfkCard(g, x0, x1, y);



            return y + 8;
        }

        float Section(Graphics g, string glyph, string title, float y, string right)
        {
            if (glyph == G_MONITOR) MonitorIcon(g, new RectangleF(PAD - 1, y + 7, 24, 21), -1, true);
            else Glyph(g, glyph, 18, Theme.Accent, new RectangleF(PAD - 4, y, 30, 34));
            DrawText(g, title, Fonts.Get(Fonts.SemiBold, 15.5f), Theme.Text, new RectangleF(PAD + IND, y, W - 2 * PAD - IND - 90, 34), FmtL);
            if (right != null)
                DrawText(g, right, Fonts.Get(Fonts.Regular, 12), Theme.TextDim, new RectangleF(W - PAD - 120, y, 120, 34), FmtR);
            return y + 38;
        }

        float Divider(Graphics g, float y)
        {
            using (Pen p = new Pen(Theme.Divider, 1f)) g.DrawLine(p, 1, y, W - 1, y);
            return y + 14;
        }

        float MonitorRow(Graphics g, int i, float cx, float x1, float y)
        {
            Mon m = _mons[i];
            string id = "mon" + i;
            bool busy = _busyIdx == i;
            RectangleF row = new RectangleF(cx - 10, y, x1 - cx + 14, 42);
            int idx = i;
            Add(id, row, delegate { ToggleMonitor(idx); });
            if (_hover == id) Fill(g, row, 6, Theme.Hover);

            MonitorIcon(g, new RectangleF(cx - 2, y + 9, 27, 23), i, m.Active);

            // имя + метка («Основной» / «Выключен»)
            float sw = 46;                       // переключатель справа
            float nx = cx + 34, maxW = x1 - sw - 10 - nx;
            Font fn = Fonts.Get(Fonts.Regular, 14);
            Font fb = Fonts.Get(Fonts.Medium, 11);
            string badge = !m.Active ? Loc.Get(_app.Afk.IsSleeping(m) ? "badge_afk" : "badge_off")
                                     : (m.Primary ? Loc.Get("badge_primary") : null);
            float bwid = badge != null ? g.MeasureString(badge, fb, PointF.Empty, FmtL).Width + 16 : 0;
            float tw = g.MeasureString(m.Name, fn, PointF.Empty, FmtL).Width + 2;
            float nameW = Math.Min(tw, maxW - (badge != null ? bwid + 8 : 0));
            DrawText(g, m.Name, fn, m.Active ? Theme.Text : Theme.TextDim, new RectangleF(nx, y, nameW, row.Height), FmtL);
            if (badge != null)
            {
                RectangleF rb = new RectangleF(nx + nameW + 8, y + row.Height / 2 - 10, bwid, 20);
                Color bc = m.Active ? Theme.Accent : Theme.Off;
                Fill(g, rb, 10, Color.FromArgb(Ui.DarkTheme ? 40 : 26, bc));
                DrawText(g, badge, fb, bc, rb, FmtC);
            }

            Switch(g, new RectangleF(x1 - sw + 6, y + row.Height / 2 - 10, 40, 20), m.Active, _hover == id, busy);
            return y + row.Height + 2;
        }

        // переключатель в стиле Windows 11 (busy — серый, бегунок посередине)
        internal static void Switch(Graphics g, RectangleF ts, bool on, bool hov, bool busy)
        {
            if (busy)
            {
                using (GraphicsPath p = Round(ts, 10))
                using (Pen pen = new Pen(Theme.TextDim, 1f)) g.DrawPath(pen, p);
                float d = 10;
                using (SolidBrush b = new SolidBrush(Theme.TextDim))
                    g.FillEllipse(b, ts.X + ts.Width / 2 - d / 2, ts.Y + 5, d, d);
            }
            else if (on)
            {
                Fill(g, ts, 10, Theme.Accent);
                float d = hov ? 14 : 12;
                using (SolidBrush b = new SolidBrush(Theme.AccentText))
                    g.FillEllipse(b, ts.Right - 10 - d / 2, ts.Y + 10 - d / 2, d, d);
            }
            else
            {
                using (GraphicsPath p = Round(ts, 10))
                using (Pen pen = new Pen(Theme.TextDim, 1f)) g.DrawPath(pen, p);
                float d = hov ? 12 : 10;
                using (SolidBrush b = new SolidBrush(Theme.TextDim))
                    g.FillEllipse(b, ts.X + 10 - d / 2, ts.Y + 10 - d / 2, d, d);
            }
        }

        // «30 мин», «1 ч», «1 ч 15 мин»
        static string AfkTime(int mins)
        {
            string m = Loc.Get("afk_min"), h = Loc.Get("afk_hour");
            if (mins < 60) return mins + " " + m;
            return (mins / 60) + " " + h + (mins % 60 > 0 ? " " + (mins % 60) + " " + m : "");
        }

        // шаг времени: до 10 мин — по 1, до часа — по 5, дальше — по 15 (максимум 4 часа)
        static int AfkStep(int v, int dir)
        {
            if (dir > 0) v += v < 10 ? 1 : (v < 60 ? 5 : 15);
            else v -= v <= 10 ? 1 : (v <= 60 ? 5 : 15);
            return Math.Max(1, Math.Min(240, v));
        }

        // карточка AFK-режима: заголовок с переключателем, при включении — своё время «− 30 мин +»
        float AfkCard(Graphics g, float x0, float x1, float y)
        {
            bool on = Ui.AfkEnabled;
            RectangleF head = new RectangleF(x0, y, x1 - x0, 60);
            RectangleF card = new RectangleF(x0, y, x1 - x0, on ? 100 + _mons.Count * 32 + 6 : 60);
            Add("afk", head, ToggleAfk);
            Card(g, card, false);
            if (_hover == "afk")
                using (GraphicsPath p = Round(head, 7))
                using (SolidBrush b = new SolidBrush(Theme.Hover)) g.FillPath(b, p);
            Glyph(g, G_CLOCK, 18, Theme.Accent, new RectangleF(head.X + 6, head.Y, 44, head.Height));
            float tw = head.Width - 52 - 62;
            DrawText(g, Loc.Get("afk_title"), Fonts.Get(Fonts.Medium, 14), Theme.Text, new RectangleF(head.X + 52, head.Y + 10, tw, 21), FmtL);
            DrawText(g, string.Format(Loc.Get("afk_sub"), AfkTime(Ui.AfkMinutes)), Fonts.Get(Fonts.Regular, 11.5f), Theme.TextDim,
                new RectangleF(head.X + 52, head.Y + 31, tw, 18), FmtL);
            Switch(g, new RectangleF(head.Right - 54, head.Y + 20, 40, 20), on, _hover == "afk", false);
            if (on)
            {
                float cy = head.Y + 62;
                DrawText(g, Loc.Get("afk_after"), Fonts.Get(Fonts.Regular, 12.5f), Theme.TextDim, new RectangleF(head.X + 52, cy, 60, 28), FmtL);
                // [−]  1 ч 15 мин  [+]; колесо мыши над ним тоже меняет время
                float sx = head.X + 112;
                RectangleF rm = new RectangleF(sx, cy, 30, 28);
                RectangleF rv = new RectangleF(sx + 32, cy, 96, 28);
                RectangleF rp = new RectangleF(sx + 130, cy, 30, 28);
                Add("afk_val", rv, null);
                Add("afk_minus", rm, delegate { SetAfkMinutes(AfkStep(Ui.AfkMinutes, -1)); });
                Add("afk_plus", rp, delegate { SetAfkMinutes(AfkStep(Ui.AfkMinutes, 1)); });
                foreach (RectangleF rb in new RectangleF[] { rm, rp })
                {
                    string id = rb == rm ? "afk_minus" : "afk_plus";
                    if (_hover == id) Fill(g, rb, 14, Theme.Hover);
                    using (GraphicsPath p = Round(rb, 14))
                    using (Pen pen = new Pen(Theme.Stroke, 1f)) g.DrawPath(pen, p);
                }
                Glyph(g, "\uE738", 11, Theme.Text, rm);   // минус
                Glyph(g, "\uE710", 11, Theme.Text, rp);   // плюс
                if (_hover == "afk_val") Fill(g, rv, 14, Theme.Hover);
                DrawText(g, AfkTime(Ui.AfkMinutes), Fonts.Get(Fonts.Medium, 13), Theme.Text, rv, FmtC);

                // какие мониторы уходят в AFK — у каждого свой переключатель
                using (Pen pen = new Pen(Theme.Divider, 1f)) g.DrawLine(pen, head.X + 52, head.Y + 97, head.Right - 14, head.Y + 97);
                float ry = head.Y + 100;
                for (int i = 0; i < _mons.Count; i++)
                {
                    Mon m = _mons[i];
                    string id = "afkm" + i;
                    RectangleF rr = new RectangleF(head.X + 44, ry, head.Width - 52, 32);
                    Add(id, rr, delegate { ToggleAfkMonitor(m); });
                    if (_hover == id) Fill(g, rr, 6, Theme.Hover);
                    MonitorIcon(g, new RectangleF(rr.X + 8, ry + 8, 20, 17), i, m.Active);
                    DrawText(g, m.Name, Fonts.Get(Fonts.Regular, 13), Theme.Text, new RectangleF(rr.X + 36, ry, rr.Width - 36 - 56, 32), FmtL);
                    Switch(g, new RectangleF(head.Right - 54, ry + 6, 40, 20), Ui.AfkIncluded(m), _hover == id, false);
                    ry += 32;
                }
            }
            return y + card.Height + 6;
        }

        // плашка «Доступна версия X» с кнопкой «Обновить» (во время загрузки — проценты)
        float UpdateBanner(Graphics g, float x0, float x1, float y)
        {
            Updater u = _app.Updates;
            RectangleF r = new RectangleF(x0 - 8, y, x1 - x0 + 16, 54);
            Fill(g, r, 8, Color.FromArgb(Ui.DarkTheme ? 36 : 20, Theme.Accent));
            Glyph(g, G_DOWNLOAD, 17, Theme.Accent, new RectangleF(r.X + 4, r.Y, 40, r.Height));
            string sub = u.Downloading ? string.Format(Loc.Get("upd_progress"), u.Progress)
                       : (u.Error != null ? Loc.Get("upd_failed") + ": " + u.Error : Loc.Get("upd_sub"));
            float bw = 0;
            if (!u.Downloading)
            {
                string bt = Loc.Get(u.Error != null ? "upd_open" : "upd_button");
                Font fbt = Fonts.Get(Fonts.Medium, 12.5f);
                bw = g.MeasureString(bt, fbt, PointF.Empty, FmtL).Width + 28;
                RectangleF rb = new RectangleF(r.Right - 12 - bw, r.Y + 13, bw, 28);
                Add("upd", rb, u.Error != null ? (Action)OpenReleases : (Action)u.Start);
                Fill(g, rb, 14, _hover == "upd" ? Blend(Theme.Accent, Theme.Text, 0.15f) : Theme.Accent);
                DrawText(g, bt, fbt, Theme.AccentText, rb, FmtC);
                bw += 12;
            }
            float tx = r.X + 44, tw = r.Right - 12 - bw - tx;
            DrawText(g, string.Format(Loc.Get("upd_title"), Updater.Pretty(u.Available)), Fonts.Get(Fonts.Medium, 13.5f), Theme.Text,
                new RectangleF(tx, r.Y + 8, tw, 20), FmtL);
            DrawText(g, sub, Fonts.Get(Fonts.Regular, 11.5f), Theme.TextDim, new RectangleF(tx, r.Y + 28, tw, 18), FmtL);
            return y + r.Height + 10;
        }

        float Slider(Graphics g, string id, string label, int value, float cx, float x1, float y)
        {
            DrawText(g, label, Fonts.Get(Fonts.Regular, 13), Theme.Text, new RectangleF(cx, y, x1 - cx, 20), FmtL);
            y += 20;
            float vw = 48;
            RectangleF track = new RectangleF(cx + 2, y + 15, x1 - vw - cx - 10, 0);
            _tracks[id] = track;
            Add("s:" + id, new RectangleF(cx - 8, y, x1 - vw - cx + 8, 30), null);
            bool act = _hover == "s:" + id || _drag == id;

            float tx = track.X + track.Width * value / 100f;
            using (Pen p = new Pen(Theme.Track, 4f))
            {
                p.StartCap = p.EndCap = LineCap.Round;
                g.DrawLine(p, tx, track.Y, track.Right, track.Y);
            }
            using (Pen p = new Pen(Theme.Accent, 4f))
            {
                p.StartCap = p.EndCap = LineCap.Round;
                g.DrawLine(p, track.X, track.Y, tx, track.Y);
            }
            // бегунок: светлое кольцо + точка акцентного цвета
            float R = 10;
            using (SolidBrush b = new SolidBrush(Theme.Surface)) g.FillEllipse(b, tx - R, track.Y - R, 2 * R, 2 * R);
            using (Pen p = new Pen(Theme.Stroke, 1f)) g.DrawEllipse(p, tx - R, track.Y - R, 2 * R, 2 * R);
            float r = _drag == id ? 5f : (act ? 7f : 6f);
            using (SolidBrush b = new SolidBrush(Theme.Accent)) g.FillEllipse(b, tx - r, track.Y - r, 2 * r, 2 * r);

            DrawText(g, value + "%", Fonts.Get(Fonts.Medium, 13), Theme.Text, new RectangleF(x1 - vw, y, vw, 30), FmtR);
            return y + 34;
        }

        string BrightName(BrightEntry b)
        {
            foreach (Mon m in _mons) if (m.GdiName == b.Gdi) return m.Name;
            return b.Gdi;
        }

        // Цветной значок монитора: рамка, экран с «обоями» в духе Windows 11 и подставка.
        // palette: -1 — значок заголовка, 0.. — по номеру монитора; выключенный — тёмный экран.
        static readonly Color[][] Wallpapers = new Color[][]
        {
            new Color[] { Color.FromArgb(92, 192, 255), Color.FromArgb(0, 103, 214), Color.FromArgb(0, 38, 118), Color.FromArgb(175, 228, 255) },
            new Color[] { Color.FromArgb(255, 214, 92), Color.FromArgb(247, 108, 48), Color.FromArgb(186, 28, 72), Color.FromArgb(255, 236, 170) },
            new Color[] { Color.FromArgb(124, 236, 200), Color.FromArgb(0, 168, 140), Color.FromArgb(0, 78, 92), Color.FromArgb(205, 255, 236) },
            new Color[] { Color.FromArgb(212, 164, 255), Color.FromArgb(128, 78, 230), Color.FromArgb(58, 30, 140), Color.FromArgb(236, 214, 255) },
        };
        static readonly Color[] HeaderWallpaper =
            { Color.FromArgb(150, 232, 255), Color.FromArgb(38, 170, 240), Color.FromArgb(8, 96, 188), Color.FromArgb(215, 245, 255) };

        internal static void MonitorIcon(Graphics g, RectangleF r, int palette, bool on)
        {
            float w = r.Width, cx = r.X + w / 2;
            float bodyH = r.Height * 0.78f;
            RectangleF body = new RectangleF(r.X, r.Y, w, bodyH);
            Color bezel = Ui.DarkTheme ? Color.FromArgb(14, 15, 18) : Color.FromArgb(36, 40, 48);

            // подставка
            using (SolidBrush b = new SolidBrush(bezel))
            {
                float nw = w * 0.17f;
                g.FillRectangle(b, cx - nw / 2, body.Bottom - 0.5f, nw, r.Height * 0.13f + 0.5f);
            }
            Fill(g, new RectangleF(cx - w * 0.26f, r.Bottom - r.Height * 0.1f, w * 0.52f, r.Height * 0.1f), 1f, bezel);

            // корпус
            Fill(g, body, 2.6f, bezel);
            if (Ui.DarkTheme)
                using (GraphicsPath p = Round(body, 2.6f))
                using (Pen pen = new Pen(Color.FromArgb(52, 255, 255, 255), 0.8f)) g.DrawPath(pen, p);

            // экран
            float b0 = Math.Max(1.4f, w * 0.06f);
            RectangleF scr = new RectangleF(body.X + b0, body.Y + b0, body.Width - 2 * b0, body.Height - b0 * 2.2f);
            using (GraphicsPath sp = Round(scr, 1.2f))
            {
                GraphicsState st = g.Save();
                g.SetClip(sp);
                RectangleF gr = RectangleF.Inflate(scr, 1, 1);
                if (on)
                {
                    Color[] c = palette < 0 ? HeaderWallpaper : Wallpapers[palette % Wallpapers.Length];
                    using (LinearGradientBrush lb = new LinearGradientBrush(gr, c[0], c[2], 35f))
                    {
                        ColorBlend cb = new ColorBlend(3);
                        cb.Colors = new Color[] { c[0], c[1], c[2] };
                        cb.Positions = new float[] { 0f, 0.5f, 1f };
                        lb.InterpolationColors = cb;
                        g.FillRectangle(lb, gr);
                    }
                    // «лепесток» как на обоях Windows 11
                    using (SolidBrush sb = new SolidBrush(Color.FromArgb(150, c[3])))
                        g.FillEllipse(sb, scr.X + scr.Width * 0.22f, scr.Y + scr.Height * 0.38f, scr.Width * 1.25f, scr.Height * 1.5f);
                    using (SolidBrush sb = new SolidBrush(Color.FromArgb(200, c[1])))
                        g.FillEllipse(sb, scr.X + scr.Width * 0.42f, scr.Y + scr.Height * 0.62f, scr.Width * 1.1f, scr.Height * 1.3f);
                    using (Pen pp = new Pen(Color.FromArgb(150, 255, 255, 255), Math.Max(0.8f, w * 0.04f)))
                        g.DrawEllipse(pp, scr.X + scr.Width * 0.3f, scr.Y + scr.Height * 0.5f, scr.Width * 1.2f, scr.Height * 1.4f);
                }
                else
                {
                    using (LinearGradientBrush lb = new LinearGradientBrush(gr, Color.FromArgb(70, 74, 82), Color.FromArgb(26, 28, 32), 35f))
                        g.FillRectangle(lb, gr);
                }
                // блик стекла
                using (GraphicsPath gl = new GraphicsPath())
                {
                    gl.AddPolygon(new PointF[] {
                        new PointF(scr.X, scr.Y), new PointF(scr.X + scr.Width * 0.55f, scr.Y),
                        new PointF(scr.X, scr.Y + scr.Height * 0.75f) });
                    using (SolidBrush sb = new SolidBrush(Color.FromArgb(on ? 34 : 22, 255, 255, 255))) g.FillPath(sb, gl);
                }
                g.Restore(st);
            }
        }

        // ---------------------------------------------------------------- примитивы
        Hit Add(string id, RectangleF r, Action click)
        {
            Hit h = new Hit();
            h.Id = id; h.R = r; h.Click = click;
            _hits.Add(h);
            return h;
        }

        void Card(Graphics g, RectangleF r, bool hover)
        {
            using (GraphicsPath p = Round(r, 7))
            {
                using (SolidBrush b = new SolidBrush(hover ? Theme.SurfaceHover : Theme.Surface)) g.FillPath(b, p);
                using (Pen pen = new Pen(Theme.Stroke, 1f)) g.DrawPath(pen, p);
            }
        }

        internal static void Fill(Graphics g, RectangleF r, float rad, Color c)
        {
            using (GraphicsPath p = Round(r, rad))
            using (SolidBrush b = new SolidBrush(c)) g.FillPath(b, p);
        }

        internal static void DrawText(Graphics g, string s, Font f, Color c, RectangleF r, StringFormat fmt)
        {
            using (SolidBrush b = new SolidBrush(c)) g.DrawString(s, f, b, r, fmt);
        }

        internal static void Glyph(Graphics g, string glyph, float px, Color c, RectangleF r)
        {
            // значки — без субпиксельного сглаживания, иначе на тонких линиях цветная кайма
            TextRenderingHint old = g.TextRenderingHint;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using (SolidBrush b = new SolidBrush(c)) g.DrawString(glyph, Fonts.Get(Fonts.Icons, px), b, r, FmtC);
            g.TextRenderingHint = old;
        }

        static Color Blend(Color a, Color b, float t)
        {
            return Color.FromArgb(255, (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        internal static GraphicsPath Round(RectangleF r, float rad)
        {
            GraphicsPath p = new GraphicsPath();
            float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
            if (d <= 0.5f) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // ---------------------------------------------------------------- мышь
        PointF ToCard(Point p) { return new PointF(p.X / _drawScale - SH, p.Y / _drawScale - SH); }

        Hit HitAt(PointF p)
        {
            for (int i = _hits.Count - 1; i >= 0; i--) if (_hits[i].R.Contains(p)) return _hits[i];
            return null;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            PointF p = ToCard(e.Location);
            if (_drag != null) { SetSlider(_drag, p.X); Render(); return; }
            Hit h = HitAt(p);
            string id = h != null ? h.Id : null;
            Cursor = h != null ? Cursors.Hand : Cursors.Default;
            if (id != _hover)
            {
                _hover = id;
                _tip.Hide(this);
                _tipTimer.Stop();
                if (id == "close" || id == "theme") _tipTimer.Start();
                Render();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _tipTimer.Stop();
            if (_drag == null && _hover != null) { _hover = null; Render(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            PointF p = ToCard(e.Location);
            Hit h = HitAt(p);
            if (h == null) return;
            if (h.Id.StartsWith("s:"))
            {
                _drag = h.Id.Substring(2);
                Capture = true;
                SetSlider(_drag, p.X);
                Render();
                return;
            }
            _press = h.Id;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_drag != null) { _drag = null; Capture = false; Render(); return; }
            Hit h = HitAt(ToCard(e.Location));
            string pressed = _press;
            _press = null;
            if (h != null && h.Id == pressed && h.Click != null) h.Click();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            Hit h = HitAt(ToCard(PointToClient(Cursor.Position)));
            if (h != null && (h.Id == "afk_val" || h.Id == "afk_minus" || h.Id == "afk_plus"))
            {
                SetAfkMinutes(AfkStep(Ui.AfkMinutes, e.Delta > 0 ? 1 : -1));
                return;
            }
            if (h == null || !h.Id.StartsWith("s:")) return;
            string id = h.Id.Substring(2);
            int v = (id == "all" ? _allValue : _brights[int.Parse(id.Substring(1))].Value) + (e.Delta > 0 ? 5 : -5);
            SetValue(id, v);
            Render();
        }

        void SetSlider(string id, float x)
        {
            RectangleF t;
            if (!_tracks.TryGetValue(id, out t) || t.Width <= 0) return;
            SetValue(id, (int)Math.Round((x - t.X) / t.Width * 100));
        }

        void SetValue(string id, int v)
        {
            if (_brights == null || _brights.Count == 0) return;
            v = Math.Max(0, Math.Min(100, v));
            if (id == "all")
            {
                _allValue = v;
                foreach (BrightEntry b in _brights) { b.Value = v; b.Pending = v; }
            }
            else
            {
                BrightEntry b = _brights[int.Parse(id.Substring(1))];
                b.Value = v; b.Pending = v;
                int sum = 0;
                foreach (BrightEntry bb in _brights) sum += bb.Value;
                _allValue = sum / _brights.Count;
            }
            _brightTimer.Stop();
            _brightTimer.Start(); // применить отложенно, чтобы не заспамить DDC
        }

        // ---------------------------------------------------------------- действия
        void ToggleTheme()
        {
            Ui.DarkTheme = !Ui.DarkTheme;
            Theme.Apply(Ui.DarkTheme);
            Ui.Save();
            _app.RefreshIcon();
            Render();
        }

        void ToggleSettings()
        {
            Ui.SettingsExpanded = !Ui.SettingsExpanded;
            Ui.Save();
            Render();
        }

        void ToggleSplit()
        {
            Ui.SlidersExpanded = !Ui.SlidersExpanded;
            Ui.Save();
            Render();
        }

        void ToggleAfk()
        {
            Ui.AfkEnabled = !Ui.AfkEnabled;
            Ui.Save();
            _app.Afk.Update();
            Render();
        }

        void SetAfkMinutes(int v)
        {
            Ui.AfkMinutes = v;
            Ui.Save();
            Render();
        }

        void OpenReleases()
        {
            HidePopup();
            try { Process.Start(Updater.ReleasesPage); } catch { }
        }

        // AFK-режим что-то выключил/включил — показать свежее состояние, если окно открыто
        public void RefreshIfVisible()
        {
            if (Visible && !Busy && _drag == null) { Reload(); Render(); }
        }

        public void RepaintIfVisible()
        {
            if (Visible) Render();
        }

        void ToggleAfkMonitor(Mon m)
        {
            Ui.AfkMonitors[Ui.MonKey(m)] = !Ui.AfkIncluded(m);
            Ui.Save();
            Render();
        }

        void ToggleMonitor(int i)
        {
            if (_busyIdx >= 0 || i >= _mons.Count) return;
            Mon m = _mons[i];
            _busyIdx = i;
            Render();
            BeginInvoke((MethodInvoker)delegate
            {
                Busy = true;
                try { _app.Toggle(m); }
                finally { Busy = false; _busyIdx = -1; }
                if (!Visible) return;
                Reload();
                AnchorAt(Cursor.Position); // раскладка экранов могла измениться
                Render();
                Activate();
                Native.SetForegroundWindow(Handle);
            });
        }

        void SleepAll()
        {
            HidePopup();
            System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
            t.Interval = 400; // дать окну исчезнуть и мыши успокоиться
            t.Tick += delegate { t.Stop(); t.Dispose(); Native.DpmsOffAll(); };
            t.Start();
        }

    }

    // ------------------------------------------------------------------ GUI: меню трея
    // Меню по правому клику на значке, как в Windows 11: переключатели «Запускать при входе
    // в Windows» и «Проверять обновления», язык EN | RU, «Открыть MonitorTray», «О программе» и «Выход».
    // Рисуется так же, как основное окно (тема, ClearType, скругления, тень).
    internal class TrayMenu : Form
    {
        const float W = 300;     // ширина (логические px)
        const float SH = 14;     // поле под тень
        const float ROW = 40;

        class Hit { public RectangleF R; public string Id; public Action Click; }

        readonly TrayApp _app;
        readonly List<Hit> _hits = new List<Hit>();
        string _hover, _press;
        bool _auto;
        float _scale = 1f, _drawScale = 1f, _cardH = 120;
        Point _at;
        Rectangle _wa;
        public int HiddenAt;

        public TrayMenu(TrayApp app)
        {
            _app = app;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            KeyPreview = true;
            Text = "MonitorTray";
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x80000 /*WS_EX_LAYERED*/ | 0x80 /*WS_EX_TOOLWINDOW*/;
                return cp;
            }
        }

        public void ShowAt(Point p)
        {
            IntPtr h = Handle;
            _at = p;
            _wa = Screen.FromPoint(p).WorkingArea;
            _scale = Layered.ScaleAt(p);
            _auto = Autostart.IsEnabled();
            _hover = _press = null;
            Render();
            Show();
            Activate();
            Native.SetForegroundWindow(Handle);
        }

        public void HideMenu()
        {
            if (!Visible) return;
            Hide();
            HiddenAt = Environment.TickCount;
            _hover = _press = null;
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            HideMenu();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) HideMenu();
            base.OnKeyDown(e);
        }

        void Render()
        {
            using (Bitmap tmp = new Bitmap(1, 1))
            using (Graphics g = Graphics.FromImage(tmp))
                _cardH = PaintCard(g);
            int cw = (int)Math.Ceiling(W * _scale), ch = (int)Math.Ceiling(_cardH * _scale);
            using (Bitmap card = new Bitmap(cw, ch, System.Drawing.Imaging.PixelFormat.Format24bppRgb))
            {
                using (Graphics g = Graphics.FromImage(card))
                {
                    g.Clear(Theme.Bg);
                    g.ScaleTransform(_scale, _scale);
                    PaintCard(g);
                }
                using (Bitmap bmp = Popup.Compose(card, _scale, SH, W, _cardH, 8))
                {
                    // как меню Windows: над курсором (над панелью задач), не вылезая за рабочую область
                    int ox = (int)Math.Round(SH * _scale);
                    int left = Math.Max(_wa.Left + 4, Math.Min(_at.X - cw / 2, _wa.Right - cw - 4));
                    int bottom = Math.Min(_at.Y, _wa.Bottom) - 8;
                    int top = bottom - ch;
                    if (top < _wa.Top) top = Math.Min(_at.Y + 8, _wa.Bottom - ch);
                    _drawScale = _scale;
                    Layered.Push(Handle, bmp, new Point(left - ox, top - ox));
                }
            }
        }

        float PaintCard(Graphics g)
        {
            _hits.Clear();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            float y = 6;
            y = ToggleRow(g, "auto", Loc.Get("autostart"), _auto, ToggleAuto, y);
            y = ToggleRow(g, "upd", Loc.Get("upd_check"), Ui.AutoUpdate, ToggleUpdates, y);
            y = LangRow(g, y);
            using (Pen p = new Pen(Theme.Divider, 1f)) g.DrawLine(p, 12, y + 4, W - 12, y + 4);
            y += 9;
            y = ItemRow(g, "open", "", Loc.Get("menu_open"), delegate { HideMenu(); _app.OpenPopup(); }, y);
            y = ItemRow(g, "about", "\uE946", Loc.Get("about"), delegate
            {
                HideMenu();
                MessageBox.Show(Loc.Get("about_text"), Loc.Get("about"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            }, y);
            y = ItemRow(g, "exit", "", Loc.Get("menu_exit"), delegate { HideMenu(); _app.ExitApp(); }, y);
            return y + 6;
        }

        // строка с переключателем справа (переключается, меню остаётся открытым)
        float ToggleRow(Graphics g, string id, string text, bool on, Action click, float y)
        {
            RectangleF r = new RectangleF(6, y, W - 12, ROW);
            Add(id, r, click);
            if (_hover == id) Popup.Fill(g, r, 6, Theme.Hover);
            Popup.DrawText(g, text, Fonts.Get(Fonts.Regular, 13.5f), Theme.Text, new RectangleF(r.X + 12, r.Y, r.Width - 76, r.Height), Popup.FmtL);
            Popup.Switch(g, new RectangleF(r.Right - 52, r.Y + 10, 40, 20), on, _hover == id, false);
            return y + ROW;
        }

        // строка «Язык» с переключателем EN | RU справа (язык меняется сразу, меню остаётся)
        float LangRow(Graphics g, float y)
        {
            RectangleF r = new RectangleF(6, y, W - 12, ROW);
            Popup.DrawText(g, Loc.Get("language"), Fonts.Get(Fonts.Regular, 13.5f), Theme.Text,
                new RectangleF(r.X + 12, r.Y, r.Width - 120, r.Height), Popup.FmtL);
            string[] codes = { "en", "ru" }, names = { "EN", "RU" };
            RectangleF seg = new RectangleF(r.Right - 12 - 92, r.Y + 7, 92, 26);
            using (GraphicsPath p = Popup.Round(seg, 13))
            using (Pen pen = new Pen(Theme.Stroke, 1f)) g.DrawPath(pen, p);
            for (int k = 0; k < 2; k++)
            {
                string code = codes[k];
                string id = "lang_" + code;
                RectangleF rb = new RectangleF(seg.X + 2 + k * 44, seg.Y + 2, 44, 22);
                Add(id, rb, delegate { SetLang(code); });
                bool sel = Loc.Lang == code;
                if (sel) Popup.Fill(g, rb, 11, Theme.Accent);
                else if (_hover == id) Popup.Fill(g, rb, 11, Theme.Hover);
                Popup.DrawText(g, names[k], Fonts.Get(Fonts.Medium, 12), sel ? Theme.AccentText : Theme.Text, rb, Popup.FmtC);
            }
            return y + ROW;
        }

        void SetLang(string code)
        {
            if (Loc.Lang == code) return;
            Loc.Set(code);
            _app.UpdateTooltip();
            Render();
        }

        // строка-команда со значком слева
        float ItemRow(Graphics g, string id, string glyph, string text, Action click, float y)
        {
            RectangleF r = new RectangleF(6, y, W - 12, ROW);
            Add(id, r, click);
            if (_hover == id) Popup.Fill(g, r, 6, Theme.Hover);
            Popup.Glyph(g, glyph, 15, Theme.TextDim, new RectangleF(r.X + 4, r.Y, 34, r.Height));
            Popup.DrawText(g, text, Fonts.Get(Fonts.Regular, 13.5f), Theme.Text, new RectangleF(r.X + 42, r.Y, r.Width - 50, r.Height), Popup.FmtL);
            return y + ROW;
        }

        void Add(string id, RectangleF r, Action click)
        {
            Hit h = new Hit();
            h.Id = id; h.R = r; h.Click = click;
            _hits.Add(h);
        }

        Hit HitAt(Point p)
        {
            PointF c = new PointF(p.X / _drawScale - SH, p.Y / _drawScale - SH);
            for (int i = _hits.Count - 1; i >= 0; i--) if (_hits[i].R.Contains(c)) return _hits[i];
            return null;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Hit h = HitAt(e.Location);
            string id = h != null ? h.Id : null;
            if (id != _hover) { _hover = id; Render(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hover != null) { _hover = null; Render(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Hit h = HitAt(e.Location);
            _press = h != null ? h.Id : null;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            Hit h = HitAt(e.Location);
            string pressed = _press;
            _press = null;
            if (h != null && h.Id == pressed && h.Click != null) h.Click();
        }

        void ToggleAuto()
        {
            try { Autostart.Set(!_auto); } catch { }
            _auto = Autostart.IsEnabled();
            Render();
        }

        void ToggleUpdates()
        {
            Ui.AutoUpdate = !Ui.AutoUpdate;
            Ui.Save();
            if (Ui.AutoUpdate) _app.Updates.Check();
            Render();
        }
    }

    // ------------------------------------------------------------------ GUI: трей
    internal class TrayApp : ApplicationContext
    {
        internal NotifyIcon _icon;
        Icon _appIcon;
        Popup _popup;
        TrayMenu _menu;
        readonly Control _ui = new Control();   // для передачи результатов фоновых потоков в UI
        internal AfkWatcher Afk;
        internal Updater Updates;
        Version _announced;                       // о какой версии уже сказали в уведомлении
        internal static bool JustUpdated;         // запущены после автообновления

        public TrayApp()
        {
            IntPtr h = _ui.Handle; // создать окно-получатель для BeginInvoke
            _appIcon = AppIcon.Create(Ui.DarkTheme);
            _popup = new Popup(this);
            _menu = new TrayMenu(this);

            _icon = new NotifyIcon();
            _icon.Icon = _appIcon;
            _icon.Text = Loc.Get("tray_title");
            _icon.Visible = true;
            _icon.MouseUp += new MouseEventHandler(IconMouseUp);
            _icon.BalloonTipClicked += delegate { OpenPopup(); };

            // смена масштаба экрана — перерисовать значок под новый размер
            SystemEvents.DisplaySettingsChanged += OnSystemChanged;

            Afk = new AfkWatcher(this);
            Updates = new Updater(this);

            UpdateTooltip();
            if (JustUpdated)
                _icon.ShowBalloonTip(4000, Loc.Get("tray_title"),
                    string.Format(Loc.Get("upd_done"), Updater.Pretty(Updater.Current)), ToolTipIcon.Info);
        }

        // выполнить в UI-потоке (из фоновых потоков автообновления)
        internal void Post(MethodInvoker a)
        {
            try { _ui.BeginInvoke(a); } catch { }
        }

        // окно открыто или в нём идёт переключение — AFK-режим ждёт
        internal bool PopupBusy { get { return _popup.Visible || _popup.Busy; } }

        // AFK-режим выключил или включил монитор
        internal void OnAfkChanged(string name, bool off)
        {
            UpdateTooltip();
            _popup.RefreshIfVisible();
            if (off && !Ui.AfkHintShown)
            {
                Ui.AfkHintShown = true;
                Ui.Save();
                _icon.ShowBalloonTip(8000, Loc.Get("tray_title"), string.Format(Loc.Get("afk_off_hint"), name), ToolTipIcon.Info);
            }
        }

        // у автообновления новости: нашлась версия, идёт загрузка, ошибка
        internal void OnUpdateChanged(bool found)
        {
            if (found && Updates.Available != null && Updates.Available != _announced)
            {
                _announced = Updates.Available;
                _icon.ShowBalloonTip(6000, Loc.Get("tray_title"),
                    string.Format(Loc.Get("upd_balloon"), Updater.Pretty(Updates.Available)), ToolTipIcon.Info);
            }
            _popup.RepaintIfVisible();
        }

        void OnSystemChanged(object sender, EventArgs e) { RefreshIcon(); }

        // перерисовать значок трея (после смены темы программы или масштаба)
        internal void RefreshIcon()
        {
            Icon old = _appIcon;
            _appIcon = AppIcon.Create(Ui.DarkTheme);
            _icon.Icon = _appIcon;
            old.Dispose();
        }

        internal void UpdateTooltip()
        {
            try
            {
                List<Mon> mons = Svc.List();
                int active = 0;
                foreach (Mon m in mons) if (m.Active) active++;
                string t = string.Format(Loc.Get("tooltip"), active, mons.Count);
                if (t.Length > 63) t = t.Substring(0, 63);
                _icon.Text = t;
            }
            catch { }
        }

        // левый клик — окно, правый — меню (автозапуск, обновления, выход)
        void IconMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                _popup.HidePopup();
                if (_menu.Visible) { _menu.HideMenu(); return; }
                if (unchecked(Environment.TickCount - _menu.HiddenAt) < 300) return;
                _menu.ShowAt(Cursor.Position);
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            _menu.HideMenu();
            if (_popup.Visible) { _popup.HidePopup(); return; }
            // это же нажатие только что закрыло окно (оно теряет фокус раньше, чем приходит MouseUp)
            if (unchecked(Environment.TickCount - _popup.HiddenAt) < 300) return;
            _popup.ShowNearTray();
        }

        internal void OpenPopup()
        {
            if (!_popup.Visible) _popup.ShowNearTray();
        }

        internal void Toggle(Mon m)
        {
            Afk.Forget(m); // включили/выключили вручную — AFK-режим этот монитор больше не ведёт
            int hr;
            if (m.Active)
            {
                hr = Svc.Disable(m, false);
                if (hr == 0) _icon.ShowBalloonTip(2500, Loc.Get("tray_title"),
                    string.Format(Loc.Get("off_done"), m.Name), ToolTipIcon.Info);
            }
            else
            {
                hr = Svc.Enable(m, false);
                if (hr == 0) _icon.ShowBalloonTip(2500, Loc.Get("tray_title"),
                    string.Format(Loc.Get("on_done"), m.Name), ToolTipIcon.Info);
            }
            if (hr != 0)
            {
                string msg = string.Format(Loc.Get("failed"), m.Name, Svc.LastError);
                _icon.ShowBalloonTip(4000, Loc.Get("tray_title"), msg, ToolTipIcon.Error);
            }
            UpdateTooltip();
        }

        internal void ExitApp()
        {
            _popup.HidePopup();
            _menu.HideMenu();
            Afk.WakeAll(); // не оставлять мониторы выключенными после выхода
            SystemEvents.DisplaySettingsChanged -= OnSystemChanged;
            _icon.Visible = false;
            _icon.Dispose();
            _appIcon.Dispose();
            Application.Exit();
        }
    }

    // ------------------------------------------------------------------ CLI
    internal static class Cli
    {
        public static int Run(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch { }
            string cmd = args[0].ToLowerInvariant();

            if (cmd == "list") { ListOut(); return 0; }

            if (cmd == "restore")
            {
                int hr = Svc.RestoreAll();
                Console.WriteLine("restore -> 0x" + hr.ToString("X") + (hr == 0 ? " OK" : " FAILED"));
                return hr == 0 ? 0 : 1;
            }

            if (cmd == "dpms-off") { Native.DpmsOffAll(); Console.WriteLine("dpms-off sent"); return 0; }

            if (cmd == "bright")
            {
                List<BrightEntry> bs = Bright.Open();
                if (bs.Count == 0) Console.WriteLine("no DDC-capable monitors");
                foreach (BrightEntry e in bs)
                {
                    Console.WriteLine(e.Gdi + ": " + Bright.ToPercent(e) + "%  (raw " + e.Min + ".." + e.Cur + ".." + e.Max + ")");
                }
                Bright.Close(bs);
                return 0;
            }

            if (cmd == "dbg")
            {
                WinSnap.Snapshot();
                Console.Write(WinSnap.Dump());
                Console.WriteLine("sizeof PATH_INFO = " + Marshal.SizeOf(typeof(Native.DISPLAYCONFIG_PATH_INFO)) +
                    "  MODE_INFO = " + Marshal.SizeOf(typeof(Native.DISPLAYCONFIG_MODE_INFO)));
                Console.WriteLine("virtual=" + Svc.IsVirtual);
                Native.DISPLAYCONFIG_PATH_INFO[] tp;
                Native.DISPLAYCONFIG_MODE_INFO[] tm;
                if (Native.Query(Native.QDC_ONLY_ACTIVE_PATHS, out tp, out tm) == 0)
                {
                    foreach (Screen s in Screen.AllScreens)
                        Console.WriteLine("screen " + s.DeviceName + " " + s.Bounds + (s.Primary ? " PRIMARY" : ""));
                    for (int i = 0; i < tp.Length; i++)
                        Console.WriteLine("path" + i + " src=0x" + tp[i].sourceInfo.id.ToString("X") + " tgt=0x" + tp[i].targetInfo.id.ToString("X") +
                            " srcIdx=0x" + tp[i].sourceInfo.modeInfoIdx.ToString("X") + " tgtIdx=0x" + tp[i].targetInfo.modeInfoIdx.ToString("X"));
                    for (int i = 0; i < tm.Length; i++)
                        Console.WriteLine("mode[" + i + "] type=" + tm[i].infoType + " id=" + tm[i].id +
                            " u0=" + tm[i].u0.ToString("X") + " u1=" + tm[i].u1.ToString("X") + " u2=" + tm[i].u2.ToString("X"));
                }
                Native.DISPLAYCONFIG_PATH_INFO[] allp;
                Native.DISPLAYCONFIG_MODE_INFO[] allm;
                if (Native.Query(Native.QDC_ALL_PATHS, out allp, out allm) == 0)
                {
                    int shown = 0;
                    foreach (Native.DISPLAYCONFIG_PATH_INFO p in allp)
                    {
                        if (p.targetInfo.targetAvailable == 0 && shown >= 8) continue;
                        Console.WriteLine("allpath tgt=0x" + p.targetInfo.id.ToString("X") + " src=0x" + p.sourceInfo.id.ToString("X") +
                            " srcIdx=0x" + p.sourceInfo.modeInfoIdx.ToString("X") + " tgtIdx=0x" + p.targetInfo.modeInfoIdx.ToString("X") +
                            " avail=" + p.targetInfo.targetAvailable + " flags=" + p.flags.ToString("X"));
                        shown++;
                    }
                }
                foreach (Svc.GdiEntry d in Svc.EnumGdi())
                    Console.WriteLine("gdi " + d.Dev + " flags=" + d.Flags + " mon='" + (d.MonStr ?? "") + "' id=" + (d.MonId ?? "(none)"));
                return 0;
            }

            if (cmd == "on" || cmd == "off" || cmd == "toggle")
            {
                if (args.Length < 2) { Usage(); return 2; }
                int idx;
                if (!int.TryParse(args[1], out idx) || idx < 1) { Usage(); return 2; }
                List<Mon> mons = Svc.List();
                if (idx > mons.Count) { Console.WriteLine("No monitor with index " + idx); return 1; }
                Mon m = mons[idx - 1];
                bool turnOn = cmd == "on" || (cmd == "toggle" && !m.Active);
                int hr = turnOn ? Svc.Enable(m, true) : Svc.Disable(m, true);
                Console.WriteLine((turnOn ? "on " : "off ") + idx + " '" + m.Name + "' -> 0x" + hr.ToString("X") +
                    (hr == 0 ? " OK" : " FAILED (" + Svc.LastError + ")"));
                return hr == 0 ? 0 : 1;
            }

            Usage();
            return 2;
        }

        static void ListOut()
        {
            List<Mon> mons = Svc.List();
            for (int i = 0; i < mons.Count; i++)
            {
                Mon m = mons[i];
                string st = m.Active ? "ON" : "OFF";
                string extra = "";
                if (m.Primary) extra += " PRIMARY";
                if (m.Active && !m.Bounds.IsEmpty) extra += " " + m.Bounds.Width + "x" + m.Bounds.Height;
                Console.WriteLine("[" + (i + 1) + "] " + st.PadRight(4) + (m.Name ?? "?") + "  " + (m.GdiName ?? "") + extra);
            }
            if (mons.Count == 0) Console.WriteLine("(no monitors) " + Svc.LastError);
        }

        static void Usage()
        {
            Console.WriteLine("Usage: MonitorTray list | on N | off N | toggle N | restore | dpms-off | bright | dbg");
        }
    }

    // ------------------------------------------------------------------ entry
    internal static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Loc.Load();
            Ui.Load();
            Fonts.Load();

            // перезапуск после автообновления: дождаться выхода старой версии и убрать её файл
            if (args != null && args.Length > 0 && args[0] == "--updated")
            {
                int pid;
                if (args.Length > 1 && int.TryParse(args[1], out pid))
                    try { Process.GetProcessById(pid).WaitForExit(10000); } catch { }
                try { File.Delete(Application.ExecutablePath + ".old"); } catch { }
                TrayApp.JustUpdated = true;
                args = new string[0];
            }

            if (args != null && args.Length > 0)
            {
                Environment.Exit(Cli.Run(args));
                return;
            }

            bool createdNew;
            Mutex mtx = new Mutex(true, "Local\\MonitorTraySingleInstance", out createdNew);
            if (!createdNew)
            {
                MessageBox.Show(Loc.Get("already_running"),
                    Loc.Get("tray_title"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try { Native.SetProcessDPIAware(); } catch { }
            Svc.GuiMode = true;
            Application.Run(new TrayApp());
            GC.KeepAlive(mtx);
        }
    }
}
