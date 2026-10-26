// Lists the connected displays and the resolutions and refresh rates each one supports.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace DisplayDeck.Ctl
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public ushort dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public uint dmFields;
        public int dmPositionX, dmPositionY;
        public uint dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public ushort dmLogPixels;
        public uint dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public uint dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct SourceDeviceName
    {
        public uint headerType;
        public uint headerSize;
        public Luid headerAdapterId;
        public uint headerId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string viewGdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TargetPreferredMode
    {
        public uint headerType;
        public uint headerSize;
        public Luid headerAdapterId;
        public uint headerId;
        public uint width;
        public uint height;
        public VideoSignalInfo targetMode;
    }

    public class ModeOption
    {
        public int Width, Height;
        public SortedDictionary<int, bool> Rates = new SortedDictionary<int, bool>();
    }

    public static class DisplayModes
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern bool EnumDisplaySettingsEx(string deviceName, int modeNum, ref DevMode devMode, uint flags);

        [DllImport("user32.dll")]
        static extern int DisplayConfigGetDeviceInfo(ref SourceDeviceName request);

        [DllImport("user32.dll")]
        static extern int DisplayConfigGetDeviceInfo(ref TargetPreferredMode request);

        /// <summary>Resolutions and refresh rates the display on this source supports (empty when it cannot be read).</summary>
        public static List<ModeOption> ForSource(Luid adapter, uint sourceId)
        {
            SourceDeviceName n = new SourceDeviceName();
            n.headerType = 1; // GET_SOURCE_NAME
            n.headerSize = (uint)Marshal.SizeOf(typeof(SourceDeviceName));
            n.headerAdapterId = adapter;
            n.headerId = sourceId;
            List<ModeOption> list = new List<ModeOption>();
            if (DisplayConfigGetDeviceInfo(ref n) != 0 || string.IsNullOrEmpty(n.viewGdiDeviceName)) return list;

            Dictionary<string, ModeOption> byRes = new Dictionary<string, ModeOption>();
            DevMode dm = new DevMode();
            dm.dmSize = (ushort)Marshal.SizeOf(typeof(DevMode));
            for (int i = 0; i < 4000 && EnumDisplaySettingsEx(n.viewGdiDeviceName, i, ref dm, 0); i++)
            {
                if (dm.dmBitsPerPel != 32 || dm.dmPelsWidth == 0) continue;
                string key = dm.dmPelsWidth + "x" + dm.dmPelsHeight;
                ModeOption o;
                if (!byRes.TryGetValue(key, out o))
                {
                    o = new ModeOption();
                    o.Width = (int)dm.dmPelsWidth; o.Height = (int)dm.dmPelsHeight;
                    byRes[key] = o; list.Add(o);
                }
                if (dm.dmDisplayFrequency > 1) o.Rates[(int)dm.dmDisplayFrequency] = true;
            }
            list.Sort(delegate (ModeOption a, ModeOption b)
            {
                int c = (b.Width * b.Height).CompareTo(a.Width * a.Height);
                return c != 0 ? c : b.Width.CompareTo(a.Width);
            });
            return list;
        }

        /// <summary>The mode the monitor itself prefers, or null when Windows will not say.</summary>
        public static ModeOption Preferred(Luid adapter, uint targetId)
        {
            try
            {
                TargetPreferredMode p = new TargetPreferredMode();
                p.headerType = 3; // GET_TARGET_PREFERRED_MODE
                p.headerSize = (uint)Marshal.SizeOf(typeof(TargetPreferredMode));
                p.headerAdapterId = adapter;
                p.headerId = targetId;
                if (DisplayConfigGetDeviceInfo(ref p) != 0 || p.width == 0) return null;
                ModeOption o = new ModeOption();
                o.Width = (int)p.width; o.Height = (int)p.height;
                uint d = p.targetMode.vSyncFreq.Denominator;
                if (d != 0) o.Rates[(int)Math.Round((double)p.targetMode.vSyncFreq.Numerator / d)] = true;
                return o;
            }
            catch (Exception) { return null; }
        }
    }
}
