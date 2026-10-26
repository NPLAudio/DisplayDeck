// Builds a profile from an edited layout (which displays are on, where, at what size and refresh rate).
// Pure logic: the live display list and the saved profiles used as templates are passed in.
using System;
using System.Collections.Generic;
using System.Globalization;

namespace DisplayDeck.Ctl
{
    /// <summary>One display in an edited layout.</summary>
    public class SpecDisplay
    {
        public uint TargetId;
        public string Name = "";
        public ushort Manufacturer;
        public ushort Product;
        public bool Enabled = true;
        public bool Primary;
        public int Width, Height, X, Y;
        public double Refresh;     // Hz; 0 = keep the template's
        public uint Rotation;      // 1..4 (0, 90, 180, 270 degrees); 0 = keep the template's
    }

    /// <summary>A connected display as Windows reports it right now (one path per target is enough).</summary>
    public class LiveTarget
    {
        public PathInfo Path;
        public MonitorInfo Monitor;
    }

    public static class LayoutBuilder
    {
        public static List<SpecDisplay> ParseSpec(object json)
        {
            Dictionary<string, object> root = json as Dictionary<string, object>;
            if (root == null || !root.ContainsKey("displays")) throw new CtlException("The layout has no displays.");
            List<SpecDisplay> list = new List<SpecDisplay>();
            foreach (object o in (System.Collections.IEnumerable)root["displays"])
            {
                Dictionary<string, object> d = (Dictionary<string, object>)o;
                SpecDisplay s = new SpecDisplay();
                s.TargetId = (uint)Num(d, "targetId", 0);
                s.Name = d.ContainsKey("name") ? Convert.ToString(d["name"]) : "";
                s.Manufacturer = (ushort)Num(d, "manufacturer", 0);
                s.Product = (ushort)Num(d, "product", 0);
                s.Enabled = !d.ContainsKey("enabled") || Convert.ToBoolean(d["enabled"]);
                s.Primary = d.ContainsKey("primary") && Convert.ToBoolean(d["primary"]);
                s.Width = (int)Num(d, "width", 0);
                s.Height = (int)Num(d, "height", 0);
                s.X = (int)Num(d, "x", 0);
                s.Y = (int)Num(d, "y", 0);
                s.Refresh = Num(d, "refresh", 0);
                s.Rotation = (uint)Num(d, "rotation", 0);
                list.Add(s);
            }
            return list;
        }

