// Bot Beacons v1.0 (with per-beacon hide/show): small floating desktop avatars ("beacons"), one per bot, on an end-to-end encrypted relay.
using System;
using System.Linq;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Threading;
using System.Windows.Forms;

namespace BotBeacons
{
    static class Program
    {
        public const string Version = "1.1.3";
        public static readonly List<BeaconForm> Beacons = new List<BeaconForm>();
        static Relay relay;
        static NotifyIcon tray;
        static PairForm pairForm;
        static WakeForm wakeForm;

        public static volatile bool SessionLocked;

        public static bool LaunchedAtStartup;

        [STAThread]

        static void Main(string[] args)
        {
            args = args ?? new string[0];
            LaunchedAtStartup = Array.Exists(args, a => string.Equals(a, "--startup", StringComparison.OrdinalIgnoreCase));
            // "BotBeacons.exe --autostart on|off": set Start with Windows (per-user Run entry) and exit, no window.
            // Lets a setup script switch it on without clicking; run it while the app is closed.
            int ai = Array.FindIndex(args, a => string.Equals(a, "--autostart", StringComparison.OrdinalIgnoreCase));
            if (ai >= 0 && ai + 1 < args.Length)
            {
                Settings.Load();
                AutoStart.Set(string.Equals(args[ai + 1], "on", StringComparison.OrdinalIgnoreCase));
                return;
            }
            bool created;
            // one pet per Windows session: a second launch (double-click, Run entry + manual start) only tells the
            // running one, which logs it, and exits. No duplicate beacons.
            using (var mutex = new Mutex(true, "BotBeacons.SingleInstance", out created))
            {
                if (!created)
                {
                    try { using (var ev = EventWaitHandle.OpenExisting("BotBeacons.SecondLaunch")) ev.Set(); } catch { }
                    return;
                }
                var second = new EventWaitHandle(false, EventResetMode.AutoReset, "BotBeacons.SecondLaunch");
                try { Native.SetProcessDPIAware(); } catch { }
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;   // TLS 1.2
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += (s, e) => Log.Write("UI error: " + e.Exception);
                AppDomain.CurrentDomain.UnhandledException += (s, e) => Log.Write("Fatal: " + e.ExceptionObject);

                Settings.Load();
                AnimSettings.Mode = AnimSettings.Parse(Settings.Get("anim", "full"));
                // locked session: nothing is visible, so the beacons idle at 2 ticks a second
                Microsoft.Win32.SystemEvents.SessionSwitch += (s, e) =>
                {
                    if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionLock) SessionLocked = true;
                    else if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionUnlock) SessionLocked = false;
                };
                Wake.Load();
                try { var cf = Path.Combine(Settings.Dir, "control.txt"); if (File.Exists(cf) && File.GetLastWriteTimeUtc(cf) < DateTime.UtcNow.AddSeconds(-10)) { File.Delete(cf); Log.Write("Ignored a stale control command from before start-up."); } } catch { }
                var defs = BeaconConfig.Load();
                AutoStart.Sync();
                Log.Write("Started Bot Beacons v" + Version + (LaunchedAtStartup ? " (at Windows sign-in)" : "") + " with " + defs.Count + " beacon(s), bubble style " + Theme.BubbleStyle() + ", animations " + AnimSettings.Name(AnimSettings.Mode) + ". Secure link: " + (Settings.Link.Valid ? "paired with " + Settings.Link.Peer : "not paired"));

                for (int i = 0; i < defs.Count; i++) Beacons.Add(new BeaconForm(defs[i], i, defs.Count));
                foreach (var kv in AttentionStore.Load()) { var sa = Find(kv.Key); if (sa != null) sa.RestoreAttention(kv.Value); else AttentionStore.Set(kv.Key, null); }
                var main = Beacons[0];
                FrameClock.Start();

                tray = new NotifyIcon();
                try { tray.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { tray.Icon = SystemIcons.Application; }
                tray.Text = "Bot Beacons";
                tray.ContextMenuStrip = BuildMenu(null);
                tray.Visible = true;
                TrayBadge.Init(tray, tray.Icon);
                // left click: open the first visible beacon with news (or show a hidden one that has some); double-click:
                // pause/resume all beacons. The single click waits out the double-click time so a double-click doesn't also open a bubble.
                var clickWait = new System.Windows.Forms.Timer { Interval = Math.Max(200, SystemInformation.DoubleClickTime) };
                clickWait.Tick += (s, e) => { clickWait.Stop(); TrayClick(); };
                tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) { clickWait.Stop(); clickWait.Start(); } };
                tray.MouseDoubleClick += (s, e) => { if (e.Button == MouseButtons.Left) { clickWait.Stop(); Visibility.SetPaused(!Visibility.Paused); } };

