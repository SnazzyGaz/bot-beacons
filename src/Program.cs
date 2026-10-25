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
        public const string Version = "1.2.5";
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
            // one copy per Windows session: a second launch (double-click, Run entry + manual start) only tells the
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
                // 1.2.0: tabs added for bots (1.2.5: a new bot answered Add; before: "+ Add tab") are beacons too; tab name overrides apply everywhere
                foreach (var u in TabStore.Added) if (!defs.Exists(d => string.Equals(d.Id, u.Id, StringComparison.OrdinalIgnoreCase))) defs.Add(TabStore.DefFor(u));
                foreach (var d in defs) TabStore.ApplyName(d);
                AutoStart.Sync();
                Log.Write("Started Bot Beacons v" + Version + (LaunchedAtStartup ? " (at Windows sign-in)" : "") + " with " + defs.Count + " beacon(s), bubble style " + Theme.BubbleStyle() + ", animations " + AnimSettings.Name(AnimSettings.Mode) + ", skin " + Skin.Name + ". Secure link: " + (Settings.Link.Valid ? "paired with " + Settings.Link.Peer : "not paired"));

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
                tray.BalloonTipClicked += (s, e) => { if (Updater.Available != null) Updater.UpdateNow(); };   // 1.2.3
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
                Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (s, e) => { try { main.BeginInvoke((Action)(() => { foreach (var sk in Beacons) sk.RefitToScreens(); TabsSkin.Relayout(); })); } catch { } };
                // start-up work runs once the message loop is going (not on main.Shown: the main beacon may be hidden)
                var boot = new System.Windows.Forms.Timer { Interval = 200 };
                boot.Tick += (s, e) =>
                {
                    boot.Stop();
                    Visibility.Apply();
                    if (Visibility.HiddenList.Length > 0 || Visibility.Paused) Log.Write("Hidden beacons: " + (Visibility.Paused ? "all (beacons paused)" : Visibility.HiddenList) + ".");
                    relay.Start();
                    Updater.Cleanup(); Updater.Start(main);   // 1.2.3: update checks (Updates > Notify / Off)
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

        // 1.2.3: a tray balloon (updates)
        public static void Balloon(string title, string text, ToolTipIcon icon)
        {
            if (tray == null) return;
            try { tray.ShowBalloonTip(8000, title, text, icon); } catch { }
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
            var h = Beacons.FirstOrDefault(s => !TabsSkin.OnScreen(s) && (s.Attention || s.Unread.Count > 0) && (!s.Def.Test || ShowTest));
            if (h != null) { Visibility.Reveal(h); return; }
            var first = Beacons.FirstOrDefault(s => TabsSkin.OnScreen(s));
            if (first != null) first.OpenBubble(true);
        }

        static BeaconForm FirstUnread() { foreach (var s in Beacons) if (s.Unread.Count > 0 && TabsSkin.OnScreen(s)) return s; return null; }

        // 1.2.0: an added tab becomes a beacon straight away (1.2.5: added when a new bot's prompt is answered Add)
        public static BeaconForm AddBeacon(BeaconDef def)
        {
            if (Find(def.Id) != null) return Find(def.Id);
            TabStore.ApplyName(def);
            var b = new BeaconForm(def, Beacons.Count, Beacons.Count + 1);
            Beacons.Add(b);
            if (!b.IsHandleCreated) { var h = b.Handle; }
            Visibility.Apply();
            return b;
        }
        // and goes again on Remove tab (never the first beacon: it owns the message loop)
        public static void RemoveBeacon(BeaconForm b)
        {
            int i = Beacons.IndexOf(b); if (i <= 0) { if (i == 0) Visibility.SetHidden(b, true); return; }
            var bf = b.OpenBubbleForm; if (bf != null) { bf.CloseReason = "tab removed"; bf.Close(); }
            Beacons.RemoveAt(i);
            AttentionStore.Set(b.Def.Id, null);
            b.Hide(); b.Dispose();
            Visibility.Apply();
        }

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
            if (target != null) { target.Notify(m); return; }
            if (NewBots.Offer(m)) return;   // 1.2.5: a signed message for a new id: "New bot X wants a tab. Add / Not now"
            RouteFallback(m);
        }
        // an id with no beacon (unverified, or the user said Not now): the fallback beacon shows it, tagged with the id
        public static void RouteFallback(PetMessage m)
        {
            BeaconForm target = null;
            foreach (var s in Beacons) if (s.Def.Fallback) target = s;
            if (target == null) target = Beacons[0];
            m.Title = (m.Title.Length > 0 ? m.Title + " " : "") + "[" + m.Bot + "]";
            target.Notify(m);
        }

        // 1.1.3: removes and disposes a submenu's old items before it is refilled (Clear alone kept them alive)
        static void ClearItems(ToolStripItemCollection c)
        {
            var old = new ToolStripItem[c.Count]; c.CopyTo(old, 0); c.Clear();
            foreach (var it in old) it.Dispose();
        }

        // 1.2.3: one menu for every place it opens: a floating beacon, a tab, the shelf's menu button and the tray icon
        // (from == null for the last two). A short top level (the bots, Reply, Mark all read) and grouped submenus:
        // Skin, Layout, Sounds, Bots, Updates, then Start with Windows, About and Exit. Each beacon builds its menu once and
        // reuses it (1.1.3); checks, labels and lists refresh when the menu or a submenu opens, so nothing is rebuilt while
        // it might be showing. An item that can't do anything right now is hidden, not shown dead.
        public static ContextMenuStrip BuildMenu(BeaconForm from)
        {
            var m = NoTaskbar.Hook(new ContextMenuStrip());   // 1.1.1: menus and submenus never get a taskbar button
            var opening = new List<Action>();
            m.Opening += (s, e) => { foreach (var a in opening) a(); };

            // ---- top: the bots / Reply… / Mark all read
            if (from != null)
            {
                var reply = new ToolStripMenuItem("Reply to " + from.Def.Name + "\u2026", null, (s, e) => from.OpenBubble(true)) { ToolTipText = "Open " + from.Def.Name + "'s conversation" };
                reply.Font = new Font(reply.Font, FontStyle.Bold);
                m.Items.Add(reply);
                m.Items.Add("Ping " + from.Def.Name, null, (s, e) => from.PingClick());
                opening.Add(() => reply.Text = (from.Unread.Count > 0 ? "Reply to " + from.Def.Name + "  (" + from.Unread.Count + " unread)" : "Reply to " + from.Def.Name) + "\u2026");
            }
            else
            {
                // the tray / shelf menu lists the bots themselves: click one to open its conversation
                var bots = new List<ToolStripItem>();
                int at = m.Items.Count;
                opening.Add(() =>
                {
                    foreach (var it in bots) { m.Items.Remove(it); it.Dispose(); }
                    bots.Clear();
                    int i = at;
                    foreach (var sk in Beacons)
                    {
                        var k = sk; if (k.Def.Test && !ShowTest) continue;
                        string extra = k.Attention ? "  (needs you)" : k.Unread.Count > 0 ? "  (" + k.Unread.Count + " unread)" : "";
                        var it = new ToolStripMenuItem("Reply to " + k.Def.Name + extra + "\u2026", null, (s2, e2) => k.OpenBubble(true));
                        m.Items.Insert(i++, it); bots.Add(it);
                    }
                });
            }
            var markRead = new ToolStripMenuItem("Mark all read", null, (s, e) => { foreach (var k in Beacons.ToArray()) k.MarkRead(); });
            m.Items.Add(markRead);
            opening.Add(() => markRead.Visible = Beacons.Any(k => k.CanMarkRead));
            m.Items.Add(new ToolStripSeparator());

            // ---- Skin ▸ how beacons look: floating or tabs (each beacon can be either), bubble style, animations
            var skin = new ToolStripMenuItem("Skin");
            var skB = new ToolStripMenuItem("All floating", null, (s, e) => Skin.Set("floating"));
            var skT = new ToolStripMenuItem("All tabs", null, (s, e) => Skin.Set("tabs")) { ToolTipText = "Every beacon docks as a tab on a screen edge" };
            var skM = new ToolStripMenuItem("Mixed (drag a beacon to an edge, or a tab off it)") { Enabled = false };   // a state, shown only while it's true
            skin.DropDownItems.Add(skB); skin.DropDownItems.Add(skT); skin.DropDownItems.Add(skM);
            ToolStripMenuItem asTab = null;
            if (from != null)
            {
                asTab = new ToolStripMenuItem("Make " + from.Def.Name + " a tab", null, (s, e) => Skin.SetMode(from, !Skin.IsTab(from)));   // 1.2.2 (same as dragging it to / off an edge)
                skin.DropDownItems.Add(new ToolStripSeparator()); skin.DropDownItems.Add(asTab);
            }
            skin.DropDownItems.Add(new ToolStripSeparator());
            var styles = new List<ToolStripMenuItem>();
            foreach (var st in Theme.BubbleStyles) { var k = st; var it = new ToolStripMenuItem("Bubble: " + (k == "card" ? "Card (dark)" : "Cloud"), null, (s, e) => Theme.SetBubbleStyle(k)) { Tag = k }; styles.Add(it); skin.DropDownItems.Add(it); }
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
            skin.DropDownItems.Add(new ToolStripSeparator()); skin.DropDownItems.Add(anim);
            skin.DropDownOpening += (s, e) =>
            {
                var n = Skin.Name; skB.Checked = n == "floating"; skT.Checked = n == "tabs"; skM.Checked = skM.Visible = n == "mixed";
                if (asTab != null) asTab.Text = Skin.IsTab(from) ? "Make " + from.Def.Name + " float" : "Make " + from.Def.Name + " a tab";
                string cur = Theme.BubbleStyle(); foreach (var it in styles) it.Checked = (string)it.Tag == cur;
            };
            m.Items.Add(skin);

            // ---- Layout ▸ tabs and docking
            var layout = new ToolStripMenuItem("Layout");
            var size = new ToolStripMenuItem("Tab size");
            foreach (var sz in new[] { "small", "normal", "large" }) { var k = sz; size.DropDownItems.Add(new ToolStripMenuItem(char.ToUpper(k[0]) + k.Substring(1) + (k == "normal" ? " (default)" : ""), null, (s, e) => TabsSkin.SetSize(k)) { Tag = k }); }
            size.DropDownOpening += (s, e) => { foreach (ToolStripMenuItem it in size.DropDownItems) it.Checked = (string)it.Tag == TabsSkin.SizeSetting; };
            var hov = new ToolStripMenuItem("Open tabs on");   // 1.2.2
            var hTab = new ToolStripMenuItem("Click (default)", null, (s, e) => TabsSkin.SetOpen("click"));
            var hHov = new ToolStripMenuItem("Hover", null, (s, e) => TabsSkin.SetOpen("hover"));
            hov.DropDownItems.Add(hTab); hov.DropDownItems.Add(hHov);
            hov.DropDownOpening += (s, e) => { hTab.Checked = !TabsSkin.HoverOpens; hHov.Checked = TabsSkin.HoverOpens; };
            // 1.2.3: throw a floating beacon at an outer screen edge and it docks there as a tab
            var thr = new ToolStripMenuItem("Throw to dock", null, (s, e) => TabsSkin.SetThrowToDock(!TabsSkin.ThrowToDock)) { ToolTipText = "Fling a floating beacon at the outer edge of a screen and it becomes a tab there" };
            var shelf = new ToolStripMenuItem("Open the whole shelf", null, (s, e) => TabsSkin.OpenWholeShelf(from != null && Skin.IsTab(from) ? from : null));   // 1.2.1
            layout.DropDownItems.Add(size); layout.DropDownItems.Add(hov); layout.DropDownItems.Add(thr);
            layout.DropDownItems.Add(new ToolStripSeparator()); layout.DropDownItems.Add(shelf);
            layout.DropDownOpening += (s, e) => { thr.Checked = TabsSkin.ThrowToDock; shelf.Visible = Skin.AnyTabs; layout.DropDownItems[3].Visible = Skin.AnyTabs; };
            m.Items.Add(layout);

            // ---- Sounds ▸
            m.Items.Add(SoundMenu(from));

            // ---- Bots ▸ which beacons exist and show, editing, pairing
            var botsM = new ToolStripMenuItem("Bots");
            ToolStripMenuItem editTab = null, hideIt = null;
            if (from != null)
            {
                editTab = new ToolStripMenuItem("Edit " + from.Def.Name + "\u2026", null, (s, e) => TabsSkin.Edit(from, Rectangle.Empty)) { ToolTipText = "Name, purpose, colour and tab picture" };
                hideIt = new ToolStripMenuItem("Hide " + from.Def.Name, null, (s, e) => Visibility.SetHidden(from, true));
                botsM.DropDownItems.Add(editTab); botsM.DropDownItems.Add(hideIt);
                botsM.DropDownItems.Add("Test glow on " + from.Def.Name, null, (s, e) => from.Notify(new PetMessage { Id = "local", Bot = from.Def.Id, Text = from.Def.Gist.Length > 0 ? from.Def.Gist : "Local test: the glow works. Click me to clear it.", Title = from.Def.Gist.Length > 0 ? "Local test" : "" }));
                botsM.DropDownItems.Add(new ToolStripSeparator());
            }
            // which beacons are on screen (real beacons; the test beacons keep their own switch below)
            var vis = new ToolStripMenuItem("Show or hide");
            Action fillVis = () =>
            {
                ClearItems(vis.DropDownItems);
                bool paused = Visibility.Paused;
                foreach (var sk in Beacons)
                {
                    if (sk.Def.Test) continue; var k = sk;
                    string extra = k.Attention ? "  (needs you)" : k.Unread.Count > 0 ? "  (" + k.Unread.Count + " unread)" : "";
                    var it = new ToolStripMenuItem(k.Def.Name + extra) { Checked = !Visibility.IsHidden(k.Def.Id), Enabled = !paused };
                    it.Click += (s2, e2) => Visibility.SetHidden(k, !Visibility.IsHidden(k.Def.Id));
                    vis.DropDownItems.Add(it);
                }
                vis.DropDownItems.Add(new ToolStripSeparator());
                if (!paused)
                {
                    vis.DropDownItems.Add("Show all", null, (s2, e2) => Visibility.SetAll(false));
                    vis.DropDownItems.Add("Hide all", null, (s2, e2) => Visibility.SetAll(true));
                }
                else vis.DropDownItems.Add(new ToolStripMenuItem("(paused: untick Pause beacons to show them)") { Enabled = false });
            };
            fillVis();
            vis.DropDownOpening += (s, e) => fillVis();
            botsM.DropDownItems.Add(vis);
            var pause = new ToolStripMenuItem("Pause beacons (hide all)", null, (s, e) => Visibility.SetPaused(!Visibility.Paused));
            botsM.DropDownItems.Add(pause);
            // test beacons: the main switch, then a one-by-one checklist (used while the switch is on)
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
            botsM.DropDownItems.Add(testMenu);
            botsM.DropDownItems.Add(new ToolStripSeparator());
            var pairItem = new ToolStripMenuItem("Pair with bots\u2026", null, (s, e) => StartPairing());
            botsM.DropDownItems.Add(pairItem);
            botsM.DropDownItems.Add("Wake hook\u2026", null, (s, e) => OpenWakeForm());
            botsM.DropDownItems.Add("Open settings folder", null, (s, e) => { try { System.Diagnostics.Process.Start(Settings.Dir); } catch { } });
            botsM.DropDownOpening += (s, e) =>
            {
                pause.Checked = Visibility.Paused;
                pairItem.Text = Settings.Link.Valid ? "Re-pair with bots\u2026" : "Pair with bots\u2026";
                if (from != null) { bool t = Skin.IsTab(from); editTab.Visible = t; hideIt.Text = "Hide " + from.Def.Name + (t ? " (tab)" : ""); }
            };
            m.Items.Add(botsM);

            // ---- Updates ▸ (1.2.3)
            var upd = new ToolStripMenuItem("Updates");
            var updNow = new ToolStripMenuItem("Update now", null, (s, e) => Updater.UpdateNow());
            updNow.Font = new Font(updNow.Font, FontStyle.Bold);
            var updCheck = new ToolStripMenuItem("Check for updates now", null, (s, e) => Updater.CheckNow(true));
            var updOn = new ToolStripMenuItem("Notify me about updates (default)", null, (s, e) => Updater.SetNotify(true));
            var updOff = new ToolStripMenuItem("Off (don't check)", null, (s, e) => Updater.SetNotify(false));
            var updInfo = new ToolStripMenuItem("") { Enabled = false };
            upd.DropDownItems.Add(updNow); upd.DropDownItems.Add(updCheck); upd.DropDownItems.Add(new ToolStripSeparator());
            upd.DropDownItems.Add(updOn); upd.DropDownItems.Add(updOff); upd.DropDownItems.Add(new ToolStripSeparator());
            upd.DropDownItems.Add(updInfo);
            Action fillUpd = () =>
            {
                var a = Updater.Available;
                updNow.Visible = a != null; if (a != null) updNow.Text = "Update now to " + a.Tag + "\u2026";
                updCheck.Enabled = !Updater.Busy; updCheck.Text = Updater.Busy ? "Checking\u2026" : "Check for updates now";
                updOn.Checked = Updater.Notify; updOff.Checked = !Updater.Notify;
                updInfo.Text = "This is v" + Version + (Updater.Status.Length > 0 ? " \u2014 " + Updater.Status : "");
            };
            upd.DropDownOpening += (s, e) => fillUpd();
            m.Items.Add(upd);
            // a release found: the top-level item says so ("Update available v1.3.0")
            Font plain = upd.Font, bold = new Font(upd.Font, FontStyle.Bold);
            opening.Add(() => { var a = Updater.Available; upd.Text = a != null ? "Update available " + a.Tag : "Updates"; upd.Font = a != null ? bold : plain; });

            m.Items.Add(new ToolStripSeparator());
            var auto = new ToolStripMenuItem("Start with Windows", null, (s, e) => AutoStart.Set(!AutoStart.IsOn));
            m.Items.Add(auto);
            opening.Add(() => auto.Checked = AutoStart.IsOn);
            m.Items.Add("About Bot Beacons\u2026", null, (s, e) => ShowAbout());
            m.Items.Add("Exit", null, (s, e) => Application.Exit());
            // 1.1.4: each submenu opens beside the menu on the side with room (hooked once, after the fill handlers)
            HookAim(m.Items);
            return m;
        }

        static void HookAim(ToolStripItemCollection items)
        {
            foreach (ToolStripItem it in items)
            {
                var di = it as ToolStripMenuItem; if (di == null) continue;
                var d = di; d.DropDownOpening += (s, e) => AimSubmenu(d);
                HookAim(d.DropDownItems);
            }
        }

        static void ShowAbout()
        {
            string st = Updater.Status;
            MessageBox.Show("Bot Beacons v" + Version + "\n\nLittle desktop beacons that glow when your bots message you.\n\n" +
                "Updates: " + (Updater.Notify ? "notify" : "off") + (st.Length > 0 ? " \u2014 " + st : "") + "\n" +
                "Settings folder: " + Settings.Dir + "\n\nhttps://github.com/" + UpdateLogic.Repo,
                "About Bot Beacons", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // 1.1.5: Sounds submenu (1.2.3: the set and Off as one choice, then volume, quiet hours, tests)
        static ToolStripMenuItem SoundMenu(BeaconForm from)
        {
            var snd = new ToolStripMenuItem("Sounds");
            // Custom = each beacon's own sounds (1.1.5), Classic = the Windows system sounds (1.2.0), Off = Sound on unticked
            var setC = new ToolStripMenuItem("Custom (beacon sounds)", null, (s, e) => { Sound.SetSet("custom"); if (!Sound.On) Sound.SetOn(true); });
            var setK = new ToolStripMenuItem("Classic (Windows sounds)", null, (s, e) => { Sound.SetSet("classic"); if (!Sound.On) Sound.SetOn(true); });
            var setO = new ToolStripMenuItem("Off", null, (s, e) => Sound.SetOn(false));
            snd.DropDownItems.Add(setC); snd.DropDownItems.Add(setK); snd.DropDownItems.Add(setO);
            snd.DropDownItems.Add(new ToolStripSeparator());
            var vol = new ToolStripMenuItem("Volume");
            foreach (var v in Sound.Volumes) { var k = v; vol.DropDownItems.Add(new ToolStripMenuItem(char.ToUpper(k[0]) + k.Substring(1) + (k == "normal" ? " (default)" : ""), null, (s, e) => Sound.SetVolume(k)) { Tag = k }); }
            vol.DropDownOpening += (s, e) => { foreach (ToolStripMenuItem it in vol.DropDownItems) it.Checked = (string)it.Tag == Sound.Volume; };
            snd.DropDownItems.Add(vol);
            var quiet = new ToolStripMenuItem("Quiet hours (23:00\u201307:00)", null, (s, e) => Sound.SetQuietHours(!Sound.QuietHours));
            snd.DropDownItems.Add(quiet);
            ToolStripMenuItem mute = null;
            if (from != null)
            {
                mute = new ToolStripMenuItem("Mute " + from.Def.Name, null, (s, e) => Sound.SetMuted(from.Def.Id, !Sound.IsMuted(from.Def.Id)));
                snd.DropDownItems.Add(mute);
            }
            snd.DropDownItems.Add(new ToolStripSeparator());
            // tests play the beacon's own sounds (the first real beacon's from the tray), even while sound is off
            Func<BeaconDef> who = () => { if (from != null) return from.Def; foreach (var k in Beacons) if (!k.Def.Test) return k.Def; return Beacons.Count > 0 ? Beacons[0].Def : null; };
            var t1 = new ToolStripMenuItem("Test message sound", null, (s, e) => { var d = who(); if (d != null) Sound.Preview(d, false); });
            var t2 = new ToolStripMenuItem("Test attention sound", null, (s, e) => { var d = who(); if (d != null) Sound.Preview(d, true); });
            snd.DropDownItems.Add(t1); snd.DropDownItems.Add(t2);
            snd.DropDownOpening += (s, e) =>
            {
                setC.Checked = Sound.On && Sound.Set == "custom"; setK.Checked = Sound.On && Sound.Set == "classic"; setO.Checked = !Sound.On;
                quiet.Checked = Sound.QuietHours;
                if (mute != null) mute.Checked = Sound.IsMuted(from.Def.Id);
                bool any = who() != null; t1.Visible = t2.Visible = any;
                string n = from != null ? " (" + from.Def.Name + ")" : ""; t1.Text = "Test message sound" + n; t2.Text = "Test attention sound" + n;
            };
            return snd;
        }

        static void AimSubmenu(ToolStripMenuItem di)
        {
            var parent = di.Owner; if (parent == null || !di.HasDropDownItems) return;
            var b = parent.Bounds;   // a drop-down's bounds are in screen coordinates
            if (!(parent is ToolStripDropDown)) b = parent.RectangleToScreen(parent.ClientRectangle);
            di.DropDownDirection = Placement.Submenu(b, di.DropDown.GetPreferredSize(Size.Empty).Width, Screen.FromRectangle(b).WorkingArea);
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
        //   ping <beacon> (the menu's Ping: wiggle + ping, 30 s limit) | click <beacon> | attention <beacon> <reason> (local test, no relay) | openapp <beacon> (the bubble's Open in app path)
        //   type <beacon> <text> (types into the open bubble's reply field and presses Enter) | key <beacon> esc|enter | bubble card|cloud (theme.json)
        //   fps 30|60|monitor | clock legacy|auto | frametimes <s> (frame interval stats -> frametimes.txt)
        //   hide <beacon> | show <beacon> | hideall | showall | pause on|off | trayclick | trayicons (saves the tray badge icons to snaps\)
        //   autostart on|off | netsim <seconds> (relay connections fail as if offline; 0 = stop) | anim full|subtle|off | act <beacon> <action name|idle|twitch> | fling <beacon> <vx> <vy> (px/s)
        //   frames <beacon> <count> <everyMs> [action]  (saves the beacon's own drawn frames to frames\frames_<id>_NN.png + offsets in frames_<id>.txt)
        //   throwdock on|off | update check (1.2.3)
        //   skin beacons|tabs | shelf <beacon> | edittab <beacon> | soundset custom|classic | tabsnap (1.2.0 Tabs skin; tab/shelf pixels -> snaps\)
        //   card <beacon> | openshelf | tabsize small|normal|large | tabhover tab|shelf (1.2.1)
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
                    var lines = new List<string> { "bubbleStyle=" + Theme.BubbleStyle(), "anim=" + AnimSettings.Name(AnimSettings.Mode), "frameClock=" + FrameClock.Backend + " fps=" + FrameClock.FpsSetting + " effective=" + FrameClock.EffectiveFps + " display=" + FrameClock.RefreshHz.ToString("0.#") + "Hz missed=" + FrameClock.Missed, "locked=" + SessionLocked, "relay=" + relay.State, "relayLastOk=" + (relay.LastOk == DateTime.MinValue ? "never" : relay.LastOk.ToString("HH:mm:ss")), "tray=" + (tray != null ? tray.Text : ""), "trayBadge=" + TrayBadge.State, "hidden=" + Visibility.HiddenList, "paused=" + Visibility.Paused, "startup=" + LaunchedAtStartup, "autostart=" + AutoStart.IsOn + " wanted=" + AutoStart.Wanted, "grokApp=" + (System.Diagnostics.Process.GetProcessesByName("Grok Bot").Length > 0 ? "running" : "not running"), "protocol=" + AppLink.ProtocolTarget(), "paired=" + Settings.Link.Valid, "peer=" + Settings.Link.Peer, "rejected=" + relay.Rejected, Wake.StatusLine(), "soundSet=" + Sound.Set, TabsSkin.Status(), "updates=" + (Updater.Notify ? "notify" : "off") + " available=" + (Updater.Available != null ? Updater.Available.Tag : "none") + " status=" + Updater.Status };
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
                else if ((cmd == "skin" || cmd == "layout") && parts.Length > 1) Skin.Set(parts[1].Trim().ToLowerInvariant());
                else if (cmd == "update" && parts.Length > 1 && parts[1].Trim().ToLowerInvariant() == "check") Updater.CheckNow(false);   // 1.2.3: result in status.txt / the log (no balloon)
                else if (cmd == "throwdock" && parts.Length > 1) TabsSkin.SetThrowToDock(parts[1].Trim().ToLowerInvariant() != "off");   // 1.2.3
                else if (cmd == "mode" && sk != null && parts.Length > 2) Skin.SetMode(sk, parts[2].Trim().ToLowerInvariant() == "tab");   // 1.2.2: mode <beacon> tab|float
                else if (cmd == "shelf" && sk != null) TabsSkin.DevOpenShelf(sk);
                else if (cmd == "card" && sk != null) TabsSkin.DevOpenCard(sk);
                else if (cmd == "openshelf") TabsSkin.OpenWholeShelf(null);
                else if (cmd == "tabsize" && parts.Length > 1) TabsSkin.SetSize(parts[1].Trim().ToLowerInvariant());
                else if ((cmd == "tabopen" || cmd == "tabhover") && parts.Length > 1) TabsSkin.SetOpen(parts[1].Trim().ToLowerInvariant());   // 1.2.2: tabopen click|hover
                else if (cmd == "edittab" && sk != null) TabsSkin.Edit(sk, Rectangle.Empty);
                else if (cmd == "soundset" && parts.Length > 1) Sound.SetSet(parts[1].Trim().ToLowerInvariant());
                else if (cmd == "tabsnap") { string dir = Path.Combine(Settings.Dir, "snaps"); Directory.CreateDirectory(dir); TabsSkin.SaveSnaps(dir); }
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
