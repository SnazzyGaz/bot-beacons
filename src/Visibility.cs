using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace BotBeacons
{
    // Which beacons are on screen. Two saved settings (settings.ini), so the choice survives restarts and sign-in starts:
    //   hidden=id1,id2   beacons the user hid one by one (right-click > Hide this beacon, or the tray checklist)
    //   paused=1         "Pause beacons": every beacon is hidden; un-pausing brings back exactly the ones that were shown
    // Hidden beacons keep running underneath: messages and attention calls still arrive, queue up (unread + glow state)
    // and show on the tray icon; showing the beacon again brings back its glow (and its bubble, if it was open).
    static class Visibility
    {
        static HashSet<string> hidden;
        static HashSet<string> Hidden
        {
            get
            {
                if (hidden == null) hidden = new HashSet<string>(Settings.Get("hidden", "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()), StringComparer.OrdinalIgnoreCase);
                return hidden;
            }
        }
        public static bool Paused { get { return Settings.Get("paused", "0") == "1"; } }
        public static bool IsHidden(string id) { return Hidden.Contains(id); }
        public static string HiddenList { get { return string.Join(",", Hidden.OrderBy(x => x).ToArray()); } }

        public static bool AttentionShows(BeaconForm s) { return s.Attention && !Paused; }

        // should this beacon be on screen right now?
        public static bool ShouldShow(BeaconForm s)
        {
            if (Paused) return false;
            // 1.2.4: a pending attention call brings the beacon (or its tab) on screen even if it is hidden or a hidden
            // test beacon: the sound always played for hidden beacons, but there was nothing to see. It goes back once
            // the call is resolved (Open in chat / Dismiss). Pause still clears the desktop (tray badge only).
            if (AttentionShows(s)) return true;
            if (IsHidden(s.Def.Id)) return false;
            return !s.Def.Test || Program.ShowTest;
        }

        static void Save() { Settings.Set("hidden", HiddenList); Settings.Save(); }

        public static void SetHidden(BeaconForm s, bool hide)
        {
            bool changed = hide ? Hidden.Add(s.Def.Id) : Hidden.Remove(s.Def.Id);
            if (changed) { Save(); Log.Write(s.Def.Id + ": " + (hide ? "hidden by the user" : "shown again") + "."); }
            Apply();
        }

        public static void SetAll(bool hide)
        {
            foreach (var s in Program.Beacons) if (!s.Def.Test) { if (hide) Hidden.Add(s.Def.Id); else Hidden.Remove(s.Def.Id); }
            Save(); Log.Write(hide ? "All beacons hidden." : "All beacons shown.");
            if (!hide && Paused) SetPaused(false); else Apply();
        }

        public static void SetPaused(bool on)
        {
            Settings.Set("paused", on ? "1" : "0"); Settings.Save();
            Log.Write(on ? "Beacons paused (all beacons hidden)." : "Beacons resumed.");
            Apply();
        }

        // the user explicitly asked for this one beacon (menu "Beacons" list, tray click on news): show just it. If beacons
        // are paused, the pause becomes "every other beacon hidden", so nothing else pops up.
        public static void Reveal(BeaconForm s)
        {
            if (Paused)
            {
                foreach (var o in Program.Beacons) if (!o.Def.Test && o != s) Hidden.Add(o.Def.Id);
                Settings.Set("paused", "0");
            }
            Hidden.Remove(s.Def.Id); Save();
            Log.Write(s.Def.Id + ": shown again (opened by the user).");
            Apply();
        }

        // 1.2.4: tools/ClickHarness.cs.txt (headless) records what Apply would show/hide instead of touching windows
        public static Action<BeaconForm, bool> ApplyHook = null;

        public static void Apply()
        {
            if (ApplyHook != null) { foreach (var s in Program.Beacons) ApplyHook(s, ShouldShow(s) && !Skin.IsTab(s)); return; }
            foreach (var s in Program.Beacons)
            {
                bool show = ShouldShow(s) && !Skin.IsTab(s);   // 1.2.2: per beacon; a tab's floating window stays hidden (TabsSkin draws it)
                if (show && !s.Visible) s.ShowBeacon();
                else if (!show && s.Visible) s.HideBeacon();
            }
            TabsSkin.Sync();
            TrayBadge.Update();
        }
    }

    // The tray icon tells you about hidden beacons: an amber dot when a hidden beacon has unread messages, a pulsing
    // red dot with "!" when one needs attention, and the tooltip names them ("Bot Beacons: 2 unread (hidden)").
    // No balloons, no sounds, no windows: nothing that can take focus.
    static class TrayBadge
    {
        public static NotifyIcon Tray;
        static Icon plain, dot, attOn, attOff;
        static readonly Timer pulse = new Timer { Interval = 550 };
        static bool phase;
        public static string State = "none";   // none | unread | attention (for status.txt)
        static string lastTip; static Icon lastIcon;

        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);

        public static void Init(NotifyIcon tray, Icon baseIcon)
        {
            Tray = tray;
            plain = baseIcon;
            dot = Make(baseIcon, Color.FromArgb(255, 176, 32), false);
            attOn = Make(baseIcon, Color.FromArgb(235, 36, 30), true);
            attOff = Make(baseIcon, Color.FromArgb(120, 20, 18), true);
            pulse.Tick += (s, e) => { phase = !phase; Set(phase ? attOn : attOff); };
        }

        static Icon Make(Icon b, Color c, bool bang)
        {
            using (var bmp = new Bitmap(32, 32))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias; g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.Clear(Color.Transparent);
                    using (var big = new Icon(b, 32, 32)) g.DrawIcon(big, new Rectangle(0, 0, 32, 32));
                    var r = new RectangleF(15, 15, 17, 17);
                    using (var br = new SolidBrush(c)) g.FillEllipse(br, r);
                    using (var pen = new Pen(Color.White, 2f)) g.DrawEllipse(pen, r);
                    if (bang) using (var f = new Font("Segoe UI", 12f, FontStyle.Bold, GraphicsUnit.Pixel)) using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center }) g.DrawString("!", f, Brushes.White, new RectangleF(r.X, r.Y + 0.5f, r.Width, r.Height), sf);
                }
                IntPtr h = bmp.GetHicon();
                using (var tmp = Icon.FromHandle(h)) { var copy = (Icon)tmp.Clone(); DestroyIcon(h); return copy; }
            }
        }

        static void Set(Icon i) { if (Tray != null && i != null && !ReferenceEquals(i, lastIcon)) { Tray.Icon = i; lastIcon = i; } }

        // dev hook: save the badge variants (the app's own icons, not the screen)
        public static void SaveIcons(string dir)
        {
            var list = new[] { plain, dot, attOn, attOff }; var names = new[] { "plain", "unread", "attention_on", "attention_off" };
            for (int i = 0; i < list.Length; i++) if (list[i] != null) using (var b = list[i].ToBitmap()) b.Save(System.IO.Path.Combine(dir, "tray_" + names[i] + ".png"));
        }

        public static void Update()
        {
            if (Tray == null) return;
            var hiddenAtt = Program.Beacons.Where(s => !TabsSkin.OnScreen(s) && s.Attention && (!s.Def.Test || Program.ShowTest)).ToList();
            var hiddenUnread = Program.Beacons.Where(s => !TabsSkin.OnScreen(s) && s.Unread.Count > 0 && (!s.Def.Test || Program.ShowTest)).ToList();
            string tip;
            if (hiddenAtt.Count > 0)
            {
                State = "attention";
                tip = hiddenAtt[0].Def.Name + " needs attention (hidden)" + (hiddenAtt.Count > 1 ? " +" + (hiddenAtt.Count - 1) : "");
                if (!pulse.Enabled) { phase = true; Set(attOn); pulse.Start(); }
            }
            else
            {
                pulse.Stop();
                if (hiddenUnread.Count > 0)
                {
                    State = "unread"; Set(dot);
                    var s0 = hiddenUnread[0];
                    tip = s0.Def.Name + ": " + s0.Unread.Count + " unread (hidden)";
                    if (hiddenUnread.Count > 1) tip += ", +" + (hiddenUnread.Count - 1) + " more";
                }
                else { State = "none"; Set(plain); tip = Visibility.Paused ? "Bot Beacons - paused" : "Bot Beacons"; }
            }
            string relay = Program.RelayState;
            if (Program.PairingActive && tip.Length + 12 <= 63) tip += " - pairing\u2026";
            else if (relay != "connected" && tip.Length + relay.Length + 9 <= 63) tip += " - relay " + relay;
            if (tip.Length > 63) tip = tip.Substring(0, 62) + "\u2026";
            if (tip != lastTip) { Tray.Text = tip; lastTip = tip; }
        }
    }
}
