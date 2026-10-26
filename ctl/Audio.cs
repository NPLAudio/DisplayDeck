// Playback devices: listing, default device, and spatial sound format, via the Core Audio COM APIs.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace DisplayDeck.Ctl
{
    [StructLayout(LayoutKind.Sequential)]
    public struct PropertyKey
    {
        public Guid fmtid;
        public uint pid;
        public PropertyKey(string g, uint p) { fmtid = new Guid(g); pid = p; }
    }

    // x64 layout: vt at 0, payload at 8 (pointer / int / BLOB size) and 16 (BLOB data pointer).
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    public struct PropVariant
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public IntPtr ptr;
        [FieldOffset(8)] public uint ui4;
        [FieldOffset(8)] public uint blobSize;
        [FieldOffset(16)] public IntPtr blobData;
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class MMDeviceEnumeratorCom { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, uint stateMask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object result);
        [PreserveSig] int OpenPropertyStore(uint access, out IPropertyStore properties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out uint state);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }

    // Undocumented policy interface (the same one the Sound control panel uses). Only the slots we call matter;
    // the others are declared so the vtable positions line up.
    [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr format);
        [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string name, int def, IntPtr format);
        [PreserveSig] int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr endpointFormat, IntPtr mixFormat);
        [PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string name, int def, IntPtr defaultPeriod, IntPtr minPeriod);
        [PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr period);
        [PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr mode);
        [PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr mode);
        [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string name, int fxStore, ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string name, int fxStore, ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string name, int role);
        [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string name, int visible);
    }

    [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
    class PolicyConfigCom { }

    public class SpatialFormat
    {
        public string Name = "";
        public string Guid = "";
    }

    public class AudioDevice
    {
        public string EndpointId = "";
        public string Name = "";
        public string Adapter = "";
        public string CliId = "";
        public bool IsDefault;
        public string SpatialGuid = "";
        public List<SpatialFormat> Formats = new List<SpatialFormat>();
    }

    public static class AudioOps
    {
        const int eRender = 0;
        const uint DEVICE_STATE_ACTIVE = 1;
        const ushort VT_I4 = 3, VT_UI4 = 19, VT_LPWSTR = 31, VT_BLOB = 65;
        public const string OffGuid = "{00000000-0000-0000-0000-000000000000}";

        static readonly PropertyKey KeyDeviceDesc = new PropertyKey("a45c254e-df1c-4efd-8020-67d146a850e0", 2);
        static readonly PropertyKey KeyAdapter = new PropertyKey("026e516e-b814-414b-83cd-856d6fef4822", 2);
        static readonly PropertyKey KeyAdapterAlt = new PropertyKey("b3f8fa53-0004-438e-9003-51a46e139bfc", 6);
        static readonly PropertyKey KeySpatial = new PropertyKey("1da5d803-d492-4edd-8c23-e0c0ffee7f0e", 2);
        const string FormatTableGuid = "a45429a4-aa63-4480-b7f8-3f2552daee93";

        [DllImport("ole32.dll")] static extern int PropVariantClear(ref PropVariant pv);

        static void Check(int hr, string what)
        {
            if (hr < 0) throw new CtlException(what + " failed (0x" + hr.ToString("X8") + ")", hr == unchecked((int)0x80070005) ? 5 : 1);
        }

        static IMMDeviceEnumerator Enumerator() { return (IMMDeviceEnumerator)new MMDeviceEnumeratorCom(); }

        static string ReadString(IPropertyStore ps, PropertyKey key)
        {
            PropVariant pv;
            if (ps.GetValue(ref key, out pv) < 0) return "";
            try { return pv.vt == VT_LPWSTR && pv.ptr != IntPtr.Zero ? Marshal.PtrToStringUni(pv.ptr) : ""; }
            finally { PropVariantClear(ref pv); }
        }

        static byte[] ReadBlob(IPropertyStore ps, PropertyKey key)
        {
            PropVariant pv;
            if (ps.GetValue(ref key, out pv) < 0) return null;
            try
            {
                if (pv.vt != VT_BLOB || pv.blobData == IntPtr.Zero || pv.blobSize == 0) return null;
                byte[] b = new byte[pv.blobSize];
                Marshal.Copy(pv.blobData, b, 0, (int)pv.blobSize);
                return b;
            }
            finally { PropVariantClear(ref pv); }
        }

        static string Utf16At(byte[] b, ref int o)
        {
            int start = o;
            while (o + 1 < b.Length && !(b[o] == 0 && b[o + 1] == 0)) o += 2;
            string s = Encoding.Unicode.GetString(b, start, o - start);
            o += 2;
            return s;
        }

        // A format-table entry is a fixed-size record: an optional 8-byte serialization header, then the
        // UTF-16 name, store link and other fields, with the format's GUID at a fixed distance from the name.
        const int GuidOffsetFromName = 768;

        public static SpatialFormat ParseFormat(byte[] b)
        {
            if (b == null) return null;
            // Some records carry the header (41 00 00 00 01 00 00 00) and some, written by other components,
            // do not, so detect it rather than assuming.
            bool header = b.Length >= 8 && b[0] == 0x41 && b[1] == 0 && b[2] == 0 && b[3] == 0
                          && b[4] == 1 && b[5] == 0 && b[6] == 0 && b[7] == 0;
            int nameAt = header ? 8 : 0;
            if (nameAt + GuidOffsetFromName + 16 > b.Length) return null;
            int o = nameAt;
            string name = Utf16At(b, ref o);
            if (name.Length < 3) return null;
            foreach (char c in name) if (c < 32 || c > 0xFFFD) return null;
            byte[] g = new byte[16];
            Array.Copy(b, nameAt + GuidOffsetFromName, g, 0, 16);
            SpatialFormat f = new SpatialFormat();
            f.Name = name;
            f.Guid = new Guid(g).ToString("B").ToUpperInvariant();
            return f;
        }

        static AudioDevice Describe(IMMDevice dev, string defaultId)
        {
            AudioDevice d = new AudioDevice();
            string id; Check(dev.GetId(out id), "GetId");
            d.EndpointId = id;
            d.IsDefault = string.Equals(id, defaultId, StringComparison.OrdinalIgnoreCase);
            IPropertyStore ps; Check(dev.OpenPropertyStore(0, out ps), "OpenPropertyStore");
            try
            {
                d.Name = ReadString(ps, KeyDeviceDesc);
                d.Adapter = ReadString(ps, KeyAdapter);
                if (d.Adapter.Length == 0) d.Adapter = ReadString(ps, KeyAdapterAlt);
                d.CliId = d.Adapter + "\\Device\\" + d.Name + "\\Render";
                d.SpatialGuid = ReadString(ps, KeySpatial);
                for (uint pid = 2; pid <= 14; pid++)
                {
                    SpatialFormat f = ParseFormat(ReadBlob(ps, new PropertyKey(FormatTableGuid, pid)));
                    if (f != null) d.Formats.Add(f);
                }
            }
            finally { Marshal.ReleaseComObject(ps); }
            return d;
        }

        public static List<AudioDevice> List()
        {
            IMMDeviceEnumerator en = Enumerator();
            string defaultId = "";
            IMMDevice def;
            if (en.GetDefaultAudioEndpoint(eRender, 0, out def) >= 0 && def != null) { def.GetId(out defaultId); Marshal.ReleaseComObject(def); }

            IMMDeviceCollection col; Check(en.EnumAudioEndpoints(eRender, DEVICE_STATE_ACTIVE, out col), "EnumAudioEndpoints");
            uint n; col.GetCount(out n);
            List<AudioDevice> list = new List<AudioDevice>();
            for (uint i = 0; i < n; i++)
            {
                IMMDevice dev;
                if (col.Item(i, out dev) < 0) continue;
                try { list.Add(Describe(dev, defaultId)); } finally { Marshal.ReleaseComObject(dev); }
            }
            return list;
        }

        /// <summary>Accepts a CLI id (Adapter\Device\Name\Render) or a raw endpoint id.</summary>
        public static AudioDevice Find(string idOrCli)
        {
            List<AudioDevice> all = List();
            foreach (AudioDevice d in all)
                if (string.Equals(d.CliId, idOrCli, StringComparison.OrdinalIgnoreCase) || string.Equals(d.EndpointId, idOrCli, StringComparison.OrdinalIgnoreCase)) return d;
            throw new CtlException("No active playback device matches: " + idOrCli);
        }

        public static void SetDefault(string endpointId)
        {
            IPolicyConfig pc = (IPolicyConfig)new PolicyConfigCom();
            try
            {
                for (int role = 0; role < 3; role++) Check(pc.SetDefaultEndpoint(endpointId, role), "SetDefaultEndpoint");
            }
            finally { Marshal.ReleaseComObject(pc); }
        }

        /// <summary>Resolves "off", a GUID, or a (partial) format name against the device's own format table.</summary>
        public static string ResolveFormat(AudioDevice d, string wanted)
        {
            string w = (wanted ?? "").Trim();
            if (w.Length == 0 || w.Equals("off", StringComparison.OrdinalIgnoreCase) || w.Equals("none", StringComparison.OrdinalIgnoreCase)) return OffGuid;
            Guid g;
            if (Guid.TryParse(w, out g)) return g.ToString("B").ToUpperInvariant();
            foreach (SpatialFormat f in d.Formats) if (f.Name.Equals(w, StringComparison.OrdinalIgnoreCase)) return f.Guid;
            foreach (SpatialFormat f in d.Formats) if (f.Name.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0) return f.Guid;
            throw new CtlException("\"" + wanted + "\" is not a spatial format this device offers.");
        }

        /// <summary>Writes the device's spatial-format property. Throws CtlException(code 5) when access is denied.</summary>
        public static void SetSpatial(AudioDevice d, string guid)
        {
            IMMDeviceEnumerator en = Enumerator();
            IMMDevice dev; Check(en.GetDevice(d.EndpointId, out dev), "GetDevice");
            IPropertyStore ps = null;
            IntPtr str = Marshal.StringToCoTaskMemUni(guid);
            try
            {
                Check(dev.OpenPropertyStore(1 /* STGM_WRITE */, out ps), "OpenPropertyStore(write)");
                PropVariant pv = new PropVariant();
                pv.vt = VT_LPWSTR;
                pv.ptr = str;
                PropertyKey key = KeySpatial;
                Check(ps.SetValue(ref key, ref pv), "SetValue(spatial)");
                Check(ps.Commit(), "Commit");
            }
            finally
            {
                if (ps != null) Marshal.ReleaseComObject(ps);
                Marshal.ReleaseComObject(dev);
                Marshal.FreeCoTaskMem(str);
            }
        }
    }
}
