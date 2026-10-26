// Saving the live display layout and applying a saved one.
using System;
using System.Collections.Generic;
using System.Globalization;

namespace DisplayDeck.Ctl
{
    public class MatchedDisplay
    {
        public uint TargetId;
        public string Name = "";
        public PathInfo SavedPath;
        public PathInfo CurrentPath;
    }

    public class ApplyResult
    {
        public List<MatchedDisplay> Matched = new List<MatchedDisplay>();
        public List<string> Skipped = new List<string>();
        public string Method = "";
        public bool Validated;
        public bool Applied;
    }

    public static class DisplayOps
    {
        // ---------------- save ----------------

        public static Profile CaptureLive()
        {
            PathInfo[] paths; ModeInfo[] modes;
            DisplayNative.Query(DisplayNative.QDC_ONLY_ACTIVE_PATHS, out paths, out modes);
            Profile p = new Profile();
            p.Paths.AddRange(paths);
            p.Modes.AddRange(modes);
            foreach (PathInfo pi in paths)
            {
                TargetDeviceName n = DisplayNative.GetTargetName(pi.targetInfo.adapterId, pi.targetInfo.id);
                MonitorInfo m = new MonitorInfo();
                m.ManufactureId = n.edidManufactureId;
                m.ProductCodeId = n.edidProductCodeId;
                m.FriendlyName = n.monitorFriendlyDeviceName ?? "";
                m.DevicePath = n.monitorDevicePath ?? "";
                // Windows does not always return the device path. Rebuild an equivalent one from what it does
                // return (the connector's target id and the EDID ids), so the saved file always identifies
                // each monitor the same way.
                if (m.DevicePath.Length == 0)
                    m.DevicePath = ProfileFile.SynthesizeDevicePath(pi.targetInfo.id, n.edidManufactureId, n.edidProductCodeId);
                m.Valid = true;
                p.Monitors.Add(m);
            }
            if (p.Paths.Count == 0) throw new CtlException("No active displays were found.");
            return p;
        }

        // ---------------- matching ----------------

        static string NameFor(Profile saved, uint targetId)
        {
            foreach (MonitorInfo m in saved.Monitors)
            {
                uint? t = m.TargetId();
                if (t.HasValue && t.Value == targetId && m.FriendlyName.Length > 0)
                    return System.Text.RegularExpressions.Regex.Replace(m.FriendlyName, "\\s+", " ").Trim();
            }
            return "display " + targetId.ToString(CultureInfo.InvariantCulture);
        }

        public static MonitorInfo MonitorFor(Profile saved, uint targetId)
        {
            foreach (MonitorInfo m in saved.Monitors)
            {
                uint? t = m.TargetId();
                if (t.HasValue && t.Value == targetId) return m;
            }
            // Files whose monitor entries carry no target id (an empty device path) were written one entry per
            // active path, in path order.
            List<PathInfo> active = new List<PathInfo>();
            foreach (PathInfo sp in saved.Paths) if ((sp.flags & DisplayNative.PATH_ACTIVE) != 0) active.Add(sp);
            if (active.Count == saved.Monitors.Count)
            {
                for (int i = 0; i < active.Count; i++)
                    if (active[i].targetInfo.id == targetId && saved.Monitors[i].TargetId() == null) return saved.Monitors[i];
            }
            return null;
        }

