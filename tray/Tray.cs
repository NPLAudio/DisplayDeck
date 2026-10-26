// DisplayDeck tray host.
// Starts the bundled Node server hidden, shows a notification-area icon, and
// lets you open the web page or apply a profile from the icon's menu.
// Written for the in-box C# 5 compiler (.NET Framework 4), so no newer syntax.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DisplayDeck
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            bool created;
            using (Mutex mutex = new Mutex(true, @"Local\DisplayDeckTray", out created))
            {
                if (!created) return;
                Application.EnableVisualStyles();
                Application.Run(new TrayContext());
            }
        }
    }

    class ProfileItem
    {
        public string Name;
        public bool Current;
        public bool LastApplied;
    }

    class TrayContext : ApplicationContext
    {
        const int Port = 8765;
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunValue = "DisplayDeck";

        readonly string root = AppDomain.CurrentDomain.BaseDirectory;
        readonly string url = "http://127.0.0.1:" + Port + "/";
        readonly NotifyIcon icon = new NotifyIcon();
        readonly ContextMenuStrip menu = new ContextMenuStrip();
        readonly object gate = new object();
        List<ProfileItem> profiles = new List<ProfileItem>();
        Process server;
        System.Windows.Forms.Timer refreshTimer = new System.Windows.Forms.Timer();

        public TrayContext()
        {
            StartServer();
            EnableAutoStartOnFirstRun();

            string ico = Path.Combine(root, "app.ico");
            icon.Icon = File.Exists(ico) ? new Icon(ico) : SystemIcons.Application;
            icon.Text = "DisplayDeck";
            icon.ContextMenuStrip = menu;
            icon.MouseClick += delegate(object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) OpenPage();
            };
            menu.Opening += delegate { BuildMenu(); RefreshProfilesAsync(); };
            icon.Visible = true;

            refreshTimer.Interval = 20000;
            refreshTimer.Tick += delegate { RefreshProfilesAsync(); };
            refreshTimer.Start();
            RefreshProfilesAsync();
            BuildMenu();
        }

        void StartServer()
        {
            if (IsServerUp()) return; // something (e.g. a dev server) already answers on the port
            string node = Path.Combine(root, @"node\node.exe");
            string script = Path.Combine(root, @"web\server.js");
            if (!File.Exists(node) || !File.Exists(script)) return;

            string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DisplayDeck");
            Directory.CreateDirectory(data);

            ProcessStartInfo psi = new ProcessStartInfo(node, "\"" + script + "\"");
            psi.WorkingDirectory = root;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.EnvironmentVariables["STATE_FILE"] = Path.Combine(data, "state.json");
            psi.EnvironmentVariables["TRAY_PID"] = Process.GetCurrentProcess().Id.ToString();
            if (LanEnabled())
            {
                psi.EnvironmentVariables["HOST"] = "0.0.0.0";
                psi.EnvironmentVariables["ACCESS_KEY"] = AccessKey();
                string ip = LanAddress();
                if (ip != null) psi.EnvironmentVariables["LAN_IP"] = ip;
            }
            server = Process.Start(psi);
        }

        string DataDir()
        {
            string d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DisplayDeck");
            Directory.CreateDirectory(d);
            return d;
        }

        bool LanEnabled() { return File.Exists(Path.Combine(DataDir(), "lan.enabled")); }

        // Random 32-hex-char key, created once. Devices other than this PC must present it.
        string AccessKey()
        {
            string f = Path.Combine(DataDir(), "access.key");
            if (File.Exists(f))
            {
                string k = File.ReadAllText(f).Trim();
                if (k.Length >= 16) return k;
            }
            byte[] bytes = new byte[16];
            using (System.Security.Cryptography.RandomNumberGenerator rng = System.Security.Cryptography.RandomNumberGenerator.Create())
                rng.GetBytes(bytes);
            StringBuilder sb = new StringBuilder();
            foreach (byte b in bytes) sb.Append(b.ToString("x2"));
            File.WriteAllText(f, sb.ToString());
            return sb.ToString();
        }

        const string RuleName = "DisplayDeck";

        string NodePath() { return Path.Combine(root, @"node\node.exe"); }

        static string RunHidden(string exe, string args, out int exitCode)
        {
            ProcessStartInfo psi = new ProcessStartInfo(exe, args);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            using (Process p = Process.Start(psi))
            {
                string o = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                exitCode = p.ExitCode;
                return o;
            }
        }

        // Reading firewall rules needs no elevation; only adding one does.
        bool FirewallRuleOk()
        {
            try
            {
                int code;
                string o = RunHidden("netsh.exe", "advfirewall firewall show rule name=\"" + RuleName + "\" verbose", out code);
                return code == 0 && o.IndexOf(NodePath(), StringComparison.OrdinalIgnoreCase) >= 0
                    && o.IndexOf("8765", StringComparison.Ordinal) >= 0;
            }
            catch (Exception) { return false; }
        }

        // Adds (or replaces) the inbound rule with a single UAC prompt. False if declined or failed.
        bool EnsureFirewallRule()
        {
            if (FirewallRuleOk()) return true;
            string add = "netsh advfirewall firewall delete rule name=\"" + RuleName + "\" & "
                + "netsh advfirewall firewall add rule name=\"" + RuleName + "\" dir=in action=allow protocol=TCP localport=" + Port
                + " profile=any remoteip=localsubnet program=\"" + NodePath() + "\"";
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/c " + add);
                psi.Verb = "runas";
                psi.UseShellExecute = true;
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                using (Process p = Process.Start(psi)) { p.WaitForExit(); }
            }
            catch (System.ComponentModel.Win32Exception) { return false; } // UAC prompt declined
            catch (Exception) { return false; }
            return FirewallRuleOk();
        }

        void SetLan(bool on)
        {
            string flag = Path.Combine(DataDir(), "lan.enabled");
            if (on && !EnsureFirewallRule())
                Balloon("DisplayDeck", "Windows Firewall access was not granted, so phones may not be able to connect. Use \"Allow through firewall\" in the menu to retry.", ToolTipIcon.Warning);
            if (on) File.WriteAllText(flag, "1"); else File.Delete(flag);
            try { if (server != null && !server.HasExited) { server.Kill(); server.WaitForExit(3000); } } catch (Exception) { }
            server = null;
            StartServer();
            Balloon("DisplayDeck", on ? "Other devices on your network can now connect. Use Copy phone link." : "Only this PC can connect now.", ToolTipIcon.Info);
        }

        static string LanAddress()
        {
            foreach (System.Net.NetworkInformation.NetworkInterface ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;
                System.Net.NetworkInformation.IPInterfaceProperties props = ni.GetIPProperties();
                if (props.GatewayAddresses.Count == 0) continue;
                foreach (System.Net.NetworkInformation.UnicastIPAddressInformation ua in props.UnicastAddresses)
                    if (ua.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) return ua.Address.ToString();
            }
            return null;
        }

        void CopyPhoneLink()
        {
            string ip = LanAddress();
            if (ip == null) { Balloon("DisplayDeck", "No network connection found.", ToolTipIcon.Warning); return; }
            Clipboard.SetText("http://" + ip + ":" + Port + "/?key=" + AccessKey());
            Balloon("DisplayDeck", "Phone link copied. Open it once on the device; it remembers you.", ToolTipIcon.Info);
        }

        bool IsServerUp()
        {
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url + "api/audio-devices-ping");
                req.Timeout = 800;
                using (req.GetResponse()) { }
                return true;
            }
            catch (WebException e) { return e.Response != null; } // 404 still means "up"
            catch (Exception) { return false; }
        }

        void OpenPage() { OpenPage(""); }

        void OpenPage(string fragment)
        {
            try { Process.Start(new ProcessStartInfo(url + fragment) { UseShellExecute = true }); }
            catch (Exception ex) { Balloon("Could not open browser", ex.Message, ToolTipIcon.Error); }
        }

        void Balloon(string title, string text, ToolTipIcon kind)
        {
            icon.ShowBalloonTip(4000, title, text, kind);
        }

        bool AutoStartEnabled()
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey))
                return k != null && k.GetValue(RunValue) != null;
        }

        // Default to starting with Windows once; after that the menu toggle is the user's choice.
        void EnableAutoStartOnFirstRun()
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DisplayDeck");
                Directory.CreateDirectory(dir);
                string flag = Path.Combine(dir, "firstrun.done");
                if (File.Exists(flag)) return;
                SetAutoStart(true);
                File.WriteAllText(flag, "1");
            }
            catch (Exception) { }
        }

        void SetAutoStart(bool on)
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (on) k.SetValue(RunValue, "\"" + Application.ExecutablePath + "\"");
                else k.DeleteValue(RunValue, false);
            }
        }

        void BuildMenu()
        {
            menu.Items.Clear();
            ToolStripMenuItem open = new ToolStripMenuItem("Open DisplayDeck");
            open.Font = new Font(open.Font, FontStyle.Bold);
            open.Click += delegate { OpenPage(); };
            menu.Items.Add(open);
            AddSeparator();

            List<ProfileItem> snapshot;
            lock (gate) { snapshot = new List<ProfileItem>(profiles); }
            if (snapshot.Count == 0)
            {
                ToolStripMenuItem none = new ToolStripMenuItem("(no profiles found)");
                none.Enabled = false;
                menu.Items.Add(none);
            }
            foreach (ProfileItem p in snapshot)
            {
                ProfileItem item = p;
                ToolStripMenuItem mi = new ToolStripMenuItem(item.Name);
                mi.Checked = item.Current || (item.LastApplied && !AnyCurrent(snapshot));
                mi.Click += delegate { ApplyAsync(item.Name); };
                menu.Items.Add(mi);
            }

            AddSeparator();
            ToolStripMenuItem saveLayout = new ToolStripMenuItem("Save current layout...");
            saveLayout.Click += delegate { OpenPage("#save"); };
            menu.Items.Add(saveLayout);
            ToolStripMenuItem undo = new ToolStripMenuItem("Undo layout change");
            undo.Click += delegate { UndoAsync(); };
            menu.Items.Add(undo);
            AddSeparator();
            ToolStripMenuItem lan = new ToolStripMenuItem("Allow other devices (phone)");
            lan.Checked = LanEnabled();
            lan.Click += delegate { SetLan(!LanEnabled()); };
            menu.Items.Add(lan);
            if (lan.Checked)
            {
                ToolStripMenuItem qr = new ToolStripMenuItem("Show phone QR code");
                qr.Click += delegate { OpenPage("phone"); };
                menu.Items.Add(qr);
                ToolStripMenuItem link = new ToolStripMenuItem("Copy phone link");
                link.Click += delegate { CopyPhoneLink(); };
                menu.Items.Add(link);
                if (!FirewallRuleOk())
                {
                    ToolStripMenuItem fw = new ToolStripMenuItem("Allow through firewall...");
                    fw.Click += delegate
                    {
                        if (EnsureFirewallRule()) Balloon("DisplayDeck", "Firewall access granted.", ToolTipIcon.Info);
                        else Balloon("DisplayDeck", "Firewall access was not granted.", ToolTipIcon.Warning);
                    };
                    menu.Items.Add(fw);
                }
            }
            string plugin = Path.Combine(root, @"streamdeck\com.nplaudio.displaydeck.streamDeckPlugin");
            if (File.Exists(plugin) && StreamDeckInstalled())
            {
                AddSeparator();
                ToolStripMenuItem sd = new ToolStripMenuItem("Install Stream Deck plugin...");
                sd.Click += delegate
                {
                    try { Process.Start(new ProcessStartInfo(plugin) { UseShellExecute = true }); } // Stream Deck owns this file type
                    catch (Exception ex) { Balloon("Could not open the plugin", ex.Message, ToolTipIcon.Error); }
                };
                menu.Items.Add(sd);
            }
            AddSeparator();
            ToolStripMenuItem auto = new ToolStripMenuItem("Start with Windows");
            auto.Checked = AutoStartEnabled();
            auto.Click += delegate { SetAutoStart(!AutoStartEnabled()); };
            menu.Items.Add(auto);
            ToolStripMenuItem settings = new ToolStripMenuItem("Settings...");
            settings.Click += delegate { OpenPage("settings"); };
            menu.Items.Add(settings);
            AddSeparator();
            ToolStripMenuItem quit = new ToolStripMenuItem("Quit");
            quit.Click += delegate { Quit(); };
            menu.Items.Add(quit);
        }

        static bool StreamDeckInstalled()
        {
            foreach (Environment.SpecialFolder f in new Environment.SpecialFolder[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
            {
                string dir = Environment.GetFolderPath(f);
                if (dir.Length > 0 && File.Exists(Path.Combine(dir, @"Elgato\StreamDeck\StreamDeck.exe"))) return true;
            }
            return false;
        }

        // Adds a separator only between two items: never first, never twice in a row.
        void AddSeparator()
        {
            if (menu.Items.Count == 0) return;
            if (menu.Items[menu.Items.Count - 1] is ToolStripSeparator) return;
            menu.Items.Add(new ToolStripSeparator());
        }

        static bool AnyCurrent(List<ProfileItem> list)
        {
            foreach (ProfileItem p in list) if (p.Current) return true;
            return false;
        }

        void RefreshProfilesAsync()
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    string json = Http("GET", "api/profiles", 20000);
                    JavaScriptSerializer js = new JavaScriptSerializer();
                    Dictionary<string, object> root2 = (Dictionary<string, object>)js.DeserializeObject(json);
                    List<ProfileItem> list = new List<ProfileItem>();
                    foreach (object o in (IEnumerable)root2["profiles"])
                    {
                        Dictionary<string, object> d = (Dictionary<string, object>)o;
                        ProfileItem p = new ProfileItem();
                        p.Name = (string)d["name"];
                        p.Current = d.ContainsKey("current") && d["current"] is bool && (bool)d["current"];
                        p.LastApplied = d.ContainsKey("lastApplied") && d["lastApplied"] is bool && (bool)d["lastApplied"];
                        list.Add(p);
                    }
                    lock (gate) { profiles = list; }
                }
                catch (Exception) { /* server not ready yet; the next tick retries */ }
            });
        }

        void UndoAsync()
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    Http("POST", "api/undo-layout", 45000);
                    Balloon("DisplayDeck", "Went back to the previous layout", ToolTipIcon.Info);
                    RefreshProfilesAsync();
                }
                catch (Exception ex)
                {
                    Balloon("Could not undo the layout change", ex.Message, ToolTipIcon.Error);
                }
            });
        }

        void ApplyAsync(string name)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    Http("POST", "api/apply/" + Uri.EscapeDataString(name), 45000);
                    Balloon("DisplayDeck", "Applied " + name, ToolTipIcon.Info);
                    RefreshProfilesAsync();
                }
                catch (Exception ex)
                {
                    Balloon("Could not apply " + name, ex.Message, ToolTipIcon.Error);
                }
            });
        }

        string Http(string method, string path, int timeoutMs)
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url + path);
            req.Method = method;
            req.Timeout = timeoutMs;
            if (method == "POST") req.ContentLength = 0;
            try
            {
                using (WebResponse resp = req.GetResponse())
                using (StreamReader r = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    return r.ReadToEnd();
            }
            catch (WebException e)
            {
                // The server explains failures as {"error": "..."}; show that rather than "(500)".
                if (e.Response != null)
                {
                    try
                    {
                        using (StreamReader r = new StreamReader(e.Response.GetResponseStream(), Encoding.UTF8))
                        {
                            Dictionary<string, object> d = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(r.ReadToEnd());
                            if (d.ContainsKey("error")) throw new Exception(Convert.ToString(d["error"]));
                        }
                    }
                    catch (InvalidCastException) { }
                    catch (ArgumentException) { }
                }
                throw;
            }
        }

        void Quit()
        {
            refreshTimer.Stop();
            icon.Visible = false;
            try { if (server != null && !server.HasExited) server.Kill(); } catch (Exception) { }
            icon.Dispose();
            ExitThread();
        }
    }
}
