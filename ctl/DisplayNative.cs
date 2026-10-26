// Win32 display-configuration interop (user32): QueryDisplayConfig / SetDisplayConfig.
using System;
using System.Runtime.InteropServices;

namespace DisplayDeck.Ctl
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Luid
    {
        public uint LowPart;
        public int HighPart;
        public bool SameAs(Luid o) { return LowPart == o.LowPart && HighPart == o.HighPart; }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Rational
    {
        public uint Numerator;
        public uint Denominator;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PathSourceInfo
    {
        public Luid adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PathTargetInfo
    {
        public Luid adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint outputTechnology;
        public uint rotation;
        public uint scaling;
        public Rational refreshRate;
        public uint scanLineOrdering;
        public int targetAvailable;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PathInfo
    {
        public PathSourceInfo sourceInfo;
        public PathTargetInfo targetInfo;
        public uint flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VideoSignalInfo
    {
        public ulong pixelRate;
        public Rational hSyncFreq;
        public Rational vSyncFreq;
        public uint activeCx;
        public uint activeCy;
        public uint totalCx;
        public uint totalCy;
        public uint videoStandard;
        public uint scanLineOrdering;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SourceMode
    {
        public uint width;
        public uint height;
        public uint pixelFormat;
        public int positionX;
        public int positionY;
    }

    [StructLayout(LayoutKind.Explicit, Size = 64)]
    public struct ModeInfo
    {
        [FieldOffset(0)] public uint infoType;
        [FieldOffset(4)] public uint id;
        [FieldOffset(8)] public Luid adapterId;
        [FieldOffset(16)] public VideoSignalInfo targetMode;
        [FieldOffset(16)] public SourceMode sourceMode;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct TargetDeviceName
    {
        public uint headerType;
        public uint headerSize;
        public Luid headerAdapterId;
        public uint headerId;
        public uint flags;
        public uint outputTechnology;
        public ushort edidManufactureId;
        public ushort edidProductCodeId;
        public uint connectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string monitorFriendlyDeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string monitorDevicePath;
    }

    public static class DisplayNative
    {
        public const uint QDC_ALL_PATHS = 0x1;
        public const uint QDC_ONLY_ACTIVE_PATHS = 0x2;

        public const uint SDC_VALIDATE = 0x40;
        public const uint SDC_APPLY = 0x80;
        public const uint SDC_USE_SUPPLIED_DISPLAY_CONFIG = 0x20;
        public const uint SDC_SAVE_TO_DATABASE = 0x200;
        public const uint SDC_ALLOW_CHANGES = 0x400;

        public const uint PATH_ACTIVE = 0x1;
        public const uint MODE_SOURCE = 1;
        public const uint MODE_TARGET = 2;
        public const uint IDX_INVALID = 0xFFFFFFFF;
        public const int ERROR_INSUFFICIENT_BUFFER = 122;

        [DllImport("user32.dll")]
        public static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPaths, out uint numModes);

        [DllImport("user32.dll")]
        public static extern int QueryDisplayConfig(uint flags, ref uint numPaths, [Out] PathInfo[] paths,
            ref uint numModes, [Out] ModeInfo[] modes, IntPtr currentTopologyId);

        [DllImport("user32.dll")]
        public static extern int SetDisplayConfig(uint numPaths, [In] PathInfo[] paths, uint numModes,
            [In] ModeInfo[] modes, uint flags);

        [DllImport("user32.dll")]
        public static extern int DisplayConfigGetDeviceInfo(ref TargetDeviceName request);

        /// <summary>Reads the live configuration. Retries if the display set changes between the two calls.</summary>
        public static void Query(uint flags, out PathInfo[] paths, out ModeInfo[] modes)
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                uint np, nm;
                int rc = GetDisplayConfigBufferSizes(flags, out np, out nm);
                if (rc != 0) throw new CtlException("GetDisplayConfigBufferSizes failed (" + rc + ")");
                paths = new PathInfo[np];
                modes = new ModeInfo[nm];
                rc = QueryDisplayConfig(flags, ref np, paths, ref nm, modes, IntPtr.Zero);
                if (rc == ERROR_INSUFFICIENT_BUFFER) continue;
                if (rc != 0) throw new CtlException("QueryDisplayConfig failed (" + rc + ")");
                Array.Resize(ref paths, (int)np);
                Array.Resize(ref modes, (int)nm);
                return;
            }
            throw new CtlException("The display configuration kept changing; try again.");
        }

        public static TargetDeviceName GetTargetName(Luid adapter, uint targetId)
        {
            TargetDeviceName r = new TargetDeviceName();
            r.headerType = 2; // DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME
            r.headerSize = (uint)Marshal.SizeOf(typeof(TargetDeviceName));
            r.headerAdapterId = adapter;
            r.headerId = targetId;
            DisplayConfigGetDeviceInfo(ref r); // best effort: leaves empty strings on failure
            return r;
        }
    }

    public class CtlException : Exception
    {
        public int Code;
        public CtlException(string message) : base(message) { Code = 1; }
        public CtlException(string message, int code) : base(message) { Code = code; }
    }
}