        /// <summary>
        /// For each saved ACTIVE path, find the current path to the same connector. Adapter ids change between
        /// boots, so paths are matched by target id (and EDID ids when several adapters offer the same target),
        /// never by the saved adapter id.
        /// </summary>
        public static List<MatchedDisplay> Match(Profile saved, PathInfo[] current, List<string> skipped)
        {
            List<MatchedDisplay> result = new List<MatchedDisplay>();
            foreach (PathInfo sp in saved.Paths)
            {
                if ((sp.flags & DisplayNative.PATH_ACTIVE) == 0) continue;
                string name = NameFor(saved, sp.targetInfo.id);
                MonitorInfo mon = MonitorFor(saved, sp.targetInfo.id);

                List<PathInfo> candidates = new List<PathInfo>();
                foreach (PathInfo cp in current)
                    if (cp.targetInfo.id == sp.targetInfo.id && cp.targetInfo.targetAvailable != 0) candidates.Add(cp);

                if (candidates.Count > 1 && mon != null && (mon.ManufactureId != 0 || mon.ProductCodeId != 0))
                {
                    List<PathInfo> byEdid = new List<PathInfo>();
                    foreach (PathInfo cp in candidates)
                    {
                        TargetDeviceName n = DisplayNative.GetTargetName(cp.targetInfo.adapterId, cp.targetInfo.id);
                        if (n.edidManufactureId == mon.ManufactureId && n.edidProductCodeId == mon.ProductCodeId) byEdid.Add(cp);
                    }
                    if (byEdid.Count > 0) candidates = byEdid;
                }
                if (candidates.Count == 0) { skipped.Add(name + " is not connected"); continue; }

                PathInfo best = candidates[0];
                foreach (PathInfo cp in candidates)
                    if (cp.sourceInfo.id == sp.sourceInfo.id) { best = cp; break; }

                MatchedDisplay md = new MatchedDisplay();
                md.TargetId = sp.targetInfo.id;
                md.Name = name;
                md.SavedPath = sp;
                md.CurrentPath = best;
                result.Add(md);
            }
            return result;
        }

        /// <summary>Builds the path/mode arrays to hand to SetDisplayConfig, using current adapter ids.</summary>
        public static void Build(Profile saved, List<MatchedDisplay> matched, out PathInfo[] paths, out ModeInfo[] modes)
        {
            List<PathInfo> outPaths = new List<PathInfo>();
            List<ModeInfo> outModes = new List<ModeInfo>();
            Dictionary<string, uint> sourceIdx = new Dictionary<string, uint>();

            foreach (MatchedDisplay md in matched)
            {
                PathInfo np = md.CurrentPath;       // valid adapter / ids for this boot
                np.sourceInfo.id = md.SavedPath.sourceInfo.id;
                np.flags = DisplayNative.PATH_ACTIVE;
                np.targetInfo.rotation = md.SavedPath.targetInfo.rotation;
                np.targetInfo.scaling = md.SavedPath.targetInfo.scaling;
                np.targetInfo.refreshRate = md.SavedPath.targetInfo.refreshRate;
                np.targetInfo.scanLineOrdering = md.SavedPath.targetInfo.scanLineOrdering;
                np.sourceInfo.statusFlags = (uint)StatusFlags.InUse;
                np.targetInfo.statusFlags = (uint)StatusFlags.InUse;

                // source mode (shared by cloned displays on the same source)
                string key = np.sourceInfo.adapterId.LowPart + ":" + np.sourceInfo.adapterId.HighPart + ":" + np.sourceInfo.id;
                uint sIdx;
                if (!sourceIdx.TryGetValue(key, out sIdx))
                {
                    ModeInfo savedSrc = FindMode(saved, DisplayNative.MODE_SOURCE, md.SavedPath.sourceInfo.id, md.SavedPath.sourceInfo.modeInfoIdx);
                    ModeInfo sm = new ModeInfo();
                    sm.infoType = DisplayNative.MODE_SOURCE;
                    sm.id = np.sourceInfo.id;
                    sm.adapterId = np.sourceInfo.adapterId;
                    sm.sourceMode = savedSrc.sourceMode;
                    sIdx = (uint)outModes.Count;
                    outModes.Add(sm);
                    sourceIdx[key] = sIdx;
                }
                np.sourceInfo.modeInfoIdx = sIdx;

                // Edited layouts leave the signal timing out so Windows picks it for the chosen size and refresh rate.
                bool hasTarget = true;
                ModeInfo savedTgt = default(ModeInfo);
                try { savedTgt = FindMode(saved, DisplayNative.MODE_TARGET, md.SavedPath.targetInfo.id, md.SavedPath.targetInfo.modeInfoIdx); }
                catch (CtlException) { if (md.SavedPath.targetInfo.modeInfoIdx != DisplayNative.IDX_INVALID) throw; hasTarget = false; }
                if (hasTarget)
                {
                    ModeInfo tm = new ModeInfo();
                    tm.infoType = DisplayNative.MODE_TARGET;
                    tm.id = np.targetInfo.id;
                    tm.adapterId = np.targetInfo.adapterId;
                    tm.targetMode = savedTgt.targetMode;
                    np.targetInfo.modeInfoIdx = (uint)outModes.Count;
                    outModes.Add(tm);
                }
                else np.targetInfo.modeInfoIdx = DisplayNative.IDX_INVALID;

                outPaths.Add(np);
            }
            paths = outPaths.ToArray();
            modes = outModes.ToArray();
        }

