using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace BotBeacons
{
    // 1.2.0: a per-pixel-alpha tool window that never takes focus (the same rules as the beacons: layered, tool window,
    // topmost, no-activate, MA_NOACTIVATE on click, shown without activation). Its pixels come from a Bitmap.
    class LayeredWindow : Form
    {
        public LayeredWindow()
        {
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; TopMost = true;
            Size = new Size(1, 1);
        }
        protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x80000 | 0x80 | 0x8 | 0x8000000; return cp; } }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x21) { m.Result = (IntPtr)3; return; }   // WM_MOUSEACTIVATE -> MA_NOACTIVATE
            base.WndProc(ref m);
        }
        protected override void OnPaint(PaintEventArgs e) { }
        protected override void OnPaintBackground(PaintEventArgs e) { }

        public void Blit(Bitmap bmp, Point at)
        {
            if (IsDisposed) return;
            if (!IsHandleCreated) CreateHandle();
            IntPtr screen = Native.GetDC(IntPtr.Zero), mem = Native.CreateCompatibleDC(screen), hb = IntPtr.Zero, old = IntPtr.Zero;
            try
            {
                hb = bmp.GetHbitmap(Color.FromArgb(0)); old = Native.SelectObject(mem, hb);
                var sz = new Native.SIZE { cx = bmp.Width, cy = bmp.Height }; var src = new Native.POINT(); var dst = new Native.POINT { x = at.X, y = at.Y };
                var bf = new Native.BLENDFUNCTION { BlendOp = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
                Native.UpdateLayeredWindow(Handle, screen, ref dst, ref sz, mem, ref src, 0, ref bf, 2);
            }
            finally
            {
                if (old != IntPtr.Zero) Native.SelectObject(mem, old);
                if (hb != IntPtr.Zero) Native.DeleteObject(hb);
                Native.DeleteDC(mem); Native.ReleaseDC(IntPtr.Zero, screen);
            }
        }
        public void ShowQuiet() { if (!Visible) Show(); }
    }

    // one bookmark tab
    class TabWindow : LayeredWindow
    {
        public readonly BeaconForm B;
        public int ScreenIdx; public TabEdge Edge = TabEdge.Right; public Rectangle Slot { get; set; } public float Along;
        public float DepthNow = TabLayout.Depth; public bool Hot, Dragging;
        public double SettleAt = -1;   // 1.2.3: just docked by a throw: wobble out and settle
        public Point DragAt; public TabEdge DragEdge;
        string lastSig = "";
        bool down; Point downAt;

        public TabWindow(BeaconForm b) { B = b; Text = b.Def.Name; }

        public Rectangle BodyOnScreen { get { var r = TabLayout.Body(Edge, (int)Math.Round(DepthNow)); r.Offset(Slot.Location); return r; } }
        public void CancelPress() { down = false; }

        public void Render(bool force, float pulse)
        {
            var d = B.Def; bool att = B.Attention, unread = B.Unread.Count > 0;
            var edge = Dragging ? DragEdge : Edge;
            string sig = edge + "|" + (int)Math.Round(DepthNow) + "|" + att + "|" + unread + "|" + Hot + "|" + Dragging + "|" + TabStore.Version + "|" + Avatars.Version + "|" + d.Name + "|" + (att ? (int)(pulse * 8) : 0)
                + "|" + (Dragging ? DragAt.ToString() : Slot.Location.ToString()) + "|" + TabLayout.Scale + "|" + TabStore.FrameTag(d);   // 1.2.6: an animated picture's frame
            if (!force && sig == lastSig) return;
            lastSig = sig;
            var look = new TabRender.TabLook { Image = TabStore.ImageFor(d), Color = TabStore.ColorFor(d), Attention = att, Unread = unread, Count = B.Unread.Count, Hot = Hot, Ghost = Dragging, Depth = DepthNow, Pulse = pulse };
            using (var bmp = TabRender.Tab(edge, look))
            {
                var at = Dragging ? new Point(DragAt.X - bmp.Width / 2, DragAt.Y - bmp.Height / 2) : Slot.Location;
                Blit(bmp, at);
            }
        }

        // 1.2.1: a press that moves more than 6 px starts a drag. From then on TabsSkin's drag loop follows the cursor and
        // watches the button itself (it does not rely on this window still getting mouse messages).
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left) { down = true; downAt = Cursor.Position; midDown = false; }
            else if (e.Button == MouseButtons.Middle) { midDown = !down && !Dragging; midAt = Cursor.Position; midMax = 0; }   // 1.2.6
        }
        bool midDown; Point midAt; double midMax;
        ToolTip hint;   // 1.2.6: "Click: open, Middle-click: ping" after a pause (not in hover-open mode: the card opens instead)
        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            if (TabsSkin.HoverOpens || Dragging) { if (hint != null) hint.SetToolTip(this, null); return; }
            if (hint == null) hint = new ToolTip { InitialDelay = 900, ReshowDelay = 300, AutoPopDelay = 5000, ShowAlways = true };   // the tab never takes activation
            hint.SetToolTip(this, ClickRules.HoverHint(B.Def.Name));
        }
        protected override void OnHandleDestroyed(EventArgs e) { if (hint != null) { hint.Dispose(); hint = null; } base.OnHandleDestroyed(e); }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var c = Cursor.Position;
            if (midDown) midMax = Math.Max(midMax, Math.Sqrt((double)(c.X - midAt.X) * (c.X - midAt.X) + (double)(c.Y - midAt.Y) * (c.Y - midAt.Y)));
            if (Dragging) { TabsSkin.DragPoll(); return; }
            if (down && (Math.Abs(c.X - downAt.X) > 6 || Math.Abs(c.Y - downAt.Y) > 6)) { down = false; TabsSkin.BeginDrag(this); }
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left)
            {
                bool was = down; down = false;
                if (Dragging) TabsSkin.EndDrag(this, Cursor.Position);
                else if (was) TabsSkin.TabClicked(this);
            }
            else if (e.Button == MouseButtons.Right && !Dragging) TabsSkin.ShowBeaconMenu(this, B, Cursor.Position);
            else if (ClickRules.ForButton(e.Button) == PressKind.Ping)
            {
                bool ping = ClickRules.MiddlePing(midDown, down || Dragging, midMax, 6); midDown = false;
                if (ping) B.PingClick();   // 1.2.6: middle-click pings the tab's bot (the card stays as it is)
            }
        }
        // capture loss no longer ends a drag (1.2.0 did, at whatever spot the cursor had then): the drag loop sees the release
    }

    // 1.2.1: the grip at the end of a strip of tabs: hover (or click) opens that edge's whole shelf
    class GripWindow : LayeredWindow
    {
        public string DockKey = ""; public TabEdge Edge; public Rectangle Rect { get; set; } public bool Hot;
        string lastSig = "";
        public void Render(bool force)
        {
            string sig = Edge + "|" + Rect + "|" + Hot;
            if (!force && sig == lastSig) return;
            lastSig = sig;
            using (var b = TabRender.Grip(Edge, Rect.Size, Hot)) Blit(b, Rect.Location);
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left) TabsSkin.OpenShelfFor(DockKey, false);
            else if (e.Button == MouseButtons.Right) TabsSkin.ShowMainMenu(this, Cursor.Position);
        }
    }

    // the dashed "lands here" outline while dragging
    class SnapWindow : LayeredWindow
    {
        Rectangle shown = Rectangle.Empty;
        public void ShowAt(Rectangle r)
        {
            if (r == shown && Visible) return;
            shown = r;
            using (var b = TabRender.SnapPreview(r.Size, "")) Blit(b, r.Location);
            ShowQuiet();
        }
        public void HideNow() { shown = Rectangle.Empty; Hide(); }
        protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x20; return cp; } }   // click-through
    }

    // the slide-out shelf for one edge, or (1.2.1) one tab's card
    class ShelfWindow : LayeredWindow
    {
        public string DockKey = ""; public TabEdge Edge; public int ScreenIdx;
        public BeaconForm CardFor;      // set: this is the hover card of that tab only
        public bool IsCard { get { return CardFor != null; } }
        public bool Pinned; public double OpenedAt;   // opened from a menu / grip click: stays until the mouse has been in it
        public TabRender.ShelfModel Model; public TabRender.ShelfLayout Lay; public Rectangle Target { get; set; }
        public float K; public int Dir;   // K: 0 in .. 1 out; Dir: +1 opening, -1 closing, 0 still
        readonly Stopwatch anim = new Stopwatch(); float k0;
        string lastSig = "";
        bool down; Point downAt; TabRender.Hit downHit;

        public bool IsClosing { get { return Dir < 0; } }
        public void Slide(int dir) { if (Dir == dir) return; Dir = dir; k0 = K; anim.Restart(); }
        public void CancelPress() { down = false; }

        // returns false once fully closed
        public bool Step()
        {
            if (Dir != 0)
            {
                bool off = AnimSettings.Mode == AnimMode.Off;
                float ms = Dir > 0 ? 190f : 150f, t = off ? 1f : (float)anim.Elapsed.TotalMilliseconds / ms;
                K = Math.Max(0f, Math.Min(1f, k0 + Dir * t));
                if (K >= 1f && Dir > 0) Dir = 0;
                if (K <= 0f && Dir < 0) { Dir = 0; return false; }
            }
            Render(false);
            return true;
        }

        public void Render(bool force)
        {
            if (Model == null || Lay == null || IsDisposed) return;
            string sig = K.ToString("0.000") + "|" + Model.Hot + "|" + TabsSkin.ModelSig(Model);
            if (!force && sig == lastSig) return;
            lastSig = sig;
            var off = TabLayout.SlideOffset(Edge, Lay.Size, K);
            using (var bmp = TabRender.Shelf(Model, Lay, off)) Blit(bmp, Target.Location);
        }

        TabRender.Hit HitAt(Point screen)
        {
            if (Lay == null || K < 0.9f) return null;
            return TabRender.HitTest(Lay, new Point(screen.X - Target.X, screen.Y - Target.Y));
        }
        static string HotKey(TabRender.Hit h) { return h == null ? "" : h.Action == "row" || h.Action == "edit" ? h.Action + ":" + h.Id : h.Action; }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var c = Cursor.Position;
            if (down && downHit != null && (Math.Abs(c.X - downAt.X) > 6 || Math.Abs(c.Y - downAt.Y) > 6))
            {
                // 1.2.1: drag the header = move the whole strip; drag a row or the card = move that one tab
                var h0 = downHit; down = false;
                if (h0.Action == "header") { TabsSkin.BeginDockDrag(this); return; }
                if ((h0.Action == "row" || h0.Action == "edit") && TabsSkin.BeginDragOf(h0.Id)) return;
            }
            var h = HitAt(c); string hk = HotKey(h);
            Cursor = h != null && h.Action != "header" ? Cursors.Hand : Cursors.Default;
            if (Model != null && hk != Model.Hot) { Model.Hot = hk; Render(false); SetTip(h); }
        }
        // 1.2.4: hover tips on the shelf controls (segments, speaker, Skin, Open on)
        ToolTip tip;
        void SetTip(TabRender.Hit h)
        {
            string text = h == null ? "" : TabRender.TipFor(h.Action, Model);
            if (tip == null) { if (text.Length == 0) return; tip = new ToolTip { InitialDelay = 450, ReshowDelay = 120, AutoPopDelay = 8000, ShowAlways = true }; }
            tip.SetToolTip(this, text.Length > 0 ? text : null);
        }
        // after the speaker click: say what it did, beside the speaker, for a moment
        public void FlashTip(string text, Rectangle nearScreen)
        {
            if (tip == null) tip = new ToolTip { InitialDelay = 450, ReshowDelay = 120, AutoPopDelay = 8000, ShowAlways = true };
            var at = PointToClient(new Point(nearScreen.Left, nearScreen.Bottom + 4));
            try { tip.Show(text, this, at, 1800); } catch { }
        }
        public Rectangle HitOnScreen(string action)
        {
            if (Lay == null) return Rectangle.Empty;
            foreach (var h in Lay.Hits) if (h.Action == action) { var r = Lay.ToPx(h.R); r.Offset(Target.Location); return r; }
            return Rectangle.Empty;
        }
        protected override void OnHandleDestroyed(EventArgs e) { if (tip != null) { tip.Dispose(); tip = null; } base.OnHandleDestroyed(e); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); if (Model != null && Model.Hot != "") { Model.Hot = ""; Render(false); } }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left) { down = true; downAt = Cursor.Position; downHit = HitAt(downAt); }
            else if (e.Button == MouseButtons.Middle) { midHit = down ? null : HitAt(Cursor.Position); midAt = Cursor.Position; }   // 1.2.6
        }
        TabRender.Hit midHit; Point midAt;
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (TabsSkin.Dragging) { down = false; return; }
            var c = Cursor.Position; var h = HitAt(c);
            if (e.Button == MouseButtons.Left)
            {
                bool was = down; down = false;
                if (was && h != null && downHit != null && HotKey(h) == HotKey(downHit)) TabsSkin.ShelfAction(this, h, c);
            }
            else if (ClickRules.ForButton(e.Button) == PressKind.Ping)
            {
                // 1.2.6: middle-click on a bot's row pings that bot (released on the same row, without moving away)
                var h0 = midHit; midHit = null;
                double dist = Math.Sqrt((double)(c.X - midAt.X) * (c.X - midAt.X) + (double)(c.Y - midAt.Y) * (c.Y - midAt.Y));
                if (h0 != null && h != null && h0.Action == "row" && h.Action == "row" && h0.Id == h.Id && ClickRules.MiddlePing(true, down, dist, 6))
                { var b = Program.Find(h.Id); if (b != null) b.PingClick(); }
            }
            else if (e.Button == MouseButtons.Right)
            {
                if (h != null && (h.Action == "row" || h.Action == "edit")) { var b = Program.Find(h.Id); if (b != null) TabsSkin.ShowBeaconMenu(this, b, c); }
                else if (h != null && (h.Action == "speaker" || h.Action.StartsWith("snd:"))) TabsSkin.ShowVolumeMenu(this, c);   // 1.2.4
                else TabsSkin.ShowMainMenu(this, c);
            }
        }
        public Rectangle RowOnScreen(string id)
        {
            if (Lay == null) return Rectangle.Empty;
            foreach (var kv in Lay.Rows) if (kv.Value.Id == id) { var r = Lay.ToPx(kv.Key); r.Offset(Target.Location); return r; }
            return Rectangle.Empty;
        }
    }

    // The Tabs skin. The floating beacon windows stay hidden; each shown beacon gets a tab on a screen edge instead. Same
    // beacons, messages, glow/attention state, sounds and relay: only the look differs.
    // 1.2.1: Size (tabSize=small|normal|large, default normal = 1.4x the 1.2.0 size), hovering a tab slides out only
    // that tab's card (tabHover=tab, default) or the whole shelf (tabHover=shelf); a grip at the end of each strip and
    // "Open shelf" in the menus open the whole shelf. Dragging is a polled loop that commits on release.
    static class TabsSkin
    {
        static readonly Dictionary<BeaconForm, TabWindow> tabs = new Dictionary<BeaconForm, TabWindow>();
        static readonly Dictionary<string, GripWindow> grips = new Dictionary<string, GripWindow>();
        static readonly List<Form> retired = new List<Form>();
        static Timer timer, dragTimer;
        static readonly Stopwatch clock = Stopwatch.StartNew();
        static ShelfWindow shelf;
        static SnapWindow snap;
        static TabEditForm editForm;
        static ContextMenuStrip mainMenu, skinMenu;
        static Rectangle[] work = new Rectangle[0]; static string[] names = new string[0];
        static string hoverKey = ""; static double hoverSince, leftSince = -1, quietUntil;
        static TabWindow dragTab; static ShelfWindow dragShelf;

        public const double HoverDelay = 0.15, LeaveDelay = 0.4;

        public static bool Active { get { return Skin.AnyTabs; } }   // 1.2.2: any beacon is a tab (the look is per beacon)
        public static bool Dragging { get { return dragTab != null || dragShelf != null; } }

        // ---------------------------------------------------------------- settings (1.2.1)
        public static string SizeSetting { get { return TabLayout.SizeName(Settings.Get("tabSize", "normal")); } }
        // 1.2.2: "Open tabs on: Click / Hover" (tabOpen=click|hover, default click). Click: hovering only nudges a tab a few
        // px; a click slides its card out, and a click on it again, Esc or a click elsewhere slides it back. Hover: 1.2.1's
        // way (the card slides out on hover and back 0.4 s after the mouse leaves, unless you are replying).
        public static string OpenSetting { get { return Settings.Get("tabOpen", "click") == "hover" ? "hover" : "click"; } }
        public static bool HoverOpens { get { return OpenSetting == "hover"; } }
        static void ApplyScale() { TabLayout.Scale = TabLayout.ScaleFor(SizeSetting); }

        public static void SetSize(string size)
        {
            size = TabLayout.SizeName(size);
            if (size == SizeSetting && Math.Abs(TabLayout.Scale - TabLayout.ScaleFor(size)) < 0.001f) return;
            Settings.Set("tabSize", size); Settings.Save();
            Log.Write("Tab size: " + size + ".");
            string reopen = shelf != null && !shelf.IsCard ? shelf.DockKey : null;
            CloseShelf(true);
            if (editForm != null && !editForm.IsDisposed) editForm.Close();
            ApplyScale();
            foreach (var t in tabs.Values) t.DepthNow = TabLayout.Depth;
            Relayout();
            if (reopen != null && Active) OpenShelfFor(reopen, false);
        }
        public static void SetOpen(string mode)
        {
            mode = mode == "hover" ? "hover" : "click";
            Settings.Set("tabOpen", mode); Settings.Save();
            Log.Write("Open tabs on: " + mode + ".");
            if (shelf != null && shelf.IsCard) CloseShelf(true);
            Relayout();
            if (shelf != null) RefreshModel();
        }

        // the beacon has a place on screen right now (its tab in the Tabs skin, its window in the Beacons skin)
        public static bool OnScreen(BeaconForm b) { return Skin.IsTab(b) ? Visibility.ShouldShow(b) : b.Visible; }

        // every visible taskbar (the main one and the per-monitor ones Windows 10/11 show)
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
        struct RECT { public int L, T, R, B; }
        static Rectangle[] Taskbars()
        {
            var o = new List<Rectangle>();
            try
            {
                foreach (var cls in new[] { "Shell_TrayWnd", "Shell_SecondaryTrayWnd" })
                {
                    IntPtr h = IntPtr.Zero;
                    for (int n = 0; n < 16; n++)
                    {
                        h = FindWindowEx(IntPtr.Zero, h, cls, null); if (h == IntPtr.Zero) break;
                        RECT r; if (IsWindowVisible(h) && GetWindowRect(h, out r) && r.R - r.L > 8 && r.B - r.T > 8) o.Add(Rectangle.FromLTRB(r.L, r.T, r.R, r.B));
                    }
                }
            }
            catch { }   // not on Windows (the box's Mono checks): no taskbars
            return o.ToArray();
        }

        static void Screens()
        {
            var all = Screen.AllScreens;
            work = TabLayout.TrimTaskbars(all.Select(s => s.WorkingArea).ToArray(), Taskbars()); names = all.Select(s => s.DeviceName).ToArray();
        }
        static int ScreenFor(string name)
        {
            for (int i = 0; i < names.Length; i++) if (string.Equals(names[i], name, StringComparison.OrdinalIgnoreCase)) return i;
            var all = Screen.AllScreens;
            for (int i = 0; i < names.Length && i < all.Length; i++) if (all[i].Primary) return i;
            return 0;
        }
        static string Key(int screen, TabEdge e) { return screen + ":" + TabLayout.EdgeName(e); }
        static string KeyOf(TabWindow t) { return Key(t.ScreenIdx, t.Edge); }

        // Visibility.Apply calls this: create/remove tab windows to match, or close everything outside the Tabs skin
        public static void Sync()
        {
            if (!Active)
            {
                if (tabs.Count == 0 && shelf == null && grips.Count == 0) return;
                CancelDrag();
                CloseShelf(true);
                foreach (var t in tabs.Values) { t.Hide(); t.Dispose(); }
                tabs.Clear();
                foreach (var g in grips.Values) { g.Hide(); g.Dispose(); }
                grips.Clear();
                if (snap != null) { snap.Dispose(); snap = null; }
                if (timer != null) timer.Stop();
                DisposeRetired();
                return;
            }
            ApplyScale();
            foreach (var b in Program.Beacons)
            {
                bool want = Visibility.ShouldShow(b) && Skin.IsTab(b);   // 1.2.2: only the beacons that are tabs
                TabWindow t;
                if (want && !tabs.ContainsKey(b)) { t = new TabWindow(b); tabs[b] = t; if (settling.Remove(b.Def.Id) && AnimSettings.Mode != AnimMode.Off) t.SettleAt = clock.Elapsed.TotalSeconds; }
                else if (!want && tabs.TryGetValue(b, out t)) { if (dragTab == t) CancelDrag(); tabs.Remove(b); t.Hide(); t.Dispose(); }
            }
            foreach (var b in tabs.Keys.ToList()) if (!Program.Beacons.Contains(b)) { var t = tabs[b]; if (dragTab == t) CancelDrag(); tabs.Remove(b); t.Hide(); t.Dispose(); }
            Relayout();
            if (timer == null) { timer = new Timer { Interval = 40 }; timer.Tick += (s, e) => Tick(); }
            timer.Start();
        }

        public static void Relayout()
        {
            if (!Active) return;
            ApplyScale();
            Screens(); if (work.Length == 0) return;
            var groups = new Dictionary<string, List<TabWindow>>();
            int idx = 0;
            foreach (var b in Program.Beacons)
            {
                TabWindow t; if (!tabs.TryGetValue(b, out t)) continue;
                var p = TabStore.Get(b.Def.Id);
                int si = ScreenFor(p.Monitor);
                TabEdge e; var edge = TabLayout.TryEdge(p.Edge, out e) ? e : TabEdge.Right;
                // 1.2.1: a seam between two monitors is not a dock; a tab saved there (or unplaced) goes to the outer edge on that side
                // 1.2.3: unless it was deliberately docked there by holding it on the seam (it stays on that monitor's inner edge)
                t.ScreenIdx = p.Seam ? si : TabLayout.Outward(work, si, edge); t.Edge = edge;
                t.Along = p.Along >= 0 ? (float)Math.Min(1, p.Along) : 0.22f + 0.0001f * idx;   // unplaced tabs line up from near the top
                idx++;
                string k = KeyOf(t); if (!groups.ContainsKey(k)) groups[k] = new List<TabWindow>(); groups[k].Add(t);
            }
            var gripKeys = new HashSet<string>();
            foreach (var kv in groups)
            {
                var g = kv.Value; var w = work[g[0].ScreenIdx]; var e = g[0].Edge;
                var pos = TabLayout.Stack(TabLayout.EdgeLength(w, e), g.Select(t => t.Along).ToArray());
                for (int i = 0; i < g.Count; i++) g[i].Slot = TabLayout.TabWindow(w, e, pos[i]);
                {
                    var span = g.Select(t => t.Slot).Aggregate(Rectangle.Union);
                    GripWindow gw; if (!grips.TryGetValue(kv.Key, out gw) || gw.IsDisposed) { gw = new GripWindow(); grips[kv.Key] = gw; }
                    gw.DockKey = kv.Key; gw.Edge = e; gw.Rect = TabLayout.Grip(w, e, span);
                    gripKeys.Add(kv.Key);
                }
            }
            foreach (var k in grips.Keys.ToList()) if (!gripKeys.Contains(k)) { grips[k].Hide(); grips[k].Dispose(); grips.Remove(k); }
            foreach (var t in tabs.Values)
            {
                if (t.Dragging) continue;
                t.Bounds = t.Slot;
                t.Render(true, Pulse());
                if (!Covers(shelf, t)) t.ShowQuiet();
            }
            foreach (var b in Program.Beacons) if (CardOf(b) != null) b.PlaceBubble();   // 1.2.2: an open card follows its tab
            foreach (var g in grips.Values)
            {
                g.Bounds = g.Rect; g.Render(true);
                if (shelf != null && !shelf.IsCard && shelf.DockKey == g.DockKey) g.Hide(); else g.ShowQuiet();
            }
        }

        static float Pulse()
        {
            if (AnimSettings.Mode == AnimMode.Off) return 1f;
            return 0.5f + 0.5f * (float)Math.Sin(clock.Elapsed.TotalSeconds * Math.PI * 2 / 1.3);
        }

        // the tabs a shelf or card is drawn over (hidden while it is out)
        static bool Covers(ShelfWindow s, TabWindow t) { return s != null && (s.IsCard ? s.CardFor == t.B : s.DockKey == KeyOf(t)); }
        static List<TabWindow> Covered(ShelfWindow s)
        {
            if (s == null) return new List<TabWindow>();
            if (s.IsCard) { TabWindow t; return tabs.TryGetValue(s.CardFor, out t) ? new List<TabWindow> { t } : new List<TabWindow>(); }
            return DockTabs(s.DockKey);
        }

        static void DisposeRetired()
        {
            if (retired.Count == 0 || Dragging) return;
            foreach (var f in retired) { try { if (!f.IsDisposed) f.Dispose(); } catch { } }
            retired.Clear();
        }

        static void Tick()
        {
            if (!Active) return;
            DisposeRetired();
            double now = clock.Elapsed.TotalSeconds; float pulse = Pulse();
            var c = Cursor.Position; bool buttons = Control.MouseButtons != MouseButtons.None;
            bool anyDrag = Dragging;
            // over an open card/shelf: the tabs under it don't count as hovered (a card overlaps its neighbours a little)
            var card = OpenCard();
            bool overPanel = (shelf != null && shelf.K > 0.3f && !shelf.IsClosing && shelf.Target.Contains(c)) || (card != null && card.Bounds.Contains(c));
            TabWindow under = null;
            foreach (var t in tabs.Values)
            {
                bool hot = !anyDrag && !overPanel && t.Visible && t.BodyOnScreen.Contains(c);
                if (hot) under = t;
                t.Hot = hot;
                float target = t.B.Attention ? TabLayout.AlertDepth : TabLayout.Depth + (t.B.Unread.Count > 0 || hot || CardOf(t.B) != null ? TabLayout.NudgeDepth : 0);
                if (t.SettleAt >= 0)
                {
                    float el = (float)(now - t.SettleAt);
                    if (el >= TabLayout.SettleTime) { t.SettleAt = -1; t.DepthNow = target; }
                    else t.DepthNow = Math.Max(TabLayout.Depth - TabLayout.SettleDip, target + TabLayout.SettleExtra(el));   // never deeper in than the picture allows
                }
                else if (AnimSettings.Mode == AnimMode.Off) t.DepthNow = target;
                else t.DepthNow += (target - t.DepthNow) * 0.3f;
                if (Math.Abs(target - t.DepthNow) < 0.4f) t.DepthNow = target;
                t.DepthNow = Math.Min(TabLayout.MaxDepth, t.DepthNow);   // 1.2.5: the wobble never pushes the body (and its badge) past the window
                if (t.Visible || t.Dragging) t.Render(false, pulse);
            }
            GripWindow gripUnder = null;
            foreach (var g in grips.Values)
            {
                bool hot = !anyDrag && !overPanel && g.Visible && g.Rect.Contains(c);
                if (hot) gripUnder = g;
                if (g.Hot != hot) { g.Hot = hot; g.Render(false); }
            }
            if (Program.SessionLocked || anyDrag) return;
            bool hoverMode = HoverOpens, editOpen = (editForm != null && !editForm.IsDisposed) || ModalHold.Held || ModalHold.OwnDialogAt(c);   // 1.2.4: any dialog of ours holds the shelf / card
            // Esc and mouse presses, edge-detected (only acted on while a card or shelf is out and doesn't have focus)
            bool esc = (GetAsyncKeyState(0x1B) & 0x8000) != 0, escPress = esc && !escWas; escWas = esc;
            bool press = buttons && !buttonsWas; buttonsWas = buttons;
            // hover mode: hover a tab -> its card slides out; hover the grip -> the whole shelf. Click mode: hovering only nudges.
            string uk = gripUnder != null ? "grip:" + gripUnder.DockKey : under != null ? "tab:" + under.B.Def.Id : "";
            if (uk != hoverKey) { hoverKey = uk; hoverSince = now; }
            if (hoverMode && uk.Length > 0 && !buttons && now >= quietUntil && now - hoverSince >= HoverDelay)
            {
                bool shelfOpen = shelf != null && !shelf.IsClosing;
                if (gripUnder != null) { if (!(shelfOpen && !shelf.IsCard && shelf.DockKey == gripUnder.DockKey)) OpenShelfFor(gripUnder.DockKey, false); }
                else if (card == null || card.Beacon != under.B) { if (!(shelfOpen && Covers(shelf, under))) { under.B.TabAnchor = under.BodyOnScreen; under.B.OpenBubble(false); card = OpenCard(); cardLeft = -1; } }
            }
            if (card != null && !card.ContainsFocus)
            {
                TabWindow own; tabs.TryGetValue(card.Beacon, out own);
                bool onOwnTab = own != null && own.BodyOnScreen.Contains(c);
                if (escPress) { card.CloseReason = "Esc"; card.Close(); }
                else if (press && !card.Bounds.Contains(c) && !onOwnTab && !MenusOpen() && !editOpen) { card.CloseReason = "clicked away"; card.Close(); }
                else if (hoverMode && !card.Busy)
                {
                    bool keep = card.Bounds.Contains(c) || onOwnTab || MenusOpen() || editOpen || card.Expanded;
                    if (keep) cardLeft = -1;
                    else if (cardLeft < 0) cardLeft = now;
                    else if (now - cardLeft > LeaveDelay) { cardLeft = -1; card.CloseReason = "mouse left"; card.Close(); }
                }
            }
            if (shelf != null)
            {
                if (shelf.Model != null) { RefreshModel(); }
                if (shelf == null) return;
                bool inside = shelf.Target.Contains(c);
                if (inside) shelf.Pinned = false;   // the mouse got there: from now on leaving closes it
                if (!hoverMode && !shelf.IsCard)
                {
                    // click mode: the whole shelf stays out until Esc or a click elsewhere (or a row/grip click)
                    if (!shelf.IsClosing && ((escPress && !ModalHold.Held) || ModalHold.PressClosesShelf(press, inside, MenusOpen(), editOpen, ModalHold.OwnDialogAt(c), gripUnder != null && gripUnder.DockKey == shelf.DockKey))) CloseShelf(false);
                    if (!shelf.Step()) FinishClose();
                    else if (shelf.K > 0.35f) foreach (var t in Covered(shelf)) if (t.Visible) t.Hide();
                    return;
                }
                bool keep = inside || (shelf.Pinned && now - shelf.OpenedAt < 6) || (under != null && Covers(shelf, under)) || (gripUnder != null && !shelf.IsCard && gripUnder.DockKey == shelf.DockKey)
                    || MenusOpen() || editOpen;
                if (!keep && shelf.Model != null) foreach (var r in shelf.Model.Rows) { var b = Program.Find(r.Id); var bf = b != null ? b.OpenBubbleForm : null; if (bf != null && bf.ScreenBounds.Contains(c)) keep = true; }
                if (keep) { leftSince = -1; if (shelf.IsClosing) shelf.Slide(+1); }
                else if (!shelf.IsClosing) { if (leftSince < 0) leftSince = now; else if (now - leftSince > LeaveDelay) CloseShelf(false); }
                if (!shelf.Step()) FinishClose();
                else if (shelf.K > 0.35f) foreach (var t in Covered(shelf)) if (t.Visible) t.Hide();
            }
        }

        static bool escWas, buttonsWas; static double cardLeft = -1;
        // the open tab card (a tab's conversation bubble), if any
        static TabCard OpenCard()
        {
            foreach (var b in Program.Beacons) { var c = b.OpenBubbleForm as TabCard; if (c != null && !c.IsDisposed && c.Visible) return c; }
            return null;
        }
        static TabCard CardOf(BeaconForm b) { return b.OpenBubbleForm as TabCard; }
        // where a beacon's tab card goes: beside its tab (TabCard.Place)
        public static Rectangle CardRect(BeaconForm b, Size size, out TabEdge edge)
        {
            edge = TabEdge.Right; TabWindow t;
            if (!tabs.TryGetValue(b, out t) || t.ScreenIdx < 0 || t.ScreenIdx >= work.Length) return Rectangle.Empty;
            edge = t.Edge;
            return TabLayout.CardBeside(work[t.ScreenIdx], t.Edge, t.Slot, size);
        }

        static bool MenusOpen()
        {
            foreach (var m in new[] { mainMenu, skinMenu }) if (m != null && !m.IsDisposed && m.Visible) return true;
            foreach (var b in Program.Beacons) if (b.MenuIfBuilt != null && b.MenuIfBuilt.Visible) return true;
            return false;
        }

        static List<TabWindow> DockTabs(string key) { return tabs.Values.Where(t => KeyOf(t) == key).OrderBy(t => t.Edge == TabEdge.Left || t.Edge == TabEdge.Right ? t.Slot.Y : t.Slot.X).ToList(); }

        // the whole shelf of one edge (pinned: opened from a menu or a click, so it waits for the mouse to come to it)
        public static void OpenShelfFor(string key, bool pinned)
        {
            if (!Active || Dragging) return;
            var dock = DockTabs(key); if (dock.Count == 0) return;
            if (shelf != null && (shelf.IsCard || shelf.DockKey != key)) CloseShelf(true);
            if (shelf == null) shelf = new ShelfWindow();
            shelf.DockKey = key; shelf.Edge = dock[0].Edge; shelf.ScreenIdx = dock[0].ScreenIdx; shelf.CardFor = null;
            shelf.Pinned = pinned; shelf.OpenedAt = clock.Elapsed.TotalSeconds;
            shelf.Model = BuildModel(key, shelf.Edge, null);
            Show(shelf);
            GripWindow g; if (grips.TryGetValue(key, out g)) g.Hide();
        }
        // one tab's card
        static void OpenLayeredCard(TabWindow t)
        {
            if (!Active || Dragging) return;
            if (shelf != null) CloseShelf(true);
            shelf = new ShelfWindow { CardFor = t.B, DockKey = KeyOf(t), Edge = t.Edge, ScreenIdx = t.ScreenIdx, OpenedAt = clock.Elapsed.TotalSeconds };
            shelf.Model = BuildModel(shelf.DockKey, t.Edge, t.B);
            Show(shelf);
        }
        static void Show(ShelfWindow s)
        {
            Place();
            s.Bounds = s.Target;
            s.Render(true); s.ShowQuiet();
            s.Slide(+1);
            leftSince = -1;
        }

        // "Open shelf" (tray / tab menus, dev hook): the strip of that beacon's tab, else the strip with the most tabs
        public static void OpenWholeShelf(BeaconForm near)
        {
            if (!Active) return;
            TabWindow t = null;
            if (near == null || !tabs.TryGetValue(near, out t)) t = tabs.Values.GroupBy(KeyOf).OrderByDescending(g => g.Count()).Select(g => g.First()).FirstOrDefault();
            if (t != null) OpenShelfFor(KeyOf(t), true);
        }

        static void Place()
        {
            if (shelf == null) return;
            shelf.Lay = TabRender.Layout(shelf.Model);
            var span = Covered(shelf).Select(t => t.Slot).DefaultIfEmpty(new Rectangle(work[shelf.ScreenIdx].Location, Size.Empty)).Aggregate(Rectangle.Union);
            shelf.Target = TabLayout.Shelf(work[shelf.ScreenIdx], shelf.Edge, span, shelf.Lay.Size);
        }

        public static void CloseShelf(bool now)
        {
            if (shelf == null) return;
            if (now) { FinishClose(); return; }
            shelf.Slide(-1);
        }
        // the window is hidden now and disposed on a later tick (it may be the window whose mouse event we are in)
        static void FinishClose()
        {
            if (shelf == null) return;
            var old = shelf; shelf = null;
            old.Hide(); retired.Add(old);
            foreach (var t in Covered(old)) if (!t.Dragging) { t.Render(true, Pulse()); t.ShowQuiet(); }
            GripWindow g; if (!old.IsCard && grips.TryGetValue(old.DockKey, out g) && !g.IsDisposed) g.ShowQuiet();
        }

        static string Clock(DateTime t) { return t.Date == DateTime.Today ? t.ToString("HH:mm") : t.ToString("d MMM"); }

        static TabRender.ShelfModel BuildModel(string key, TabEdge edge, BeaconForm only)
        {
            var m = new TabRender.ShelfModel { Edge = edge, Sounds = !Sound.On ? "off" : Sound.Set, Volume = Sound.Volume, LastSet = Sound.Set, Skin = "Tabs", TotalTabs = tabs.Count, Size = SizeSetting, Hover = OpenSetting, Card = only != null };
            bool conn = Program.RelayState == "connected";
            var list = only != null ? tabs.Values.Where(t => t.B == only).ToList() : DockTabs(key);
            foreach (var t in list)
            {
                var b = t.B; var d = b.Def; PetMessage last = b.History.Count > 0 ? b.History[b.History.Count - 1] : null;
                string msg = last == null ? "" : (last.FromUser ? "You: " : "") + (last.IsAttention ? "Needs you: " : "") + (last.Text ?? "").Replace("\r", " ").Replace("\n", " ");
                if (b.Attention && b.AttentionMsg != null) msg = "Needs you: " + (b.AttentionMsg.Text ?? "").Replace("\n", " ");
                m.Rows.Add(new TabRender.ShelfRow { Id = d.Id, Name = d.Name, Purpose = d.Purpose, Message = msg, Time = last != null ? Clock(last.Time) : "", Image = TabStore.ImageFor(d), Color = TabStore.ColorFor(d), Attention = b.Attention, Unread = b.Unread.Count > 0, Count = b.Unread.Count, Connected = conn });
            }
            return m;
        }

        public static string ModelSig(TabRender.ShelfModel m)
        {
            var s = new System.Text.StringBuilder(); s.Append(m.Sounds).Append('|').Append(m.Volume).Append('|').Append(m.TotalTabs).Append('|').Append(m.Size).Append('|').Append(m.Hover).Append('|').Append(m.Card);
            foreach (var r in m.Rows) s.Append('|').Append(r.Id).Append(r.Name).Append(r.Message.Length).Append(r.Message.GetHashCode()).Append(r.Time).Append(r.Attention).Append(r.Count).Append(r.Connected).Append(r.Color.ToArgb()).Append(r.Image != null ? r.Image.GetHashCode() : 0).Append(r.Purpose);
            return s.ToString();
        }

        static string modelBase = "";
        static void RefreshModel()
        {
            if (shelf == null) return;
            var fresh = BuildModel(shelf.DockKey, shelf.Edge, shelf.CardFor); fresh.Hot = shelf.Model.Hot;
            string a = ModelSig(fresh) + "|" + TabStore.Version + "|" + Avatars.Version + "|" + shelf.GetHashCode();
            if (a == modelBase) return;
            modelBase = a;
            bool sizeChange = fresh.Rows.Count != shelf.Model.Rows.Count;
            shelf.Model = fresh;
            if (fresh.Rows.Count == 0) { CloseShelf(true); return; }
            if (sizeChange) { Place(); shelf.Bounds = shelf.Target; }
            shelf.Render(true);
        }

        // ---------------------------------------------------------------- clicks
        public static void TabClicked(TabWindow t)
        {
            // 1.2.2: a click slides the tab's card (its conversation) out without taking focus; a click on the tab again slides
            // it back. In hover mode the card is usually out already, so the click opens the reply box instead.
            var c = CardOf(t.B);
            if (c != null && HoverOpens && !c.Expanded) { c.Expand(); return; }
            t.B.TabAnchor = t.BodyOnScreen; t.B.OpenBubble(false);
        }

        public static void ShelfAction(ShelfWindow s, TabRender.Hit h, Point at)
        {
            var b = h.Id.Length > 0 ? Program.Find(h.Id) : null;
            switch (h.Action)
            {
                case "row": if (b != null) { CloseShelf(true); if (CardOf(b) == null) b.OpenBubble(false); } return;   // 1.2.2: that tab's card, beside its tab
                case "edit": if (b != null) Edit(b, s.RowOnScreen(b.Def.Id)); break;
                case "snd:custom": Sound.SetOn(true); Sound.SetSet("custom"); break;
                case "snd:classic": Sound.SetOn(true); Sound.SetSet("classic"); break;
                case "snd:off": Sound.SetOn(false); break;
                case "speaker":
                    // 1.2.4: toggles Off / the last choice (the set is kept while off), then says so with the volume
                    Sound.SetOn(!Sound.On);
                    s.FlashTip(Sound.On ? "Sound on: " + (Sound.Set == "classic" ? "Classic" : "Custom") + ", volume " + Sound.Volume + " (right-click to change)" : "Sound off", s.HitOnScreen("speaker"));
                    break;
                case "skin": ShowSkinMenu(s, at); break;
                case "hover": SetOpen(HoverOpens ? "click" : "hover"); break;   // footer "Open on: Click/Hover" toggles
                case "more": ShowMainMenu(s, at); break;
                case "size:small": case "size:normal": case "size:large": SetSize(h.Action.Substring(5)); return;
            }
            if (shelf != null) RefreshModel();
        }

        static void ShowMenu(ContextMenuStrip m, Control owner, Point at)
        {
            if (m.Visible) return;
            var dir = Placement.Menu(at, m.GetPreferredSize(Size.Empty), Screen.FromPoint(at).WorkingArea);
            m.Show(owner, owner.PointToClient(at), dir);
        }

        public static void ShowBeaconMenu(Control owner, BeaconForm b, Point at) { ShowMenu(b.MenuForTabs, owner, at); }
        public static void ShowMainMenu(Control owner, Point at)
        {
            if (mainMenu == null || mainMenu.IsDisposed) mainMenu = Program.BuildMenu(null);
            ShowMenu(mainMenu, owner, at);
        }
        static ContextMenuStrip volumeMenu;
        public static void ShowVolumeMenu(Control owner, Point at)
        {
            if (volumeMenu == null || volumeMenu.IsDisposed)
            {
                volumeMenu = NoTaskbar.Hook(new ContextMenuStrip());
                foreach (var v in Sound.Volumes)
                {
                    var k = v;
                    volumeMenu.Items.Add(new ToolStripMenuItem("Volume: " + char.ToUpper(k[0]) + k.Substring(1), null, (s, e) => { Sound.SetVolume(k); if (shelf != null) RefreshModel(); }) { Tag = k });
                }
                volumeMenu.Opening += (s, e) => { foreach (ToolStripMenuItem it in volumeMenu.Items) it.Checked = (string)it.Tag == Sound.Volume; };
            }
            ShowMenu(volumeMenu, owner, at);
        }
        static void ShowSkinMenu(Control owner, Point at)
        {
            if (skinMenu == null || skinMenu.IsDisposed)
            {
                skinMenu = NoTaskbar.Hook(new ContextMenuStrip());
                var a = new ToolStripMenuItem("All floating", null, (s, e) => Skin.Set("floating"));
                var t = new ToolStripMenuItem("All tabs", null, (s, e) => Skin.Set("tabs"));
                var m = new ToolStripMenuItem("Mixed (drag a beacon to an edge, or a tab off it)") { Enabled = false };
                skinMenu.Items.Add(a); skinMenu.Items.Add(t); skinMenu.Items.Add(m);
                skinMenu.Opening += (s, e) => { var n = Skin.Name; a.Checked = n == "floating"; t.Checked = n == "tabs"; m.Checked = n == "mixed"; m.Visible = n == "mixed"; };
            }
            ShowMenu(skinMenu, owner, at);
        }
        // ---------------------------------------------------------------- edit / remove (1.2.5: tabs are added by bots, see NewBots)
        public static void Edit(BeaconForm b, Rectangle near)
        {
            if (editForm != null && !editForm.IsDisposed) editForm.Close();
            if (near.IsEmpty) { TabWindow t; near = tabs.TryGetValue(b, out t) ? t.BodyOnScreen : new Rectangle(Screen.PrimaryScreen.WorkingArea.Right - 60, Screen.PrimaryScreen.WorkingArea.Top + 200, 40, 40); }
            editForm = new TabEditForm(b);
            ModalHold.Track(editForm, "edit tab");   // 1.2.4: holds the shelf / card open; a top-level window, owned by neither
            var wa = Screen.FromRectangle(near).WorkingArea;
            var sz = editForm.Size;
            // beside the row/tab, on the side with room, clamped to the screen
            int x = near.Left - sz.Width - 12 >= wa.Left ? near.Left - sz.Width - 12 : near.Right + 12;
            if (x + sz.Width > wa.Right) x = wa.Right - sz.Width;
            int y = near.Top + near.Height / 2 - 60; y = Math.Max(wa.Top, Math.Min(wa.Bottom - sz.Height, y));
            if (shelf != null && (shelf.Edge == TabEdge.Top || shelf.Edge == TabEdge.Bottom))
            {
                x = Math.Max(wa.Left, Math.Min(wa.Right - sz.Width, near.Left + near.Width / 2 - sz.Width / 2));
                y = shelf.Edge == TabEdge.Top ? shelf.Target.Bottom + 8 : shelf.Target.Top - sz.Height - 8;
                y = Math.Max(wa.Top, Math.Min(wa.Bottom - sz.Height, y));
            }
            editForm.Location = new Point(x, y);
            editForm.FormClosed += (s, e) => { editForm = null; };
            editForm.Show();   // the user clicked the pencil (or Edit tab): this one takes focus so they can type
            Native.SetForegroundWindow(editForm.Handle); editForm.Activate();
        }

        public static void Remove(BeaconForm b)
        {
            string id = b.Def.Id;
            if (TabStore.IsAdded(id))
            {
                TabStore.RemoveAdded(id);
                Program.RemoveBeacon(b);
                NewBots.Decline(id);   // 1.2.5: its bot sending again doesn't re-ask straight away (asks again after a restart)
                Log.Write("Tab removed: " + id + " (user-added).");
            }
            else
            {
                // a beacon from beacons.json: drop the user's edits and take it off the shelf (Bots > Show or hide brings it back)
                TabStore.Reset(id); TabStore.Save(); TabStore.ApplyName(b.Def);
                Visibility.SetHidden(b, true);
                Log.Write("Tab removed: " + id + " (hidden, edits reset).");
            }
            Sync();
        }

        public static void Changed() { TabStore.Save(); Relayout(); if (shelf != null) RefreshModel(); }

        // ---------------------------------------------------------------- dragging (1.2.1)
        // 1.2.0 ended a drag from the tab window's own MouseUp / capture-lost events and snapped to the nearest edge,
        // seams included. A drop near the seam between two side-by-side monitors snapped to the seam right beside where
        // the tab started, and a press usually landed on the shelf (it slid over the tab after 0.14 s), whose rows could
        // not be dragged. Now: a drag can start from the tab, its card or its shelf row; a 15 ms loop follows
        // Cursor.Position and watches the left button itself (it does not need this window to keep getting mouse
        // messages); on release it docks to the nearest outer edge of the monitor under the cursor, saves that, and
        // re-lays out at once.
        static void StartLoop()
        {
            if (dragTimer == null) { dragTimer = new Timer { Interval = 15 }; dragTimer.Tick += (s, e) => DragPoll(); }
            dragTimer.Start();
        }

        public static bool BeginDragOf(string id)
        {
            var b = Program.Find(id); TabWindow t;
            if (b == null || !tabs.TryGetValue(b, out t)) return false;
            BeginDrag(t); return true;
        }

        public static void BeginDrag(TabWindow t)
        {
            if (Dragging || t.IsDisposed) return;
            if (shelf != null) shelf.CancelPress();
            CloseShelf(true);
            Screens(); if (work.Length == 0) return;
            if (snap == null || snap.IsDisposed) snap = new SnapWindow();
            dragTab = t; t.CancelPress();
            t.Dragging = true; t.DragAt = Cursor.Position; t.DragEdge = t.Edge;
            if (TabStore.Get(t.B.Def.Id).Seam) dwell.Prime(new TabDock { Screen = t.ScreenIdx, Edge = t.Edge, Along = t.Along }); else dwell.Reset();   // 1.2.3
            t.ShowQuiet();
            try { t.Capture = true; } catch { }   // the release comes here even off the window; the loop below does not depend on it
            foreach (var g in grips.Values) g.Hide();
            Log.Write(t.B.Def.Id + ": tab drag started.");
            StartLoop();
            DragMove(t, Cursor.Position);
        }

        public static void BeginDockDrag(ShelfWindow s)
        {
            if (Dragging || s.IsCard) return;
            Screens(); if (work.Length == 0) return;
            if (snap == null || snap.IsDisposed) snap = new SnapWindow();
            dragShelf = s; s.CancelPress(); dwell.Reset();
            var first = DockTabs(s.DockKey).FirstOrDefault();
            if (first != null && TabStore.Get(first.B.Def.Id).Seam) dwell.Prime(new TabDock { Screen = first.ScreenIdx, Edge = first.Edge, Along = first.Along });
            try { s.Capture = true; } catch { }
            StartLoop();
            DockDragMove(s, Cursor.Position);
        }

        // the loop (also called from the tab's own mouse moves, so it follows without lag)
        public static void DragPoll()
        {
            var c = Cursor.Position; bool held = (Control.MouseButtons & MouseButtons.Left) != 0;
            if (dragTab != null) { if (dragTab.IsDisposed) CancelDrag(); else if (held) DragMove(dragTab, c); else EndDrag(dragTab, c); }
            else if (dragShelf != null) { if (dragShelf.IsDisposed) CancelDrag(); else if (held) DockDragMove(dragShelf, c); else DockDragEnd(dragShelf, c); }
            else if (dragTimer != null) dragTimer.Stop();
        }

        public static void DragMove(TabWindow t, Point c)
        {
            if (dragTab != t || work.Length == 0) return;
            TabDock d;
            if (!TabLayout.Undocks(work, c, out d)) { dwell.Reset(); snap.ShowAt(TabLayout.Preview(work[d.Screen], d)); }
            else if (dwell.Update(work, c, clock.Elapsed.TotalSeconds, TabLayout.UndockReach)) { d = dwell.Dock; snap.ShowAt(TabLayout.Preview(work[d.Screen], d)); }   // 1.2.3: held on a seam
            else
            {
                // 1.2.2: far from every outer edge: the outline shows the floating beacon it will become
                var at = TabLayout.FloatAt(work, c, t.B.Size);
                snap.ShowAt(new Rectangle(at, t.B.Size));
            }
            t.DragAt = c; t.DragEdge = d.Edge;
            t.Render(false, Pulse());
        }

        public static void EndDrag(TabWindow t, Point c)
        {
            if (dragTab != t) return;
            dragTab = null; if (dragTimer != null) dragTimer.Stop();
            t.Dragging = false;
            if (snap != null && !snap.IsDisposed) snap.HideNow();
            try { if (t.Capture) t.Capture = false; } catch { }
            Screens();
            TabDock d = new TabDock(); bool seam = false;
            if (work.Length > 0 && TabLayout.Undocks(work, c, out d) && dwell.Update(work, c, clock.Elapsed.TotalSeconds, TabLayout.UndockReach)) { d = dwell.Dock; seam = true; }
            dwell.Reset();
            if (work.Length > 0 && !seam && TabLayout.Undocks(work, c, out d))
            {
                // 1.2.2: dropped well into the screen: this beacon floats again, where it was dropped. Done after this
                // mouse/timer message, because switching disposes this tab window
                var b = t.B; var at = TabLayout.FloatAt(work, c, b.Size);
                Log.Write(b.Def.Id + ": tab dropped " + TabLayout.EdgeDistance(work, c, d) + " px from the nearest outer edge (dropped at " + c.X + "," + c.Y + "): floating.");
                quietUntil = clock.Elapsed.TotalSeconds + 0.35;
                Later(() => { b.PlaceAt(at); Skin.SetMode(b, false); });
                return;
            }
            if (work.Length > 0)
            {
                if (!seam) d = TabLayout.Snap(work, c);
                var p = TabStore.Get(t.B.Def.Id);
                p.Monitor = names[d.Screen]; p.Edge = TabLayout.EdgeName(d.Edge); p.Along = d.Along; p.Seam = seam;
                Log.Write(t.B.Def.Id + ": tab docked to the " + p.Edge + (seam ? " (seam)" : "") + " edge of " + p.Monitor + " at " + d.Along.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " (dropped at " + c.X + "," + c.Y + ").");
            }
            quietUntil = clock.Elapsed.TotalSeconds + 0.35;   // no card popping out under the cursor the instant it lands
            Changed();
        }

        public static void DockDragMove(ShelfWindow s, Point c)
        {
            if (dragShelf != s || work.Length == 0) return;
            var d = dwell.Update(work, c, clock.Elapsed.TotalSeconds, TabLayout.UndockReach) ? dwell.Dock : TabLayout.Snap(work, c);   // 1.2.3: or held on a seam
            snap.ShowAt(TabLayout.Preview(work[d.Screen], d));
        }

        public static void DockDragEnd(ShelfWindow s, Point c)
        {
            if (dragShelf != s) return;
            dragShelf = null; if (dragTimer != null) dragTimer.Stop();
            if (snap != null && !snap.IsDisposed) snap.HideNow();
            try { if (s.Capture) s.Capture = false; } catch { }
            Screens(); if (work.Length == 0) { dwell.Reset(); return; }
            bool seam = dwell.Update(work, c, clock.Elapsed.TotalSeconds, TabLayout.UndockReach);
            var d = seam ? dwell.Dock : TabLayout.Snap(work, c); dwell.Reset();
            var dock = DockTabs(s.DockKey);
            for (int i = 0; i < dock.Count; i++)
            {
                var p = TabStore.Get(dock[i].B.Def.Id);
                p.Monitor = names[d.Screen]; p.Edge = TabLayout.EdgeName(d.Edge); p.Along = Math.Min(1.0, d.Along + i * 0.0001); p.Seam = seam;   // same spot, kept in order; Stack spreads them
            }
            Log.Write("Shelf moved to the " + TabLayout.EdgeName(d.Edge) + " edge of " + names[d.Screen] + " (" + dock.Count + " tabs, dropped at " + c.X + "," + c.Y + ").");
            quietUntil = clock.Elapsed.TotalSeconds + 0.35;
            CloseShelf(true);
            Changed();
        }

        // run after the current message (a window must not be disposed inside its own handler)
        static void Later(Action a)
        {
            var host = Program.Beacons.FirstOrDefault(x => x.IsHandleCreated && !x.IsDisposed);
            if (host != null) host.BeginInvoke(a); else a();
        }

        // ---- 1.2.2: a floating beacon dragged to an outer edge becomes a tab (BeaconForm calls these while it is dragged)
        static BeaconForm floatDrag, dwellBeacon;
        static readonly SeamDwell dwell = new SeamDwell();   // 1.2.3: one drag at a time
        static Timer dwellPoll;
        public static void FloatDragMove(BeaconForm b, Point c)
        {
            Screens(); if (work.Length == 0) return;
            TabDock d, sd;
            bool outer = TabLayout.DocksFloating(work, c, out d);
            bool seam = !outer && dwell.Update(work, c, clock.Elapsed.TotalSeconds);
            if (outer) dwell.Reset();
            if (outer || seam)
            {
                if (seam) d = dwell.Dock;
                if (snap == null || snap.IsDisposed) snap = new SnapWindow();
                floatDrag = b; snap.ShowAt(TabLayout.Preview(work[d.Screen], d));
            }
            else if (floatDrag == b) FloatDragCancel(b);
            // the beacon only reports mouse moves, and holding still sends none: poll while it is near a seam
            if (!outer && TabLayout.SeamAt(work, c, TabLayout.SeamReachPx, out sd))
            {
                dwellBeacon = b;
                if (dwellPoll == null)
                {
                    dwellPoll = new Timer { Interval = 40 };
                    dwellPoll.Tick += (s, e) =>
                    {
                        var k = dwellBeacon;
                        if (k == null || k.IsDisposed || (Control.MouseButtons & MouseButtons.Left) == 0) { dwellPoll.Stop(); return; }
                        FloatDragMove(k, Cursor.Position);
                    };
                }
                if (!dwellPoll.Enabled) dwellPoll.Start();
            }
            else if (dwellPoll != null && dwellPoll.Enabled) dwellPoll.Stop();
        }
        public static void FloatDragCancel(BeaconForm b)
        {
            if (floatDrag != b) return;
            floatDrag = null; if (snap != null && !snap.IsDisposed) snap.HideNow();
        }
        // true: the beacon was dropped at an outer edge and is now a tab there (the caller skips its fling/rest)
        // 1.2.3 throw to dock (settings.ini throwDock=1|0, default on)
        static readonly HashSet<string> settling = new HashSet<string>();
        public static bool ThrowToDock { get { return Settings.Get("throwDock", "1") != "0"; } }
        public static void SetThrowToDock(bool on) { Settings.Set("throwDock", on ? "1" : "0"); Settings.Save(); Log.Write("Throw to dock: " + (on ? "on" : "off") + "."); }
        public static bool FlingDock(BeaconForm b, TabEdge e, Point impact)
        {
            Screens(); TabDock d;
            if (!TabLayout.ImpactDock(work, impact, e, out d)) return false;
            var p = TabStore.Get(b.Def.Id);
            p.Monitor = names[d.Screen]; p.Edge = TabLayout.EdgeName(d.Edge); p.Along = d.Along; p.Seam = false;
            TabStore.Save();
            Log.Write(b.Def.Id + ": thrown into the " + p.Edge + " edge of " + p.Monitor + " (" + impact.X + "," + impact.Y + "): now a tab at " + d.Along.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + ".");
            settling.Add(b.Def.Id); quietUntil = clock.Elapsed.TotalSeconds + 0.6;
            Later(() => Skin.SetMode(b, true));
            return true;
        }

        public static bool FloatDragEnd(BeaconForm b, Point c)
        {
            FloatDragCancel(b);
            dwellBeacon = null; if (dwellPoll != null) dwellPoll.Stop();
            Screens(); if (work.Length == 0) { dwell.Reset(); return false; }
            TabDock d;
            bool outer = TabLayout.DocksFloating(work, c, out d), seam = !outer && dwell.Update(work, c, clock.Elapsed.TotalSeconds);
            if (seam) d = dwell.Dock;
            dwell.Reset();
            if (!outer && !seam) return false;
            var p = TabStore.Get(b.Def.Id);
            p.Monitor = names[d.Screen]; p.Edge = TabLayout.EdgeName(d.Edge); p.Along = d.Along; p.Seam = seam;
            TabStore.Save();
            Log.Write(b.Def.Id + ": dropped at the " + p.Edge + (seam ? " (seam, held)" : "") + " edge of " + p.Monitor + " (" + c.X + "," + c.Y + "): now a tab at " + d.Along.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + ".");
            quietUntil = clock.Elapsed.TotalSeconds + 0.35;
            Later(() => Skin.SetMode(b, true));
            return true;
        }

        static void CancelDrag()
        {
            if (dragTimer != null) dragTimer.Stop();
            if (snap != null && !snap.IsDisposed) snap.HideNow();
            if (dragTab != null) { dragTab.Dragging = false; dragTab = null; }
            dragShelf = null; dwell.Reset();
        }

        // where a bubble opened from the Tabs skin points at
        public static Rectangle AnchorFor(BeaconForm b)
        {
            TabWindow t;
            if (tabs.TryGetValue(b, out t)) return t.BodyOnScreen;
            var wa = Screen.PrimaryScreen.WorkingArea; return new Rectangle(wa.Right - 50, wa.Top + wa.Height / 3, 44, 46);
        }

        public static string Status()
        {
            return "layout=" + Skin.Name + " defaultTabs=" + Skin.DefaultTabs + " tabSize=" + SizeSetting + " tabOpen=" + OpenSetting + " throwDock=" + (ThrowToDock ? 1 : 0) + " tabs=" + tabs.Count
                + " shelf=" + (shelf != null ? (shelf.IsCard ? "card:" + shelf.CardFor.Def.Id : shelf.DockKey) + (shelf.IsClosing ? " (closing)" : "") : "none")
                + " drag=" + (dragTab != null ? dragTab.B.Def.Id : dragShelf != null ? "shelf" : "none")
                + string.Concat(tabs.Values.Select(t => " " + t.B.Def.Id + "@" + KeyOf(t) + ":" + t.Slot.X + "," + t.Slot.Y));
        }

        // dev hook "tabsnap": save the tab and shelf pixels the app is drawing (its own bitmaps, never the screen)
        public static void SaveSnaps(string dir)
        {
            foreach (var t in tabs.Values)
                using (var bmp = TabRender.Tab(t.Edge, new TabRender.TabLook { Image = TabStore.ImageFor(t.B.Def), Color = TabStore.ColorFor(t.B.Def), Attention = t.B.Attention, Unread = t.B.Unread.Count > 0, Depth = t.DepthNow, Pulse = 1 }))
                    bmp.Save(System.IO.Path.Combine(dir, "tab_" + t.B.Def.Id + ".png"));
            if (shelf != null && shelf.Lay != null) using (var bmp = TabRender.Shelf(shelf.Model, shelf.Lay, Point.Empty)) bmp.Save(System.IO.Path.Combine(dir, shelf.IsCard ? "card.png" : "shelf.png"));
        }
        // dev hooks "shelf <beacon>" (the whole shelf of that beacon's edge) and "card <beacon>" (its hover card)
        public static void DevOpenShelf(BeaconForm b) { OpenWholeShelf(b); }
        public static void DevOpenCard(BeaconForm b) { TabWindow t; if (tabs.TryGetValue(b, out t) && CardOf(b) == null) { b.TabAnchor = t.BodyOnScreen; b.OpenBubble(false); } }
    }
}
