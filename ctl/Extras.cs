// Per-display extras a profile can carry besides the layout: HDR on or off, the Windows scaling percentage, and the
// wallpaper. `extras capture` reads them from the displays that are on now; `extras apply` puts them back.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace DisplayDeck.Ctl
{
    [StructLayout(LayoutKind.Sequential)]
    public struct DeviceInfoHeader
    {
        public uint type;
        public uint size;
        public Luid adapterId;
        public uint id;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct AdvancedColorInfo
    {
        public DeviceInfoHeader header;
        public uint value;            // bit 0 supported, bit 1 enabled, bit 2 wide color enforced, bit 3 force disabled
        public uint colorEncoding;
        public uint bitsPerColorChannel;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SetAdvancedColorState
    {
        public DeviceInfoHeader header;
        public uint value;            // bit 0 enable
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DpiScaleGet
    {
        public DeviceInfoHeader header;
        public int minScaleRel;
        public int curScaleRel;
        public int maxScaleRel;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DpiScaleSet
    {
        public DeviceInfoHeader header;
        public int scaleRel;
    }

    public static class ExtrasNative
    {
        [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
        public static extern int GetAdvancedColor(ref AdvancedColorInfo request);

        [DllImport("user32.dll", EntryPoint = "DisplayConfigSetDeviceInfo")]
        public static extern int SetAdvancedColor(ref SetAdvancedColorState request);

        [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
        public static extern int GetDpiScale(ref DpiScaleGet request);

        [DllImport("user32.dll", EntryPoint = "DisplayConfigSetDeviceInfo")]
        public static extern int SetDpiScale(ref DpiScaleSet request);
    }

    public static class Extras
    {
        // The scaling steps Windows offers, in order. The undocumented scaling calls speak in steps
        // relative to the one Windows recommends for the display.
        public static readonly int[] DpiSteps = new int[] { 100, 125, 150, 175, 200, 225, 250, 300, 350, 400, 450, 500 };

        public const string HdrSection = "hdr", DpiSection = "dpi", WallpaperSection = "wallpaper";

        public static int StepIndex(int percent)
        {
            for (int i = 0; i < DpiSteps.Length; i++) if (DpiSteps[i] == percent) return i;
            return -1;
        }

        /// <summary>Absolute step indexes from the relative numbers Windows reports: (recommended, current, max).</summary>
        public static void StepsFromRelative(int minRel, int curRel, int maxRel, out int recommended, out int current, out int max)
        {
            recommended = -minRel;
            current = recommended + curRel;
            max = recommended + maxRel;
        }

        public static string FormatColor(uint colorRef)
        {
            // COLORREF is 0x00BBGGRR
            uint r = colorRef & 0xFF, g = (colorRef >> 8) & 0xFF, b = (colorRef >> 16) & 0xFF;
            return "#" + r.ToString("x2") + g.ToString("x2") + b.ToString("x2");
        }

        public static uint ParseColor(string s)
        {
            string t = (s ?? "").Trim().TrimStart('#');
            uint rgb;
            if (t.Length != 6 || !uint.TryParse(t, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out rgb))
                throw new CtlException("The wallpaper color must look like #1e1e1e.");
            uint r = (rgb >> 16) & 0xFF, g = (rgb >> 8) & 0xFF, b = rgb & 0xFF;
            return r | (g << 8) | (b << 16);
        }

        /// <summary>
        /// Picks the live display a saved entry belongs to: the same display id, otherwise the only display with the same
        /// make and model. Returns null when it is not connected (or ambiguous).
        /// </summary>
        public static LiveInfo Match(List<LiveInfo> live, long targetId, string hwId)
        {
            foreach (LiveInfo li in live)
                if (li.Active && li.Target.Path.targetInfo.id == targetId) return li;
            if (string.IsNullOrEmpty(hwId)) return null;
            LiveInfo found = null;
            foreach (LiveInfo li in live)
            {
                if (!li.Active) continue;
                if (!string.Equals(HwIdOf(li), hwId, StringComparison.OrdinalIgnoreCase)) continue;
                if (found != null) return null;
                found = li;
            }
            return found;
        }

        public static string HwIdOf(LiveInfo li)
        {
            return ProfileFile.EdidHwId(li.Target.Monitor.ManufactureId, li.Target.Monitor.ProductCodeId);
        }

        static string NameOf(LiveInfo li)
        {
            string n = li.Target.Monitor.FriendlyName;
            return string.IsNullOrEmpty(n) ? "Display " + li.Target.Path.targetInfo.id : n;
        }

        static DeviceInfoHeader Header(int type, Luid adapter, uint id, int size)
        {
            DeviceInfoHeader h = new DeviceInfoHeader();
            h.type = unchecked((uint)type);
            h.size = (uint)size;
            h.adapterId = adapter;
            h.id = id;
            return h;
        }

        // ---- HDR ----

        /// <summary>0 = not supported, 1 = supported and off, 2 = on.</summary>
        public static int HdrState(LiveInfo li)
        {
            AdvancedColorInfo c = new AdvancedColorInfo();
            c.header = Header(9, li.Target.Path.targetInfo.adapterId, li.Target.Path.targetInfo.id, Marshal.SizeOf(typeof(AdvancedColorInfo)));
            if (ExtrasNative.GetAdvancedColor(ref c) != 0) return 0;
            if ((c.value & 1) == 0) return 0;
            return (c.value & 2) != 0 ? 2 : 1;
        }

        static void SetHdr(LiveInfo li, bool on)
        {
            SetAdvancedColorState s = new SetAdvancedColorState();
            s.header = Header(10, li.Target.Path.targetInfo.adapterId, li.Target.Path.targetInfo.id, Marshal.SizeOf(typeof(SetAdvancedColorState)));
            s.value = on ? 1u : 0u;
            int rc = ExtrasNative.SetAdvancedColor(ref s);
            if (rc != 0) throw new CtlException("Windows would not switch HDR " + (on ? "on" : "off") + " (error " + rc + ").");
        }

        // ---- scaling ----

        public static bool GetDpi(LiveInfo li, out int percent, out int recommendedPercent)
        {
            percent = 0; recommendedPercent = 0;
            DpiScaleGet g = new DpiScaleGet();
            g.header = Header(-3, li.Target.Path.sourceInfo.adapterId, li.Target.Path.sourceInfo.id, Marshal.SizeOf(typeof(DpiScaleGet)));
            if (ExtrasNative.GetDpiScale(ref g) != 0) return false;
            int rec, cur, max;
            StepsFromRelative(g.minScaleRel, g.curScaleRel, g.maxScaleRel, out rec, out cur, out max);
            if (rec < 0 || rec >= DpiSteps.Length || cur < 0 || cur >= DpiSteps.Length) return false;
            percent = DpiSteps[cur];
            recommendedPercent = DpiSteps[rec];
            return true;
        }

        static void SetDpi(LiveInfo li, int percent)
        {
            DpiScaleGet g = new DpiScaleGet();
            g.header = Header(-3, li.Target.Path.sourceInfo.adapterId, li.Target.Path.sourceInfo.id, Marshal.SizeOf(typeof(DpiScaleGet)));
            if (ExtrasNative.GetDpiScale(ref g) != 0) throw new CtlException("Windows would not report this display's scaling.");
            int rec, cur, max;
            StepsFromRelative(g.minScaleRel, g.curScaleRel, g.maxScaleRel, out rec, out cur, out max);
            int want = StepIndex(percent);
            if (want < 0) throw new CtlException(percent + "% is not a scaling step Windows offers.");
            if (want > max || want < rec + g.minScaleRel)
                throw new CtlException("This display cannot be scaled to " + percent + "%.");
            DpiScaleSet s = new DpiScaleSet();
            s.header = Header(-4, li.Target.Path.sourceInfo.adapterId, li.Target.Path.sourceInfo.id, Marshal.SizeOf(typeof(DpiScaleSet)));
            s.scaleRel = want - rec;
            int rc = ExtrasNative.SetDpiScale(ref s);
            if (rc != 0) throw new CtlException("Windows would not change the scaling (error " + rc + ").");
        }

        // ---- capture ----

        static Dictionary<string, object> Entry(LiveInfo li)
        {
            Dictionary<string, object> o = new Dictionary<string, object>();
            o["targetId"] = li.Target.Path.targetInfo.id;
            o["hwId"] = HwIdOf(li);
            o["name"] = NameOf(li);
            return o;
        }

        public static Dictionary<string, object> Capture(List<string> sections, List<object> notes)
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            List<LiveInfo> live = LiveDisplays.Query(false);
            if (sections.Contains(HdrSection))
            {
                List<object> list = new List<object>();
                foreach (LiveInfo li in live)
                {
                    if (!li.Active) continue;
                    int st = HdrState(li);
                    if (st == 0) continue;
                    Dictionary<string, object> o = Entry(li);
                    o["enabled"] = st == 2;
                    list.Add(o);
                }
                if (list.Count == 0) notes.Add("None of the displays that are on support HDR, so no HDR setting was saved.");
                r[HdrSection] = list;
            }
            if (sections.Contains(DpiSection))
            {
                List<object> list = new List<object>();
                foreach (LiveInfo li in live)
                {
                    if (!li.Active) continue;
                    int pct, rec;
                    if (!GetDpi(li, out pct, out rec)) continue;
                    Dictionary<string, object> o = Entry(li);
                    o["percent"] = pct;
                    list.Add(o);
                }
                if (list.Count == 0) notes.Add("Windows would not report the scaling of any display, so none was saved.");
                r[DpiSection] = list;
            }
            if (sections.Contains(WallpaperSection))
            {
                Dictionary<string, object> w = Wallpaper.Capture(live, notes);
                if (w != null) r[WallpaperSection] = w;
            }
            return r;
        }

        // ---- apply ----

        static List<object> AsList(object o)
        {
            List<object> l = new List<object>();
            System.Collections.IEnumerable e = o as System.Collections.IEnumerable;
            if (e != null && !(o is string)) foreach (object x in e) l.Add(x);
            return l;
        }

        static long Num(Dictionary<string, object> d, string key)
        {
            return d.ContainsKey(key) && d[key] != null ? Convert.ToInt64(d[key]) : -1;
        }

        static string Str(Dictionary<string, object> d, string key)
        {
            return d.ContainsKey(key) && d[key] != null ? Convert.ToString(d[key]) : "";
        }

        public static Dictionary<string, object> Apply(Dictionary<string, object> extras)
        {
            List<object> applied = new List<object>();
            List<object> skipped = new List<object>();
            List<LiveInfo> live = LiveDisplays.Query(false);

            // Scaling first: switching HDR can re-run the display mode, so it goes last.
            if (extras.ContainsKey(DpiSection))
            {
                foreach (object o in AsList(extras[DpiSection]))
                {
                    Dictionary<string, object> d = o as Dictionary<string, object>;
                    if (d == null) continue;
                    string name = Str(d, "name");
                    LiveInfo li = Match(live, Num(d, "targetId"), Str(d, "hwId"));
                    if (li == null) { skipped.Add(name + " is not on, so its scaling was left alone."); continue; }
                    try
                    {
                        int want = (int)Num(d, "percent"), cur, rec;
                        if (GetDpi(li, out cur, out rec) && cur == want) continue;
                        SetDpi(li, want);
                        applied.Add(NameOf(li) + " scaling " + want + "%");
                    }
                    catch (CtlException e) { skipped.Add(NameOf(li) + ": " + e.Message); }
                }
            }
            if (extras.ContainsKey(HdrSection))
            {
                foreach (object o in AsList(extras[HdrSection]))
                {
                    Dictionary<string, object> d = o as Dictionary<string, object>;
                    if (d == null) continue;
                    string name = Str(d, "name");
                    LiveInfo li = Match(live, Num(d, "targetId"), Str(d, "hwId"));
                    if (li == null) { skipped.Add(name + " is not on, so its HDR setting was left alone."); continue; }
                    bool want = d.ContainsKey("enabled") && d["enabled"] is bool && (bool)d["enabled"];
                    try
                    {
                        int st = HdrState(li);
                        if (st == 0) { skipped.Add(NameOf(li) + " does not support HDR right now."); continue; }
                        if ((st == 2) == want) continue;
                        SetHdr(li, want);
                        applied.Add(NameOf(li) + " HDR " + (want ? "on" : "off"));
                    }
                    catch (CtlException e) { skipped.Add(NameOf(li) + ": " + e.Message); }
                }
            }
            if (extras.ContainsKey(WallpaperSection))
            {
                Dictionary<string, object> w = extras[WallpaperSection] as Dictionary<string, object>;
                if (w != null) Wallpaper.Apply(w, live, applied, skipped);
            }

            Dictionary<string, object> r = new Dictionary<string, object>();
            r["ok"] = true;
            r["applied"] = applied;
            r["skipped"] = skipped;
            return r;
        }
    }
}
