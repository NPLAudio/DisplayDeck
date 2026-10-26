// Reads and writes DisplayDeck profile files (.xml): the saved display paths and modes.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Xml.Linq;

namespace DisplayDeck.Ctl
{
    public enum OutputTechnology : uint
    {
        Other = 0xFFFFFFFF, Hd15 = 0, Svideo = 1, CompositeVideo = 2, ComponentVideo = 3, Dvi = 4, Hdmi = 5,
        Lvds = 6, DJpn = 8, Sdi = 9, DisplayportExternal = 10, DisplayportEmbedded = 11, UdiExternal = 12,
        UdiEmbedded = 13, Sdtvdongle = 14, Miracast = 15, IndirectWired = 16, IndirectVirtual = 17, Internal = 0x80000000
    }
    public enum Rotation : uint { Identity = 1, Rotate90 = 2, Rotate180 = 3, Rotate270 = 4 }
    public enum Scaling : uint { Identity = 1, Centered = 2, Stretched = 3, Aspectratiocenteredmax = 4, Custom = 5, Preferred = 128 }
    public enum ScanLineOrdering : uint { Unspecified = 0, Progressive = 1, Interlaced = 2, InterlacedLowerfieldfirst = 3 }
    public enum VideoStandard : uint
    {
        Uninitialized = 0, VesaDmt = 1, VesaGtf = 2, VesaCvt = 3, Ibm = 4, Apple = 5, NtscM = 6, NtscJ = 7, Ntsc443 = 8,
        PalB = 9, PalB1 = 10, PalG = 11, PalH = 12, PalI = 13, PalD = 14, PalN = 15, PalNc = 16, SecamB = 17, SecamD = 18,
        SecamG = 19, SecamH = 20, SecamK = 21, SecamK1 = 22, SecamL = 23, SecamL1 = 24, Eia861 = 25, Eia861a = 26,
        Eia861b = 27, PalK = 28, PalK1 = 29, PalL = 30, PalM = 31, Other = 255
    }
    public enum PixelFormat : uint { Pixelformat8Bpp = 1, Pixelformat16Bpp = 2, Pixelformat24Bpp = 3, Pixelformat32Bpp = 4, PixelformatNongdi = 5 }
    [Flags] public enum StatusFlags : uint { None = 0, InUse = 1, Forcible = 2, ForcedAvailabilityBoot = 4, ForcedAvailabilityPath = 8, ForcedAvailabilitySystem = 16 }

    public class MonitorInfo
    {
        public ushort ManufactureId;
        public ushort ProductCodeId;
        public bool Valid;
        public string DevicePath = "";
        public string FriendlyName = "";

        /// <summary>The numeric id after "UID" in the device path equals the display target id.</summary>
        public uint? TargetId()
        {
            int i = DevicePath.IndexOf("UID", StringComparison.OrdinalIgnoreCase);
            if (i < 0) return null;
            int j = i + 3, k = j;
            while (k < DevicePath.Length && char.IsDigit(DevicePath[k])) k++;
            uint v;
            return k > j && uint.TryParse(DevicePath.Substring(j, k - j), out v) ? (uint?)v : null;
        }
    }

    public class Profile
    {
        public List<PathInfo> Paths = new List<PathInfo>();
        public List<ModeInfo> Modes = new List<ModeInfo>();
        public List<MonitorInfo> Monitors = new List<MonitorInfo>();
    }

    public static class ProfileFile
    {
        /// <summary>EDID manufacturer id (three 5-bit letters, stored byte-swapped) plus product code, e.g. "MSI3FA8".</summary>
        public static string EdidHwId(ushort manufacturer, ushort product)
        {
            int w = ((manufacturer & 0xFF) << 8) | (manufacturer >> 8);
            char a = (char)('@' + ((w >> 10) & 0x1F)), b = (char)('@' + ((w >> 5) & 0x1F)), c = (char)('@' + (w & 0x1F));
            if (!char.IsLetter(a) || !char.IsLetter(b) || !char.IsLetter(c)) return "UNK" + product.ToString("X4");
            return new string(new char[] { a, b, c }) + product.ToString("X4");
        }

        /// <summary>A device path in the usual form, built from the target id and EDID ids.</summary>
        public static string SynthesizeDevicePath(uint targetId, ushort manufacturer, ushort product)
        {
            return "\\\\?\\DISPLAY#" + EdidHwId(manufacturer, product) + "#5&0&0&UID" + targetId.ToString(CultureInfo.InvariantCulture)
                + "#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";
        }