                relay = new Relay();
                Pairing.Paired += () => relay.Kick();   // drop the stream on the old link and connect with the new one
                relay.MessageArrived += m => main.BeginInvoke((Action)(() => Route(m)));
                // a second launch: just note it (no window, no focus change)
                ThreadPool.RegisterWaitForSingleObject(second, (st, to) => Log.Write("A second copy was started and exited; this one keeps running."), null, -1, false);
                // screens may still be settling right after sign-in: re-fit saved positions when the layout changes
                Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (s, e) => { try { main.BeginInvoke((Action)(() => { foreach (var sk in Beacons) sk.RefitToScreens(); })); } catch { } };
                // start-up work runs once the message loop is going (not on main.Shown: the main beacon may be hidden)
                var boot = new System.Windows.Forms.Timer { Interval = 200 };
                boot.Tick += (s, e) =>
                {
                    boot.Stop();
                    Visibility.Apply();
                    if (Visibility.HiddenList.Length > 0 || Visibility.Paused) Log.Write("Hidden beacons: " + (Visibility.Paused ? "all (beacons paused)" : Visibility.HiddenList) + ".");
                    relay.Start();
                    // never pop the pairing window at sign-in (it would take focus); the tray menu has Pair with bots
                    if (!Settings.Link.Valid && !Settings.DevHooks && !LaunchedAtStartup) StartPairing();
                };

                var ctl = new System.Windows.Forms.Timer { Interval = 1000 };
                ctl.Tick += (s, e) => { CheckControlFile(); UpdateTray(); };
                ctl.Start();
                boot.Start();

