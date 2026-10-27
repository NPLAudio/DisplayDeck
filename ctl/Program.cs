// DisplayDeckCtl: command-line helper used by the DisplayDeck server. JSON on stdout, exit code 0 = success.
//   display save <file.xml>
//   display apply <file.xml> [--validate]
//   display undo
//   display connected                       connected displays with the modes each supports
//   display build <layout.json> <out.xml> [template.xml ...]
//   display remember <file.xml>             make this layout the one "undo" returns to
//   extras capture <hdr,dpi,wallpaper>      read HDR, scaling and wallpaper from the displays that are on
//   extras apply <extras.json>              put captured extras back
//   ddc list                                monitors that answer DDC/CI, with their current settings
//   ddc caps <targetId>                     the capabilities string and what it lists
//   ddc get <targetId> <code>               read one feature (code in hex, for example 0x60)
//   ddc set <targetId> <code> <value>       write one feature
//   audio list
//   audio default <id>
//   audio spatial <id> <guid|name|off>
//   selftest
// Options: --result-file <path> writes the JSON there instead of stdout (used for the elevated re-run).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace DisplayDeck.Ctl
{
    public static class Program
    {
        static string resultFile;

        static int Main(string[] argv)
        {
            Console.OutputEncoding = new UTF8Encoding(false);
            bool elevated = false, validate = false;
            List<string> args = new List<string>();
            for (int i = 0; i < argv.Length; i++)
            {
                if (argv[i] == "--elevated") elevated = true;
                else if (argv[i] == "--validate") validate = true;
                else if (argv[i] == "--result-file" && i + 1 < argv.Length) resultFile = argv[++i];
                else args.Add(argv[i]);
            }

            try
            {
                Dictionary<string, object> result = Run(args, validate, elevated);
                Emit(result);
                return 0;
            }
            catch (CtlException e)
            {
                Dictionary<string, object> err = new Dictionary<string, object>();
                err["error"] = e.Message;
                if (e.Code == 5) err["needsElevation"] = true;
                Emit(err);
                return e.Code;
            }
            catch (Exception e)
            {
                Dictionary<string, object> err = new Dictionary<string, object>();
                err["error"] = e.GetType().Name + ": " + e.Message;
                Emit(err);
                return 1;
            }
        }

        static void Emit(Dictionary<string, object> o)
        {
            JavaScriptSerializer js = new JavaScriptSerializer();
            js.MaxJsonLength = int.MaxValue;
            string json = js.Serialize(o);
            if (resultFile != null) File.WriteAllText(resultFile, json, new UTF8Encoding(false));
            else Console.Out.Write(json);
        }

        static int ParseCode(string s)
        {
            string t = (s ?? "").Trim();
            if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) t = t.Substring(2);
            int v;
            if (!int.TryParse(t, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out v) || v < 0 || v > 255)
                throw new CtlException("A feature code is a hex number from 00 to FF (for example 0x60).");
            return v;
        }

        static Dictionary<string, object> VcpJson(int code, VcpValue v)
        {
            Dictionary<string, object> o = new Dictionary<string, object>();
            o["code"] = code;
            o["current"] = v.Current;
            o["max"] = v.Max;
            return o;
        }

        static Dictionary<string, object> DdcCommand(string cmd, List<string> args)
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            List<DdcMonitor> monitors = Ddc.Open();
            try
            {
                if (cmd == "list")
                {
                    int[] probe = new int[] { Ddc.InputSource, Ddc.Brightness, Ddc.Contrast, Ddc.Volume, Ddc.Mute, Ddc.PowerMode, Ddc.ColorPreset, Ddc.Sharpness };
                    List<object> list = new List<object>();
                    foreach (DdcMonitor m in monitors)
                    {
                        Dictionary<string, object> o = new Dictionary<string, object>();
                        o["targetId"] = m.TargetId;
                        o["name"] = m.Name.Length > 0 ? m.Name : m.Description;
                        o["hwId"] = m.HwId;
                        o["description"] = m.Description;
                        o["backend"] = m.Backend;
                        o["connector"] = m.Connector;
                        Dictionary<string, object> features = new Dictionary<string, object>();
                        foreach (int code in probe)
                        {
                            VcpValue v = Ddc.Get(m, code);
                            if (v != null) features[code.ToString(System.Globalization.CultureInfo.InvariantCulture)] = VcpJson(code, v);
                        }
                        // Some monitors (a Samsung among them) answer a power-state read with a copy of whatever they
                        // said last. Ask again after reading something else: a real answer does not change.
                        string powerKey = Ddc.PowerMode.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        if (features.ContainsKey(powerKey))
                        {
                            Ddc.Get(m, Ddc.Brightness);
                            VcpValue again = Ddc.Get(m, Ddc.PowerMode);
                            Dictionary<string, object> first = (Dictionary<string, object>)features[powerKey];
                            if (again == null || again.Current != Convert.ToUInt32(first["current"]) || again.Max != Convert.ToUInt32(first["max"]))
                                features.Remove(powerKey);
                        }
                        o["features"] = features;
                        o["ddc"] = features.Count > 0;
                        list.Add(o);
                    }
                    // Displays that are on but cannot be reached still get an entry, so the page can say why.
                    HashSet<uint> reached = new HashSet<uint>();
                    foreach (DdcMonitor m in monitors) reached.Add(m.TargetId);
                    foreach (LiveInfo li in LiveDisplays.Query(false))
                    {
                        if (!li.Active || reached.Contains(li.Target.Path.targetInfo.id)) continue;
                        Dictionary<string, object> o = new Dictionary<string, object>();
                        o["targetId"] = li.Target.Path.targetInfo.id;
                        o["name"] = li.Target.Monitor.FriendlyName;
                        o["hwId"] = ProfileFile.EdidHwId(li.Target.Monitor.ManufactureId, li.Target.Monitor.ProductCodeId);
                        o["backend"] = "none";
                        o["connector"] = Ddc.ConnectorName(li.Target.Path.targetInfo.outputTechnology);
                        o["features"] = new Dictionary<string, object>();
                        o["ddc"] = false;
                        list.Add(o);
                    }
                    r["monitors"] = list;
                    if (list.Count == 0) r["notes"] = new List<object>(Ddc.Notes.ToArray());
                    return r;
                }
                uint target;
                if (!uint.TryParse(Arg(args, 2, "display id"), out target)) throw new CtlException("The display id must be a number.");
                DdcMonitor mon = Ddc.Find(monitors, target);
                if (cmd == "caps")
                {
                    string raw = Ddc.ReadCapabilities(mon);
                    if (raw == null) throw new CtlException("The display did not report its capabilities. It may not support DDC/CI, or DDC/CI is switched off in its on-screen menu.");
                    Capabilities c = Ddc.ParseCapabilities(raw);
                    r["raw"] = c.Raw;
                    r["model"] = c.Model;
                    r["type"] = c.Type;
                    r["mccsVersion"] = c.MccsVersion;
                    Dictionary<string, object> codes = new Dictionary<string, object>();
                    foreach (KeyValuePair<int, List<int>> kv in c.Codes)
                        codes[kv.Key.ToString(System.Globalization.CultureInfo.InvariantCulture)] = new List<object>(kv.Value.ConvertAll<object>(delegate (int x) { return x; }).ToArray());
                    r["codes"] = codes;
                    return r;
                }
                if (cmd == "get")
                {
                    int code = ParseCode(Arg(args, 3, "feature code"));
                    VcpValue v = Ddc.Get(mon, code);
                    if (v == null) throw new CtlException("The display does not report feature 0x" + code.ToString("X2") + ".");
                    return VcpJson(code, v);
                }
                if (cmd == "set")
                {
                    int code = ParseCode(Arg(args, 3, "feature code"));
                    uint value;
                    if (!uint.TryParse(Arg(args, 4, "value"), out value) || value > 65535) throw new CtlException("The value must be a number from 0 to 65535.");
                    Ddc.Set(mon, code, value);
                    r["ok"] = true;
                    r["code"] = code;
                    r["value"] = value;
                    return r;
                }
                throw new CtlException("Unknown command: ddc " + cmd);
            }
            finally { Ddc.Release(monitors); }
        }

        static Dictionary<string, object> ModeJson(ModeOption mo)
        {
            Dictionary<string, object> o = new Dictionary<string, object>();
            o["width"] = mo.Width;
            o["height"] = mo.Height;
            List<object> rates = new List<object>();
            foreach (int hz in mo.Rates.Keys) rates.Add(hz);
            o["refresh"] = rates;
            return o;
        }

        static string Arg(List<string> a, int i, string what)
        {
            if (a.Count <= i) throw new CtlException("Missing argument: " + what);
            return a[i];
        }

        static string DataDir()
        {
            string d = Environment.GetEnvironmentVariable("DISPLAYDECK_DATA");
            if (string.IsNullOrEmpty(d))
                d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DisplayDeck");
            Directory.CreateDirectory(d);
            return d;
        }

        static string PreviousFile() { return Path.Combine(DataDir(), "previous.xml"); }

        static Dictionary<string, object> ApplyFile(string xml, bool validate)
        {
            Profile p = ProfileFile.Parse(xml);
            if (!validate)
            {
                // Remember what we are replacing, so the change can be undone.
                try { File.WriteAllText(PreviousFile(), ProfileFile.Write(DisplayOps.CaptureLive()), new UTF8Encoding(false)); }
                catch (Exception) { /* best effort */ }
            }
            ApplyResult ar = DisplayOps.Apply(p, validate);
            Dictionary<string, object> r = new Dictionary<string, object>();
            r["ok"] = true;
            r["validated"] = ar.Validated;
            r["applied"] = ar.Applied;
            r["method"] = ar.Method;
            List<object> matched = new List<object>();
            foreach (MatchedDisplay m in ar.Matched) matched.Add(m.Name);
            r["matched"] = matched;
            r["skipped"] = new List<object>(ar.Skipped.ToArray());
            return r;
        }

        static Dictionary<string, object> Run(List<string> args, bool validate, bool elevated)
        {
            string group = Arg(args, 0, "command");
            Dictionary<string, object> r = new Dictionary<string, object>();
            if (group == "selftest") return SelfTest.Run();

            string cmd = Arg(args, 1, "subcommand");

            if (group == "display" && cmd == "save")
            {
                string file = Arg(args, 2, "output file");
                Profile p = DisplayOps.CaptureLive();
                File.WriteAllText(file, ProfileFile.Write(p), new UTF8Encoding(false));
                r["ok"] = true;
                r["displays"] = p.Paths.Count;
                return r;
            }
            if (group == "display" && cmd == "connected")
            {
                List<object> list = new List<object>();
                foreach (LiveInfo li in LiveDisplays.Query(true))
                {
                    Dictionary<string, object> o = new Dictionary<string, object>();
                    MonitorInfo m = li.Target.Monitor;
                    o["targetId"] = li.Target.Path.targetInfo.id;
                    o["name"] = m.FriendlyName;
                    o["hwId"] = ProfileFile.EdidHwId(m.ManufactureId, m.ProductCodeId);
                    o["manufacturer"] = m.ManufactureId;
                    o["product"] = m.ProductCodeId;
                    o["active"] = li.Active;
                    List<object> modes = new List<object>();
                    foreach (ModeOption mo in li.Modes) modes.Add(ModeJson(mo));
                    o["modes"] = modes;
                    if (li.Preferred != null) o["preferred"] = ModeJson(li.Preferred);
                    list.Add(o);
                }
                r["displays"] = list;
                return r;
            }
            if (group == "display" && cmd == "build")
            {
                // display build <layout.json> <out.xml> [template.xml ...]
                JavaScriptSerializer js = new JavaScriptSerializer();
                js.MaxJsonLength = int.MaxValue;
                List<SpecDisplay> spec = LayoutBuilder.ParseSpec(js.DeserializeObject(File.ReadAllText(Arg(args, 2, "layout file"))));
                string outFile = Arg(args, 3, "output file");
                List<Profile> templates = new List<Profile>();
                for (int i = 4; i < args.Count; i++)
                {
                    try { templates.Add(ProfileFile.Parse(File.ReadAllText(args[i]))); } catch (CtlException) { /* an unreadable profile is just not a template */ }
                }
                List<LiveTarget> live = new List<LiveTarget>();
                foreach (LiveInfo li in LiveDisplays.Query(false)) live.Add(li.Target);
                Profile built = LayoutBuilder.Build(spec, templates, live);
                File.WriteAllText(outFile, ProfileFile.Write(built), new UTF8Encoding(false));
                r["ok"] = true;
                r["displays"] = built.Paths.Count;
                return r;
            }
            if (group == "display" && cmd == "apply")
                return ApplyFile(File.ReadAllText(Arg(args, 2, "profile file")), validate);
            if (group == "display" && cmd == "remember")
            {
                // Make a saved layout the one "undo" goes back to.
                File.Copy(Arg(args, 2, "layout file"), PreviousFile(), true);
                r["ok"] = true;
                return r;
            }
            if (group == "display" && cmd == "undo")
            {
                if (!File.Exists(PreviousFile())) throw new CtlException("There is no earlier layout to go back to.");
                return ApplyFile(File.ReadAllText(PreviousFile()), false);
            }

            if (group == "extras" && cmd == "capture")
            {
                // extras capture <hdr,dpi,wallpaper>
                List<string> sections = new List<string>();
                foreach (string s in Arg(args, 2, "sections").Split(','))
                {
                    string t = s.Trim().ToLowerInvariant();
                    if (t != Extras.HdrSection && t != Extras.DpiSection && t != Extras.WallpaperSection)
                        throw new CtlException("Unknown extra: " + t);
                    if (!sections.Contains(t)) sections.Add(t);
                }
                List<object> notes = new List<object>();
                r = Extras.Capture(sections, notes);
                r["notes"] = notes;
                return r;
            }
            if (group == "extras" && cmd == "apply")
            {
                JavaScriptSerializer js = new JavaScriptSerializer();
                js.MaxJsonLength = int.MaxValue;
                Dictionary<string, object> extras = js.DeserializeObject(File.ReadAllText(Arg(args, 2, "extras file"))) as Dictionary<string, object>;
                if (extras == null) throw new CtlException("The saved extras could not be read.");
                return Extras.Apply(extras);
            }

            if (group == "ddc") return DdcCommand(cmd, args);

            if (group == "audio" && cmd == "list")
            {
                List<object> list = new List<object>();
                foreach (AudioDevice d in AudioOps.List())
                {
                    Dictionary<string, object> o = new Dictionary<string, object>();
                    o["endpointId"] = d.EndpointId;
                    o["name"] = d.Name;
                    o["adapter"] = d.Adapter;
                    o["id"] = d.CliId;
                    o["isDefault"] = d.IsDefault;
                    o["spatialGuid"] = d.SpatialGuid;
                    List<object> fm = new List<object>();
                    foreach (SpatialFormat f in d.Formats)
                    {
                        Dictionary<string, object> fo = new Dictionary<string, object>();
                        fo["name"] = f.Name;
                        fo["guid"] = f.Guid;
                        fm.Add(fo);
                    }
                    o["formats"] = fm;
                    list.Add(o);
                }
                r["devices"] = list;
                return r;
            }
            if (group == "audio" && cmd == "default")
            {
                AudioDevice d = AudioOps.Find(Arg(args, 2, "device id"));
                AudioOps.SetDefault(d.EndpointId);
                r["ok"] = true;
                return r;
            }
            if (group == "audio" && cmd == "spatial")
            {
                AudioDevice d = AudioOps.Find(Arg(args, 2, "device id"));
                string guid = AudioOps.ResolveFormat(d, args.Count > 3 ? args[3] : "off");
                try
                {
                    AudioOps.SetSpatial(d, guid);
                }
                catch (CtlException e)
                {
                    if (e.Code != 5 || elevated) throw;
                    return RunElevated(args);        // access denied: one UAC prompt, then retry as admin
                }
                r["ok"] = true;
                r["spatialGuid"] = guid;
                return r;
            }
            throw new CtlException("Unknown command: " + group + " " + cmd);
        }

        /// <summary>Re-runs the same command with administrator rights (single UAC prompt) and returns its result.</summary>
        static Dictionary<string, object> RunElevated(List<string> args)
        {
            string tmp = Path.Combine(Path.GetTempPath(), "dd-ctl-" + Guid.NewGuid().ToString("N") + ".json");
            StringBuilder sb = new StringBuilder();
            foreach (string a in args) sb.Append('"').Append(a.Replace("\"", "\\\"")).Append("\" ");
            sb.Append("--elevated --result-file \"").Append(tmp).Append('"');
            ProcessStartInfo psi = new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName, sb.ToString());
            psi.Verb = "runas";
            psi.UseShellExecute = true;
            psi.WindowStyle = ProcessWindowStyle.Hidden;
            try
            {
                using (Process p = Process.Start(psi)) { p.WaitForExit(); }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                throw new CtlException("Changing the spatial format needs administrator permission, which was not granted.");
            }
            if (!File.Exists(tmp)) throw new CtlException("The elevated step did not report a result.");
            try
            {
                JavaScriptSerializer js = new JavaScriptSerializer();
                Dictionary<string, object> res = (Dictionary<string, object>)js.DeserializeObject(File.ReadAllText(tmp));
                if (res.ContainsKey("error")) throw new CtlException(Convert.ToString(res["error"]));
                return res;
            }
            finally { try { File.Delete(tmp); } catch (Exception) { } }
        }
    }
}