        static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";
        static readonly XNamespace Xsd = "http://www.w3.org/2001/XMLSchema";

        // ---------- reading (tolerant: unknown elements ignored, missing values default) ----------

        static XElement Kid(XElement e, string name) { return e == null ? null : e.Element(name); }
        static string Str(XElement e, string name) { XElement k = Kid(e, name); return k == null ? "" : k.Value.Trim(); }

        static uint Num(XElement e, string name, uint def)
        {
            string s = Str(e, name);
            long v;
            return long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? unchecked((uint)v) : def;
        }

        static int Int(XElement e, string name, int def)
        {
            int v;
            return int.TryParse(Str(e, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : def;
        }

        static uint EnumVal<T>(XElement e, string name, uint def) where T : struct
        {
            string s = Str(e, name);
            if (s.Length == 0) return def;
            long n;
            if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) return unchecked((uint)n);
            try { return Convert.ToUInt32(Enum.Parse(typeof(T), s.Replace(" ", ""), true)); } catch (Exception) { return def; }
        }

        static Luid ReadLuid(XElement e)
        {
            Luid l = new Luid();
            if (e == null) return l;
            l.LowPart = Num(e, "LowPart", 0);
            l.HighPart = Int(e, "HighPart", 0);
            return l;
        }

        static Rational ReadRational(XElement e)
        {
            Rational r = new Rational();
            r.Numerator = Num(e, "numerator", 0);
            r.Denominator = Num(e, "denominator", 1);
            return r;
        }

        public static string Decode(string b64)
        {
            b64 = (b64 ?? "").Trim();
            if (b64.Length == 0) return "";
            byte[] b;
            try { b = Convert.FromBase64String(b64); } catch (FormatException) { return b64; }
            if (b.Length >= 4 && b.Length % 4 == 0 && b[1] == 0 && b[2] == 0 && b[3] == 0)
            {
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < b.Length; i += 4) sb.Append(char.ConvertFromUtf32(BitConverter.ToInt32(b, i)));
                return sb.ToString().TrimEnd('\0');
            }
            return Encoding.Unicode.GetString(b).TrimEnd('\0');
        }

        public static string Encode(string s)
        {
            byte[] b = new byte[s.Length * 4];
            for (int i = 0; i < s.Length; i++) BitConverter.GetBytes((int)s[i]).CopyTo(b, i * 4);
            return Convert.ToBase64String(b);
        }

