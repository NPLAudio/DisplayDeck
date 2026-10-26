// DisplayDeckCtl: command-line helper used by the DisplayDeck server. JSON on stdout, exit code 0 = success.
//   display save <file.xml>
//   display apply <file.xml> [--validate]
//   display undo
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
            if (group == "display" && cmd == "apply")
                return ApplyFile(File.ReadAllText(Arg(args, 2, "profile file")), validate);
            if (group == "display" && cmd == "undo")
            {
                if (!File.Exists(PreviousFile())) throw new CtlException("There is no earlier layout to go back to.");
                return ApplyFile(File.ReadAllText(PreviousFile()), false);
            }

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