        static double Num(Dictionary<string, object> d, string key, double def)
        {
            if (!d.ContainsKey(key) || d[key] == null) return def;
            double v;
            return double.TryParse(Convert.ToString(d[key], CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : def;
        }

        static Rational ToRational(double hz)
        {
            Rational r = new Rational();
            r.Numerator = (uint)Math.Round(hz * 1000);
            r.Denominator = 1000;
            return r;
        }

        static double ToHz(Rational r) { return r.Denominator == 0 ? 0 : (double)r.Numerator / r.Denominator; }

        /// <summary>
        /// Turns an edited layout into a profile. Each enabled display needs a template to copy the connection
        /// details from: a saved profile that used it, or, failing that, the live display list.
        /// </summary>
        public static Profile Build(List<SpecDisplay> spec, List<Profile> templates, List<LiveTarget> live)
        {
            List<SpecDisplay> on = new List<SpecDisplay>();
            foreach (SpecDisplay s in spec) if (s.Enabled) on.Add(s);
            if (on.Count == 0) throw new CtlException("Turn on at least one display.");

            HashSet<uint> seen = new HashSet<uint>();
            foreach (SpecDisplay s in on)
            {
                if (s.Width < 320 || s.Height < 200 || s.Width > 16384 || s.Height > 16384)
                    throw new CtlException((s.Name.Length > 0 ? s.Name : "Display " + s.TargetId) + " needs a resolution between 320x200 and 16384x16384.");
                if (!seen.Add(s.TargetId)) throw new CtlException("The same display appears twice in the layout.");
            }

            // Windows wants the primary display at (0,0): shift everything so it is.
            SpecDisplay primary = on[0];
            foreach (SpecDisplay s in on) if (s.Primary) { primary = s; break; }
            int dx = -primary.X, dy = -primary.Y;

            Profile result = new Profile();
            for (int i = 0; i < on.Count; i++)
            {
                SpecDisplay s = on[i];
                string label = s.Name.Length > 0 ? s.Name : "Display " + s.TargetId;

                PathInfo path = default(PathInfo);
                ModeInfo tgtMode = default(ModeInfo);
                bool haveTgt = false, found = false;
                MonitorInfo mon = null;
                double templRefresh = 0;
                int templW = 0, templH = 0;
                uint templRot = 1;

                foreach (Profile t in templates)
                {
                    foreach (PathInfo tp in t.Paths)
                    {
                        if ((tp.flags & DisplayNative.PATH_ACTIVE) == 0 || tp.targetInfo.id != s.TargetId) continue;
                        ModeInfo src;
                        try { src = DisplayOps.FindMode(t, DisplayNative.MODE_SOURCE, tp.sourceInfo.id, tp.sourceInfo.modeInfoIdx); }
                        catch (CtlException) { continue; }
                        path = tp; found = true;
                        templW = (int)src.sourceMode.width; templH = (int)src.sourceMode.height;
                        templRefresh = ToHz(tp.targetInfo.refreshRate); templRot = tp.targetInfo.rotation;
                        try { tgtMode = DisplayOps.FindMode(t, DisplayNative.MODE_TARGET, tp.targetInfo.id, tp.targetInfo.modeInfoIdx); haveTgt = true; }
                        catch (CtlException) { haveTgt = false; }
                        mon = DisplayOps.MonitorFor(t, s.TargetId);
                        break;
                    }
                    if (found) break;
                }
                if (!found)
                {
                    foreach (LiveTarget lt in live)
                        if (lt.Path.targetInfo.id == s.TargetId) { path = lt.Path; found = true; mon = lt.Monitor; break; }
                }
                if (!found) throw new CtlException(label + " is not connected and no saved profile has it, so it cannot be added.");

                path.flags = DisplayNative.PATH_ACTIVE;
                path.sourceInfo.id = (uint)i;
                path.sourceInfo.statusFlags = (uint)StatusFlags.InUse;
                path.targetInfo.statusFlags = (uint)StatusFlags.InUse;
                if (path.targetInfo.scanLineOrdering == 0) path.targetInfo.scanLineOrdering = (uint)ScanLineOrdering.Progressive;
                if (path.targetInfo.scaling == 0) path.targetInfo.scaling = (uint)Scaling.Identity;
                uint rotation = s.Rotation >= 1 && s.Rotation <= 4 ? s.Rotation : (templRot >= 1 && templRot <= 4 ? templRot : 1);
                path.targetInfo.rotation = rotation;
                double refresh = s.Refresh > 0 ? s.Refresh : templRefresh;
                if (refresh > 0) path.targetInfo.refreshRate = ToRational(refresh);

                // The saved signal timing only stays valid when nothing about the mode changed; otherwise let
                // Windows choose the timing for the new size and refresh rate.
                bool unchanged = haveTgt && templW == s.Width && templH == s.Height && rotation == templRot
                    && (s.Refresh <= 0 || Math.Abs(s.Refresh - templRefresh) < 0.02);

                ModeInfo sm = new ModeInfo();
                sm.infoType = DisplayNative.MODE_SOURCE;
                sm.id = (uint)i;
                sm.adapterId = path.sourceInfo.adapterId;
                sm.sourceMode.width = (uint)s.Width;
                sm.sourceMode.height = (uint)s.Height;
                sm.sourceMode.pixelFormat = (uint)PixelFormat.Pixelformat32Bpp;
                sm.sourceMode.positionX = s.X + dx;
                sm.sourceMode.positionY = s.Y + dy;
                path.sourceInfo.modeInfoIdx = (uint)result.Modes.Count;
                result.Modes.Add(sm);

                if (unchanged)
                {
                    tgtMode.id = s.TargetId;
                    tgtMode.adapterId = path.targetInfo.adapterId;
                    path.targetInfo.modeInfoIdx = (uint)result.Modes.Count;
                    result.Modes.Add(tgtMode);
                }
                else path.targetInfo.modeInfoIdx = DisplayNative.IDX_INVALID;

                result.Paths.Add(path);

                MonitorInfo m = new MonitorInfo();
                if (mon != null)
                {
                    m.ManufactureId = mon.ManufactureId; m.ProductCodeId = mon.ProductCodeId;
                    m.FriendlyName = mon.FriendlyName; m.DevicePath = mon.DevicePath;
                }
                if (m.FriendlyName.Length == 0) m.FriendlyName = s.Name;
                if (m.ManufactureId == 0 && m.ProductCodeId == 0) { m.ManufactureId = s.Manufacturer; m.ProductCodeId = s.Product; }
                if (m.DevicePath.Length == 0) m.DevicePath = ProfileFile.SynthesizeDevicePath(s.TargetId, m.ManufactureId, m.ProductCodeId);
                m.Valid = true;
                result.Monitors.Add(m);
            }
            return result;
        }
    }
}