        public static Profile Parse(string xml)
        {
            XDocument doc;
            try { doc = XDocument.Parse(xml.TrimStart('﻿')); }
            catch (Exception e) { throw new CtlException("The profile file is not valid XML: " + e.Message); }
            XElement root = doc.Root;
            if (root == null) throw new CtlException("The profile file is empty.");

            Profile p = new Profile();
            XElement pathArr = Kid(root, "pathInfoArray");
            if (pathArr != null)
            {
                foreach (XElement pe in pathArr.Elements("DisplayConfigPathInfo"))
                {
                    XElement s = Kid(pe, "sourceInfo"), t = Kid(pe, "targetInfo");
                    PathInfo pi = new PathInfo();
                    pi.sourceInfo.adapterId = ReadLuid(Kid(s, "adapterId"));
                    pi.sourceInfo.id = Num(s, "id", 0);
                    pi.sourceInfo.modeInfoIdx = Num(s, "modeInfoIdx", DisplayNative.IDX_INVALID);
                    pi.sourceInfo.statusFlags = EnumVal<StatusFlags>(s, "statusFlags", 0);
                    pi.targetInfo.adapterId = ReadLuid(Kid(t, "adapterId"));
                    pi.targetInfo.id = Num(t, "id", 0);
                    pi.targetInfo.modeInfoIdx = Num(t, "modeInfoIdx", DisplayNative.IDX_INVALID);
                    pi.targetInfo.outputTechnology = EnumVal<OutputTechnology>(t, "outputTechnology", 0xFFFFFFFF);
                    pi.targetInfo.rotation = EnumVal<Rotation>(t, "rotation", 1);
                    pi.targetInfo.scaling = EnumVal<Scaling>(t, "scaling", 1);
                    pi.targetInfo.refreshRate = ReadRational(Kid(t, "refreshRate"));
                    pi.targetInfo.scanLineOrdering = EnumVal<ScanLineOrdering>(t, "scanLineOrdering", 1);
                    pi.targetInfo.targetAvailable = Str(t, "targetAvailable").ToLowerInvariant() == "false" ? 0 : 1;
                    pi.targetInfo.statusFlags = EnumVal<StatusFlags>(t, "statusFlags", 0);
                    pi.flags = Num(pe, "flags", 0);
                    p.Paths.Add(pi);
                }
            }

            XElement modeArr = Kid(root, "modeInfoArray");
            if (modeArr != null)
            {
                foreach (XElement me in modeArr.Elements("modeInfo"))
                {
                    ModeInfo mi = new ModeInfo();
                    string type = Str(me, "DisplayConfigModeInfoType");
                    mi.infoType = type.Equals("Source", StringComparison.OrdinalIgnoreCase) ? DisplayNative.MODE_SOURCE
                                : type.Equals("Target", StringComparison.OrdinalIgnoreCase) ? DisplayNative.MODE_TARGET : 0;
                    mi.id = Num(me, "id", 0);
                    mi.adapterId = ReadLuid(Kid(me, "LUID"));
                    XElement sm = Kid(me, "DisplayConfigSourceMode"), tm = Kid(me, "DisplayConfigTargetMode");
                    if (mi.infoType == DisplayNative.MODE_SOURCE && sm != null)
                    {
                        mi.sourceMode.width = Num(sm, "width", 0);
                        mi.sourceMode.height = Num(sm, "height", 0);
                        mi.sourceMode.pixelFormat = EnumVal<PixelFormat>(sm, "pixelFormat", 4);
                        XElement pos = Kid(sm, "position");
                        mi.sourceMode.positionX = Int(pos, "x", 0);
                        mi.sourceMode.positionY = Int(pos, "y", 0);
                    }
                    else if (mi.infoType == DisplayNative.MODE_TARGET && tm != null)
                    {
                        XElement sig = Kid(tm, "targetVideoSignalInfo");
                        VideoSignalInfo v = new VideoSignalInfo();
                        string pr = Str(sig, "pixelRate");
                        ulong px; ulong.TryParse(pr, NumberStyles.Integer, CultureInfo.InvariantCulture, out px);
                        v.pixelRate = px;
                        v.hSyncFreq = ReadRational(Kid(sig, "hSyncFreq"));
                        v.vSyncFreq = ReadRational(Kid(sig, "vSyncFreq"));
                        v.activeCx = Num(Kid(sig, "activeSize"), "cx", 0);
                        v.activeCy = Num(Kid(sig, "activeSize"), "cy", 0);
                        v.totalCx = Num(Kid(sig, "totalSize"), "cx", 0);
                        v.totalCy = Num(Kid(sig, "totalSize"), "cy", 0);
                        v.videoStandard = EnumVal<VideoStandard>(sig, "videoStandard", 255);
                        v.scanLineOrdering = EnumVal<ScanLineOrdering>(sig, "ScanLineOrdering", 1);
                        mi.targetMode = v;
                    }
                    p.Modes.Add(mi);
                }
            }

            XElement addl = Kid(root, "additionalInfo");
            if (addl != null)
            {
                foreach (XElement ae in addl.Elements("MonitorAdditionalInfo"))
                {
                    MonitorInfo m = new MonitorInfo();
                    m.Valid = Str(ae, "valid").ToLowerInvariant() == "true";
                    if (!m.Valid) continue;
                    m.ManufactureId = (ushort)Num(ae, "manufactureId", 0);
                    m.ProductCodeId = (ushort)Num(ae, "productCodeId", 0);
                    m.DevicePath = Decode(Str(ae, "monitorDevicePath"));
                    m.FriendlyName = Decode(Str(ae, "monitorFriendlyDevice")).Trim();
                    p.Monitors.Add(m);
                }
            }
            if (p.Paths.Count == 0) throw new CtlException("The profile has no display paths.");
            return p;
        }

        // ---------- writing ----------

        static XElement WithNs(XElement e)
        {
            e.Add(new XAttribute(XNamespace.Xmlns + "xsi", Xsi.NamespaceName));
            e.Add(new XAttribute(XNamespace.Xmlns + "xsd", Xsd.NamespaceName));
            return e;
        }

        static string EnumName<T>(uint v) where T : struct
        {
            return Enum.IsDefined(typeof(T), v) || typeof(T).IsDefined(typeof(FlagsAttribute), false)
                ? Enum.ToObject(typeof(T), v).ToString() : v.ToString(CultureInfo.InvariantCulture);
        }

        static XElement LuidEl(string name, Luid l)
        {
            return new XElement(name, new XElement("LowPart", l.LowPart.ToString(CultureInfo.InvariantCulture)),
                new XElement("HighPart", l.HighPart.ToString(CultureInfo.InvariantCulture)));
        }