                Application.ApplicationExit += (s, e) => { relay.Stop(); tray.Visible = false; tray.Dispose(); Settings.Save(); Log.Write("Exited."); };
                Application.Run(main);
            }
        }

        // tray tooltip (max 63 chars): relay state
        static string lastTip;
        static void UpdateTray()
        {
            if (tray == null || relay == null) return;
            string st = relay.State;
            if (st != lastTip) { lastTip = st; }
            TrayBadge.Update();
        }

        public static string RelayState { get { return relay != null ? relay.State : "starting"; } }
        public static bool PairingActive { get { return pairForm != null && pairForm.Session.Active; } }

        static void TrayClick()
        {
            var u = FirstUnread();
            if (u != null) { u.OpenBubble(true); return; }
            // nothing visible to read: bring back a hidden beacon that has news (its glow shows; one more click opens it)
            var h = Beacons.FirstOrDefault(s => !s.Visible && (s.Attention || s.Unread.Count > 0) && (!s.Def.Test || ShowTest));
            if (h != null) { Visibility.Reveal(h); return; }
            var first = Beacons.FirstOrDefault(s => s.Visible);
            if (first != null) first.OpenBubble(true);
        }

        static BeaconForm FirstUnread() { foreach (var s in Beacons) if (s.Unread.Count > 0 && s.Visible) return s; return null; }

        // test beacons ("test": true in beacons.json, e.g. echo) are hidden unless this is on; off by default
        public static bool ShowTest { get { return Settings.Get("showTest", "0") == "1"; } }

        public static void SetShowTest(bool on)
        {
            Settings.Set("showTest", on ? "1" : "0"); Settings.Save();
            Visibility.Apply();
            Log.Write("Test beacons " + (on ? "shown." : "hidden."));
        }

        public static BeaconForm Find(string id)
        {
            foreach (var s in Beacons) if (string.Equals(s.Def.Id, id, StringComparison.OrdinalIgnoreCase)) return s;
            return null;
        }

        static void Route(PetMessage m)
        {
            var target = Find(m.Bot);
            if (target == null)
            {
                foreach (var s in Beacons) if (s.Def.Fallback) target = s;
                if (target == null) target = Beacons[0];
                m.Title = (m.Title.Length > 0 ? m.Title + " " : "") + "[" + m.Bot + "]";
            }
            target.Notify(m);
        }

        // 1.1.3: removes and disposes a submenu's old items before it is refilled (Clear alone kept them alive)
        static void ClearItems(ToolStripItemCollection c)
        {
            var old = new ToolStripItem[c.Count]; c.CopyTo(old, 0); c.Clear();
            foreach (var it in old) it.Dispose();
        }

        // 1.1.3: each beacon builds its menu once and reuses it (BeaconForm.ContextMenuFor); the tray keeps its one menu.
        // Item states and lists are refreshed when the menu or a submenu opens, so a menu is never rebuilt or disposed
        // while it might still be showing, and its hidden windows don't pile up.
        public static ContextMenuStrip BuildMenu(BeaconForm from)
        {
            var m = NoTaskbar.Hook(new ContextMenuStrip());   // 1.1.1: menus and submenus never get a taskbar button
            if (from != null)
            {
                m.Items.Add("Open " + from.Def.Name, null, (s, e) => from.OpenBubble(true));
                m.Items.Add("Ping " + from.Def.Name, null, (s, e) => from.PingClick());
                m.Items.Add("Hide this beacon", null, (s, e) => Visibility.SetHidden(from, true));
                m.Items.Add(new ToolStripSeparator());
            }
            var open = new ToolStripMenuItem("Beacons");
            foreach (var sk in Beacons) { var k = sk; if (k.Def.Test && !ShowTest) continue; open.DropDownItems.Add(k.Def.Name + (k.Unread.Count > 0 ? "  (" + k.Unread.Count + ")" : ""), null, (s, e) => k.OpenBubble(true)); }
            m.Items.Add(open);
            // which beacons are on screen (real beacons; the test beacons keep their own switch below)
            var vis = new ToolStripMenuItem("Show beacons");
            Action fillVis = () =>
            {
                ClearItems(vis.DropDownItems);
                bool paused = Visibility.Paused;
                foreach (var sk in Beacons)
                {
                    if (sk.Def.Test) continue; var k = sk;
                    string extra = k.Attention ? "  (needs attention)" : k.Unread.Count > 0 ? "  (" + k.Unread.Count + " unread)" : "";
                    var it = new ToolStripMenuItem(k.Def.Name + extra) { Checked = !Visibility.IsHidden(k.Def.Id), Enabled = !paused };
                    it.Click += (s2, e2) => Visibility.SetHidden(k, !Visibility.IsHidden(k.Def.Id));
                    vis.DropDownItems.Add(it);
                }
                vis.DropDownItems.Add(new ToolStripSeparator());
                vis.DropDownItems.Add("Show all", null, (s2, e2) => Visibility.SetAll(false));
                vis.DropDownItems.Add("Hide all", null, (s2, e2) => Visibility.SetAll(true));
                if (paused) vis.DropDownItems.Add(new ToolStripMenuItem("(beacons paused: double-click the tray icon to resume)") { Enabled = false });
            };
            fillVis();
            vis.DropDownOpening += (s, e) => fillVis();
            m.Items.Add(vis);
            var pause = new ToolStripMenuItem("Pause beacons (hide all)") { Checked = Visibility.Paused };
            pause.Click += (s, e) => Visibility.SetPaused(!Visibility.Paused);
            m.Opening += (s, e) => pause.Checked = Visibility.Paused;
            m.Items.Add(pause);
            // test beacons: their own submenu. The main switch, then a one-by-one checklist (used while the switch is on)
            var testMenu = new ToolStripMenuItem("Test beacons");
            Action fillTest = () =>
            {
                ClearItems(testMenu.DropDownItems);
                var showTest = new ToolStripMenuItem("Show test beacons") { Checked = ShowTest };
                showTest.Click += (s2, e2) => SetShowTest(!ShowTest);
                testMenu.DropDownItems.Add(showTest);
                testMenu.DropDownItems.Add(new ToolStripSeparator());
                foreach (var sk in Beacons)
                {
                    if (!sk.Def.Test) continue; var k = sk;
                    var it = new ToolStripMenuItem(k.Def.Name + (k.Unread.Count > 0 ? "  (" + k.Unread.Count + " unread)" : "")) { Checked = !Visibility.IsHidden(k.Def.Id), Enabled = ShowTest && !Visibility.Paused };
                    it.Click += (s2, e2) => Visibility.SetHidden(k, !Visibility.IsHidden(k.Def.Id));
                    testMenu.DropDownItems.Add(it);
                }
            };
            fillTest();
            testMenu.DropDownOpening += (s, e) => fillTest();
            m.Items.Add(testMenu);
            var style = new ToolStripMenuItem("Bubble style");
            foreach (var st in Theme.BubbleStyles) { var k = st; style.DropDownItems.Add(new ToolStripMenuItem(k == "card" ? "Card (dark)" : "Cloud", null, (s, e) => Theme.SetBubbleStyle(k)) { Tag = k }); }
            style.DropDownOpening += (s, e) => { string cur = Theme.BubbleStyle(); foreach (ToolStripMenuItem it in style.DropDownItems) it.Checked = (string)it.Tag == cur; };
            m.Items.Add(style);
            var anim = new ToolStripMenuItem("Animations");
            foreach (var md in new[] { AnimMode.Full, AnimMode.Subtle, AnimMode.Off }) { var k = md; anim.DropDownItems.Add(new ToolStripMenuItem(k == AnimMode.Full ? "Full" : k == AnimMode.Subtle ? "Subtle" : "Off (still)", null, (s, e) => AnimSettings.Set(k)) { Tag = k }); }
            anim.DropDownItems.Add(new ToolStripSeparator());
            foreach (var fv in new[] { "30", "60", "monitor" }) { var k = fv; anim.DropDownItems.Add(new ToolStripMenuItem(k == "monitor" ? "Frame rate: match display (" + FrameClock.RefreshHz.ToString("0") + " Hz)" : "Frame rate: " + k + " fps", null, (s, e) => FrameClock.SetFps(k)) { Tag = "fps:" + k }); }
            anim.DropDownOpening += (s, e) =>
            {
                foreach (ToolStripItem ti in anim.DropDownItems)
                {
                    var it = ti as ToolStripMenuItem; if (it == null) continue;
                    if (it.Tag is AnimMode) it.Checked = (AnimMode)it.Tag == AnimSettings.Mode;
                    else if (it.Tag is string) it.Checked = (string)it.Tag == "fps:" + FrameClock.FpsSetting;
                }
            };
            m.Items.Add(anim);
            var auto = new ToolStripMenuItem("Start with Windows") { Checked = AutoStart.IsOn };
            auto.Click += (s, e) => AutoStart.Set(!AutoStart.IsOn);
            m.Opening += (s, e) => auto.Checked = AutoStart.IsOn;
            m.Items.Add(auto);
            m.Opening += (s, e) => { ClearItems(open.DropDownItems); foreach (var sk in Beacons) { var k = sk; if (k.Def.Test && !ShowTest) continue; open.DropDownItems.Add(k.Def.Name + (k.Unread.Count > 0 ? "  (" + k.Unread.Count + ")" : ""), null, (s2, e2) => k.OpenBubble(true)); } };
            var pairItem = new ToolStripMenuItem(Settings.Link.Valid ? "Re-pair with bots\u2026" : "Pair with bots\u2026", null, (s, e) => StartPairing());
            m.Opening += (s, e) => pairItem.Text = Settings.Link.Valid ? "Re-pair with bots\u2026" : "Pair with bots\u2026";
            m.Items.Add(pairItem);
            m.Items.Add("Wake hook\u2026", null, (s, e) => OpenWakeForm());
            if (from != null) m.Items.Add("Test glow (local)", null, (s, e) => from.Notify(new PetMessage { Id = "local", Bot = from.Def.Id, Text = from.Def.Gist.Length > 0 ? from.Def.Gist : "Local test: the glow works. Click me to clear it.", Title = from.Def.Gist.Length > 0 ? "Local test" : "" }));
            m.Items.Add("Open settings folder", null, (s, e) => { try { System.Diagnostics.Process.Start(Settings.Dir); } catch { } });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Exit", null, (s, e) => Application.Exit());
            return m;
        }

        public static WakeForm OpenWakeForm()
        {
            if (wakeForm != null && !wakeForm.IsDisposed) { wakeForm.Activate(); return wakeForm; }
            wakeForm = new WakeForm(); wakeForm.Show(); return wakeForm;
        }

        // quiet: dev hook only; shows the code window without taking focus
        public static Pairing StartPairing(bool quiet = false)
        {
            if (pairForm != null && !pairForm.IsDisposed) { if (!quiet) pairForm.Activate(); return pairForm.Session; }
            // a session still waiting for the bot (its window was closed): show the same code again
            pairForm = new PairForm(Settings.Get("server", "https://ntfy.sh"), quiet, pairForm != null ? pairForm.Session : null);
            pairForm.Show();
            return pairForm.Session;
        }

        // Developer/test hook, only when devHooks=1 in settings.ini. Write one command into %APPDATA%\BotBeacons\control.txt:
        //   open <beacon> | test <beacon> | reply <beacon> <text> | pair | status | snap | showtest on|off | exit
        //   ping <beacon> (same as clicking a quiet beacon: wiggle + ping, 30 s limit) | click <beacon> | attention <beacon> <reason> (local test, no relay) | openapp <beacon> (the bubble's Open in app path)
        //   type <beacon> <text> (types into the open bubble's reply field and presses Enter) | key <beacon> esc|enter | bubble card|cloud (theme.json)
        //   fps 30|60|monitor | clock legacy|auto | frametimes <s> (frame interval stats -> frametimes.txt)
        //   hide <beacon> | show <beacon> | hideall | showall | pause on|off | trayclick | trayicons (saves the tray badge icons to snaps\)
        //   autostart on|off | netsim <seconds> (relay connections fail as if offline; 0 = stop) | anim full|subtle|off | act <beacon> <action name|idle|twitch> | fling <beacon> <vx> <vy> (px/s)
        //   frames <beacon> <count> <everyMs> [action]  (saves the beacon's own drawn frames to frames\frames_<id>_NN.png + offsets in frames_<id>.txt)
        //   wake-dialog | wake-snap | wake-test | wake-clear | wake-set  (wake-set reads wake-set.txt: url, key, [auth], [beacon id]; the file is deleted)
        static void CheckControlFile()
        {
            if (!Settings.DevHooks) return;
            try
            {
                string p = Path.Combine(Settings.Dir, "control.txt");
                if (!File.Exists(p)) return;
                string raw = File.ReadAllText(p).Trim(); File.Delete(p);
                var parts = raw.Split(new[] { ' ' }, 3);
                string cmd = parts[0].ToLowerInvariant();
                Log.Write("Control command: " + cmd + (parts.Length > 1 ? " " + parts[1] : ""));
                BeaconForm sk = parts.Length > 1 ? Find(parts[1]) : null;
                if (cmd == "open" && sk != null) sk.OpenBubble();
                else if (cmd == "test" && sk != null) sk.Notify(new PetMessage { Id = "local", Bot = sk.Def.Id, Text = "Local test." });
                else if (cmd == "reply" && sk != null && parts.Length == 3) sk.SendReplyText(parts[2]);
                else if (cmd == "ping" && sk != null) sk.PingClick();
                else if (cmd == "click" && sk != null) sk.SimulateClick();
                else if (cmd == "attention" && parts.Length == 3) Route(new PetMessage { Id = "local", Bot = parts[1], Kind = "attention", Text = parts[2] });
                else if (cmd == "openapp" && sk != null) sk.OpenInApp(sk.AttentionMsg);
                else if (cmd == "type" && sk != null && parts.Length == 3) { var b = sk.OpenBubbleForm; if (b != null) b.DevType(parts[2]); else Log.Write("type: " + sk.Def.Id + " has no open bubble."); }
                else if (cmd == "key" && sk != null && parts.Length == 3) { var b = sk.OpenBubbleForm; if (b != null) b.DevKey(parts[2].Trim().ToLowerInvariant()); else Log.Write("key: " + sk.Def.Id + " has no open bubble."); }
                else if (cmd == "dismiss" && sk != null) { var b = sk.OpenBubbleForm; if (b != null) b.DevKey("dismiss"); else sk.DismissFromBubble(); }
                else if (cmd == "bubble" && parts.Length > 1) Theme.SetBubbleStyle(parts[1]);
                else if (cmd == "fps" && parts.Length > 1) FrameClock.SetFps(parts[1]);
                else if (cmd == "tune") { var tv = raw.Split(' '); if (tv.Length >= 4) FrameClock.Tune(tv[1], tv[2], tv[3]); }
                else if (cmd == "clock" && parts.Length > 1) FrameClock.SetLegacy(parts[1] == "legacy");
                else if (cmd == "frametimes") { double sec = 10; if (parts.Length > 1) double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out sec); FrameClock.StartMeasure(sec, Path.Combine(Settings.Dir, "frametimes.txt")); }
                else if (cmd == "hide" && sk != null) Visibility.SetHidden(sk, true);
                else if (cmd == "show" && sk != null) Visibility.SetHidden(sk, false);
                else if (cmd == "hideall") Visibility.SetAll(true);
                else if (cmd == "showall") Visibility.SetAll(false);
                else if (cmd == "pause" && parts.Length > 1) Visibility.SetPaused(parts[1] == "on");
                else if (cmd == "trayclick") TrayClick();
                else if (cmd == "trayicons") { string dir = Path.Combine(Settings.Dir, "snaps"); Directory.CreateDirectory(dir); TrayBadge.SaveIcons(dir); }
                else if (cmd == "autostart" && parts.Length > 1) AutoStart.Set(parts[1] == "on");
                else if (cmd == "netsim" && parts.Length > 1) { int sec; if (int.TryParse(parts[1], out sec)) { Relay.SimOfflineUntil = DateTime.Now.AddSeconds(sec); Log.Write("netsim: relay connections fail for " + sec + " s."); relay.Kick(); } }
                else if (cmd == "anim" && parts.Length > 1) AnimSettings.Set(AnimSettings.Parse(parts[1]));
                else if (cmd == "act" && sk != null && parts.Length == 3) Log.Write("act " + sk.Def.Id + ": " + sk.PlayAction(parts[2]));
                else if (cmd == "flingtrace" && sk != null && parts.Length == 3)
                {
                    // dry-run fling on any beacon (hidden ones too): nothing moves on screen; logs every frame to flingtrace_<id>_<new|classic>.txt
                    var v = parts[2].Split(' '); float vx, vy; var ci = System.Globalization.CultureInfo.InvariantCulture;
                    if (v.Length >= 2 && float.TryParse(v[0], System.Globalization.NumberStyles.Float, ci, out vx) && float.TryParse(v[1], System.Globalization.NumberStyles.Float, ci, out vy))
                    {
                        BeaconForm.FlingClassic = v.Length >= 3 && v[2] == "classic";
                        sk.FlingTrace(vx, vy, Path.Combine(Settings.Dir, "flingtrace_" + sk.Def.Id + "_" + (BeaconForm.FlingClassic ? "classic" : "new") + ".txt"));
                    }
                }
                else if (cmd == "flingcatch" && sk != null)
                {
                    // dry-run flings caught by a simulated mouse (hidden beacons too): nothing moves on screen and no click
                    // action can fire; logs every frame to flingcatch_<id>_<new|classic>.txt and a summary to the log
                    BeaconForm.CatchClassic = parts.Length == 3 && parts[2].Trim() == "classic";
                    sk.FlingCatch(Path.Combine(Settings.Dir, "flingcatch_" + sk.Def.Id + "_" + (BeaconForm.CatchClassic ? "classic" : "new") + ".txt"));
                }
                else if (cmd == "fling" && sk != null && parts.Length == 3)
                {
                    var v = parts[2].Split(' '); float vx, vy;
                    if (v.Length == 2 && float.TryParse(v[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out vx) && float.TryParse(v[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out vy)) { sk.Fling(vx, vy); sk.FlingCount++; Log.Write("fling " + sk.Def.Id + " at " + vx + "," + vy + " px/s."); }
                }
                else if (cmd == "frames" && sk != null && parts.Length == 3)
                {
                    var v = parts[2].Split(new[] { ' ' }, 3); int n, ms;
                    if (v.Length >= 2 && int.TryParse(v[0], out n) && int.TryParse(v[1], out ms))
                    {
                        string dir = Path.Combine(Settings.Dir, "frames"); Directory.CreateDirectory(dir);
                        foreach (var f in Directory.GetFiles(dir, "frames_" + sk.Def.Id + "*")) File.Delete(f);
                        if (v.Length == 3) Log.Write("act " + sk.Def.Id + ": " + sk.PlayAction(v[2]));
                        sk.StartFrames(n, ms, dir);
                    }
                }
                else if (cmd == "pair")
                {
                    var ses = StartPairing(true);
                    File.WriteAllText(Path.Combine(Settings.Dir, "pair-code.txt"), ses.Code);   // dev only; one-time code, useless after 10 min
                }
                else if (cmd == "status")
                {
                    var lines = new List<string> { "bubbleStyle=" + Theme.BubbleStyle(), "anim=" + AnimSettings.Name(AnimSettings.Mode), "frameClock=" + FrameClock.Backend + " fps=" + FrameClock.FpsSetting + " effective=" + FrameClock.EffectiveFps + " display=" + FrameClock.RefreshHz.ToString("0.#") + "Hz missed=" + FrameClock.Missed, "locked=" + SessionLocked, "relay=" + relay.State, "relayLastOk=" + (relay.LastOk == DateTime.MinValue ? "never" : relay.LastOk.ToString("HH:mm:ss")), "tray=" + (tray != null ? tray.Text : ""), "trayBadge=" + TrayBadge.State, "hidden=" + Visibility.HiddenList, "paused=" + Visibility.Paused, "startup=" + LaunchedAtStartup, "autostart=" + AutoStart.IsOn + " wanted=" + AutoStart.Wanted, "grokApp=" + (System.Diagnostics.Process.GetProcessesByName("Grok Bot").Length > 0 ? "running" : "not running"), "protocol=" + AppLink.ProtocolTarget(), "paired=" + Settings.Link.Valid, "peer=" + Settings.Link.Peer, "rejected=" + relay.Rejected, Wake.StatusLine() };
                    foreach (var s in Beacons) lines.Add("beacon " + s.Def.Id + " unread=" + s.Unread.Count + " attention=" + s.Attention + " visible=" + s.Visible + " history=" + s.History.Count + " at=" + s.Bounds.X + "," + s.Bounds.Y + "," + s.Bounds.Width + "," + s.Bounds.Height + " drawn=" + s.DrawnPos + " bubble=" + s.BubbleBounds + " idle=" + s.IdleCount + " twitch=" + s.TwitchCount + " fling=" + s.FlingCount + " playing=" + s.Playing + " interval=" + s.TimerInterval);
                    File.WriteAllLines(Path.Combine(Settings.Dir, "status.txt"), lines.ToArray());
                }
                else if (cmd == "snap")
                {
                    string dir = Path.Combine(Settings.Dir, "snaps"); Directory.CreateDirectory(dir);
                    foreach (var f in Directory.GetFiles(dir)) File.Delete(f);
                    foreach (var s in Beacons) if (s.Visible) s.Snap(dir);   // hidden beacons would capture the desktop
                }
                else if (cmd == "showtest") SetShowTest(parts.Length > 1 && parts[1] == "on");
                else if (cmd == "wake-dialog") OpenWakeForm();
                else if (cmd == "wake-snap")
                {
                    string dir = Path.Combine(Settings.Dir, "snaps"); Directory.CreateDirectory(dir);
                    if (wakeForm != null && !wakeForm.IsDisposed) wakeForm.SnapTo(Path.Combine(dir, "wake_dialog.png"));
                }
                else if (cmd == "wake-clear") Wake.ClearAll();
                else if (cmd == "wake-test") { var h = Wake.For(Beacons[0].Def.Id); if (h != null) ThreadPool.QueueUserWorkItem(_ => { string err; Wake.Test(h, out err); }); }
                else if (cmd == "wake-set")
                {
                    string f = Path.Combine(Settings.Dir, "wake-set.txt");
                    if (File.Exists(f))
                    {
                        var a = File.ReadAllLines(f); File.Delete(f);
                        if (a.Length >= 2)
                        {
                            var h = new WakeHook { Url = a[0].Trim(), Key = WakeHook.CleanKey(a[1]), Auth = a.Length > 2 && a[2].Trim().Length > 0 ? a[2].Trim() : "grokbot" };
                            string sc = a.Length > 3 && a[3].Trim().Length > 0 ? a[3].Trim() : null;
                            if (h.Valid) Wake.Set(sc, h); else Log.Write("wake-set: invalid URL or key.");
                        }
                    }
                }
                else if (cmd == "exit") Application.Exit();
            }
            catch (Exception ex) { Log.Write("Control error: " + ex.Message); }
        }
    }

    static class Log
    {
        static readonly object gate = new object();
        public static void Write(string line)
        {
            try
            {
                lock (gate)
                {
                    string path = Path.Combine(Settings.Dir, "log.txt");
                    if (File.Exists(path) && new FileInfo(path).Length > 512 * 1024) File.Delete(path);
                    File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + line + Environment.NewLine);
                }
            }
            catch { }
        }
    }
}
