// What is connected right now: one entry per display, with its name and (for the ones that are on) its modes.
using System;
using System.Collections.Generic;

namespace DisplayDeck.Ctl
{
    public class LiveInfo
    {
        public LiveTarget Target;
        public bool Active;
        public PathInfo? ActivePath;
        public List<ModeOption> Modes = new List<ModeOption>();
        public ModeOption Preferred;
    }

    public static class LiveDisplays
    {
        static MonitorInfo MonitorOf(PathInfo pi)
        {
            TargetDeviceName n = DisplayNative.GetTargetName(pi.targetInfo.adapterId, pi.targetInfo.id);
            MonitorInfo m = new MonitorInfo();
            m.ManufactureId = n.edidManufactureId;
            m.ProductCodeId = n.edidProductCodeId;
            m.FriendlyName = (n.monitorFriendlyDeviceName ?? "").Trim();
            m.DevicePath = n.monitorDevicePath ?? "";
            if (m.DevicePath.Length == 0)
                m.DevicePath = ProfileFile.SynthesizeDevicePath(pi.targetInfo.id, n.edidManufactureId, n.edidProductCodeId);
            m.Valid = true;
            return m;
        }

        /// <summary>Every connected display once, active paths preferred over inactive ones.</summary>
        public static List<LiveInfo> Query(bool withModes)
        {
            PathInfo[] paths; ModeInfo[] modes;
            DisplayNative.Query(DisplayNative.QDC_ALL_PATHS, out paths, out modes);
            Dictionary<uint, LiveInfo> byTarget = new Dictionary<uint, LiveInfo>();
            List<LiveInfo> order = new List<LiveInfo>();
            foreach (PathInfo pi in paths)
            {
                if (pi.targetInfo.targetAvailable == 0) continue;
                bool active = (pi.flags & DisplayNative.PATH_ACTIVE) != 0;
                LiveInfo li;
                if (byTarget.TryGetValue(pi.targetInfo.id, out li))
                {
                    if (active && !li.Active) { li.Active = true; li.ActivePath = pi; li.Target.Path = pi; }
                    continue;
                }
                li = new LiveInfo();
                li.Target = new LiveTarget();
                li.Target.Path = pi;
                li.Target.Monitor = MonitorOf(pi);
                li.Active = active;
                if (active) li.ActivePath = pi;
                byTarget[pi.targetInfo.id] = li;
                order.Add(li);
            }
            foreach (LiveInfo li in order)
            {
                // Template paths for building need to look like a plain, switched-on path.
                if (withModes)
                {
                    if (li.Active && li.ActivePath.HasValue)
                        li.Modes = DisplayModes.ForSource(li.ActivePath.Value.sourceInfo.adapterId, li.ActivePath.Value.sourceInfo.id);
                    li.Preferred = DisplayModes.Preferred(li.Target.Path.targetInfo.adapterId, li.Target.Path.targetInfo.id);
                }
            }
            return order;
        }
    }
}
