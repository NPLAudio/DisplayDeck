// Desktop wallpaper through the Windows IDesktopWallpaper interface: a picture per display, a solid color, or a
// slideshow. Windows Spotlight is not saved (it is a Windows-managed setting with no public interface).
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;

namespace DisplayDeck.Ctl
{
    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItem
    {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        void GetParent(out IShellItem ppsi);
        void GetDisplayName(uint sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        void Compare(IShellItem psi, uint hint, out int piOrder);
    }

    [ComImport, Guid("B63EA76D-1F85-456F-A19C-48159EFA858B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItemArray
    {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppvOut);
        void GetPropertyStore(int flags, ref Guid riid, out IntPtr ppv);
        void GetPropertyDescriptionList(IntPtr keyType, ref Guid riid, out IntPtr ppv);
        void GetAttributes(int attribFlags, uint sfgaoMask, out uint psfgaoAttribs);
        void GetCount(out uint pdwNumItems);
        void GetItemAt(uint dwIndex, out IShellItem ppsi);
        void EnumItems(out IntPtr ppenumShellItems);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct WallpaperRect { public int Left, Top, Right, Bottom; }

    [ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDesktopWallpaper
    {
        void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorID, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
        [return: MarshalAs(UnmanagedType.LPWStr)]
        string GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorID);
        [return: MarshalAs(UnmanagedType.LPWStr)]
        string GetMonitorDevicePathAt(uint monitorIndex);
        uint GetMonitorDevicePathCount();
        WallpaperRect GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorID);
        void SetBackgroundColor(uint color);
        uint GetBackgroundColor();
        void SetPosition(int position);
        int GetPosition();
        void SetSlideshow(IShellItemArray items);
        IShellItemArray GetSlideshow();
        void SetSlideshowOptions(int options, uint slideshowTick);
        void GetSlideshowOptions(out int options, out uint slideshowTick);
        void AdvanceSlideshow([MarshalAs(UnmanagedType.LPWStr)] string monitorID, int direction);
        int GetStatus();
        void Enable([MarshalAs(UnmanagedType.Bool)] bool enable);
    }

    [ComImport, Guid("C2CF3110-460E-4fc1-B9D0-8A1C0C9CC4BD")]
    class DesktopWallpaperClass { }

    public static class Wallpaper
    {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHParseDisplayName(string name, IntPtr bindingContext, out IntPtr pidl, uint sfgaoIn, out uint sfgaoOut);

        [DllImport("shell32.dll")]
        static extern int SHCreateShellItemArrayFromIDLists(uint cidl, IntPtr[] rgpidl, out IShellItemArray ppsiItemArray);

        const uint SigdnFileSysPath = 0x80058000;

        static readonly string[] PositionNames = new string[] { "center", "tile", "stretch", "fit", "fill", "span" };

        static void OnStaThread(ThreadStart work)
        {
            Exception error = null;
            Thread t = new Thread(delegate ()
            {
                try { work(); }
                catch (Exception e) { error = e; }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
            if (error != null)
            {
                CtlException ce = error as CtlException;
                if (ce != null) throw ce;
                throw new CtlException("Windows would not read or change the wallpaper: " + error.Message);
            }
        }

        static IDesktopWallpaper Open()
        {
            return (IDesktopWallpaper)new DesktopWallpaperClass();
        }

        /// <summary>Windows records which kind of background is set; 3 is Windows Spotlight on the builds that have it.</summary>
        static int BackgroundType()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Wallpapers"))
                {
                    object v = k == null ? null : k.GetValue("BackgroundType");
                    return v is int ? (int)v : -1;
                }
            }
            catch (Exception) { return -1; }
        }

        static string Normalize(string path)
        {
            return (path ?? "").Trim().ToLowerInvariant();
        }

        static List<string> SlideshowPaths(IDesktopWallpaper dw)
        {
            List<string> paths = new List<string>();
            IShellItemArray arr;
            try { arr = dw.GetSlideshow(); }
            catch (Exception) { return paths; }
            if (arr == null) return paths;
            try
            {
                uint count;
                arr.GetCount(out count);
                for (uint i = 0; i < count; i++)
                {
                    IShellItem item;
                    arr.GetItemAt(i, out item);
                    string p;
                    item.GetDisplayName(SigdnFileSysPath, out p);
                    if (!string.IsNullOrEmpty(p)) paths.Add(p);
                    Marshal.ReleaseComObject(item);
                }
            }
            finally { Marshal.ReleaseComObject(arr); }
            return paths;
        }

        public static Dictionary<string, object> Capture(List<LiveInfo> live, List<object> notes)
        {
            Dictionary<string, object> result = null;
            OnStaThread(delegate ()
            {
                if (BackgroundType() == 3)
                {
                    notes.Add("Windows Spotlight is on. Spotlight cannot be saved, so no wallpaper was saved. Choose a picture, a solid color or a slideshow first.");
                    return;
                }
                IDesktopWallpaper dw = Open();
                Dictionary<string, object> w = new Dictionary<string, object>();
                int pos = dw.GetPosition();
                w["position"] = pos >= 0 && pos < PositionNames.Length ? PositionNames[pos] : "fill";
                w["color"] = Extras.FormatColor(dw.GetBackgroundColor());

                List<string> show = SlideshowPaths(dw);
                List<object> monitors = new List<object>();
                bool anyPicture = false;
                uint count = dw.GetMonitorDevicePathCount();
                for (uint i = 0; i < count; i++)
                {
                    string id = dw.GetMonitorDevicePathAt(i);
                    LiveInfo li = FindByDevicePath(live, id);
                    if (li == null) continue;
                    string file = "";
                    try { file = dw.GetWallpaper(id) ?? ""; } catch (Exception) { }
                    if (file.Length > 0) anyPicture = true;
                    Dictionary<string, object> m = new Dictionary<string, object>();
                    m["targetId"] = li.Target.Path.targetInfo.id;
                    m["hwId"] = Extras.HwIdOf(li);
                    m["name"] = string.IsNullOrEmpty(li.Target.Monitor.FriendlyName) ? "Display " + li.Target.Path.targetInfo.id : li.Target.Monitor.FriendlyName;
                    m["path"] = file;
                    monitors.Add(m);
                }
                if (show.Count > 0)
                {
                    w["mode"] = "slideshow";
                    Dictionary<string, object> s = new Dictionary<string, object>();
                    s["paths"] = new List<object>(show.ConvertAll<object>(delegate (string x) { return x; }).ToArray());
                    int opts; uint tick;
                    dw.GetSlideshowOptions(out opts, out tick);
                    s["options"] = opts;
                    s["tick"] = tick;
                    w["slideshow"] = s;
                }
                else
                {
                    w["mode"] = anyPicture ? "picture" : "color";
                    w["monitors"] = monitors;
                }
                result = w;
            });
            return result;
        }

        static LiveInfo FindByDevicePath(List<LiveInfo> live, string devicePath)
        {
            string want = Normalize(devicePath);
            foreach (LiveInfo li in live)
                if (li.Active && Normalize(li.Target.Monitor.DevicePath) == want) return li;
            return null;
        }

        static string Str(Dictionary<string, object> d, string key)
        {
            return d.ContainsKey(key) && d[key] != null ? Convert.ToString(d[key]) : "";
        }

        public static void Apply(Dictionary<string, object> w, List<LiveInfo> live, List<object> applied, List<object> skipped)
        {
            OnStaThread(delegate ()
            {
                IDesktopWallpaper dw = Open();
                string mode = Str(w, "mode");
                bool changed = false;

                // The id Windows uses for each connected display, by the display it belongs to.
                Dictionary<uint, string> ids = new Dictionary<uint, string>();
                uint count = dw.GetMonitorDevicePathCount();
                for (uint i = 0; i < count; i++)
                {
                    string id = dw.GetMonitorDevicePathAt(i);
                    LiveInfo li = FindByDevicePath(live, id);
                    if (li != null) ids[li.Target.Path.targetInfo.id] = id;
                }

                string position = Str(w, "position");
                int posIndex = Array.IndexOf(PositionNames, position);
                if (posIndex >= 0 && dw.GetPosition() != posIndex) { dw.SetPosition(posIndex); changed = true; }
                if (w.ContainsKey("color"))
                {
                    uint want = Extras.ParseColor(Str(w, "color"));
                    if (dw.GetBackgroundColor() != want) { dw.SetBackgroundColor(want); changed = true; }
                }

                if (mode == "slideshow")
                {
                    Dictionary<string, object> s = w.ContainsKey("slideshow") ? w["slideshow"] as Dictionary<string, object> : null;
                    List<string> paths = new List<string>();
                    if (s != null && s.ContainsKey("paths"))
                    {
                        System.Collections.IEnumerable e = s["paths"] as System.Collections.IEnumerable;
                        if (e != null) foreach (object o in e) { string p = Convert.ToString(o); if (File.Exists(p) || Directory.Exists(p)) paths.Add(p); }
                    }
                    if (paths.Count == 0) { skipped.Add("The wallpaper slideshow folder was not found."); }
                    else if (!SameList(SlideshowPaths(dw), paths))
                    {
                        dw.SetSlideshowOptions(s.ContainsKey("options") ? Convert.ToInt32(s["options"]) : 0, s.ContainsKey("tick") ? Convert.ToUInt32(s["tick"]) : 600000u);
                        SetSlideshow(dw, paths);
                        changed = true;
                    }
                }
                else if (mode == "color")
                {
                    // A blank picture on every display leaves just the background color.
                    bool anyPicture = false;
                    foreach (string id in ids.Values)
                    {
                        string now = "";
                        try { now = dw.GetWallpaper(id) ?? ""; } catch (Exception) { }
                        if (now.Length > 0) anyPicture = true;
                    }
                    if (anyPicture) { dw.SetWallpaper(null, ""); changed = true; }
                }
                else
                {
                    System.Collections.IEnumerable list = w.ContainsKey("monitors") ? w["monitors"] as System.Collections.IEnumerable : null;
                    if (list != null)
                    {
                        foreach (object o in list)
                        {
                            Dictionary<string, object> m = o as Dictionary<string, object>;
                            if (m == null) continue;
                            LiveInfo li = Extras.Match(live, m.ContainsKey("targetId") ? Convert.ToInt64(m["targetId"]) : -1, Str(m, "hwId"));
                            string id;
                            if (li == null || !ids.TryGetValue(li.Target.Path.targetInfo.id, out id))
                            {
                                skipped.Add(Str(m, "name") + " is not on, so its wallpaper was left alone.");
                                continue;
                            }
                            string file = mode == "color" ? "" : Str(m, "path");
                            if (file.Length > 0 && !File.Exists(file)) { skipped.Add("The wallpaper picture for " + Str(m, "name") + " was not found (" + file + ")."); continue; }
                            string now = "";
                            try { now = dw.GetWallpaper(id) ?? ""; } catch (Exception) { }
                            if (Normalize(now) == Normalize(file)) continue;
                            dw.SetWallpaper(id, file);
                            changed = true;
                        }
                    }
                }
                if (changed) applied.Add(mode == "color" ? "Wallpaper color" : mode == "slideshow" ? "Wallpaper slideshow" : "Wallpaper");
            });
        }

        static bool SameList(List<string> a, List<string> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) if (Normalize(a[i]) != Normalize(b[i])) return false;
            return true;
        }

        static void SetSlideshow(IDesktopWallpaper dw, List<string> paths)
        {
            List<IntPtr> pidls = new List<IntPtr>();
            try
            {
                foreach (string p in paths)
                {
                    IntPtr pidl; uint attrs;
                    if (SHParseDisplayName(p, IntPtr.Zero, out pidl, 0, out attrs) == 0 && pidl != IntPtr.Zero) pidls.Add(pidl);
                }
                if (pidls.Count == 0) throw new CtlException("The wallpaper slideshow folder could not be opened.");
                IShellItemArray arr;
                int rc = SHCreateShellItemArrayFromIDLists((uint)pidls.Count, pidls.ToArray(), out arr);
                if (rc != 0) throw new CtlException("Windows would not build the slideshow list (error " + rc + ").");
                try { dw.SetSlideshow(arr); }
                finally { Marshal.ReleaseComObject(arr); }
            }
            finally { foreach (IntPtr pidl in pidls) Marshal.FreeCoTaskMem(pidl); }
        }
    }
}