        static XElement RatEl(string name, Rational r)
        {
            return new XElement(name, new XElement("numerator", r.Numerator), new XElement("denominator", r.Denominator));
        }

        public static string Write(Profile p)
        {
            XElement paths = new XElement("pathInfoArray");
            foreach (PathInfo pi in p.Paths)
            {
                paths.Add(WithNs(new XElement("DisplayConfigPathInfo",
                    new XElement("sourceInfo", LuidEl("adapterId", pi.sourceInfo.adapterId), new XElement("id", pi.sourceInfo.id),
                        new XElement("modeInfoIdx", pi.sourceInfo.modeInfoIdx), new XElement("statusFlags", EnumName<StatusFlags>(pi.sourceInfo.statusFlags))),
                    new XElement("targetInfo", LuidEl("adapterId", pi.targetInfo.adapterId), new XElement("id", pi.targetInfo.id),
                        new XElement("modeInfoIdx", pi.targetInfo.modeInfoIdx),
                        new XElement("outputTechnology", EnumName<OutputTechnology>(pi.targetInfo.outputTechnology)),
                        new XElement("rotation", EnumName<Rotation>(pi.targetInfo.rotation)),
                        new XElement("scaling", EnumName<Scaling>(pi.targetInfo.scaling)),
                        RatEl("refreshRate", pi.targetInfo.refreshRate),
                        new XElement("scanLineOrdering", EnumName<ScanLineOrdering>(pi.targetInfo.scanLineOrdering)),
                        new XElement("targetAvailable", pi.targetInfo.targetAvailable != 0 ? "true" : "false"),
                        new XElement("statusFlags", EnumName<StatusFlags>(pi.targetInfo.statusFlags))),
                    new XElement("flags", pi.flags))));
            }

            XElement modes = new XElement("modeInfoArray");
            foreach (ModeInfo mi in p.Modes)
            {
                XElement m = new XElement("modeInfo", new XElement("id", mi.id), WithNs(LuidEl("LUID", mi.adapterId)),
                    new XElement("DisplayConfigModeInfoType", mi.infoType == DisplayNative.MODE_SOURCE ? "Source" : "Target"));
                if (mi.infoType == DisplayNative.MODE_SOURCE)
                {
                    m.Add(WithNs(new XElement("DisplayConfigSourceMode", new XElement("width", mi.sourceMode.width),
                        new XElement("height", mi.sourceMode.height),
                        new XElement("pixelFormat", EnumName<PixelFormat>(mi.sourceMode.pixelFormat)),
                        new XElement("position", new XElement("x", mi.sourceMode.positionX), new XElement("y", mi.sourceMode.positionY)))));
                }
                else
                {
                    VideoSignalInfo v = mi.targetMode;
                    m.Add(WithNs(new XElement("DisplayConfigTargetMode", new XElement("targetVideoSignalInfo",
                        new XElement("pixelRate", v.pixelRate), RatEl("hSyncFreq", v.hSyncFreq), RatEl("vSyncFreq", v.vSyncFreq),
                        new XElement("activeSize", new XElement("cx", v.activeCx), new XElement("cy", v.activeCy)),
                        new XElement("totalSize", new XElement("cx", v.totalCx), new XElement("cy", v.totalCy)),
                        new XElement("videoStandard", EnumName<VideoStandard>(v.videoStandard)),
                        new XElement("ScanLineOrdering", EnumName<ScanLineOrdering>(v.scanLineOrdering))))));
                }
                modes.Add(m);
            }

            XElement addl = new XElement("additionalInfo");
            foreach (MonitorInfo mon in p.Monitors)
            {
                addl.Add(WithNs(new XElement("MonitorAdditionalInfo", new XElement("manufactureId", mon.ManufactureId),
                    new XElement("productCodeId", mon.ProductCodeId), new XElement("valid", "true"),
                    new XElement("monitorDevicePath", Encode(mon.DevicePath)),
                    new XElement("monitorFriendlyDevice", Encode(mon.FriendlyName)))));
                addl.Add(WithNs(new XElement("MonitorAdditionalInfo", new XElement("manufactureId", 0),
                    new XElement("productCodeId", 0), new XElement("valid", "false"),
                    new XElement("monitorDevicePath"), new XElement("monitorFriendlyDevice"))));
            }

            XDocument doc = new XDocument(new XDeclaration("1.0", "utf-8", null), new XElement("displaySettings", paths, modes, addl));
            return doc.Declaration + doc.Root.ToString(SaveOptions.DisableFormatting);
        }
    }
}
