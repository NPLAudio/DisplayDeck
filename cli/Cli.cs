// DisplayDeckCli: apply a profile from the command line, a shortcut, Task Scheduler or a game launcher.
//   DisplayDeckCli apply "Desk"      switch to a profile (starts DisplayDeck first if it is not running)
//   DisplayDeckCli list              profile names, the current one marked
//   DisplayDeckCli current           the name of the current profile
//   DisplayDeckCli undo              go back to the previous layout
//   DisplayDeckCli open              open the page
// Exit code 0 means it worked, 1 something went wrong, 2 the command or profile was not understood.
// Written for the in-box C# 5 compiler (.NET Framework 4), so no newer syntax.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace DisplayDeck.Cli
{
    class UsageException : Exception
    {
        public UsageException(string message) : base(message) { }
    }

    static class Program
    {
        static readonly int Port = EnvPort();
        static readonly string Url = "http://127.0.0.1:" + Port + "/";

        static int EnvPort()
        {
            int p;
            return int.TryParse(Environment.GetEnvironmentVariable("DISPLAYDECK_PORT"), out p) && p > 0 && p < 65536 ? p : 8765;
        }

        static int Main(string[] args)
        {
            Console.OutputEncoding = new UTF8Encoding(false);
            if (args.Length == 0 || args[0] == "help" || args[0] == "--help" || args[0] == "-h" || args[0] == "/?")
            {
                Console.WriteLine(Help());
                return args.Length == 0 ? 2 : 0;
            }
            try
            {
                string cmd = args[0].ToLowerInvariant();
                if (cmd == "apply")
                {
                    if (args.Length < 2) throw new UsageException("Say which profile to apply, for example: DisplayDeckCli apply \"Desk\"");
                    EnsureRunning();
                    string name = Resolve(string.Join(" ", args, 1, args.Length - 1));
                    Dictionary<string, object> r = Post("api/apply/" + Uri.EscapeDataString(name), 90000);
                    Console.WriteLine("Applied " + name + ".");
                    PrintAll(r, "warnings");
                    PrintAll(r, "runErrors");
                    if (r.ContainsKey("audio") && Convert.ToString(r["audio"]) == "failed")
                        Console.Error.WriteLine("Audio failed: " + Convert.ToString(r.ContainsKey("audioError") ? r["audioError"] : ""));
                    return 0;
                }
                if (cmd == "list" || cmd == "current")
                {
                    EnsureRunning();
                    Dictionary<string, object> s = Get("api/current", 20000);
                    string current = s.ContainsKey("current") && s["current"] != null ? (string)s["current"] : null;
                    if (cmd == "current")
                    {
                        if (current == null) { Console.Error.WriteLine("The screens match no saved profile."); return 1; }
                        Console.WriteLine(current);
                        return 0;
                    }
                    foreach (object o in (IEnumerable)s["profiles"])
                        Console.WriteLine(((string)o == current ? "* " : "  ") + (string)o);
                    return 0;
                }
                if (cmd == "undo")
                {
                    EnsureRunning();
                    Post("api/undo-layout", 90000);
                    Console.WriteLine("Went back to the previous layout.");
                    return 0;
                }
                if (cmd == "open")
                {
                    EnsureRunning();
                    Process.Start(new ProcessStartInfo(Url) { UseShellExecute = true });
                    return 0;
                }
                throw new UsageException("Unknown command: " + args[0] + Environment.NewLine + Help());
            }
            catch (UsageException e)
            {
                Console.Error.WriteLine(e.Message);
                return 2;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e.Message);
                return 1;
            }
        }

        static string Help()
        {
            return "DisplayDeckCli apply \"Profile name\"   switch to a profile" + Environment.NewLine
                 + "DisplayDeckCli list                   list profiles (* marks the current one)" + Environment.NewLine
                 + "DisplayDeckCli current                print the current profile" + Environment.NewLine
                 + "DisplayDeckCli undo                   go back to the previous layout" + Environment.NewLine
                 + "DisplayDeckCli open                   open the DisplayDeck page";
        }

        static void PrintAll(Dictionary<string, object> r, string key)
        {
            if (!r.ContainsKey(key)) return;
            IEnumerable list = r[key] as IEnumerable;
            if (list == null || r[key] is string) return;
            foreach (object o in list) Console.Error.WriteLine(Convert.ToString(o));
        }

        // The profile name as typed, matched ignoring case. Two profiles that differ only by case are ambiguous.
        static string Resolve(string typed)
        {
            Dictionary<string, object> s = Get("api/current", 20000);
            List<string> names = new List<string>();
            foreach (object o in (IEnumerable)s["profiles"]) names.Add((string)o);
            foreach (string n in names) if (n == typed) return n;
            List<string> hits = new List<string>();
            foreach (string n in names) if (string.Equals(n, typed, StringComparison.OrdinalIgnoreCase)) hits.Add(n);
            if (hits.Count == 1) return hits[0];
            if (hits.Count > 1) throw new UsageException("More than one profile matches \"" + typed + "\": " + string.Join(", ", hits.ToArray()));
            throw new UsageException("No profile named \"" + typed + "\"." + (names.Count > 0 ? " Profiles: " + string.Join(", ", names.ToArray()) : " There are no saved profiles yet."));
        }

        static bool ServerUp()
        {
            try
            {
                // A static file answers at once; the API calls can take a moment while Windows is asked about the screens.
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(Url + "favicon.svg");
                req.Timeout = 1500;
                using (req.GetResponse()) { }
                return true;
            }
            catch (WebException e) { return e.Response != null; }
            catch (Exception) { return false; }
        }

        // DisplayDeck keeps the server running. If it is not open, start it and wait for it.
        static void EnsureRunning()
        {
            if (ServerUp()) return;
            string tray = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), @"..\DisplayDeck.exe"));
            if (!File.Exists(tray)) throw new Exception("DisplayDeck is not running and DisplayDeck.exe was not found next to this tool. Start DisplayDeck first.");
            // Through the shell, so the tray does not inherit this tool's output pipes (a script reading our output
            // would otherwise wait for DisplayDeck to exit).
            Process.Start(new ProcessStartInfo(tray) { UseShellExecute = true });
            for (int i = 0; i < 60; i++)
            {
                Thread.Sleep(500);
                if (ServerUp()) return;
            }
            throw new Exception("DisplayDeck did not start in time.");
        }

        static Dictionary<string, object> Get(string path, int timeoutMs) { return Call("GET", path, timeoutMs); }
        static Dictionary<string, object> Post(string path, int timeoutMs) { return Call("POST", path, timeoutMs); }

        static Dictionary<string, object> Call(string method, string path, int timeoutMs)
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(Url + path);
            req.Method = method;
            req.Timeout = timeoutMs;
            if (method == "POST") req.ContentLength = 0;
            string body;
            try
            {
                using (WebResponse resp = req.GetResponse())
                using (StreamReader r = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    body = r.ReadToEnd();
            }
            catch (WebException e)
            {
                if (e.Response != null)
                {
                    try
                    {
                        using (StreamReader r = new StreamReader(e.Response.GetResponseStream(), Encoding.UTF8))
                        {
                            Dictionary<string, object> d = new JavaScriptSerializer().DeserializeObject(r.ReadToEnd()) as Dictionary<string, object>;
                            if (d != null && d.ContainsKey("error")) throw new Exception(Convert.ToString(d["error"]));
                        }
                    }
                    catch (ArgumentException) { }
                    catch (InvalidOperationException) { }
                }
                throw new Exception("Could not reach DisplayDeck: " + e.Message);
            }
            return (new JavaScriptSerializer().DeserializeObject(body) as Dictionary<string, object>) ?? new Dictionary<string, object>();
        }
    }
}