        public static ModeInfo FindMode(Profile p, uint type, uint id, uint hintIdx)
        {
            if (hintIdx != DisplayNative.IDX_INVALID && hintIdx < p.Modes.Count && p.Modes[(int)hintIdx].infoType == type)
                return p.Modes[(int)hintIdx];
            foreach (ModeInfo m in p.Modes) if (m.infoType == type && m.id == id) return m;
            throw new CtlException("The profile is missing display mode data (type " + type + ", id " + id + ").");
        }

        // ---------------- apply ----------------

        public static ApplyResult Apply(Profile saved, bool validateOnly)
        {
            PathInfo[] cur; ModeInfo[] curModes;
            DisplayNative.Query(DisplayNative.QDC_ALL_PATHS, out cur, out curModes);

            ApplyResult r = new ApplyResult();
            r.Matched = Match(saved, cur, r.Skipped);
            if (r.Matched.Count == 0)
                throw new CtlException("None of this profile's displays are connected" + (r.Skipped.Count > 0 ? " (" + string.Join("; ", r.Skipped.ToArray()) + ")" : "") + ". Nothing was changed.");

            PathInfo[] paths; ModeInfo[] modes;
            Build(saved, r.Matched, out paths, out modes);

            uint baseFlags = DisplayNative.SDC_USE_SUPPLIED_DISPLAY_CONFIG | DisplayNative.SDC_ALLOW_CHANGES;
            uint doFlags = validateOnly ? DisplayNative.SDC_VALIDATE : (DisplayNative.SDC_APPLY | DisplayNative.SDC_SAVE_TO_DATABASE);

            int rc = DisplayNative.SetDisplayConfig((uint)paths.Length, paths, (uint)modes.Length, modes, baseFlags | doFlags);
            r.Method = "full";
            if (rc != 0)
            {
                // Fallback: let Windows pick the modes for these paths, then (when applying) restore the saved ones.
                PathInfo[] loose = (PathInfo[])paths.Clone();
                for (int i = 0; i < loose.Length; i++)
                {
                    loose[i].sourceInfo.modeInfoIdx = DisplayNative.IDX_INVALID;
                    loose[i].targetInfo.modeInfoIdx = DisplayNative.IDX_INVALID;
                }
                int rc2 = DisplayNative.SetDisplayConfig((uint)loose.Length, loose, 0, null, baseFlags | doFlags);
                if (rc2 != 0)
                    throw new CtlException("Windows rejected the layout (error " + rc + ", then " + rc2 + "). Nothing was changed.");
                r.Method = "paths-only";
                if (!validateOnly) RestoreModes(saved, r.Matched);
            }
            r.Validated = validateOnly;
            r.Applied = !validateOnly;
            return r;
        }

        /// <summary>After a paths-only apply, put the saved resolution, refresh and positions back.</summary>
        static void RestoreModes(Profile saved, List<MatchedDisplay> matched)
        {
            PathInfo[] now; ModeInfo[] nowModes;
            DisplayNative.Query(DisplayNative.QDC_ONLY_ACTIVE_PATHS, out now, out nowModes);
            foreach (MatchedDisplay md in matched)
            {
                for (int i = 0; i < now.Length; i++)
                {
                    if (now[i].targetInfo.id != md.TargetId) continue;
                    ModeInfo savedSrc = FindMode(saved, DisplayNative.MODE_SOURCE, md.SavedPath.sourceInfo.id, md.SavedPath.sourceInfo.modeInfoIdx);
                    uint si = now[i].sourceInfo.modeInfoIdx;
                    if (si != DisplayNative.IDX_INVALID && si < nowModes.Length) nowModes[si].sourceMode = savedSrc.sourceMode;
                    now[i].targetInfo.refreshRate = md.SavedPath.targetInfo.refreshRate;
                }
            }
            DisplayNative.SetDisplayConfig((uint)now.Length, now, (uint)nowModes.Length, nowModes,
                DisplayNative.SDC_USE_SUPPLIED_DISPLAY_CONFIG | DisplayNative.SDC_ALLOW_CHANGES | DisplayNative.SDC_APPLY | DisplayNative.SDC_SAVE_TO_DATABASE);
        }
    }
}
