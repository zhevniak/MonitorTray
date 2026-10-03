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
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

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
            uint[] flagSets = new uint[] {
                Native.SDC_APPLY | Native.SDC_USE_SUPPLIED_DISPLAY_CONFIG | vf,
                Native.SDC_APPLY | Native.SDC_USE_SUPPLIED_DISPLAY_CONFIG | Native.SDC_NO_OPTIMIZATION | Native.SDC_ALLOW_PATH_ORDER_CHANGES | vf,
                Native.SDC_APPLY | Native.SDC_USE_SUPPLIED_DISPLAY_CONFIG | Native.SDC_SAVE_TO_DATABASE | vf,
                Native.SDC_APPLY | Native.SDC_NO_OPTIMIZATION | Native.SDC_ALLOW_PATH_ORDER_CHANGES | vf,
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

                Native.DISPLAYCONFIG_PATH_INFO[] mergedP = new Native.DISPLAYCONFIG_PATH_INFO[ap.Length + 1];
                Native.DISPLAYCONFIG_MODE_INFO[] mergedM = new Native.DISPLAYCONFIG_MODE_INFO[am.Length + uniqNeed.Count];
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
                mergedP[ap.Length] = cand;
                for (int j = 0; j < uniqNeed.Count; j++)
                    mergedM[am.Length + j] = allm[uniqNeed[j]];

                hr = TrySet(mergedP, mergedM, verbose);
                if (hr == 0 && TargetNowActive(m)) return 0;
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
            int r2 = Native.ChangeDisplaySettingsEx(null, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero);
            return r1 != 0 ? r1 : r2;
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
            int r2 = Native.ChangeDisplaySettingsEx(null, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero);
            return r1 != 0 ? r1 : r2;
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

    // ------------------------------------------------------------------ иконка
    internal static class AppIcon
    {
        public static Icon Create()
        {
            // встроенный в exe файл MonitorTray.ico
            try
            {
                System.Reflection.Assembly asm = System.Reflection.Assembly.GetExecutingAssembly();
                foreach (string n in asm.GetManifestResourceNames())
                {
                    if (n.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
                    {
                        using (System.IO.Stream st = asm.GetManifestResourceStream(n))
                        {
                            if (st != null)
                            {
                                Icon big = new Icon(st);
                                return new Icon(big, 32, 32);
                            }
                        }
                    }
                }
            }
            catch { }
            return DrawFallback();
        }

        static Icon DrawFallback()
        {
            using (Bitmap bmp = new Bitmap(32, 32))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    using (SolidBrush bezel = new SolidBrush(Color.FromArgb(52, 58, 72)))
                    {
                        g.FillRectangle(bezel, 2, 3, 28, 20);
                        g.FillRectangle(bezel, 13, 23, 6, 4);
                        g.FillRectangle(bezel, 9, 27, 14, 3);
                    }
                    using (LinearGradientBrush scr = new LinearGradientBrush(
                        new Rectangle(4, 5, 24, 16), Color.FromArgb(26, 32, 44), Color.FromArgb(12, 16, 24), 90f))
                        g.FillRectangle(scr, 4, 5, 24, 16);
                    using (Pen p = new Pen(Color.FromArgb(0, 200, 255), 2.6f))
                    {
                        p.StartCap = LineCap.Round;
                        p.EndCap = LineCap.Round;
                        g.DrawArc(p, 11.5f, 8.6f, 9f, 9f, 315f, 270f);
                        g.DrawLine(p, 16f, 6.8f, 16f, 12.4f);
                    }
                }
                return Icon.FromHandle(bmp.GetHicon());
            }
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
                case "menu_title": return ru ? "Мониторы — вкл: {0}" : "Monitors — on: {0}";
                case "primary": return ru ? "  — основной" : "  — primary";
                case "turn_on": return ru ? "  — включить" : "  — turn on";
                case "tip_off": return ru ? "Нажмите, чтобы выключить" : "Click to turn this monitor off";
                case "tip_on": return ru ? "Монитор выключен. Нажмите, чтобы включить" : "This monitor is off. Click to turn it on";
                case "no_monitors": return ru ? "Мониторы не найдены" : "No monitors found";
                case "dpms": return ru ? "Погасить все экраны (до 1-го движения мыши)" : "Put all screens to sleep (until first mouse move)";
                case "autostart": return ru ? "Запускать при входе в Windows" : "Start with Windows";
                case "about": return ru ? "О программе" : "About";
                case "exit": return ru ? "Выход" : "Exit";
                case "language": return ru ? "Язык" : "Language";
                case "tooltip": return ru ? "Мониторы: {0} из {1} вкл." : "Monitors: {0} of {1} on";
                case "off_done": return ru ? "{0} — выключен" : "{0} — turned off";
                case "on_done": return ru ? "{0} — включён" : "{0} — turned on";
                case "failed": return ru ? "Не удалось переключить {0}. {1}" : "Failed to toggle {0}. {1}";
                case "monitor_n": return ru ? "Монитор {0}" : "Monitor {0}";
                case "already_running": return ru
                    ? "MonitorTray уже запущен — значок есть в области уведомлений (возможно, под стрелкой «^»)."
                    : "MonitorTray is already running — the icon is in the notification area (possibly under the \"^\" arrow).";
                case "about_text": return ru
                    ? "MonitorTray 1.0\n\nВключение и выключение отдельных мониторов прямо из трея —\nтем же способом, что и «Параметры экрана» Windows (без DDC/CI).\n\n● — монитор включён,  ○ — выключен.\nКлик по монитору в меню переключает его состояние.\n\nУдаление: Параметры Windows → Приложения → MonitorTray."
                    : "MonitorTray 1.0\n\nTurn individual monitors on and off right from the tray —\nthe same way Windows Display Settings does it (no DDC/CI needed).\n\n● — monitor is on,  ○ — off.\nClick a monitor in the menu to toggle it.\n\nUninstall: Windows Settings → Apps → MonitorTray.";
                default: return key;
            }
        }
    }

    // ------------------------------------------------------------------ GUI
    internal class TrayApp : ApplicationContext
    {
        NotifyIcon _icon;
        ContextMenuStrip _menu;
        Form _hidden;
        Icon _appIcon;

        public TrayApp()
        {
            _hidden = new Form();
            _hidden.ShowInTaskbar = false;

            _appIcon = AppIcon.Create();
            _menu = new ContextMenuStrip();
            _menu.Opening += new System.ComponentModel.CancelEventHandler(MenuOpening);

            _icon = new NotifyIcon();
            _icon.Icon = _appIcon;
            _icon.Text = Loc.Get("tray_title");
            _icon.Visible = true;
            _icon.ContextMenuStrip = _menu;
            _icon.MouseUp += new MouseEventHandler(IconMouseUp);

            UpdateTooltip();
        }

        void UpdateTooltip()
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

        void MenuOpening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            Rebuild();
        }

        void IconMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                Rebuild();
                Native.SetForegroundWindow(_hidden.Handle);
                _menu.Show(Cursor.Position);
            }
        }

        void Rebuild()
        {
            _menu.Items.Clear();
            List<Mon> mons = Svc.List();
            int activeCount = 0;
            foreach (Mon m in mons) if (m.Active) activeCount++;

            ToolStripMenuItem title = new ToolStripMenuItem(string.Format(Loc.Get("menu_title"), activeCount));
            title.Font = new Font(SystemFonts.MenuFont, FontStyle.Bold);
            title.Enabled = false;
            _menu.Items.Add(title);
            _menu.Items.Add(new ToolStripSeparator());

            if (mons.Count == 0)
            {
                ToolStripMenuItem empty = new ToolStripMenuItem(Loc.Get("no_monitors"));
                empty.Enabled = false;
                _menu.Items.Add(empty);
            }

            foreach (Mon m in mons)
            {
                ToolStripMenuItem it = new ToolStripMenuItem();
                if (m.Active)
                {
                    it.Text = "●  " + m.Name + (m.Primary ? Loc.Get("primary") : "");
                    it.ToolTipText = Loc.Get("tip_off") +
                        (m.GdiName.Length > 0 ? "  (" + m.GdiName + ")" : "");
                }
                else
                {
                    it.Text = "○  " + m.Name + Loc.Get("turn_on");
                    it.ForeColor = Color.FromArgb(226, 120, 16);
                    it.ToolTipText = Loc.Get("tip_on");
                }
                if (!m.Bounds.IsEmpty)
                    it.ToolTipText += "\n" + m.Bounds.Width + "×" + m.Bounds.Height;
                Mon captured = m;
                it.Click += delegate(object s2, EventArgs e2) { Toggle(captured); };
                _menu.Items.Add(it);
            }

            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(MakeItem(Loc.Get("dpms"), delegate { Native.DpmsOffAll(); }));

            // переключение языка
            ToolStripMenuItem langMenu = new ToolStripMenuItem(Loc.Get("language"));
            ToolStripMenuItem langEn = new ToolStripMenuItem("English");
            langEn.Checked = Loc.Lang == "en";
            langEn.Click += delegate { Loc.Set("en"); UpdateTooltip(); Rebuild(); };
            ToolStripMenuItem langRu = new ToolStripMenuItem("Русский");
            langRu.Checked = Loc.Lang == "ru";
            langRu.Click += delegate { Loc.Set("ru"); UpdateTooltip(); Rebuild(); };
            langMenu.DropDownItems.Add(langEn);
            langMenu.DropDownItems.Add(langRu);
            _menu.Items.Add(langMenu);

            _menu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem auto = new ToolStripMenuItem(Loc.Get("autostart"));
            auto.Checked = Autostart.IsEnabled();
            auto.Click += delegate
            {
                Autostart.Set(!Autostart.IsEnabled());
                Rebuild();
            };
            _menu.Items.Add(auto);

            _menu.Items.Add(MakeItem(Loc.Get("about"), delegate
            {
                MessageBox.Show(Loc.Get("about_text"),
                    Loc.Get("about"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            }));

            _menu.Items.Add(MakeItem(Loc.Get("exit"), delegate { ExitApp(); }));
        }

        ToolStripMenuItem MakeItem(string text, EventHandler onClick)
        {
            ToolStripMenuItem it = new ToolStripMenuItem(text);
            it.Click += onClick;
            return it;
        }

        void Toggle(Mon m)
        {
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

        void ExitApp()
        {
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
            Console.WriteLine("Usage: MonitorTray list | on N | off N | toggle N | restore | dpms-off | dbg");
        }
    }

    // ------------------------------------------------------------------ entry
    internal static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Loc.Load();

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
