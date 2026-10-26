// DDC/CI: talking to the monitors themselves over their DisplayPort / HDMI / DVI cable (input source, brightness,
// contrast, volume, power, color presets, ...), through the Windows Monitor Configuration API.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace DisplayDeck.Ctl
{
    [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Unicode)]
    public struct PhysicalMonitor
    {
        public IntPtr hPhysicalMonitor;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string description;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MonitorInfoEx
    {
        public uint cbSize;
        public int left, top, right, bottom;
        public int wLeft, wTop, wRight, wBottom;
        public uint flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }

    /// <summary>One feature reading from a monitor.</summary>
    public class VcpValue
    {
        public uint Current;
        public uint Max;
    }

    /// <summary>What a monitor's capabilities string says it can do.</summary>
    public class Capabilities
    {
        public string Raw = "";
        public string Model = "";
        public string Type = "";
        public string MccsVersion = "";
        /// <summary>Supported VCP code -> the values it lists for itself (empty for plain on/off or ranges).</summary>
        public SortedDictionary<int, List<int>> Codes = new SortedDictionary<int, List<int>>();
    }

    public class DdcMonitor
    {
        public IntPtr Handle;
        public string Description = "";
        public string GdiName = "";
        public uint TargetId;
        public string Name = "";
        public string HwId = "";
        /// <summary>Set when the monitor is reached through the NVIDIA card instead of the Windows monitor API.</summary>
        public NvMonitor Nv;
        public string Backend = "windows";
        /// <summary>How the display is connected: displayport, hdmi, dvi, vga or other.</summary>
        public string Connector = "other";
    }

    public static class Ddc
    {
        delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, IntPtr rect, IntPtr data);

        [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc cb, IntPtr data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfoEx info);
        [DllImport("dxva2.dll", SetLastError = true)] static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, out uint count);
        [DllImport("dxva2.dll", SetLastError = true)] static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, uint count, [Out] PhysicalMonitor[] monitors);
        [DllImport("dxva2.dll")] static extern bool DestroyPhysicalMonitors(uint count, PhysicalMonitor[] monitors);
        [DllImport("dxva2.dll")] static extern bool GetVCPFeatureAndVCPFeatureReply(IntPtr h, byte code, out uint type, out uint current, out uint max);
        [DllImport("dxva2.dll")] static extern bool SetVCPFeature(IntPtr h, byte code, uint value);
        [DllImport("dxva2.dll")] static extern bool GetCapabilitiesStringLength(IntPtr h, out uint length);
        [DllImport("dxva2.dll")] static extern bool CapabilitiesRequestAndCapabilitiesReply(IntPtr h, byte[] buffer, uint length);

        // VCP codes DisplayDeck knows how to present. Anything else is available through the raw commands.
        public const int Brightness = 0x10, Contrast = 0x12, ColorPreset = 0x14, GainRed = 0x16, GainGreen = 0x18, GainBlue = 0x1A,
            Sharpness = 0x87, Volume = 0x62, Mute = 0x8D, InputSource = 0x60, PowerMode = 0xD6;

        /// <summary>Codes that reset the monitor to factory settings. Never sent.</summary>
        public static bool IsBlocked(int code)
        {
            return code == 0x04 || code == 0x05 || code == 0x06 || code == 0x08 || code == 0x0A;
        }

        // ---------------- finding monitors ----------------

        /// <summary>
        /// The monitors that are on in Windows, each with its DDC handle and display identity. The caller must
        /// release them with Release.
        /// </summary>
        public static List<string> Notes = new List<string>();

        public static string ConnectorName(uint outputTechnology)
        {
            switch (outputTechnology)
            {
                case 10: case 11: case 12: case 13: return "displayport";   // DisplayPort and USB-C style outputs
                case 5: return "hdmi";
                case 4: return "dvi";
                case 0: return "vga";
                default: return "other";
            }
        }

        public static List<DdcMonitor> Open()
        {
            // GDI device name -> display target id, from the active display paths.
            Dictionary<string, uint> targetByGdi = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
            Dictionary<uint, LiveInfo> liveByTarget = new Dictionary<uint, LiveInfo>();
            foreach (LiveInfo li in LiveDisplays.Query(false))
            {
                liveByTarget[li.Target.Path.targetInfo.id] = li;
                if (!li.Active || !li.ActivePath.HasValue) continue;
                string gdi = DisplayModes.GdiName(li.ActivePath.Value.sourceInfo.adapterId, li.ActivePath.Value.sourceInfo.id);
                Notes.Add("active target " + li.Target.Path.targetInfo.id + " is " + (gdi.Length > 0 ? gdi : "(no gdi name)"));
                if (gdi.Length > 0 && !targetByGdi.ContainsKey(gdi)) targetByGdi[gdi] = li.Target.Path.targetInfo.id;
            }

            List<DdcMonitor> found = new List<DdcMonitor>();
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, delegate (IntPtr hMon, IntPtr hdc, IntPtr rect, IntPtr data)
            {
                MonitorInfoEx mi = new MonitorInfoEx();
                mi.cbSize = (uint)Marshal.SizeOf(typeof(MonitorInfoEx));
                if (!GetMonitorInfo(hMon, ref mi)) { Notes.Add("GetMonitorInfo failed"); return true; }
                uint count;
                if (!GetNumberOfPhysicalMonitorsFromHMONITOR(hMon, out count) || count == 0) { Notes.Add(mi.szDevice + ": Windows lists no physical monitors (error " + Marshal.GetLastWin32Error() + ")"); return true; }
                PhysicalMonitor[] pms = new PhysicalMonitor[count];
                if (!GetPhysicalMonitorsFromHMONITOR(hMon, count, pms)) { Notes.Add(mi.szDevice + ": GetPhysicalMonitors failed (error " + Marshal.GetLastWin32Error() + ", count " + count + ", struct size " + Marshal.SizeOf(typeof(PhysicalMonitor)) + ")"); return true; }
                for (int i = 0; i < pms.Length; i++)
                {
                    DdcMonitor m = new DdcMonitor();
                    m.Handle = pms[i].hPhysicalMonitor;
                    m.Description = pms[i].description ?? "";
                    m.GdiName = mi.szDevice ?? "";
                    uint t;
                    if (i == 0 && targetByGdi.TryGetValue(m.GdiName, out t))
                    {
                        m.TargetId = t;
                        MonitorInfo mon = liveByTarget[t].Target.Monitor;
                        m.Name = mon.FriendlyName;
                        m.HwId = ProfileFile.EdidHwId(mon.ManufactureId, mon.ProductCodeId);
                        m.Connector = ConnectorName(liveByTarget[t].Target.Path.targetInfo.outputTechnology);
                    }
                    else
                    {
                        // The extra monitors of a cloned display have no target of their own: give up their handle.
                        DestroyPhysicalMonitors(1, new PhysicalMonitor[] { pms[i] });
                        continue;
                    }
                    found.Add(m);
                }
                return true;
            }, IntPtr.Zero);

            // Displays Windows would not hand out a monitor for (it refuses some NVIDIA-driven ones): ask the card.
            HashSet<string> handled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (DdcMonitor have in found) handled.Add(have.GdiName);
            foreach (KeyValuePair<string, uint> kv in targetByGdi)
            {
                if (handled.Contains(kv.Key)) continue;
                NvMonitor nv = Nvapi.Open(kv.Key);
                if (nv == null) continue;
                DdcMonitor m = new DdcMonitor();
                m.Nv = nv;
                m.Backend = "nvidia";
                m.GdiName = kv.Key;
                m.TargetId = kv.Value;
                MonitorInfo mon = liveByTarget[kv.Value].Target.Monitor;
                m.Name = mon.FriendlyName;
                m.HwId = ProfileFile.EdidHwId(mon.ManufactureId, mon.ProductCodeId);
                m.Connector = ConnectorName(liveByTarget[kv.Value].Target.Path.targetInfo.outputTechnology);
                found.Add(m);
            }
            found.Sort(delegate (DdcMonitor a, DdcMonitor b) { return a.TargetId.CompareTo(b.TargetId); });
            return found;
        }

        public static void Release(List<DdcMonitor> monitors)
        {
            foreach (DdcMonitor m in monitors)
            {
                if (m.Handle == IntPtr.Zero) continue;
                PhysicalMonitor pm = new PhysicalMonitor();
                pm.hPhysicalMonitor = m.Handle;
                DestroyPhysicalMonitors(1, new PhysicalMonitor[] { pm });
            }
        }

        public static DdcMonitor Find(List<DdcMonitor> monitors, uint targetId)
        {
            foreach (DdcMonitor m in monitors) if (m.TargetId == targetId) return m;
            throw new CtlException("That display is not on, or Windows cannot reach it. Only displays that are on can be controlled.");
        }

        // ---------------- reading and writing ----------------

        /// <summary>Reads one feature, retrying once: monitors drop the odd request. Null when unsupported.</summary>
        public static VcpValue Get(DdcMonitor m, int code)
        {
            if (m.Nv != null) return Nvapi.GetVcp(m.Nv, code);
            for (int attempt = 0; attempt < 2; attempt++)
            {
                uint type, cur, max;
                if (GetVCPFeatureAndVCPFeatureReply(m.Handle, (byte)code, out type, out cur, out max))
                {
                    VcpValue v = new VcpValue();
                    v.Current = cur; v.Max = max;
                    return v;
                }
                System.Threading.Thread.Sleep(60);
            }
            return null;
        }

        public static void Set(DdcMonitor m, int code, uint value)
        {
            if (IsBlocked(code)) throw new CtlException("That command resets the display to factory settings, so DisplayDeck will not send it.");
            if (m.Nv != null)
            {
                if (!Nvapi.SetVcp(m.Nv, code, value)) throw new CtlException("The display did not accept that command (DDC/CI may be switched off in its on-screen menu).");
                return;
            }
            for (int attempt = 0; attempt < 2; attempt++)
            {
                if (SetVCPFeature(m.Handle, (byte)code, value)) return;
                System.Threading.Thread.Sleep(80);
            }
            throw new CtlException("The display did not accept that command (DDC/CI may be switched off in its on-screen menu).");
        }

        /// <summary>The capabilities string, which can take a few seconds; null when the display does not answer.</summary>
        public static string ReadCapabilities(DdcMonitor m)
        {
            // Some monitors (a Samsung among them) sometimes return a garbled or shortened list. Trust a result only
            // when two reads in a row agree, and always when its brackets balance.
            string previous = null;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                string s = ReadCapabilitiesOnce(m);
                if (s != null && IsComplete(s))
                {
                    if (previous != null && previous == s) return s;
                    previous = s;
                }
                else System.Threading.Thread.Sleep(150);
            }
            return previous;
        }

        /// <summary>True when the text starts with ( and every ( has its ).</summary>
        public static bool IsComplete(string s)
        {
            if (s == null) return false;
            string t = s.Trim();
            if (t.Length < 2 || t[0] != (char)40) return false;
            int depth = 0;
            foreach (char c in t)
            {
                if (c == (char)40) depth++;
                else if (c == (char)41) { depth--; if (depth < 0) return false; }
            }
            return depth == 0;
        }

        static string ReadCapabilitiesOnce(DdcMonitor m)
        {
            if (m.Nv != null) return Nvapi.ReadCapabilities(m.Nv);
            uint len;
            if (!GetCapabilitiesStringLength(m.Handle, out len) || len < 2 || len > 8192) return null;
            byte[] buf = new byte[len];
            if (!CapabilitiesRequestAndCapabilitiesReply(m.Handle, buf, len)) return null;
            int n = 0;
            while (n < buf.Length && buf[n] != 0) n++;
            return Encoding.ASCII.GetString(buf, 0, n).Trim();
        }

        // ---------------- capabilities string ----------------

        /// <summary>
        /// Parses a capabilities string such as
        /// (prot(monitor)type(LCD)model(X)vcp(02 10 12 14(01 05) 60(0F 11 12) D6(01 04 05))mccs_ver(2.2)).
        /// </summary>
        public static Capabilities ParseCapabilities(string raw)
        {
            Capabilities c = new Capabilities();
            c.Raw = raw ?? "";
            string s = c.Raw.Trim();
            if (s.Length == 0) return c;
            c.Model = Group(s, "model");
            c.Type = Group(s, "type");
            c.MccsVersion = Group(s, "mccs_ver");
            string vcp = Group(s, "vcp");
            int i = 0;
            while (i < vcp.Length)
            {
                char ch = vcp[i];
                if (Uri.IsHexDigit(ch) && i + 1 < vcp.Length && Uri.IsHexDigit(vcp[i + 1]))
                {
                    int code = int.Parse(vcp.Substring(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    i += 2;
                    List<int> values = new List<int>();
                    if (i < vcp.Length && vcp[i] == '(')
                    {
                        int depth = 0, start = i + 1, j = i;
                        for (; j < vcp.Length; j++)
                        {
                            if (vcp[j] == '(') depth++;
                            else if (vcp[j] == ')') { depth--; if (depth == 0) break; }
                        }
                        // Values are a flat list of hex bytes; a nested group (some monitors add sub-features) is skipped.
                        string inner = vcp.Substring(start, Math.Max(0, j - start));
                        int d2 = 0;
                        StringBuilder token = new StringBuilder();
                        Action flush = delegate
                        {
                            if (token.Length > 0)
                            {
                                int v;
                                if (d2 == 0 && int.TryParse(token.ToString(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v)) values.Add(v);
                                token.Length = 0;
                            }
                        };
                        foreach (char x in inner)
                        {
                            if (x == '(') { flush(); d2++; }
                            else if (x == ')') { flush(); d2--; }
                            else if (x == ' ') flush();
                            else token.Append(x);
                        }
                        flush();
                        i = j + 1;
                    }
                    c.Codes[code] = values;
                }
                else i++;
            }
            return c;
        }

        /// <summary>The text inside name( ... ), balanced, or "" when absent.</summary>
        static string Group(string s, string name)
        {
            int idx = 0;
            while (true)
            {
                idx = s.IndexOf(name + "(", idx, StringComparison.OrdinalIgnoreCase);
                if (idx < 0) return "";
                // must start a token: the start of the string or just after '(' or ')' or a space
                if (idx == 0 || "() ".IndexOf(s[idx - 1]) >= 0) break;
                idx += name.Length;
            }
            int start = idx + name.Length + 1, depth = 1, j = start;
            for (; j < s.Length; j++)
            {
                if (s[j] == '(') depth++;
                else if (s[j] == ')') { depth--; if (depth == 0) break; }
            }
            return s.Substring(start, Math.Max(0, j - start));
        }
    }
}
