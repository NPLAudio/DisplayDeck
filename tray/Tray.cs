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
using System.Runtime.InteropServices;
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
            // A test copy on another port (DISPLAYDECK_PORT) gets its own lock, so it can run beside the installed one.
            string suffix = Environment.GetEnvironmentVariable("DISPLAYDECK_PORT");
            using (Mutex mutex = new Mutex(true, @"Local\DisplayDeckTray" + (string.IsNullOrEmpty(suffix) ? "" : "-" + suffix), out created))
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

    // A hidden window that receives the WM_HOTKEY messages for the profile shortcuts.
    class HotkeyWindow : NativeWindow
    {
        const int WM_HOTKEY = 0x0312;
        const uint MOD_NOREPEAT = 0x4000;

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);
        [DllImport("user32.dll")]
        static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        public event Action<int> Pressed;
        readonly List<int> ids = new List<int>();

        public HotkeyWindow() { CreateHandle(new CreateParams()); }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY && Pressed != null) Pressed(m.WParam.ToInt32());
            base.WndProc(ref m);
        }

        // "Ctrl+Alt+1" -> Windows modifier flags and virtual-key code. False for anything we do not understand.
        public static bool TryParse(string spec, out uint mods, out uint vk)
        {
            mods = 0; vk = 0;
            if (string.IsNullOrEmpty(spec)) return false;
            foreach (string raw in spec.Split('+'))
            {
                string p = raw.Trim();
                if (p == "Alt") mods |= 1;
                else if (p == "Ctrl") mods |= 2;
                else if (p == "Shift") mods |= 4;
                else if (p == "Win") mods |= 8;
                else if (vk != 0) return false;
                else vk = KeyCode(p);
            }
            return vk != 0 && (mods & (1 | 2 | 8)) != 0;
        }

        static uint KeyCode(string p)
        {
            if (p.Length == 1 && ((p[0] >= 'A' && p[0] <= 'Z') || (p[0] >= '0' && p[0] <= '9'))) return (uint)p[0];
            int n;
            if (p.Length > 1 && p[0] == 'F' && int.TryParse(p.Substring(1), out n) && n >= 1 && n <= 24) return (uint)(0x70 + n - 1);
            if (p.StartsWith("Numpad") && p.Length == 7 && p[6] >= '0' && p[6] <= '9') return (uint)(0x60 + (p[6] - '0'));
            switch (p)
            {
                case "Up": return 0x26; case "Down": return 0x28; case "Left": return 0x25; case "Right": return 0x27;
                case "Home": return 0x24; case "End": return 0x23; case "PageUp": return 0x21; case "PageDown": return 0x22;
                case "Insert": return 0x2D; case "Delete": return 0x2E; case "Space": return 0x20;
            }
            return 0;
        }

        /// <summary>Registers one shortcut. Returns null when it worked, otherwise the reason it did not.</summary>
        public string Register(int id, string spec)
        {
            uint mods, vk;
            if (!TryParse(spec, out mods, out vk)) return "DisplayDeck does not understand " + spec + ".";
            if (RegisterHotKey(Handle, id, mods | MOD_NOREPEAT, vk)) { ids.Add(id); return null; }
            int err = Marshal.GetLastWin32Error();
            return err == 1409 ? "Another program already uses " + spec + "." : "Windows would not register " + spec + " (error " + err + ").";
        }

        public void UnregisterAll()
        {
            foreach (int id in ids) UnregisterHotKey(Handle, id);
            ids.Clear();
        }
    }

    class TrayContext : ApplicationContext
    {
        // A test copy can use another port; installed copies always use 8765.
        static readonly int Port = EnvPort();
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
        System.Windows.Forms.Timer hotkeyTimer = new System.Windows.Forms.Timer();
        readonly HotkeyWindow hotkeyWindow = new HotkeyWindow();
        readonly Dictionary<int, string> hotkeyProfiles = new Dictionary<int, string>();
        string hotkeySignature = "";
        bool hotkeyPolling;

        static int EnvPort()
        {
            int p;
            return int.TryParse(Environment.GetEnvironmentVariable("DISPLAYDECK_PORT"), out p) && p > 0 && p < 65536 ? p : 8765;
        }

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

            // Keyboard shortcuts set on the profile page. Checked every few seconds so a change takes effect quickly.
            hotkeyWindow.Pressed += delegate(int id)
            {
                string name;
                if (hotkeyProfiles.TryGetValue(id, out name)) ApplyAsync(name);
            };
            hotkeyTimer.Interval = 4000;
            hotkeyTimer.Tick += delegate { RefreshHotkeysAsync(); };
            hotkeyTimer.Start();
            RefreshHotkeysAsync();
        }

        // Reads the shortcut list from the server and, only when it changed, registers it with Windows.
        void RefreshHotkeysAsync()
        {
            if (hotkeyPolling) return;
            hotkeyPolling = true;
            System.Threading.SynchronizationContext ui = System.Threading.SynchronizationContext.Current;
            if (ui == null) { hotkeyPolling = false; return; } // the message loop is not running yet; the timer asks again
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    string json = Http("GET", "api/hotkeys", 5000);
                    Dictionary<string, object> root2 = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(json);
                    List<KeyValuePair<string, string>> wanted = new List<KeyValuePair<string, string>>();
                    foreach (object o in (IEnumerable)root2["hotkeys"])
                    {
                        Dictionary<string, object> d = (Dictionary<string, object>)o;
                        wanted.Add(new KeyValuePair<string, string>((string)d["name"], (string)d["hotkey"]));
                    }
                    StringBuilder sig = new StringBuilder();
                    foreach (KeyValuePair<string, string> w in wanted) sig.Append(w.Key).Append('\u0001').Append(w.Value).Append('\u0002');
                    if (sig.ToString() == hotkeySignature) return;
                    ui.Post(delegate { ApplyHotkeys(wanted, sig.ToString()); }, null);
                }
                catch (Exception) { /* server not ready yet; the next tick retries */ }
                finally { hotkeyPolling = false; }
            });
        }

        void ApplyHotkeys(List<KeyValuePair<string, string>> wanted, string signature)
        {
            hotkeyWindow.UnregisterAll();
            hotkeyProfiles.Clear();
            Dictionary<string, object> failed = new Dictionary<string, object>();
            int id = 1;
            foreach (KeyValuePair<string, string> w in wanted)
            {
                string problem = hotkeyWindow.Register(id, w.Value);
                if (problem == null) hotkeyProfiles[id] = w.Key; else failed[w.Key] = problem;
                id++;
            }
            hotkeySignature = signature;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    Dictionary<string, object> body = new Dictionary<string, object>();
                    body["failed"] = failed;
                    Http("POST", "api/hotkeys/report", 5000, new JavaScriptSerializer().Serialize(body));
                }
                catch (Exception) { }
            });
        }

        void StartServer()
        {
            if (IsServerUp()) return; // something (e.g. a dev server) already answers on the port
            string node = Path.Combine(root, @"node\node.exe");
            string script = Path.Combine(root, @"web\server.js");
            if (!File.Exists(node) || !File.Exists(script)) return;

            string data = DataDir();

            ProcessStartInfo psi = new ProcessStartInfo(node, "\"" + script + "\"");
            psi.WorkingDirectory = root;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.EnvironmentVariables["STATE_FILE"] = Path.Combine(data, "state.json");
            psi.EnvironmentVariables["TRAY_PID"] = Process.GetCurrentProcess().Id.ToString();
            psi.EnvironmentVariables["PORT"] = Port.ToString();
            if (LanEnabled())
            {
                psi.EnvironmentVariables["HOST"] = "0.0.0.0";
                psi.EnvironmentVariables["ACCESS_KEY"] = AccessKey();
                string ip = LanAddress();
                if (ip != null) psi.EnvironmentVariables["LAN_IP"] = ip;
            }
            server = Process.Start(psi);
        }

        // DISPLAYDECK_DATA moves the settings, key and state elsewhere (used by test copies; the helper honors it too).
        string DataDir()
        {
            string d = Environment.GetEnvironmentVariable("DISPLAYDECK_DATA");
            if (string.IsNullOrEmpty(d))
                d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DisplayDeck");
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
                    && o.IndexOf(Port.ToString(), StringComparison.Ordinal) >= 0;
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
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAYDECK_DATA"))) return; // a test copy must not touch the real startup entry
                string dir = DataDir();
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
            return Http(method, path, timeoutMs, null);
        }

        string Http(string method, string path, int timeoutMs, string jsonBody)
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url + path);
            req.Method = method;
            req.Timeout = timeoutMs;
            if (method == "POST")
            {
                byte[] bytes = jsonBody == null ? new byte[0] : Encoding.UTF8.GetBytes(jsonBody);
                req.ContentLength = bytes.Length;
                if (bytes.Length > 0)
                {
                    req.ContentType = "application/json";
                    using (Stream s = req.GetRequestStream()) s.Write(bytes, 0, bytes.Length);
                }
            }
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
            hotkeyTimer.Stop();
            hotkeyWindow.UnregisterAll();
            icon.Visible = false;
            try { if (server != null && !server.HasExited) server.Kill(); } catch (Exception) { }
            icon.Dispose();
            ExitThread();
        }
    }
}
