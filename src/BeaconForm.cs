using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace BotBeacons
{
    // One beacon: transparent, always-on-top, per-pixel-alpha layered window (UpdateLayeredWindow).
    class BeaconForm : Form
    {
        public BeaconDef Def { get; private set; }   // 1.2.7: replaced (with the art below) when the skin's theme changes
        Bitmap idle, alertArt;
        int cw, ch;   // the window (1.2.5: the art plus Pad on every side)
        int aw, ah;   // the art itself; everything art-relative (eye, lamp, layers, pivots) is drawn at (Pad, Pad)
        // 1.2.5: transparent room round the art so the "!" badge, its halo, the red aura, tilts and hops are never clipped by
        // the window (1.2.4 drew them inside a window exactly the art's size). Alpha-0 pixels pass clicks through.
        public const int Pad = 32;
        readonly FrameTimer timer = new FrameTimer { Interval = 40 };   // shared display-paced clock (FrameClock.cs)
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly Random rnd;
        readonly double phase, bobSpeed, swaySpeed;   // per-beacon animation personality

        float homeX, homeY, wanderX, wanderY, targetX, targetY; double nextWander;
        int winX, winY;
        bool leftDown, dragging; Point dragStart, winStart;
        // v0.10.1 catch/drag: the beacon is held at cursor + grab offset, in sub-pixels. posX/posY = where it was last drawn
        // (exact, before the 1/4-px quantization). caught = the press landed on a moving beacon (flinging or settling).
        float posX, posY, grabDX, grabDY, heldX, heldY; bool caught;
        public static bool CatchClassic;   // dev hook "flingcatch ... classic": replays the v0.10 catch for comparison
        public int ClickCount;             // click actions fired (ping / open bubble / close bubble)

        public readonly List<PetMessage> Unread = new List<PetMessage>();
        public readonly List<PetMessage> History = new List<PetMessage>();   // this session only, newest last
        bool alert; double alertStart;
        public bool Attention;            // a pending "needs attention" call: stronger pulse + "!" badge. Sticky: only the bubble's Open in chat or Dismiss link clears it
        public bool Alerting { get { return alert; } }   // 1.2.4: the glow/pulse is on (harness)
        public PetMessage AttentionMsg;   // the latest attention call (the bubble shows its reason; Open in chat / Dismiss end it)
        BubbleBase bubble;
        public BubbleBase OpenBubbleForm { get { return bubble != null && !bubble.IsDisposed && bubble.Visible ? bubble : null; } }
        bool bubbleOpenAtDown; double bubbleClosedAt = -10;
        public const double PingEverySeconds = 30;   // one ping per beacon per 30 s, however often it is clicked
        double lastPingAt = -1000, wiggleStart = -1000, wiggleAmp;

        public BeaconForm(BeaconDef def, int index, int count)
        {
            Def = def;
            var lk = BuildLook(def);
            idle = lk.Idle; alertArt = lk.Alert; hitPad = lk.Hit;
            aw = idle.Width; ah = idle.Height; cw = aw + 2 * Pad; ch = ah + 2 * Pad;
            Cursor = Cursors.Hand;   // over the beacon (its hit pixels); set as the form's Cursor, applied in WM_SETCURSOR
            int seed = 17; foreach (char c in def.Id) seed = seed * 31 + c;
            rnd = new Random(seed);
            phase = rnd.NextDouble() * Math.PI * 2; bobSpeed = 1.3 + rnd.NextDouble() * 0.6; swaySpeed = 0.7 + rnd.NextDouble() * 0.4;
            nextWander = rnd.NextDouble() * 4;

            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
            TopMost = true; Text = def.Name; Size = new Size(cw, ch);

            var wa = Screen.PrimaryScreen.WorkingArea;
            int defX = wa.Right - (index + 1) * (cw + 40) - 40, defY = wa.Bottom - ch - 110 - (index % 2) * 60;
            var pos = Settings.Get("pos." + def.Id, "").Split(',');
            int px, py;
            if (pos.Length == 2 && int.TryParse(pos[0], out px) && int.TryParse(pos[1], out py)) { homeX = px - Pad; homeY = py - Pad; } else { homeX = defX; homeY = defY; }   // saved = the art's top-left
            ClampHome();
            winX = (int)homeX; winY = (int)homeY; posX = winX; posY = winY; Location = new Point(winX, winY);
            timer.Tick += (s, e) => Tick();
        }

        // 1.2.7: a beacon's look (the idle and alert art and its hit mask), built before anything is swapped so a theme
        // change that fails half way leaves every beacon as it was
        public class Look { public BeaconDef Def; public Bitmap Idle, Alert; public int[] Hit; public void Dispose() { if (Alert != null && Alert != Idle) Alert.Dispose(); if (Idle != null) Idle.Dispose(); Idle = Alert = null; } }
        public static Look BuildLook(BeaconDef def)
        {
            var lk = new Look { Def = def };
            if (def.Layered) { lk.Idle = BeaconConfig.Compose(def, false); lk.Alert = BeaconConfig.Compose(def, true); }
            if (lk.Idle == null) lk.Idle = BeaconConfig.LoadArt(def.Art, def.ArtDir) ?? BeaconConfig.LoadArt("beacon.png");
            if (lk.Alert == null) lk.Alert = BeaconConfig.LoadArt(def.AlertArt, def.ArtDir) ?? lk.Idle;
            int w = lk.Idle.Width, h = lk.Idle.Height;
            using (var pi = PadArt(lk.Idle)) using (var pa = PadArt(lk.Alert))
                lk.Hit = HitPad.Build(new[] { pi, pa }, HitPad.Margin, Pad + w / 2f, Pad + h * 0.42f, HitPad.TiltDeg);   // 1.2.4: solid silhouette, any tilt, + margin
            return lk;
        }

        // 1.2.7: Skin > Theme: wear a new look in place. Position (the art's top-left stays put), unread messages, the
        // attention call, history, an open bubble and the tab all stay; only the art, colours, sounds and animations change.
        public void ApplyLook(Look lk)
        {
            var oldIdle = idle; var oldAlert = alertArt; var oldLayers = layersP;
            Def = lk.Def; idle = lk.Idle; alertArt = lk.Alert; hitPad = lk.Hit;
            int ncw = idle.Width + 2 * Pad, nch = idle.Height + 2 * Pad;
            aw = idle.Width; ah = idle.Height;
            if (ncw != cw || nch != ch) { FreeSurface(); cw = ncw; ch = nch; }
            if (idleP != null) { idleP.Dispose(); idleP = null; } if (alertP != null) { alertP.Dispose(); alertP = null; }
            for (int i = 0; i < alertCache.Length; i++) if (alertCache[i] != null) { alertCache[i].Dispose(); alertCache[i] = null; }
            fastSrc.Clear();
            foreach (var l in splitCache.Values) foreach (var kv in l) if (kv.Key.Length == 0) kv.Value.Dispose();
            splitCache.Clear(); layersP = null;
            if (oldLayers != null) foreach (var kv in oldLayers) kv.Value.Dispose();
            if (oldAlert != null && oldAlert != oldIdle) oldAlert.Dispose(); if (oldIdle != null) oldIdle.Dispose();
            content = Rectangle.Empty; play = null; nextIdle = -1; nextTwitch = -1; lastIdleIdx = -1;
            lastSway = 999; lastPulse = -1; lastX = int.MinValue;
            Text = Def.Name;
            if (menu != null && !menu.IsDisposed && !menu.Visible) { menu.Dispose(); menu = null; }   // names in it may have changed
            if (!dragging && !flinging) { ClampHome(); }
            if (Size.Width != cw || Size.Height != ch) try { Size = new Size(cw, ch); } catch { }
            if (IsHandleCreated && Visible) try { Render(0, 0); } catch (Exception ex) { Log.Write(Def.Id + ": redraw after the theme change failed: " + ex.Message); }
            if (bubble != null && !bubble.IsDisposed && bubble.Visible) try { bubble.Refresh2(); PlaceBubble(); } catch { }
        }
        public string LookTag { get { return Def.ArtDir.Length > 0 ? (Def.Look.Length > 0 ? "look:" + Def.Look : "skin") : "built-in"; } }   // harness / status
        public Size ArtSize { get { return new Size(aw, ah); } }

        void FreeSurface()
        {
            if (fg != null) { fg.Dispose(); fg = null; }
            if (frame != null) { frame.Dispose(); frame = null; }
            if (memDC != IntPtr.Zero) { try { Native.SelectObject(memDC, oldObj); Native.DeleteObject(dib); Native.DeleteDC(memDC); } catch { } memDC = IntPtr.Zero; }
            fastBuf = null;
        }

        // dev hook: capture exactly this beacon's on-screen pixels (and its bubble) plus the current sprite alpha
        public void Snap(string dir)
        {
            var r = new Rectangle(winX, winY, cw, ch);
            using (var b = new Bitmap(r.Width, r.Height)) { using (var g = Graphics.FromImage(b)) g.CopyFromScreen(r.Location, Point.Empty, r.Size); b.Save(System.IO.Path.Combine(dir, "snap_" + Def.Id + ".png")); }
            if (frame != null) using (var c = new Bitmap(frame)) c.Save(System.IO.Path.Combine(dir, "mask_" + Def.Id + ".png"));
            if (bubble != null && !bubble.IsDisposed && bubble.Visible) bubble.SaveFrame(System.IO.Path.Combine(dir, "snapbubble_" + Def.Id + ".png"));
            System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "snap.txt"), Def.Id + " " + r.X + "," + r.Y + "," + r.Width + "," + r.Height + " bubble=" + BubbleBounds + (bubble != null && !bubble.IsDisposed && bubble.Visible ? " style=" + bubble.Style : "") + " alert=" + alert + " attention=" + Attention + Environment.NewLine);
        }

        public string DrawnPos { get { return winX + "," + winY; } }
        public string BubbleBounds { get { return bubble != null && !bubble.IsDisposed && bubble.Visible ? bubble.ScreenBounds.X + "," + bubble.ScreenBounds.Y + "," + bubble.ScreenBounds.Width + "," + bubble.ScreenBounds.Height : "none"; } }

        // ------------------------------------------------------------ messages
        public void Notify(PetMessage m)
        {
            AddHistory(m);
            // 1.1.5: incoming only (own replies/pings never come through here; probes never reach Notify); plays for hidden/paused beacons too unless muted
            if (!m.FromUser && !m.Silent) Sound.Alert(Def, m.IsAttention);
            if (m.IsAttention)
            {
                // sticky: the badge and pulse stay on even while the bubble is open, until Open in chat or Dismiss
                AttentionMsg = m; Attention = true; alert = true; alertStart = clock.Elapsed.TotalSeconds;
                AttentionStore.Set(Def.Id, m);
                // 1.2.4: the call must be seen: a hidden beacon (hidden, a hidden test beacon) comes on screen for it
                if (!TabsSkin.OnScreen(this) && Visibility.AttentionShows(this))
                {
                    Log.Write(Def.Id + ": shown for an attention call (it was hidden" + (Def.Test ? ", test beacon" : "") + ").");
                    Visibility.Apply();
                }
            }
            if (bubble != null && !bubble.IsDisposed && bubble.Visible)
            {
                // never reopen or refocus an open bubble for an incoming call: the beacon's badge pulses, the history shows it,
                // and the card shows the attention panel next time it opens (the cloud shows it in place)
                if (!(m.IsAttention && bubble.ShowAttention(m))) { bubble.Refresh2(); PlaceBubble(); }
                Log.Write(Def.Id + ": " + (m.IsAttention ? "attention call" : "message") + " shown in the open bubble.");
                return;
            }
            Unread.Add(m);
            if (!m.IsAttention && !alert) { alert = true; alertStart = clock.Elapsed.TotalSeconds; }
            Log.Write(Def.Id + ": alert on" + (Attention ? " (needs attention)" : "") + ", " + Unread.Count + " unread" + (TabsSkin.OnScreen(this) ? (Skin.IsTab(this) ? " (tab)" : "") : " (beacon hidden)") + ".");
            TrayBadge.Update();
        }

        void AddHistory(PetMessage m) { History.Add(m); while (History.Count > 50) History.RemoveAt(0); }

        public string LastIncomingId { get { for (int i = History.Count - 1; i >= 0; i--) if (!History[i].FromUser) return History[i].Id; return ""; } }

        // 1.1.6: 0 if a reply with this text fits in one relay message, else about what % of it has to go
        public int ReplyOverPercent(string text) { return Relay.ReplyOverPercent(Def.Id, text ?? "", LastIncomingId); }
        public bool ReplyTooLong(string text) { return Relay.ReplyEnvelopeBytes(Def.Id, text ?? "", LastIncomingId) > Crypto.MaxEnvelopeBytes; }   // one seal: cheap enough per keystroke

        public void SendReplyText(string text, Action<bool, string> done = null)
        {
            var mine = new PetMessage { Id = "", Bot = Def.Id, Text = text, FromUser = true };
            AddHistory(mine);
            if (bubble != null && !bubble.IsDisposed) bubble.Refresh2();
            Relay.SendReply(Def.Id, text, LastIncomingId, (ok, err) => { if (!ok) mine.Title = "not sent: " + err; else if (!Wake.Covers(Def.Id)) Program.WakeNotSet(); if (done != null) done(ok, err); });
        }

        // opening the bubble reads the normal messages; a pending attention call stays (badge + pulse) until resolved
        void ClearAlert() { Unread.Clear(); if (!Attention) alert = false; }
        // 1.2.3: the menu's "Mark all read". Clears the unread glow; a sticky attention call stays (only Open in chat / Dismiss end it)
        public bool CanMarkRead { get { return Unread.Count > 0 || (alert && !Attention); } }
        public bool MarkRead() { if (!CanMarkRead) return false; ClearAlert(); Log.Write(Def.Id + ": marked read (menu)."); return true; }

        // the only ways an attention call ends: "opened app" (the Open in chat link) or "dismissed" (the Dismiss link)
        public void ResolveAttention(string how)
        {
            if (!Attention && AttentionMsg == null) return;
            Attention = false; AttentionMsg = null; alert = false; Unread.Clear();
            AttentionStore.Set(Def.Id, null);
            Log.Write(Def.Id + ": attention cleared (" + how + ").");
            // 1.2.4: a beacon that was only on screen for the call goes back to hidden (after the click that resolved it)
            if (!Visibility.ShouldShow(this) && TabsSkin.OnScreen(this)) { if (IsHandleCreated) BeginInvoke((Action)Visibility.Apply); else Visibility.Apply(); }
        }

        // v0.10 bubble footer links, the same in every bubble (both styles, messages, ping replies, replies, attention).
        // "Open in chat": the bot's chat in the Grok Bot app (same deep link as Open in app), marks everything read and
        // ends a sticky attention call. "Dismiss": marks everything read and ends the attention call, no app. These two
        // are the only things that clear an attention call; closing the bubble any other way leaves it pulsing.
        public string OpenChatFromBubble(PetMessage att)
        {
            PetMessage m = att ?? AttentionMsg;
            if (m == null) for (int i = History.Count - 1; i >= 0; i--) if (!History[i].FromUser) { m = History[i]; break; }
            string how = OpenInApp(m);
            if (Attention || AttentionMsg != null) ResolveAttention("opened app"); else ClearAlert();
            TrayBadge.Update();
            Log.Write(Def.Id + ": Open in chat (" + how + ").");
            return how;
        }
        public void DismissFromBubble()
        {
            if (Attention || AttentionMsg != null) ResolveAttention("dismissed"); else ClearAlert();
            TrayBadge.Update();
            Log.Write(Def.Id + ": dismissed from the bubble.");
        }

        // start-up: put back an attention call saved in attention.txt that was never opened in the app or dismissed
        public void RestoreAttention(PetMessage m)
        {
            AddHistory(m); AttentionMsg = m; Attention = true; alert = true; alertStart = clock.Elapsed.TotalSeconds;
            Log.Write(Def.Id + ": attention restored from attention.txt (" + m.Time.ToString("HH:mm") + ").");
        }

        // Clicking a beacon that isn't glowing pings it: a short wiggle as feedback, and (at most once per 30 s) a ping
        // up the relay plus a wake, like a reply. The owning bot answers on the beacon, which then glows as usual.
        bool midDown; Point midAt; double midMax;   // 1.2.6: middle-button press (ping)
        ToolTip hint;   // 1.2.6: "Click: open, Middle-click: ping" after a pause, not while the bubble is open
        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            if (sim) return;
            bool open = bubble != null && !bubble.IsDisposed && bubble.Visible;
            if (open) { if (hint != null) hint.SetToolTip(this, null); return; }
            if (hint == null) hint = new ToolTip { InitialDelay = 1200, ReshowDelay = 400, AutoPopDelay = 5000, ShowAlways = true };   // the beacon never takes activation
            hint.SetToolTip(this, ClickRules.HoverHint(Def.Name));
        }
        public void PingClick()
        {
            double t = clock.Elapsed.TotalSeconds;
            wiggleStart = t;
            if (t - lastPingAt < PingEverySeconds)
            {
                wiggleAmp = 4;   // small wiggle: "already pinged"
                Log.Write(Def.Id + ": ping skipped (last one " + (int)(t - lastPingAt) + " s ago, limit one per " + (int)PingEverySeconds + " s).");
                return;
            }
            wiggleAmp = 11; lastPingAt = t;
            SendPing((ok, err) => { if (!ok) Log.Write(Def.Id + ": ping not sent (" + err + ")."); });
        }

        // dev hook "click": the same decision as a real left click (close an open bubble / open a glowing beacon / ping)
        public void SimulateClick()
        {
            if (bubble != null && !bubble.IsDisposed && bubble.Visible) { bubble.CloseReason = "beacon clicked"; bubble.Close(); Log.Write(Def.Id + ": click closed the bubble."); }
            else if (alert || Unread.Count > 0) OpenBubble();
            else PingClick();
        }

        void SendPing(Action<bool, string> done = null)
        {
            var mine = new PetMessage { Id = "", Bot = Def.Id, Text = "Ping", Kind = "ping", FromUser = true };
            AddHistory(mine);
            if (bubble != null && !bubble.IsDisposed) bubble.Refresh2();
            Relay.SendPing(Def.Id, (ok, err) => { if (!ok) mine.Title = "not sent: " + err; else if (!Wake.Covers(Def.Id)) Program.WakeNotSet(); if (done != null) done(ok, err); });
        }

        // Open this beacon's bot chat in the Grok Bot app (grokbot:// agent deep link), else just bring the app forward.
        public string OpenInApp(PetMessage m)
        {
            string agent = m != null && m.AgentId.Length > 0 ? m.AgentId : (m == null || string.Equals(m.Bot, Def.Id, StringComparison.OrdinalIgnoreCase) ? Def.AgentId : "");
            return AppLink.Open(agent);
        }

        // display layout changed (e.g. monitors waking after sign-in): go back to the saved spot, fitted to the screens now present
        public void RefitToScreens()
        {
            var pos = Settings.Get("pos." + Def.Id, "").Split(','); int px, py;
            if (!dragging && !flinging && pos.Length == 2 && int.TryParse(pos[0], out px) && int.TryParse(pos[1], out py)) { homeX = px - Pad; homeY = py - Pad; }
            ClampHome();
        }

        // hide/show keep all state: unread messages, glow, sticky attention. A bubble open at hide time reopens on show
        // (without focus).
        bool bubbleWasOpen;
        public void HideBeacon()
        {
            bubbleWasOpen = bubble != null && !bubble.IsDisposed && bubble.Visible;
            if (bubbleWasOpen) { bubble.CloseReason = "beacon hidden"; bubble.Close(); }
            Hide();
        }
        public void ShowBeacon()
        {
            if (!Visibility.ShouldShow(this) || Skin.IsTab(this)) return;
            lastSway = 999; lastX = int.MinValue;   // force a full redraw (the glow may have changed while hidden)
            Show();
            if (bubbleWasOpen) { bubbleWasOpen = false; OpenBubble(false); }
        }
        // hidden beacons never appear by accident (start-up, "Show test beacons", the main form at Application.Run)
        protected override void SetVisibleCore(bool value)
        {
            if (value && (!Visibility.ShouldShow(this) || Skin.IsTab(this))) { if (!IsHandleCreated) CreateHandle(); value = false; }
            base.SetVisibleCore(value);
        }

        // takeFocus: true only for the user's own beacon/tray/menu click; dev hooks open without touching focus
        public void OpenBubble(bool takeFocus = false)
        {
            if (bubble != null && !bubble.IsDisposed && bubble.Visible) { bubble.CloseReason = "toggled"; bubble.Close(); return; }
            if (!TabsSkin.OnScreen(this))
            {
                // a hidden beacon opens only on the user's own request, and then comes back on screen first
                if (!takeFocus || (Def.Test && !Program.ShowTest && !Attention)) { Log.Write(Def.Id + ": not opened (beacon hidden)."); TabAnchor = Rectangle.Empty; return; }
                Visibility.Reveal(this);
                if (!TabsSkin.OnScreen(this)) return;
            }
            // 1.2.0 Tabs skin: the bubble points at the shelf row or tab it was opened from (else this beacon's tab)
            bubbleAnchor = Skin.IsTab(this) ? (TabAnchor.IsEmpty ? TabsSkin.AnchorFor(this) : TabAnchor) : Rectangle.Empty;
            TabAnchor = Rectangle.Empty;
            bool att = Attention;
            ClearAlert();
            bubble = Theme.CreateBubble(this, att ? AttentionMsg : null);
            PlaceBubble();
            var bb = bubble; bool logged = false;
            bb.FormClosed += (s, e) => { if (logged) return; logged = true; bubbleClosedAt = clock.Elapsed.TotalSeconds; Log.Write(Def.Id + ": bubble closed (" + bb.CloseReason + ")."); };
            bubble.ShowAndFocus(takeFocus);
            Log.Write(Def.Id + ": bubble opened (" + bubble.Style + ").");
        }

        public void PlaceBubble()
        {
            if (bubble == null || bubble.IsDisposed) return;
            bubble.Place(Skin.IsTab(this) && !bubbleAnchor.IsEmpty ? bubbleAnchor : ArtRect);
        }
        public Rectangle TabAnchor;   // set by the Tabs skin just before OpenBubble
        // 1.2.7: a handoff spark landed here: one soft pulse (floating beacon); the centre of the art on screen
        double handoffAt = -10;
        public void HandoffPulse() { handoffAt = clock.Elapsed.TotalSeconds; if (Visible) timer.Interval = 40; }
        public Point ArtCenter { get { var r = ArtRect; return new Point(r.X + r.Width / 2, r.Y + (int)(r.Height * 0.42f)); } }
        Rectangle bubbleAnchor;

        // ------------------------------------------------------------ animation
        // Motion is cheap by design: bob/wander only move the window (SetWindowPos, no redraw); the sprite is redrawn
        // only when its rotation changes by 0.2 deg, a pulse level changes, or an action/aura is playing. In "off" mode
        // with nothing going on the timer drops to 4 ticks a second; hidden beacons stop their timer; a locked session pauses.
        double lastT;
        AnimPlay play; double nextIdle = -1, nextTwitch = -1; int lastIdleIdx = -1;
        public int IdleCount, TwitchCount, FlingCount;
        public int TimerInterval { get { return timer.Enabled ? timer.Interval : 0; } }
        public string Playing { get { return play != null ? play.A.Name : ""; } }
        bool flinging; float fx, fy, fvx, fvy; int bounces; double restAt = -100;
        // motion envelope (v0.10): bob, sway and wander fade out while dragged or flung and fade back in afterwards,
        // continuously, so nothing restarts with a jump when the beacon comes to rest
        float motionU = 1f, curBob;
        float motionK { get { return motionU * motionU * (3 - 2 * motionU); } }
        // dev hook "flingtrace": a dry-run fling (no window move, no drawing) that logs every frame; Classic = the v0.9 stop
        public static bool FlingClassic; bool sim; System.Text.StringBuilder simLog; double simEnd; string simPath; object[] simSave;
        readonly List<KeyValuePair<double, Point>> dragTrail = new List<KeyValuePair<double, Point>>();
        Rectangle content = Rectangle.Empty;   // the opaque part of the sprite (sprite coordinates)

        float Rand(float a, float b) { return a + (float)rnd.NextDouble() * (b - a); }
        void ScheduleIdle(double t, float first = 1f) { var s = Def.Anim; nextIdle = t + Rand(s.IdleMin, s.IdleMax) * (AnimSettings.Mode == AnimMode.Subtle ? 2f : 1f) * first; }
        void ScheduleTwitch(double t, float first = 1f) { var s = Def.Anim; nextTwitch = t + Rand(s.TwitchMin, s.TwitchMax) * first; }
        void Start(AnimAction a, double t, float k) { play = new AnimPlay(a, t, k, rnd.Next()); lastSway = 999; }

        // dev hook / menu: play a named idle action or twitch now ("idle" / "twitch" = a random one)
        public string PlayAction(string name)
        {
            var s = Def.Anim; AnimAction a = null; name = (name ?? "").Trim();
            if (name == "idle" && s.Idle.Count > 0) a = s.Idle[rnd.Next(s.Idle.Count)];
            else if (name == "twitch" && s.Twitches.Count > 0) a = s.Twitches[rnd.Next(s.Twitches.Count)];
            else foreach (var x in s.Idle.Concat(s.Twitches)) if (string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)) { a = x; break; }
            if (a == null) return "no action '" + name + "' (have: " + string.Join(", ", s.Idle.Concat(s.Twitches).Select(x => x.Name).ToArray()) + ")";
            Start(a, clock.Elapsed.TotalSeconds, AnimSettings.Mode == AnimMode.Subtle ? 0.6f : 1f);
            timer.Interval = 40;
            return "playing '" + a.Name + "' (" + a.Dur + " s)";
        }

        // dev hook: throw the beacon as if released mid-drag with this velocity (px/s)
        public void Fling(float vx, float vy)
        {
            // v0.10.1: start from the exact spot the beacon is drawn at (v0.10 used the whole-pixel window position)
            if (CatchClassic) { fx = winX; fy = winY; } else { fx = posX; fy = posY; }
            fvx = vx; fvy = vy; bounces = 0; flinging = true; play = null; timer.Interval = 40;
        }

        void Tick()
        {
            if ((!Visible && !sim) || IsDisposed) return;
            double t = clock.Elapsed.TotalSeconds; float dt = (float)Math.Max(0, Math.Min(0.1, t - lastT)); lastT = t;
            if (Program.SessionLocked && !sim) { timer.Interval = 500; return; }   // locked: nothing to see
            if (sim && catchCases != null) { CatchScript(t); if (!sim) return; }   // dev hook flingcatch: simulated mouse, before this frame; stop if the run just ended
            var mode = AnimSettings.Mode; bool full = mode == AnimMode.Full, off = mode == AnimMode.Off;
            float k = full ? 1f : mode == AnimMode.Subtle ? 0.6f : 0f;
            var spec = Def.Anim;
            if (nextIdle < 0) { ScheduleIdle(t, Rand(0.3f, 1f)); ScheduleTwitch(t, Rand(0.3f, 1f)); }

            // idle actions and twitches: not during alerts, drags or flings (their schedule just moves on)
            if (play != null && play.Done(t)) { play = null; lastSway = 999; }
            if (alert || dragging || flinging || off || sim) { if (t >= nextIdle) ScheduleIdle(t); if (t >= nextTwitch) ScheduleTwitch(t); }
            else if (play == null)
            {
                if (t >= nextIdle)
                {
                    if (spec.Idle.Count > 0) { int i = rnd.Next(spec.Idle.Count); if (i == lastIdleIdx && spec.Idle.Count > 1) i = (i + 1) % spec.Idle.Count; lastIdleIdx = i; Start(spec.Idle[i], t, k); IdleCount++; }
                    ScheduleIdle(t); if (nextTwitch < t + 4) nextTwitch = t + 4;
                }
                else if (full && t >= nextTwitch)
                {
                    if (spec.Twitches.Count > 0) { Start(spec.Twitches[rnd.Next(spec.Twitches.Count)], t, k); TwitchCount++; }
                    ScheduleTwitch(t);
                }
            }

            if (!dragging && !flinging && full)
            {
                if (t > nextWander)
                {
                    targetX = (float)(rnd.NextDouble() * 2 - 1) * 60; targetY = (float)(rnd.NextDouble() * 2 - 1) * 26;
                    nextWander = t + 7 + rnd.NextDouble() * 8;
                }
                float wk = 1f - (float)Math.Exp(-0.15 * dt);   // time-based (was 0.006 per 40 ms tick), same speed at any fps
                if (!FlingClassic) wk *= motionK;   // wander eases back in after a drag/fling instead of resuming at full speed
                wanderX += (targetX - wanderX) * wk; wanderY += (targetY - wanderY) * wk;
            }
            // after a drag or fling the bob fades back in, so the beacon doesn't jump when it lands
            float ramp;
            if (FlingClassic) { ramp = (float)Math.Max(0, Math.Min(1, (t - restAt) / 1.2)); ramp = ramp * ramp * (3 - 2 * ramp); }
            else
            {
                // fades out over 0.4 s while held or flung, back in over 1.2 s; never reset, so sway can't jump
                motionU = (dragging || flinging) ? Math.Max(0f, motionU - dt / 0.4f) : Math.Min(1f, motionU + dt / 1.2f);
                ramp = motionU * motionU * (3 - 2 * motionU);
            }
            float bobK = (full ? 1f : mode == AnimMode.Subtle ? 0.5f : 0f) * ramp;
            float bob = (float)(Math.Sin(t * bobSpeed + phase) * 6 + Math.Sin(t * 0.7 + phase * 0.5) * 2) * bobK;
            curBob = bob;
            float sway = full ? (float)(Math.Sin(t * swaySpeed + phase) * 3.0) * ramp : 0f;
            float shakeX = 0, shakeY = 0, pulse = 0, aura = 0, hop = 0;
            float mk = off ? 0f : full ? 1f : 0.6f;   // alert motion scale
            if (alert && Attention)
            {
                // "needs attention": a red heartbeat. Double-beat glow that never fully dims, a red aura behind the beacon,
                // and a forward nod on each beat. Different from a normal message on purpose.
                double at = t - alertStart, cyc = at % 1.1;
                double beat = Math.Exp(-Math.Pow((cyc - 0.08) / 0.07, 2)) + 0.8 * Math.Exp(-Math.Pow((cyc - 0.34) / 0.07, 2));
                pulse = (float)Math.Min(1.0, 0.35 + 0.65 * beat);
                aura = (float)Math.Min(1.0, 0.18 + beat);
                sway -= (float)(4.5 * beat) * mk; shakeY -= (float)(3 * beat) * mk;
            }
            else if (alert)
            {
                // a normal message: the beacon's own colour pulses, and it does two eager hops every 2 s
                double at = t - alertStart;
                pulse = (float)(0.55 + 0.45 * Math.Sin(at * 5.0));
                double cyc = at % 2.0;
                if (cyc < 0.7)
                {
                    hop = (float)(Math.Abs(Math.Sin(cyc / 0.35 * Math.PI)) * (cyc < 0.35 ? 10 : 6)) * mk;
                    sway += (float)(Math.Sin(cyc / 0.7 * 2 * Math.PI) * 4) * mk;
                }
            }
            double wt = t - wiggleStart;
            if (wt >= 0 && wt < 0.7)
            {
                // ping feedback: a quick side-to-side wiggle with a small hop, fading out over 0.7 s
                double kk = 1 - wt / 0.7;
                sway += (float)(Math.Sin(wt * 38) * wiggleAmp * kk);
                shakeY -= (float)(Math.Sin(Math.PI * wt / 0.7) * wiggleAmp * 0.5);
            }
            if (play != null) { sway += play.Tilt(t); hop += play.Hop(t); }
            // 1.2.7: a handoff spark arriving: one soft pulse
            double ht = t - handoffAt;
            if (ht >= 0 && ht < 0.7) pulse = Math.Max(pulse, (float)(0.6 * Math.Sin(Math.PI * ht / 0.7)));

            // v0.10 sub-pixel motion: the window sits on the whole pixel and the sprite is drawn at the fractional rest
            // (bilinear), so a slow bob glides instead of stepping 1 px every few frames. Legacy clock: whole pixels.
            int x, y; float ex = 0, ey = 0, fracX = 0, fracY = 0; bool smooth = FrameClock.Smooth;
            if (dragging && !CatchClassic)
            {
                // held: exactly cursor + grab offset (sub-pixel), no bob/wander/clamp while in the hand
                ex = heldX; ey = heldY;
                if (smooth) { x = (int)Math.Floor(ex); y = (int)Math.Floor(ey); fracX = ex - x; fracY = ey - y; } else { x = (int)Math.Round(ex); y = (int)Math.Round(ey); }
                ex = float.NaN;
            }
            else if (dragging) { x = winX; y = winY; }
            else
            {
                if (flinging) StepFling(dt, t);
                if (flinging) { ex = fx; ey = fy; } else { ex = homeX + wanderX + shakeX; ey = homeY + wanderY + bob + shakeY - hop; }
                if (smooth) { x = (int)Math.Floor(ex); y = (int)Math.Floor(ey); } else { x = (int)Math.Round(ex); y = (int)Math.Round(ey); }
                int x0 = x, y0 = y;
                if (!flinging) KeepOnScreen(ref x, ref y);
                if (smooth) { if (x == x0) fracX = ex - x; if (y == y0) fracY = ey - y; }
                if (sim) { simEx = ex; simEy = ey; }
                if (x != x0 || y != y0 || flinging) ex = float.NaN;   // clamped at a screen edge or flinging: not measured
            }
            posX = x + fracX; posY = y + fracY;
            if (sim) { winX = x; winY = y; if (catchCases != null) CatchFrame(t, sway); else SimFrame(t, x, y, fracX, fracY, ex, ey, sway); return; }
            winX = x; winY = y;
            if (OnAnyScreen()) Render(sway, pulse, aura, t, fracX, fracY);
            // dev hook "frametimes": how far the drawn position (whole pixel + drawn fraction) is from the exact one
            if (!dragging && !float.IsNaN(ex)) FrameClock.ReportPos(Math.Sqrt(Math.Pow(x + lastFX - ex, 2) + Math.Pow(y + lastFY - ey, 2)));
            bool busy = !off || alert || dragging || flinging || play != null || wt < 0.8 || dumpLeft > 0 || ramp < 1 || motionU < 1 || ht < 0.8;
            int iv = busy ? 40 : 250; if (timer.Interval != iv) timer.Interval = iv;
        }

        // ---------------------------------------------------------------- fling
        void StepFling(float dt, double t)
        {
            fx += fvx * dt; fy += fvy * dt;
            float damp = (float)Math.Exp(-2.4 * dt); fvx *= damp; fvy *= damp;
            var c = ContentAt(fx, fy);
            var s = Screen.FromPoint(new Point(c.X + c.Width / 2, c.Y + c.Height / 2)).WorkingArea;
            int cy = c.Y + c.Height / 2, cx = c.X + c.Width / 2;
            // bounce on an edge unless another monitor continues there; lose 40% of the speed per bounce
            // 1.2.3 throw to dock: hitting an outer edge docks the beacon there as a tab instead of bouncing (setting, default on)
            if (c.Left < s.Left && fvx < 0 && !ScreenAt(c.Left - 2, cy)) { if (ThrowDock(TabEdge.Left, new Point(s.Left, cy))) return; fx += s.Left - c.Left; fvx = -fvx * 0.6f; bounces++; }
            if (c.Right > s.Right && fvx > 0 && !ScreenAt(c.Right + 2, cy)) { if (ThrowDock(TabEdge.Right, new Point(s.Right - 1, cy))) return; fx -= c.Right - s.Right; fvx = -fvx * 0.6f; bounces++; }
            if (c.Top < s.Top && fvy < 0 && !ScreenAt(cx, c.Top - 2)) { if (ThrowDock(TabEdge.Top, new Point(cx, s.Top))) return; fy += s.Top - c.Top; fvy = -fvy * 0.6f; bounces++; }
            if (c.Bottom > s.Bottom && fvy > 0 && !ScreenAt(cx, c.Bottom + 2)) { if (ThrowDock(TabEdge.Bottom, new Point(cx, s.Bottom - 1))) return; fy -= c.Bottom - s.Bottom; fvy = -fvy * 0.6f; bounces++; }
            // v0.10: plain exponential decay all the way down, stopping only below 0.05 px per frame (3 px/s at 60 fps),
            // and the sub-pixel position is kept (v0.9 stopped at 30 px/s = 0.5 px/frame and rounded to a whole pixel)
            float fdt = Math.Max(dt, 1f / 60f), sp2 = fvx * fvx + fvy * fvy;
            if (FlingClassic ? sp2 < 30 * 30 : sp2 * fdt * fdt < 0.05f * 0.05f)
            {
                flinging = false; fvx = fvy = 0;
                if (FlingClassic) { int x = (int)Math.Round(fx), y = (int)Math.Round(fy); KeepOnScreen(ref x, ref y); fx = x; fy = y; winX = x; winY = y; }
                else { int x = (int)Math.Floor(fx), y = (int)Math.Floor(fy), x0 = x, y0 = y; KeepOnScreen(ref x, ref y); fx += x - x0; fy += y - y0; winX = x; winY = y; }
                Rest(t, fx, fy);
                if (Settings.DevHooks && !sim) Log.Write(Def.Id + ": fling came to rest at " + winX + "," + winY + " (" + bounces + " bounce" + (bounces == 1 ? "" : "s") + ").");
            }
        }

        bool ThrowDock(TabEdge e, Point impact)
        {
            if (sim || !TabsSkin.ThrowToDock || !TabsSkin.FlingDock(this, e, impact)) return false;
            flinging = false; fvx = fvy = 0;
            // its floating spot stays where it was before it was picked up (Layout > All floating puts it back there)
            homeX = dragHomeX; homeY = dragHomeY; ClampHome(); SavePos();
            return true;
        }

        float simEx, simEy;
        public void FlingTrace(float vx, float vy, string path)
        {
            if (sim || dragging || flinging) return;
            simSave = SaveSim();
            simLog = new System.Text.StringBuilder("# t exactX exactY drawnX drawnY swayDeg flinging (classic=" + FlingClassic + ", " + FrameClock.FpsSetting + " fps)\r\n");
            simPath = path; simEnd = double.MaxValue; sim = true; lastT = clock.Elapsed.TotalSeconds;
            Fling(vx, vy); timer.Enabled = true;
        }
        void SimFrame(double t, int x, int y, float fracX, float fracY, float ex, float ey, float sway)
        {
            float q = FrameClock.Smooth ? FrameClock.SubSteps : 1f;
            float dx = x + (float)Math.Floor(fracX * q) / q, dy = y + (float)Math.Floor(fracY * q) / q;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            simLog.AppendLine(string.Format(ci, "{0:0.0000} {1:0.0000} {2:0.0000} {3:0.000} {4:0.000} {5:0.0000} {6}", t, simEx, simEy, dx, dy, sway, flinging ? 1 : 0));
            if (!flinging && simEnd == double.MaxValue) simEnd = t + 2.5;
            if (t < simEnd) return;
            sim = false;
            RestoreSim(simSave);
            timer.Enabled = Visible;
            try { System.IO.File.WriteAllText(simPath, simLog.ToString()); } catch (Exception ex2) { Log.Write("flingtrace: " + ex2.Message); }
            Log.Write(Def.Id + ": flingtrace done (classic=" + FlingClassic + ").");
            FlingClassic = false;   // only ever for the trace
        }

        // the drag or fling is over: this spot becomes home, the bob fades back in, the position is saved
        void Rest(double t) { Rest(t, winX, winY); }
        void Rest(double t, float px, float py)
        {
            // the bob still showing (if the envelope hasn't fully faded) is folded into home, so the position carries on
            homeX = px - wanderX; homeY = py - wanderY - (FlingClassic ? 0f : curBob);
            ClampHome(); if (!sim) SavePos(); restAt = t;
            if (bubble != null && !bubble.IsDisposed && bubble.Visible) PlaceBubble();
        }

        static bool ScreenAt(int x, int y) { foreach (var s in Screen.AllScreens) if (s.WorkingArea.Contains(x, y)) return true; return false; }
        bool OnAnyScreen() { var r = new Rectangle(winX, winY, cw, ch); foreach (var s in Screen.AllScreens) if (s.Bounds.IntersectsWith(r)) return true; return false; }
        Rectangle ContentAt(float x, float y) { var c = Content; return new Rectangle((int)x + c.X, (int)y + c.Y, c.Width, c.Height); }

        Rectangle Content
        {
            get
            {
                if (!content.IsEmpty) return content;
                int x0 = aw, y0 = ah, x1 = 0, y1 = 0;
                var data = idle.LockBits(new Rectangle(0, 0, aw, ah), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                var px = new byte[data.Stride * ah]; Marshal.Copy(data.Scan0, px, 0, px.Length); idle.UnlockBits(data);
                for (int yy = 0; yy < ah; yy++) for (int xx = 0; xx < aw; xx++) if (px[yy * data.Stride + xx * 4 + 3] > 24) { if (xx < x0) x0 = xx; if (xx > x1) x1 = xx; if (yy < y0) y0 = yy; if (yy > y1) y1 = yy; }
                content = x1 > x0 ? new Rectangle(x0, y0, x1 - x0 + 1, y1 - y0 + 1) : new Rectangle(0, 0, aw, ah);
                content.Offset(Pad, Pad);   // window coordinates
                return content;
            }
        }
        Rectangle ArtContent { get { var c = Content; c.Offset(-Pad, -Pad); return c; } }
        Rectangle ArtRect { get { return new Rectangle(winX + Pad, winY + Pad, aw, ah); } }   // the art on screen (bubbles anchor to it)
        static Bitmap PadArt(Bitmap a)
        {
            var b = new Bitmap(a.Width + 2 * Pad, a.Height + 2 * Pad, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(b)) { g.Clear(Color.Transparent); g.DrawImage(a, Pad, Pad, a.Width, a.Height); }
            return b;
        }
        // 1.2.5: the "!" badge with its halo, and the red attention aura (window coordinates). Kept inside the window (Pad) and,
        // with the art, inside the monitor's work area (KeepOnScreen), so an alert never draws off screen.
        public const float BadgeR = 13.5f, BadgeHalo = 4f;
        public RectangleF BadgeBox { get { float cx = Pad + aw - 20f, cy = Pad + 22f, r = BadgeR + BadgeHalo + 1f; return new RectangleF(cx - r, cy - r, 2 * r, 2 * r); } }
        public RectangleF AuraBox { get { var c = Content; float cx = c.X + c.Width / 2f, cy = c.Y + c.Height * 0.42f, r = Math.Min(aw, ah) * 0.5f; return new RectangleF(cx - r, cy - r, 2 * r, 2 * r); } }
        public Rectangle VisibleBox { get { return Rectangle.Union(Content, Rectangle.Round(RectangleF.Union(BadgeBox, AuraBox))); } }
        public Size WindowSize { get { return new Size(cw, ch); } }

        // keep the visible beacon (not its transparent margin) inside the screen it is on
        void KeepOnScreen(ref int x, ref int y)
        {
            var c = Content; var s = Screen.FromPoint(new Point(x + c.X + c.Width / 2, y + c.Y + c.Height / 2)).WorkingArea;
            c = VisibleBox;   // 1.2.5: the badge and aura too, not just the art
            x = Math.Max(s.Left - c.Left, Math.Min(x, s.Right - c.Right));
            y = Math.Max(s.Top - c.Top, Math.Min(y, s.Bottom - c.Bottom));
        }

        void ClampHome()
        {
            int x = (int)Math.Floor(homeX), y = (int)Math.Floor(homeY), x0 = x, y0 = y;
            var c = Content;
            if (!ScreenAt(x + c.X + c.Width / 2, y + c.Y + c.Height / 2)) { var wa = Screen.PrimaryScreen.WorkingArea; x = wa.Right - cw - 80; y = wa.Bottom - ch - 120; }
            KeepOnScreen(ref x, ref y);
            // keep the sub-pixel part unless the home had to move (off screen / past an edge)
            if (x != x0) homeX = x; if (y != y0) homeY = y;
        }

        // Rendering: one 32-bit premultiplied DIB per beacon, created once; GDI+ draws straight into it and
        // UpdateLayeredWindow pushes it. No per-frame allocations, so five beacons stay cheap.
        Bitmap frame; Graphics fg; IntPtr memDC, dib, oldObj, frameBits; int[] fastBuf;
        readonly System.Collections.Generic.Dictionary<Bitmap, FastBlit.Src> fastSrc = new System.Collections.Generic.Dictionary<Bitmap, FastBlit.Src>();
        FastBlit.Src FastSrc(Bitmap b) { FastBlit.Src v; if (!fastSrc.TryGetValue(b, out v)) { v = FastBlit.From(b, ArtContent); fastSrc[b] = v; } return v; }
        static Bitmap ToPArgb(Bitmap src)
        {
            var b = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(b)) { g.Clear(Color.Transparent); g.DrawImage(src, 0, 0, src.Width, src.Height); }
            return b;
        }
        Bitmap idleP, alertP;
        const int Levels = 16;
        readonly Bitmap[] alertCache = new Bitmap[Levels + 1];
        // Alert frames are pre-blended at 16 pulse levels (art + coloured eye/lamp glow), built lazily once.
        Bitmap AlertLevel(float pulse)
        {
            int k = Math.Max(1, Math.Min(Levels, (int)Math.Round(pulse * Levels)));
            if (alertCache[k] != null) return alertCache[k];
            float p = k / (float)Levels;
            var b = new Bitmap(aw, ah, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(b))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Color.Transparent);
                g.DrawImage(idleP, 0, 0, aw, ah);
                var cm = new ColorMatrix { Matrix33 = Math.Min(1f, 0.35f + p) };
                using (var ia = new ImageAttributes()) { ia.SetColorMatrix(cm); g.DrawImage(alertP, new Rectangle(0, 0, aw, ah), 0, 0, aw, ah, GraphicsUnit.Pixel, ia); }
                Glow(g, Def.Eye, 13f + 7f * p, (int)(120 + 110 * p), Def.Color);
                Glow(g, Def.Lamp, 9f + 5f * p, (int)(80 + 90 * p), Def.Color);
            }
            alertCache[k] = b; return b;
        }

        // Split stacks for animating single overlays: for each set of moving layers, the static layers between them
        // are pre-merged once, so an action draws at most a few bitmaps per frame.
        List<KeyValuePair<string, Bitmap>> layersP;
        readonly Dictionary<string, List<KeyValuePair<string, Bitmap>>> splitCache = new Dictionary<string, List<KeyValuePair<string, Bitmap>>>();
        List<KeyValuePair<string, Bitmap>> Split(AnimPlay p)
        {
            if (!Def.Layered) return null;
            if (layersP == null) { layersP = new List<KeyValuePair<string, Bitmap>>(); foreach (var kv in BeaconConfig.IdleLayers(Def)) { layersP.Add(new KeyValuePair<string, Bitmap>(kv.Key, ToPArgb(kv.Value))); kv.Value.Dispose(); } }
            var moving = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var n in p.Layers()) foreach (var kv in layersP) if (kv.Key.Length > 0 && string.Equals(kv.Key, n, StringComparison.OrdinalIgnoreCase)) moving.Add(kv.Key);
            if (moving.Count == 0) return null;
            var key = string.Join("|", moving.OrderBy(s => s).ToArray());
            List<KeyValuePair<string, Bitmap>> list;
            if (splitCache.TryGetValue(key, out list)) return list;
            list = new List<KeyValuePair<string, Bitmap>>(); Bitmap run = null; Graphics rg = null;
            foreach (var kv in layersP)
            {
                if (moving.Contains(kv.Key))
                {
                    if (run != null) { rg.Dispose(); list.Add(new KeyValuePair<string, Bitmap>("", run)); run = null; }
                    list.Add(kv);
                }
                else
                {
                    if (run == null) { run = new Bitmap(aw, ah, PixelFormat.Format32bppPArgb); rg = Graphics.FromImage(run); rg.Clear(Color.Transparent); }
                    rg.DrawImage(kv.Value, 0, 0, aw, ah);
                }
            }
            if (run != null) { rg.Dispose(); list.Add(new KeyValuePair<string, Bitmap>("", run)); }
            splitCache[key] = list; return list;
        }

        float lastSway = 999, lastPulse = -1, lastFX, lastFY; bool lastDynamic; int lastX = int.MinValue, lastY;
        readonly Matrix lm = new Matrix();

        void EnsureSurface()
        {
            if (frame != null) { if (idleP == null) { idleP = ToPArgb(idle); alertP = ToPArgb(alertArt); } return; }   // 1.2.7: a same-size theme change keeps the surface but drops the sprites
            idleP = ToPArgb(idle); alertP = ToPArgb(alertArt);
            var bi = new Native.BITMAPINFOHEADER { biSize = 40, biWidth = cw, biHeight = -ch, biPlanes = 1, biBitCount = 32 };
            IntPtr bits;
            memDC = Native.CreateCompatibleDC(IntPtr.Zero);
            dib = Native.CreateDIBSection(memDC, ref bi, 0, out bits, IntPtr.Zero, 0);
            oldObj = Native.SelectObject(memDC, dib);
            frame = new Bitmap(cw, ch, cw * 4, PixelFormat.Format32bppPArgb, bits); frameBits = bits; fastBuf = new int[cw * ch];
            fg = Graphics.FromImage(frame);
            fg.InterpolationMode = InterpolationMode.Bilinear; fg.SmoothingMode = SmoothingMode.AntiAlias; fg.PixelOffsetMode = PixelOffsetMode.Half;
            fg.CompositingQuality = CompositingQuality.HighSpeed;
        }

        void Render(float swayDeg, float pulse, float aura = 0, double t = 0, float fracX = 0, float fracY = 0)
        {
            if (IsDisposed) return;
            EnsureSurface();
            bool att = Attention && alert;
            bool dynamic = play != null || (att && aura > 0.2f) || dumpLeft > 0;
            // legacy: redraw after 0.2 deg of sway; smooth: after 0.02 deg or 1/32 px of sub-pixel motion
            bool smooth = FrameClock.Smooth;
            // the sub-pixel offset is snapped to FrameClock.SubStep (default 1/4 px) and the sway to SwayStep, so the
            // sprite is redrawn only when that changes what you'd see
            if (smooth) { float q = FrameClock.SubSteps; fracX = (float)Math.Floor(fracX * q) / q; fracY = (float)Math.Floor(fracY * q) / q; }
            bool same = !dynamic && !lastDynamic && Math.Abs(swayDeg - lastSway) < (smooth ? FrameClock.SwayStep : 0.2f) && Math.Abs(pulse - lastPulse) < 0.5f / Levels
                        && fracX == lastFX && fracY == lastFY;
            if (!same && !att && play == null && FrameClock.FastCopy)
            {
                // fast path (idle and plain glow): one cached sprite, rotated + sub-pixel offset, no GDI+
                long c0 = FrameClock.Ticks;
                FastBlit.Draw(FastSrc(pulse > 0 ? AlertLevel(pulse) : idleP), fastBuf, cw, ch, aw / 2f, ah * 0.42f, swayDeg, fracX + Pad, fracY + Pad);
                HitPad.Apply(hitPad, fastBuf);
                Marshal.Copy(fastBuf, 0, frameBits, fastBuf.Length);
                DumpFrame(t);   // the frames hook saves exactly what the fast path shows
                lastSway = swayDeg; lastPulse = pulse; lastDynamic = dynamic; lastFX = fracX; lastFY = fracY;
                lastX = winX; lastY = winY;
                var dst = new Native.POINT { x = winX, y = winY }; var sz = new Native.SIZE { cx = cw, cy = ch }; var s0 = new Native.POINT();
                var bf = new Native.BLENDFUNCTION { BlendOp = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
                long c1 = FrameClock.Ticks;
                Native.UpdateLayeredWindow(Handle, IntPtr.Zero, ref dst, ref sz, memDC, ref s0, 0, ref bf, 2);
                FrameClock.ReportRender(c1 - c0, FrameClock.Ticks - c1);
            }
            else if (!same)
            {
                var g = fg;
                long c0 = FrameClock.Ticks;
                PaintFrame(g, swayDeg, pulse, aura, t, fracX, fracY, att);
                g.Flush(FlushIntention.Sync);
                HitPad.Apply(hitPad, frameBits);
                lastSway = swayDeg; lastPulse = pulse; lastDynamic = dynamic; lastFX = fracX; lastFY = fracY;
                DumpFrame(t);
                lastX = winX; lastY = winY;
                var dst = new Native.POINT { x = winX, y = winY }; var sz = new Native.SIZE { cx = cw, cy = ch }; var s0 = new Native.POINT();
                var bf = new Native.BLENDFUNCTION { BlendOp = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
                long c1 = FrameClock.Ticks;
                Native.UpdateLayeredWindow(Handle, IntPtr.Zero, ref dst, ref sz, memDC, ref s0, 0, ref bf, 2);
                FrameClock.ReportRender(c1 - c0, FrameClock.Ticks - c1);
            }
            else if (winX != lastX || winY != lastY)
            {
                lastX = winX; lastY = winY;   // position only: move the window, keep its pixels
                Native.SetWindowPos(Handle, IntPtr.Zero, winX, winY, 0, 0, 0x1 | 0x4 | 0x10);   // NOSIZE | NOZORDER | NOACTIVATE
            }
        }

        // 1.2.5: the full (GDI+) drawing of one frame into a cw x ch surface: red aura, art (tilted about its pivot), layer
        // animation, glows and the "!" badge. Split out of Render so the harness can paint a real frame without a window.
        public void PaintFrame(Graphics g, float swayDeg, float pulse, float aura, double t, float fracX, float fracY, bool att)
        {
            if (idleP == null) { idleP = ToPArgb(idle); alertP = ToPArgb(alertArt); }
            g.ResetTransform(); g.Clear(Color.Transparent);
            g.TranslateTransform(fracX, fracY);
            if (att) AnimPlay.Glow(g, new PointF(Content.X + Content.Width / 2f, Content.Y + Content.Height * 0.42f), Math.Min(aw, ah) * 0.5f * (0.82f + 0.18f * aura), (int)(40 + 120 * aura), Color.FromArgb(235, 30, 24));   // red heartbeat aura
            g.TranslateTransform(Pad, Pad);   // art coordinates from here
            g.TranslateTransform(aw / 2f, ah * 0.42f); g.RotateTransform(swayDeg); g.TranslateTransform(-aw / 2f, -ah * 0.42f);
            var split = pulse <= 0 && play != null ? Split(play) : null;
            if (pulse > 0) g.DrawImage(AlertLevel(pulse), 0, 0, aw, ah);
            else if (split != null)
            {
                foreach (var kv in split)
                {
                    float a = 1f; lm.Reset();
                    if (kv.Key.Length == 0 || !play.LayerXform(kv.Key, t, lm, out a)) { g.DrawImage(kv.Value, 0, 0, aw, ah); continue; }
                    var st = g.Save(); g.MultiplyTransform(lm);
                    if (a >= 0.99f) g.DrawImage(kv.Value, 0, 0, aw, ah);
                    else using (var ia = new ImageAttributes()) { ia.SetColorMatrix(new ColorMatrix { Matrix33 = Math.Max(0f, a) }); g.DrawImage(kv.Value, new Rectangle(0, 0, aw, ah), 0, 0, aw, ah, GraphicsUnit.Pixel, ia); }
                    g.Restore(st);
                }
            }
            else g.DrawImage(idleP, 0, 0, aw, ah);
            if (play != null) play.Effects(g, t, Def, Def.Anim);
            if (att)
            {
                Glow(g, Def.Eye, 18f + 14f * pulse, (int)(90 + 150 * pulse), Color.White);   // white flare over the coloured glow
                g.ResetTransform(); g.TranslateTransform(fracX + Pad, fracY + Pad);
                DrawBadge(g, pulse);
            }
        }

        // dev hook "frames": save the next n drawn sprite frames (the app's own pixels, never the screen)
        int dumpLeft, dumpIndex; double dumpEvery, nextDump; string dumpDir;
        public void StartFrames(int n, int everyMs, string dir)
        {
            dumpDir = dir; dumpLeft = Math.Max(1, Math.Min(120, n)); dumpEvery = Math.Max(40, everyMs) / 1000.0; nextDump = clock.Elapsed.TotalSeconds; dumpIndex = 0;
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "frames_" + Def.Id + ".txt"), "");
            timer.Interval = 40;
        }
        void DumpFrame(double t)
        {
            if (dumpLeft <= 0 || t < nextDump) return;
            try
            {
                using (var c = new Bitmap(frame)) c.Save(System.IO.Path.Combine(dumpDir, "frames_" + Def.Id + "_" + dumpIndex.ToString("00") + ".png"));
                System.IO.File.AppendAllText(System.IO.Path.Combine(dumpDir, "frames_" + Def.Id + ".txt"), dumpIndex.ToString("00") + " " + winX + " " + winY + " " + (int)((t - nextDump) * 1000) + " " + Playing + Environment.NewLine);
            }
            catch (Exception ex) { Log.Write("frames: " + ex.Message); dumpLeft = 1; }
            dumpIndex++; dumpLeft--; nextDump += dumpEvery;
            if (dumpLeft == 0) Log.Write(Def.Id + ": saved " + dumpIndex + " frames.");
        }

        // "!" badge in the top-right corner of the sprite (outside the beacon art), drawn unrotated
        void DrawBadge(Graphics g, float pulse)
        {
            float r = BadgeR - 1.5f + 1.5f * pulse, cx = aw - 20f, cy = 22f;   // art coordinates (BadgeBox)
            using (var halo = new SolidBrush(Color.FromArgb((int)(60 + 90 * pulse), 255, 60, 40))) g.FillEllipse(halo, cx - r - 4, cy - r - 4, 2 * r + 8, 2 * r + 8);
            using (var b = new SolidBrush(Color.FromArgb(255, 230, 40, 30))) g.FillEllipse(b, cx - r, cy - r, 2 * r, 2 * r);
            using (var pen = new Pen(Color.White, 2f)) g.DrawEllipse(pen, cx - r, cy - r, 2 * r, 2 * r);
            using (var f = new Font("Segoe UI", 13f, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString("!", f, Brushes.White, new RectangleF(cx - r, cy - r + 0.5f, 2 * r, 2 * r), sf);
        }

        static void Glow(Graphics g, PointF c, float r, int alpha, Color col)
        {
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(c.X - r, c.Y - r, r * 2, r * 2);
                using (var br = new PathGradientBrush(path))
                {
                    br.CenterColor = Color.FromArgb(Math.Min(255, alpha), col.R, col.G, col.B);
                    br.SurroundColors = new[] { Color.FromArgb(0, col.R, col.G, col.B) };
                    g.FillPath(br, path);
                }
            }
        }

        ContextMenuStrip menu;   // 1.1.3: this beacon's right-click menu, reused
        // 1.2.0: the same reusable menu, for the Tabs skin (a tab or shelf row shows it, owned by that window)
        public ContextMenuStrip MenuForTabs { get { if (menu == null || menu.IsDisposed) menu = Program.BuildMenu(this); return menu; } }
        public ContextMenuStrip MenuIfBuilt { get { return menu != null && !menu.IsDisposed ? menu : null; } }

        protected override void Dispose(bool disposing)
        {
            if (disposing && menu != null) { menu.Dispose(); menu = null; }
            if (disposing && hint != null) { hint.Dispose(); hint = null; }
            for (int i = 0; i < alertCache.Length; i++) if (alertCache[i] != null) { alertCache[i].Dispose(); alertCache[i] = null; }
            if (fg != null) { fg.Dispose(); fg = null; }
            if (frame != null) { frame.Dispose(); frame = null; }
            if (memDC != IntPtr.Zero) { Native.SelectObject(memDC, oldObj); Native.DeleteObject(dib); Native.DeleteDC(memDC); memDC = IntPtr.Zero; }
            base.Dispose(disposing);
        }

        // ------------------------------------------------------------ window plumbing
        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= 0x80000 | 0x80 | 0x8 | 0x8000000; return cp; }   // layered | toolwindow | topmost | noactivate
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        // Clicking a beacon must not activate it: activation deactivates (and so closes) an open bubble, and Windows
        // could swallow the button-down as the "activation click", leaving only the button-up, which reopened it.
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x21) { m.Result = (IntPtr)3; return; }   // WM_MOUSEACTIVATE -> MA_NOACTIVATE
            base.WndProc(ref m);
        }
        protected override void OnPaint(PaintEventArgs e) { }
        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnLoad(EventArgs e) { base.OnLoad(e); Render(0, 0); timer.Enabled = Visible; }
        // hidden beacons (e.g. test beacons switched off) stop their timer entirely
        protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); timer.Enabled = Visible && IsHandleCreated; }
        protected override void OnFormClosed(FormClosedEventArgs e) { SavePos(); base.OnFormClosed(e); }

        float dragHomeX, dragHomeY;
        int[] hitPad; double downAt;
        // 1.2.4: press bookkeeping for ClickRules (screen px from the press point; see ClickLogic.cs)
        double pressMaxDist; bool caughtFling; double lastOpenClickAt = -10; int clickSlop = -1;
        int ClickSlop
        {
            get
            {
                if (clickSlop < 0)
                {
                    float scale = 1f;
                    try { using (var g = CreateGraphics()) scale = g.DpiX / 96f; } catch { }
                    clickSlop = ClickRules.Slop(SystemInformation.DragSize, scale);
                }
                return clickSlop;
            }
        }
        // 1.2.2: a tab dragged off its edge floats again here (window position, already fitted to the monitor)
        public void PlaceAt(Point at)
        {
            if (dragging) return;
            flinging = false; fvx = fvy = 0;
            homeX = at.X; homeY = at.Y; ClampHome(); SavePos();
            posX = homeX; posY = homeY; winX = (int)homeX; winY = (int)homeY; lastX = int.MinValue; lastSway = 999;
            if (IsHandleCreated) Native.SetWindowPos(Handle, IntPtr.Zero, winX, winY, cw, ch, 0x4 | 0x10);
        }
        void SavePos() { Settings.Set("pos." + Def.Id, ((int)homeX + Pad) + "," + ((int)homeY + Pad)); Settings.Save(); }   // 1.2.5: the art's top-left, as before the pad

        // ---------------------------------------------------------------- mouse: click, drag, catch (v0.10.1)
        // Threading: the frame clock thread never touches beacon state. It only BeginInvokes the tick onto the UI thread
        // (FrameClock.Dispatch), so Tick and these mouse handlers always run one at a time on the same thread; a catch
        // can't land half-way through a frame and no locking is needed.
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Middle && !sim)   // 1.2.6: middle-click pings; never a drag, never the bubble
            {
                midDown = !leftDown; midAt = Cursor.Position; midMax = 0;
                if (midDown) Capture = true;
                return;
            }
            if (e.Button != MouseButtons.Left || sim) return;
            midDown = false;   // a left press cancels a middle press in progress
            // Was the bubble open when this click started? Clicking the beacon can deactivate the bubble (it closes as
            // "clicked away") just before the click lands; without this the click would reopen it at once.
            // 1.2.4: only a bubble that is open right now. The beacon never takes activation (MA_NOACTIVATE), so clicking it
            // can't close the bubble first; the old "closed in the last 0.4 s" rule ate the click after any close.
            bubbleOpenAtDown = bubble != null && !bubble.IsDisposed && bubble.Visible;
            if (Settings.DevHooks) Log.Write(Def.Id + ": mouse down, bubble " + (bubbleOpenAtDown ? "open" : "closed") + " (" + (int)((clock.Elapsed.TotalSeconds - bubbleClosedAt) * 1000) + " ms since close)" + (flinging ? ", caught mid-fling" : motionU < 1f ? ", caught while settling" : "") + ".");
            Capture = true;
            PointerDown(Cursor.Position, clock.Elapsed.TotalSeconds);
            if (ClickLog.On)
            {
                var cp = PointToClient(Cursor.Position);
                ClickLog.Write(Def.Id + " down at " + Cursor.Position.X + "," + Cursor.Position.Y + " (window " + cp.X + "," + cp.Y + ", clicks " + e.Clicks + "), bubble " + (bubbleOpenAtDown ? "open" : "closed")
                    + (caughtFling ? ", caught mid-fling" : caught ? ", held while settling" : "") + ", slop " + ClickSlop + " px.");
            }
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (midDown) { var c = Cursor.Position; double dd = Math.Sqrt((double)(c.X - midAt.X) * (c.X - midAt.X) + (double)(c.Y - midAt.Y) * (c.Y - midAt.Y)); if (dd > midMax) midMax = dd; }
            if (!sim) PointerMove(Cursor.Position, clock.Elapsed.TotalSeconds);
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (sim) return;
            if (ClickRules.ForButton(e.Button) == PressKind.Ping)
            {
                bool ping = ClickRules.MiddlePing(midDown, leftDown, midMax, ClickSlop);
                midDown = false; if (!leftDown) Capture = false;
                ClickLog.Write(Def.Id + " middle up, moved " + midMax.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " px: " + (ping ? "ping" : "ignored") + ".");
                if (ping) PingClick();
                return;
            }
            if (e.Button == MouseButtons.Left)
            {
                if (!leftDown)
                {
                    // a button-up without our button-down (the press started on another window): never a click
                    Capture = false;
                    if (Settings.DevHooks) Log.Write(Def.Id + ": mouse up without down (ignored).");
                    ClickLog.Write(Def.Id + " up without down: ignored.");
                    return;
                }
                double held = clock.Elapsed.TotalSeconds - downAt, dist = pressMaxDist;
                var verdict = PointerVerdict(clock.Elapsed.TotalSeconds);
                Capture = false;
                ClickLog.Write(Def.Id + " up after " + (int)(held * 1000) + " ms, moved " + dist.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " px: " + verdict + ".");
                if (verdict == ClickVerdict.Click) ClickAction();
            }
            else if (e.Button == MouseButtons.Right)
            {
                // 1.1.1: shown with this beacon as the source control, so the menu is owned by the beacon's tool window
                // (Show(Point) left it unowned, which put a button on the taskbar).
                // 1.1.3: one menu per beacon, built on first use and reused; it is disposed only with the beacon
                if (menu == null || menu.IsDisposed) menu = Program.BuildMenu(this);
                if (!menu.Visible)
                {
                    // 1.1.4: open towards the side with room on this monitor (right/down by default)
                    var at = Cursor.Position;
                    var dir = Placement.Menu(at, menu.GetPreferredSize(Size.Empty), Screen.FromPoint(at).WorkingArea);
                    menu.Show(this, PointToClient(at), dir);
                }
            }
        }
        // the mouse was taken away mid-hold (another window grabbed it, Alt+Tab, ...): let go where it is, no click.
        // Checked after the queued messages, so a normal button-up (which also ends the capture) is never cut short.
        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (!leftDown || sim || !IsHandleCreated) return;
            BeginInvoke((Action)(() =>
            {
                if (!leftDown || Capture || (Control.MouseButtons & MouseButtons.Left) != 0) return;
                if (Settings.DevHooks) Log.Write(Def.Id + ": mouse capture lost while held; letting go without a click.");
                PointerUp(clock.Elapsed.TotalSeconds);
            }));
        }

        void PointerDown(Point c, double t)
        {
            leftDown = true; dragStart = c; dragTrail.Clear(); dragHomeX = homeX; dragHomeY = homeY; downAt = t; pressMaxDist = 0; caughtFling = false;
            if (CatchClassic)
            {
                // v0.10: the fling stopped but nothing held the beacon until the cursor moved 4 px, so the next frame drew it
                // at its old home; release without moving counted as a click
                dragging = false; winStart = new Point(winX, winY);
                if (flinging) { flinging = false; winStart = new Point(winX, winY); }
                return;
            }
            // grab offset from the exact (sub-pixel) spot the beacon is drawn at right now, so it stays under the cursor
            grabDX = posX - c.X; grabDY = posY - c.Y; heldX = posX; heldY = posY;
            caught = flinging || motionU < 1f;
            caughtFling = flinging;   // 1.2.4: only stopping a slide is "not a click"; a press while it settles still clicks
            if (caught)
            {
                // a moving beacon: stop the fling and hold it at once (no 4 px dead zone, which would make it lag then jump).
                // The motion envelope keeps fading out from where it is, so bob/sway/wander don't snap.
                flinging = false; fvx = fvy = 0; bounces = 0;
                dragging = true; dragTrail.Add(new KeyValuePair<double, Point>(t, c));
                if (timer.Interval != 40) timer.Interval = 40;
            }
            else dragging = false;
        }
        void PointerMove(Point c, double t)
        {
            if (!leftDown) return;
            double dd = Math.Sqrt((double)(c.X - dragStart.X) * (c.X - dragStart.X) + (double)(c.Y - dragStart.Y) * (c.Y - dragStart.Y));
            if (dd > pressMaxDist) pressMaxDist = dd;
            // 1.2.4: Euclidean, screen px, the larger of Windows' drag size and 7 px x dpi (was > 4 px on either axis)
            if (!dragging && ClickRules.PastSlop(dragStart, c, sim ? 4 : ClickSlop)) dragging = true;
            if (!dragging) return;
            if (CatchClassic) { winX = winStart.X + c.X - dragStart.X; winY = winStart.Y + c.Y - dragStart.Y; }
            else
            {
                heldX = grabDX + c.X; heldY = grabDY + c.Y;
                if (FrameClock.Smooth) { winX = (int)Math.Floor(heldX); winY = (int)Math.Floor(heldY); } else { winX = (int)Math.Round(heldX); winY = (int)Math.Round(heldY); }
            }
            lastX = winX; lastY = winY; if (!caught) play = null;
            // the drawn fraction can't change during a drag (the cursor moves in whole pixels), so moving the window is enough
            if (!sim) Native.SetWindowPos(Handle, IntPtr.Zero, winX, winY, cw, ch, 0x4 | 0x10);
            // 1.1.4: an open bubble follows the dragged beacon and flips sides at screen edges (a move, not a redraw)
            if (!sim && bubble != null && !bubble.IsDisposed && bubble.Visible) bubble.Follow(ArtRect);
            if (!sim) TabsSkin.FloatDragMove(this, c);   // 1.2.2: near an outer screen edge: show where it would dock as a tab
            dragTrail.Add(new KeyValuePair<double, Point>(t, c));
            while (dragTrail.Count > 0 && t - dragTrail[0].Key > 0.1) dragTrail.RemoveAt(0);
        }
        // returns true when the press was a click (not a drag, not a catch)
        bool PointerUp(double now) { return PointerVerdict(now) == ClickVerdict.Click; }
        ClickVerdict PointerVerdict(double now)
        {
            if (!leftDown) { dragging = false; caught = false; return ClickVerdict.NoPress; }
            bool wasCaught = caught; caught = false; leftDown = false;
            // 1.2.4: the cursor never got past the slop: a click (or a long still press), wherever the window went meanwhile
            if (!dragging || (wasCaught && !caughtFling && pressMaxDist <= (sim ? 4 : ClickSlop)))
            {
                var v = ClickRules.Classify(now - downAt, pressMaxDist, caughtFling, sim ? 4 : ClickSlop);
                if (dragging)
                {
                    // held while settling (the hold froze it in place): settle where it is held, then judge the press
                    dragging = false; dragTrail.Clear();
                    if (!CatchClassic) { posX = heldX; posY = heldY; Rest(now, heldX, heldY); } else Rest(now);
                }
                return v;
            }
            dragging = false;
            // 1.2.2: let go within ~24 px of an outer screen edge: this beacon becomes a tab there (no fling). Its floating
            // spot goes back to where it was before the drag, so Layout > All floating puts it back there
            if (!sim && TabsSkin.FloatDragEnd(this, Cursor.Position))
            {
                dragTrail.Clear(); flinging = false; fvx = fvy = 0;
                homeX = dragHomeX; homeY = dragHomeY; ClampHome(); SavePos();
                return ClickVerdict.Drag;
            }
            // released while moving: keep going with the hand's speed (last ~100 ms), then slide to rest
            float vx = 0, vy = 0;
            while (dragTrail.Count > 0 && now - dragTrail[0].Key > 0.1) dragTrail.RemoveAt(0);
            if (dragTrail.Count >= 2)
            {
                var a = dragTrail[0]; var b = dragTrail[dragTrail.Count - 1]; double span = Math.Max(0.016, now - a.Key);
                vx = (float)((b.Value.X - a.Value.X) / span); vy = (float)((b.Value.Y - a.Value.Y) / span);
            }
            dragTrail.Clear();
            if (!CatchClassic) { posX = heldX; posY = heldY; }
            float sp = (float)Math.Sqrt(vx * vx + vy * vy);
            if (sp > 300 && AnimSettings.Mode != AnimMode.Off)
            {
                if (sp > 4000) { vx *= 4000 / sp; vy *= 4000 / sp; }
                Fling(vx, vy); FlingCount++;
                if (Settings.DevHooks && !sim) Log.Write(Def.Id + ": flung at " + (int)vx + "," + (int)vy + " px/s.");
            }
            else if (CatchClassic) Rest(now);
            else Rest(now, heldX, heldY);
            return ClickVerdict.Drag;
        }
        // what a click does: close an open bubble / open a glowing beacon / ping. Never during a dry run.
        void ClickAction()
        {
            ClickCount++;
            if (sim) return;
            double now = clock.Elapsed.TotalSeconds;
            var act = ClickRules.Act(bubbleOpenAtDown, downAt - lastOpenClickAt, SystemInformation.DoubleClickTime / 1000.0);
            ClickLog.Write(Def.Id + " click: " + act + ".");
            if (act == ClickAct.KeepOpen) { Log.Write(Def.Id + ": second click of a double-click; the bubble stays open."); return; }
            if (act == ClickAct.Close)
            {
                if (bubble != null && !bubble.IsDisposed && bubble.Visible) { bubble.CloseReason = "beacon clicked"; bubble.Close(); }
                Log.Write(Def.Id + ": click closed the bubble.");
            }
            // 1.2.3: a click anywhere on the beacon opens its conversation (Ping is on the right-click menu)
            else { lastOpenClickAt = now; if (bubble == null || bubble.IsDisposed || !bubble.Visible) OpenBubble(true); }
        }

        // ---------------------------------------------------------------- dev hook "flingcatch" (v0.10.1)
        // Dry runs like flingtrace (nothing moves on screen, nothing is drawn, nothing is saved): each case flings the
        // beacon, a simulated mouse catches it (mid-fling, after a bounce, or while it settles), holds, and lets go.
        // Logs every frame and, per case, the position jump at each catch/release, the hold error, the largest step
        // and sway step once it is resting again, and whether any click action fired.
        class CatchCase
        {
            public string Name; public float Vx, Vy; public int When; public double At, Hold; public float MoveVx, MoveVy; public int Wobble;
            public double Again = -1, AgainHold;   // a second catch this long after the first release (double catch)
        }
        static CatchCase[] CatchCases()
        {
            return new[] {
                new CatchCase { Name = "early-hold", Vx = -600, Vy = -150, When = 0, At = 0.10, Hold = 0.40 },
                new CatchCase { Name = "late-wobble", Vx = -600, Vy = -150, When = 0, At = 1.00, Hold = 0.25, Wobble = 2 },
                new CatchCase { Name = "quick-tap", Vx = -600, Vy = -150, When = 0, At = 0.50, Hold = 0.03 },
                new CatchCase { Name = "rethrow-double", Vx = -600, Vy = -150, When = 0, At = 0.30, Hold = 0.30, MoveVx = 900, MoveVy = 300, Again = 0.20, AgainHold = 0.25 },
                new CatchCase { Name = "settle-double", Vx = -600, Vy = -150, When = 2, At = 0.30, Hold = 0.30, Again = 0.20, AgainHold = 0.10 },
                new CatchCase { Name = "bounce", Vx = -4000, Vy = -300, When = 1, At = 1, Hold = 0.20 },
            };
        }
        CatchCase[] catchCases; int caseIdx, catchPhase, caseFrames, caughtFrames; double caseStart, phaseAt, restSeenAt; Point simCursor, simCursor0;
        bool evCatch, evRelease; float prevPX, prevPY, prevSway; bool havePrev; int bouncesSeen; string pendingEv;
        float mCatchJump, mReleaseJump, mHoldErr, mRestStep, mSway; int mCatches, mReleases, caseClicks0;
        System.Text.StringBuilder catchSummary; float worstCatchJump; int totalClicks;

        public void FlingCatch(string path)
        {
            if (sim || dragging || flinging || leftDown) return;
            simSave = SaveSim(); simPath = path; catchCases = CatchCases(); caseIdx = -1;
            simLog = new System.Text.StringBuilder("# flingcatch (classic=" + CatchClassic + ", " + FrameClock.FpsSetting + " fps): case t posX posY swayDeg state event\r\n");
            catchSummary = new System.Text.StringBuilder(); worstCatchJump = 0; totalClicks = 0;
            sim = true; lastT = clock.Elapsed.TotalSeconds; NextCase(lastT); timer.Enabled = true;
        }
        object[] SaveSim() { return new object[] { homeX, homeY, wanderX, wanderY, targetX, targetY, nextWander, restAt, motionU, winX, winY, nextIdle, nextTwitch, posX, posY, ClickCount }; }
        void RestoreSim(object[] v)
        {
            homeX = (float)v[0]; homeY = (float)v[1]; wanderX = (float)v[2]; wanderY = (float)v[3]; targetX = (float)v[4]; targetY = (float)v[5];
            nextWander = (double)v[6]; restAt = (double)v[7]; motionU = (float)v[8]; winX = (int)v[9]; winY = (int)v[10]; nextIdle = (double)v[11]; nextTwitch = (double)v[12];
            posX = (float)v[13]; posY = (float)v[14]; ClickCount = (int)v[15];
            flinging = false; dragging = false; leftDown = false; caught = false; fvx = fvy = 0; dragTrail.Clear();
        }
        void NextCase(double t)
        {
            caseIdx++;
            RestoreSim(simSave);
            if (caseIdx >= catchCases.Length) { EndCatch(); return; }
            var c = catchCases[caseIdx];
            caseStart = t; catchPhase = 0; restSeenAt = -1; bouncesSeen = 0; havePrev = false; caseFrames = 0; caughtFrames = 0;
            mCatchJump = mReleaseJump = mHoldErr = mRestStep = mSway = 0; mCatches = mReleases = 0; caseClicks0 = ClickCount;
            Fling(c.Vx, c.Vy);
        }
        // phases: 0 waiting to catch, 1 held, 2 released (waiting for a second catch or the rest), 3 resting tail
        void CatchScript(double t)
        {
            if (caseIdx < 0 || caseIdx >= catchCases.Length) return;
            var c = catchCases[caseIdx]; double ct = t - caseStart;
            if (!flinging && !dragging && restSeenAt < 0 && catchPhase != 1) restSeenAt = t;
            if (catchPhase == 0)
            {
                bool go = c.When == 0 ? ct >= c.At : c.When == 1 ? bounces > 0 && ++bouncesSeen > (int)c.At : restSeenAt >= 0 && t - restSeenAt >= c.At;
                if (c.When == 1 && bounces == 0 && ct > 1.5) go = true;   // no edge nearby: catch anyway
                if (c.When == 2 && ct > 8) go = true;
                if (go) Catch(t, c.Hold);
            }
            else if (catchPhase == 1)
            {
                double th = t - phaseAt, hold = mCatches > 1 ? c.AgainHold : c.Hold;
                var p = new Point(simCursor0.X + (int)Math.Round((mCatches > 1 ? 0 : c.MoveVx) * th), simCursor0.Y + (int)Math.Round((mCatches > 1 ? 0 : c.MoveVy) * th));
                if (c.Wobble != 0 && th > 0.05 && th < 0.15) p.X += c.Wobble;   // barely moves (under the 4 px drag threshold)
                if (p != simCursor) { simCursor = p; PointerMove(p, t); }
                if (th >= hold)
                {
                    if (PointerUp(t)) ClickAction();
                    evRelease = true; mReleases++; restSeenAt = -1;
                    catchPhase = (c.Again >= 0 && mCatches == 1) ? 2 : 3; phaseAt = t; pendingEv = "release" + (flinging ? "+fling" : "");
                }
            }
            else if (catchPhase == 2)
            {
                if (t - phaseAt >= c.Again) Catch(t, c.AgainHold);
            }
            else if (catchPhase == 3)
            {
                if (!flinging && restSeenAt >= 0 && t - restSeenAt > 1.5) { FinishCase(); NextCase(t); }
                else if (t - caseStart > 15) { pendingEv = "timeout"; FinishCase(); NextCase(t); }
            }
        }
        void Catch(double t, double hold)
        {
            simCursor0 = simCursor = new Point((int)Math.Floor(posX) + 70, (int)Math.Floor(posY) + 90);
            pendingEv = "catch" + (flinging ? "(flinging" + (bounces > 0 ? ", after bounce" : "") + ")" : "(settling u=" + motionU.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + ")");
            PointerDown(simCursor, t);
            evCatch = true; mCatches++; catchPhase = 1; phaseAt = t;
        }
        void CatchFrame(double t, float sway)
        {
            if (caseIdx < 0 || caseIdx >= catchCases.Length) return;
            var ci = System.Globalization.CultureInfo.InvariantCulture; caseFrames++;
            float d = havePrev ? (float)Math.Sqrt((posX - prevPX) * (posX - prevPX) + (posY - prevPY) * (posY - prevPY)) : 0f;
            if (havePrev && Math.Abs(sway - prevSway) > mSway) mSway = Math.Abs(sway - prevSway);
            if (evCatch && havePrev && d > mCatchJump) mCatchJump = d;
            if (evRelease && havePrev && !flinging && d > mReleaseJump) mReleaseJump = d;
            if (dragging && !CatchClassic) { caughtFrames++; float he = (float)Math.Sqrt(Math.Pow(posX - (simCursor.X + grabDX), 2) + Math.Pow(posY - (simCursor.Y + grabDY), 2)); if (he > mHoldErr) mHoldErr = he; }
            if (catchPhase == 3 && !flinging && !dragging && !evRelease && havePrev && d > mRestStep) mRestStep = d;
            string state = dragging ? "held" : flinging ? "fling" : "rest";
            simLog.AppendLine(string.Format(ci, "{0} {1:0.0000} {2:0.0000} {3:0.0000} {4:0.0000} {5} {6}", catchCases[caseIdx].Name, t - caseStart, posX, posY, sway, state, pendingEv ?? "-"));
            pendingEv = null; evCatch = evRelease = false; prevPX = posX; prevPY = posY; prevSway = sway; havePrev = true;
        }
        void FinishCase()
        {
            var c = catchCases[caseIdx]; var ci = System.Globalization.CultureInfo.InvariantCulture; int clicks = ClickCount - caseClicks0;
            string line = string.Format(ci, "{0}: catches={1} catch jump max {2:0.000} px | release jump {3:0.000} px | hold error {4:0.000} px | rest step max {5:0.000} px/frame | sway step max {6:0.000} deg | clicks fired {7}",
                c.Name, mCatches, mCatchJump, mReleaseJump, mHoldErr, mRestStep, mSway, clicks);
            catchSummary.AppendLine(line); simLog.AppendLine("# " + line);
            if (mCatchJump > worstCatchJump) worstCatchJump = mCatchJump; totalClicks += clicks;
        }
        void EndCatch()
        {
            sim = false; catchCases = null; timer.Enabled = Visible;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            string verdict = string.Format(ci, "max catch jump {0:0.000} px ({1}), clicks fired {2} ({3})", worstCatchJump, worstCatchJump < 0.5f ? "PASS < 0.5" : "FAIL", totalClicks, totalClicks == 0 ? "PASS" : "FAIL");
            simLog.AppendLine("# " + verdict);
            try { System.IO.File.WriteAllText(simPath, simLog.ToString()); } catch (Exception ex2) { Log.Write("flingcatch: " + ex2.Message); }
            Log.Write(Def.Id + ": flingcatch done (classic=" + CatchClassic + "): " + verdict + ".");
            foreach (var l in catchSummary.ToString().Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)) Log.Write("  " + l);
            CatchClassic = false;   // only ever for the test
        }
    }

    // Opens the Grok Bot app at an agent's chat: grokbot://app/v1/agent?id=<agentId> when the grokbot: protocol is
    // registered, then makes sure the app window is in front. Without a protocol handler it only brings the window forward.
    static class AppLink
    {
        public static string Open(string agentId)
        {
            string how = "";
            bool registered = false;
            try { using (var k = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey("grokbot\\shell\\open\\command")) registered = k != null; } catch { }
            if (registered)
            {
                string url = agentId.Length > 0 ? "grokbot://app/v1/agent?id=" + Uri.EscapeDataString(agentId) : "grokbot://app/v1";
                try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); how = agentId.Length > 0 ? "deep link (agent chat)" : "deep link (app)"; }
                catch (Exception ex) { how = "deep link failed (" + ex.GetType().Name + ")"; }
            }
            else how = "grokbot: protocol not registered";
            // If the app was closed, the link starts it; its window can take a while to appear, so keep trying for 15 s.
            bool wasRunning = Process.GetProcessesByName("Grok Bot").Length > 0;
            var t = new System.Threading.Thread(() =>
            {
                string r = "";
                for (int i = 0; i < (wasRunning ? 1 : 15); i++)
                {
                    System.Threading.Thread.Sleep(wasRunning ? 900 : 1000);
                    r = BringToFront();
                    if (r != "Grok Bot app is not running" && !(r.StartsWith("Grok Bot app is starting"))) break;
                }
                Log.Write("Open in app: " + r + (wasRunning ? "" : " (the app was closed; the link started it)") + ".");
            }) { IsBackground = true };
            t.Start();
            Log.Write("Open in app: " + how + (agentId.Length > 0 ? " for agent " + agentId.Substring(0, Math.Min(8, agentId.Length)) + "\u2026" : "") + ".");
            return how;
        }

        public static string BringToFront()
        {
            foreach (var p in Process.GetProcessesByName("Grok Bot"))
            {
                var h = p.MainWindowHandle; if (h == IntPtr.Zero) continue;
                if (Native.IsIconic(h)) Native.ShowWindow(h, 9);   // SW_RESTORE
                Native.keybd_event(0x12, 0, 0, UIntPtr.Zero); Native.keybd_event(0x12, 0, 2, UIntPtr.Zero);   // Alt tap lets SetForegroundWindow succeed
                Native.SetForegroundWindow(h);
                uint fp; Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out fp);
                return fp == (uint)p.Id ? "Grok Bot window in front" : "could not bring the Grok Bot window to the front";
            }
            return Process.GetProcessesByName("Grok Bot").Length > 0 ? "Grok Bot app is starting (no window yet)" : "Grok Bot app is not running";
        }

        // what the grokbot: link opens (for status/docs), e.g. "D:\...\Grok Bot.exe" "%1"
        public static string ProtocolTarget()
        {
            try { using (var k = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey("grokbot\\shell\\open\\command")) return k == null ? "not registered" : (k.GetValue("") as string ?? "(empty)"); } catch (Exception ex) { return "unreadable (" + ex.GetType().Name + ")"; }
        }
    }

    static class Native
    {
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int x, y; }
        [StructLayout(LayoutKind.Sequential)] public struct SIZE { public int cx, cy; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] public struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint f);
        [DllImport("user32.dll")] public static extern bool UpdateLayeredWindow(IntPtr h, IntPtr dst, ref POINT pd, ref SIZE sz, IntPtr src, ref POINT ps, int key, ref BLENDFUNCTION bf, int flags);
        [StructLayout(LayoutKind.Sequential)] public struct BITMAPINFOHEADER { public int biSize, biWidth, biHeight; public short biPlanes, biBitCount; public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant; }
        [DllImport("gdi32.dll")] public static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFOHEADER bi, uint usage, out IntPtr bits, IntPtr section, uint offset);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr o);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr dc);
    }
}
