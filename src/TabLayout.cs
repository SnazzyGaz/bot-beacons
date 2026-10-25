using System;
using System.Collections.Generic;
using System.Drawing;

namespace BotBeacons
{
    // 1.2.0 Tabs skin: pure placement maths (no windows, no Screen calls), so tools/TabLayoutHarness.cs.txt can check it on the box.
    // A tab is docked to one edge of one monitor's WorkingArea. Its spot along that edge is a fraction 0..1 (centre of the tab
    // along the usable length), so a spot survives resolution and scaling changes.
    enum TabEdge { Left, Right, Top, Bottom }

    struct TabDock
    {
        public int Screen;      // index into the work-area list (resolved from the saved monitor name)
        public TabEdge Edge;
        public float Along;     // 0..1
        public bool Vertical { get { return Edge == TabEdge.Left || Edge == TabEdge.Right; } }   // tabs stacked top-to-bottom
    }

    static class TabLayout
    {
        // 1.2.1: everything scales with the Size setting (tabSize=small|normal|large). Normal is the bigger 1.2.1 look.
        public static float Scale = 1.4f;
        public static float ScaleFor(string size) { switch ((size ?? "").Trim().ToLowerInvariant()) { case "small": return 1.1f; case "large": return 1.7f; default: return 1.4f; } }
        public static string SizeName(string size) { var s = (size ?? "").Trim().ToLowerInvariant(); return s == "small" || s == "large" ? s : "normal"; }
        static int S(float v) { return (int)Math.Round(v * Scale); }
        // 1.2.5: the window is bigger than the deepest tab body plus the "!" badge and glow, so nothing is clipped
        // (1.2.4: 80 x 66; a popped-out or wobbling attention tab drew its badge past the window's outer edge)
        public const float BaseWinDepth = 86f, BaseWinLength = 70f;   // the drawing's own units (TabRender scales them)
        public const float BaseLength = 46f, BaseMargin = (BaseWinLength - BaseLength) / 2f;
        public const float MaxDepthBase = 76f;   // deepest body ever drawn (pop-out + throw wobble are clamped to this)
        public const float BadgeInDepth = 4f, BadgeInAlong = 5f;   // badge centre: this far inside the body's outer corner (overlaps the frame)
        public const float BadgeRad = 8.5f, BadgeHalo = 3f;        // the red disc and its pulsing halo
        public const float AvatarR = 15f, AvatarInset = 10f, GlowPad = 4f, WaveOuter = 29f;   // picture, its amber halo, the sound-wave arcs (outer radius incl. pen)
        public static int Length { get { return S(46); } }      // tab size along the edge
        public static int Gap { get { return S(8); } }          // between tabs on one edge
        // 1.2.2: deeper, so the whole picture (ImageRect) sits inside the visible tab with room to spare on every edge
        public static int Depth { get { return S(54); } }       // how far a collapsed tab sticks out from the edge
        public static int AlertDepth { get { return S(66); } }  // an alerted tab pops out further
        public static int NudgeDepth { get { return S(5); } }   // hover (click mode) / unread: a few px further out, no slide-out
        public static int ImageSize { get { return S(30); } }
        public static int ImageInset { get { return S(10); } }  // from the tab's outer end (the end away from the screen edge)
        public static int WinDepth { get { return S(BaseWinDepth); } }    // tab window size across the edge (fixed; the body is drawn inside it)
        public static int WinLength { get { return S(BaseWinLength); } }
        public static int Margin { get { return (WinLength - Length) / 2; } }   // room around the tab body for the "!" badge and the sound waves
        public static int MaxDepth { get { return S(MaxDepthBase); } }

        // 1.2.5: canonical (right-edge, base units) point -> the same point on a tab drawn for edge e (TabRender draws with this)
        public static PointF MapBase(TabEdge e, PointF p)
        {
            float W = BaseWinDepth, H = BaseWinLength;
            switch (e)
            {
                case TabEdge.Left: return new PointF(W - p.X, p.Y);
                case TabEdge.Top: return new PointF(p.Y, W - p.X);
                case TabEdge.Bottom: return new PointF(H - p.Y, p.X);
                default: return p;
            }
        }
        // body depth (base units) TabRender uses for a depth in pixels
        public static float DrawDepthBase(float depthPx) { return Math.Max(20f, Math.Min(MaxDepthBase, depthPx / Scale)); }
        public static PointF BadgeCentreBase(float dBase) { return new PointF(BaseWinDepth - dBase + BadgeInDepth, BaseMargin + BadgeInAlong); }
        public static PointF AvatarCentreBase(float dBase) { return new PointF(BaseWinDepth - dBase + AvatarInset + AvatarR, BaseWinLength / 2f); }
        // a base-unit circle on edge e -> its bounding box in window pixels
        static RectangleF CircleWin(TabEdge e, PointF canon, float r)
        {
            var c = MapBase(e, canon); bool v = e == TabEdge.Left || e == TabEdge.Right;
            float sx = (v ? WinDepth : WinLength) / (v ? BaseWinDepth : BaseWinLength), sy = (v ? WinLength : WinDepth) / (v ? BaseWinLength : BaseWinDepth);
            return new RectangleF((c.X - r) * sx, (c.Y - r) * sy, 2 * r * sx, 2 * r * sy);
        }
        // the "!" badge with its halo, and the attention glow (picture halo + sound waves), for a depth in pixels (window coords)
        public static RectangleF BadgeRect(TabEdge e, float depthPx) { return CircleWin(e, BadgeCentreBase(DrawDepthBase(depthPx)), BadgeRad + BadgeHalo + 1f); }
        // glow = the picture's amber halo (all round) + the two sound-wave arcs (outer side only, +-28 degrees, so they reach WaveOuter outwards)
        public static RectangleF GlowRect(TabEdge e, float depthPx)
        {
            var c = AvatarCentreBase(DrawDepthBase(depthPx)); float hr = AvatarR + GlowPad + 1f;
            var a = CircleWin(e, c, hr); var w = CircleWin(e, new PointF(c.X - (WaveOuter - hr), c.Y), hr);   // the outward reach as a shifted circle
            return RectangleF.Union(a, w);
        }
        public static Size WinSize(TabEdge e) { return e == TabEdge.Left || e == TabEdge.Right ? new Size(WinDepth, WinLength) : new Size(WinLength, WinDepth); }

        public static int GripLength { get { return S(30); } }  // the "open the whole shelf" handle at the end of a strip
        public const int SnapRange = 0; // any drop snaps to the nearest edge of the monitor under the cursor

        public static string EdgeName(TabEdge e) { return e.ToString().ToLowerInvariant(); }
        public static bool TryEdge(string s, out TabEdge e)
        {
            switch ((s ?? "").Trim().ToLowerInvariant())
            {
                case "left": e = TabEdge.Left; return true;
                case "right": e = TabEdge.Right; return true;
                case "top": e = TabEdge.Top; return true;
                case "bottom": e = TabEdge.Bottom; return true;
            }
            e = TabEdge.Right; return false;
        }

        // the monitor a point belongs to: the work area containing it, else the nearest one
        public static int ScreenAt(Rectangle[] work, Point p)
        {
            int best = 0; long bd = long.MaxValue;
            for (int i = 0; i < work.Length; i++)
            {
                var b = work[i]; if (b.Contains(p)) return i;
                long dx = Math.Max(0, Math.Max(b.Left - p.X, p.X - (b.Right - 1))), dy = Math.Max(0, Math.Max(b.Top - p.Y, p.Y - (b.Bottom - 1)));
                long d = dx * dx + dy * dy; if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        // drop point -> dock: the nearest edge of that monitor's work area, and where along it the cursor is
        // 1.2.1: an edge that runs into another monitor (the seam between side-by-side screens) is not a dock: a drop there
        // used to land the tab right beside where it started, so moving to the other monitor looked like it never happened.
        public const int SeamReach = 80;   // a neighbouring work area this close past the edge (taskbar in between) counts
        public static bool Inner(Rectangle[] work, int si, TabEdge e)
        {
            var w = work[si]; Rectangle strip; int len;
            switch (e)
            {
                case TabEdge.Left: strip = new Rectangle(w.Left - SeamReach, w.Top, SeamReach, w.Height); len = w.Height; break;
                case TabEdge.Right: strip = new Rectangle(w.Right, w.Top, SeamReach, w.Height); len = w.Height; break;
                case TabEdge.Top: strip = new Rectangle(w.Left, w.Top - SeamReach, w.Width, SeamReach); len = w.Width; break;
                default: strip = new Rectangle(w.Left, w.Bottom, w.Width, SeamReach); len = w.Width; break;
            }
            long covered = 0;
            for (int i = 0; i < work.Length; i++)
            {
                if (i == si) continue;
                var x = Rectangle.Intersect(strip, work[i]); if (x.IsEmpty) continue;
                covered += e == TabEdge.Left || e == TabEdge.Right ? x.Height : x.Width;
            }
            return covered * 2 >= len;   // at least half the edge faces another monitor
        }

        // a tab saved on a seam (from 1.2.0, or never placed: the default is the primary's right edge) is shown on the same
        // side's outer edge, walking across the desk: on two side-by-side screens, primary right -> the other screen's right.
        public static int Outward(Rectangle[] work, int si, TabEdge e)
        {
            for (int hop = 0; hop < work.Length && Inner(work, si, e); hop++)
            {
                var w = work[si];
                Rectangle strip = e == TabEdge.Left ? new Rectangle(w.Left - SeamReach, w.Top, SeamReach, w.Height) : e == TabEdge.Right ? new Rectangle(w.Right, w.Top, SeamReach, w.Height)
                    : e == TabEdge.Top ? new Rectangle(w.Left, w.Top - SeamReach, w.Width, SeamReach) : new Rectangle(w.Left, w.Bottom, w.Width, SeamReach);
                int best = -1; long area = 0;
                for (int i = 0; i < work.Length; i++) { if (i == si) continue; var x = Rectangle.Intersect(strip, work[i]); long a = (long)x.Width * x.Height; if (a > area) { area = a; best = i; } }
                if (best < 0) break; si = best;
            }
            return si;
        }

        // the edge a drop at p docks to: the monitor under p (else the nearest one), its nearest outer edge
        public static TabDock Snap(Rectangle[] work, Point p)
        {
            int si = ScreenAt(work, p); var w = work[si];
            int x = Math.Max(w.Left, Math.Min(w.Right - 1, p.X)), y = Math.Max(w.Top, Math.Min(w.Bottom - 1, p.Y));
            var dist = new Dictionary<TabEdge, int> { { TabEdge.Right, w.Right - 1 - x }, { TabEdge.Left, x - w.Left }, { TabEdge.Bottom, w.Bottom - 1 - y }, { TabEdge.Top, y - w.Top } };
            var order = new[] { TabEdge.Right, TabEdge.Left, TabEdge.Bottom, TabEdge.Top };   // ties: sides first (the usual place for tabs)
            var d = new TabDock { Screen = si }; int best = int.MaxValue; bool any = false;
            for (int pass = 0; pass < 2 && !any; pass++)
                foreach (var e in order)
                {
                    if (pass == 0 && Inner(work, si, e)) continue;   // second pass only if every edge is a seam (cannot happen with real layouts)
                    if (dist[e] < best) { best = dist[e]; d.Edge = e; any = true; }
                }
            d.Along = AlongFor(w, d.Edge, d.Vertical ? y : x);
            return d;
        }

        // 1.2.3 "throw to dock": a flung floating beacon hitting edge e of a monitor at p docks there, at the impact point,
        // unless that edge is a seam (another monitor continues there: the beacon flies on or bounces as before)
        public static bool ImpactDock(Rectangle[] work, Point p, TabEdge e, out TabDock d)
        {
            d = new TabDock { Edge = e };
            if (work.Length == 0) return false;
            int si = ScreenAt(work, p); if (Inner(work, si, e)) return false;
            var w = work[si];
            int x = Math.Max(w.Left, Math.Min(w.Right - 1, p.X)), y = Math.Max(w.Top, Math.Min(w.Bottom - 1, p.Y));
            d.Screen = si; d.Along = AlongFor(w, e, d.Vertical ? y : x);
            return true;
        }
        // the settle after a throw: how far past its resting depth a just-docked tab sticks out, t seconds after impact
        // (a quick damped wobble; 0 after SettleTime)
        public const float SettleTime = 0.6f;
        public static int SettleDip { get { return S(3); } }   // the wobble swings at most this far inside the resting depth
        public static float SettleExtra(float t)
        {
            if (t < 0 || t >= SettleTime) return 0f;
            return S(22) * (float)(Math.Exp(-t * 7.0) * Math.Cos(t * 2 * Math.PI / 0.3));
        }

        // 1.2.3 seam docking. Seams (an edge that runs into another monitor) are crossed freely, but holding a dragged beacon,
        // tab or shelf still within SeamReach px of one for SeamHold seconds arms a dock there: on that edge of the monitor
        // the cursor is on, so the tab sits on that monitor and slides out into it. Moving on (past the seam, away from it,
        // or more than SeamStill px before it arms) cancels it, and the drag crosses as before.
        public const int SeamReachPx = 12, SeamStill = 8;
        public const double SeamHold = 0.5;
        // the seam edge of the monitor p is on that p is within reach px of (false: not near a seam)
        public static bool SeamAt(Rectangle[] work, Point p, int reach, out TabDock d)
        {
            d = new TabDock();
            for (int si = 0; si < work.Length; si++)
            {
                var w = work[si]; if (!w.Contains(p)) continue;
                TabEdge best = TabEdge.Right; int bd = int.MaxValue;
                foreach (TabEdge e in new[] { TabEdge.Right, TabEdge.Left, TabEdge.Bottom, TabEdge.Top })
                {
                    if (!Inner(work, si, e)) continue;
                    int dist = e == TabEdge.Left ? p.X - w.Left : e == TabEdge.Right ? w.Right - 1 - p.X : e == TabEdge.Top ? p.Y - w.Top : w.Bottom - 1 - p.Y;
                    if (dist <= reach && dist < bd) { bd = dist; best = e; }
                }
                if (bd == int.MaxValue) return false;
                d.Screen = si; d.Edge = best; d.Along = AlongFor(w, best, d.Vertical ? p.Y : p.X);
                return true;
            }
            return false;
        }
        // distance from p to edge e of monitor si (int.MaxValue if p is not on that monitor)
        public static int DistanceTo(Rectangle[] work, Point p, int si, TabEdge e)
        {
            if (si < 0 || si >= work.Length || !work[si].Contains(p)) return int.MaxValue;
            var w = work[si];
            return e == TabEdge.Left ? p.X - w.Left : e == TabEdge.Right ? w.Right - 1 - p.X : e == TabEdge.Top ? p.Y - w.Top : w.Bottom - 1 - p.Y;
        }

        // 1.2.2: dragging switches a beacon between floating and tab. Distances are from the cursor to the edge Snap picks
        // (the nearest OUTER edge of the monitor under the cursor; seams never count), in pixels at any Size.
        public const int DockReach = 24;     // a floating beacon dropped this close to an outer edge (or beyond it) becomes a tab
        public const int UndockReach = 80;   // a tab dropped further than this into the screen becomes a floating beacon
        public static int EdgeDistance(Rectangle[] work, Point p, TabDock d)
        {
            var w = work[d.Screen];
            int x = Math.Max(w.Left, Math.Min(w.Right - 1, p.X)), y = Math.Max(w.Top, Math.Min(w.Bottom - 1, p.Y));
            switch (d.Edge)
            {
                case TabEdge.Left: return x - w.Left;
                case TabEdge.Right: return w.Right - 1 - x;
                case TabEdge.Top: return y - w.Top;
                default: return w.Bottom - 1 - y;
            }
        }
        // a floating beacon dragged to p: true (and where) if it should dock as a tab
        public static bool DocksFloating(Rectangle[] work, Point p, out TabDock d)
        {
            d = Snap(work, p); return EdgeDistance(work, p, d) <= DockReach;
        }
        // a tab dragged to p: true if it should become a floating beacon (d is where it docks otherwise)
        public static bool Undocks(Rectangle[] work, Point p, out TabDock d)
        {
            d = Snap(work, p); return EdgeDistance(work, p, d) > UndockReach;
        }
        // where a floating beacon of size sz lands when a tab is dropped at p: centred on the cursor, kept on that monitor
        public static Point FloatAt(Rectangle[] work, Point p, Size sz)
        {
            var w = work[ScreenAt(work, p)];
            int x = p.X - sz.Width / 2, y = p.Y - sz.Height / 2;
            x = Math.Max(w.Left, Math.Min(w.Right - sz.Width, x)); y = Math.Max(w.Top, Math.Min(w.Bottom - sz.Height, y));
            return new Point(x, y);
        }

        // usable length of an edge for tab centres
        static void Span(Rectangle w, TabEdge e, out int start, out int len)
        {
            bool v = e == TabEdge.Left || e == TabEdge.Right;
            start = v ? w.Top : w.Left; len = v ? w.Height : w.Width;
        }

        public static float AlongFor(Rectangle w, TabEdge e, int coord)
        {
            int s, len; Span(w, e, out s, out len);
            int lo = s + WinLength / 2, hi = s + len - WinLength / 2;
            if (hi <= lo) return 0.5f;
            return Math.Max(0f, Math.Min(1f, (coord - lo) / (float)(hi - lo)));
        }

        // Stack the tabs of one edge: each wants its own spot; overlapping ones are pushed apart (in order of their spot),
        // then the whole run is pulled back inside the edge. Returns each tab's start offset (window, along the edge,
        // relative to the edge start). If they can't all fit they share the space evenly (overlapping).
        public static int[] Stack(int edgeLen, float[] along)
        {
            int n = along.Length; var res = new int[n]; if (n == 0) return res;
            int L = WinLength, step = Length + Gap;   // windows overlap by their margins; bodies keep Gap apart
            var order = new int[n]; for (int i = 0; i < n; i++) order[i] = i;
            Array.Sort(order, (a, b) => { int c = along[a].CompareTo(along[b]); return c != 0 ? c : a.CompareTo(b); });
            int usable = Math.Max(0, edgeLen - L);
            if ((n - 1) * step > usable)
            {
                float st = n > 1 ? usable / (float)(n - 1) : 0;
                for (int k = 0; k < n; k++) res[order[k]] = (int)Math.Round(k * st);
                return res;
            }
            var pos = new int[n];
            for (int k = 0; k < n; k++)
            {
                int want = (int)Math.Round(Math.Max(0f, Math.Min(1f, along[order[k]])) * usable);
                pos[k] = k == 0 ? want : Math.Max(want, pos[k - 1] + step);
            }
            int limit = usable;
            for (int k = n - 1; k >= 0; k--) { if (pos[k] > limit) pos[k] = limit; limit = pos[k] - step; }
            for (int k = 0; k < n; k++) res[order[k]] = pos[k];
            return res;
        }

        // the tab window's rectangle for a start offset on an edge
        public static Rectangle TabWindow(Rectangle w, TabEdge e, int start)
        {
            switch (e)
            {
                case TabEdge.Left: return new Rectangle(w.Left, w.Top + start, WinDepth, WinLength);
                case TabEdge.Right: return new Rectangle(w.Right - WinDepth, w.Top + start, WinDepth, WinLength);
                case TabEdge.Top: return new Rectangle(w.Left + start, w.Top, WinLength, WinDepth);
                default: return new Rectangle(w.Left + start, w.Bottom - WinDepth, WinLength, WinDepth);
            }
        }

        // the visible tab body inside its window for a pop-out depth (window coordinates)
        public static Rectangle Body(TabEdge e, int depth)
        {
            switch (e)
            {
                case TabEdge.Left: return new Rectangle(0, Margin, depth, Length);
                case TabEdge.Right: return new Rectangle(WinDepth - depth, Margin, depth, Length);
                case TabEdge.Top: return new Rectangle(Margin, 0, Length, depth);
                default: return new Rectangle(Margin, WinDepth - depth, Length, depth);
            }
        }

        // 1.2.2: the picture inside a tab window (window coordinates). TabRender draws it here; the harness checks it is
        // inside Body() for every edge, size and depth, so a collapsed tab always shows the whole picture.
        public static Rectangle ImageRect(TabEdge e, int depth)
        {
            int im = ImageSize, along = Margin + (Length - im) / 2, inner = WinDepth - depth + ImageInset;
            switch (e)
            {
                case TabEdge.Left: return new Rectangle(WinDepth - inner - im, along, im, im);
                case TabEdge.Right: return new Rectangle(inner, along, im, im);
                case TabEdge.Top: return new Rectangle(along, WinDepth - inner - im, im, im);
                default: return new Rectangle(along, inner, im, im);
            }
        }

        // 1.2.2: Screen.WorkingArea can include a taskbar (secondary-monitor taskbars, DPI virtualisation), and a taskbar
        // drawn over a bottom tab cut its picture in half. Trim each work area by every taskbar rect lying along one of its edges.
        public static Rectangle[] TrimTaskbars(Rectangle[] work, Rectangle[] bars)
        {
            var o = (Rectangle[])work.Clone();
            for (int i = 0; i < o.Length; i++)
                foreach (var bar in bars)
                {
                    var w = o[i]; var x = Rectangle.Intersect(w, bar);
                    if (x.Width <= 0 || x.Height <= 0) continue;
                    if (x.Width >= x.Height)
                    {
                        if (x.Height > w.Height / 3) continue;   // not a bar along an edge
                        if (x.Top + x.Height / 2 > w.Top + w.Height / 2) w = Rectangle.FromLTRB(w.Left, w.Top, w.Right, x.Top);
                        else w = Rectangle.FromLTRB(w.Left, x.Bottom, w.Right, w.Bottom);
                    }
                    else
                    {
                        if (x.Width > w.Width / 3) continue;
                        if (x.Left + x.Width / 2 > w.Left + w.Width / 2) w = Rectangle.FromLTRB(w.Left, w.Top, x.Left, w.Bottom);
                        else w = Rectangle.FromLTRB(x.Right, w.Top, w.Right, w.Bottom);
                    }
                    o[i] = w;
                }
            return o;
        }

        // 1.2.2: a tab's conversation card, beside its tab (so the tab stays visible and a second click on it closes the
        // card), centred along the tab, clamped to the work area. 1.2.3: past the tab window, which never overlaps it
        public static Rectangle CardBeside(Rectangle w, TabEdge e, Rectangle tabWin, Size size)
        {
            var r = Shelf(w, e, tabWin, size);
            int push = WinDepth + S(4);   // 1.2.3: clear of the whole tab window (not just the body), so no window of the skin overlaps the card
            switch (e)
            {
                case TabEdge.Left: r.X = Math.Min(w.Right - r.Width, w.Left + push); break;
                case TabEdge.Right: r.X = Math.Max(w.Left, w.Right - push - r.Width); break;
                case TabEdge.Top: r.Y = Math.Min(w.Bottom - r.Height, w.Top + push); break;
                default: r.Y = Math.Max(w.Top, w.Bottom - push - r.Height); break;
            }
            return r;
        }

        // The shelf for one edge: it slides out of the edge, centred on the dock's tabs, clamped to the work area.
        // tabsSpan = the bounding box of that edge's tab windows.
        public static Rectangle Shelf(Rectangle w, TabEdge e, Rectangle tabsSpan, Size size)
        {
            int cw = Math.Min(size.Width, w.Width), ch = Math.Min(size.Height, w.Height);
            int x, y;
            if (e == TabEdge.Left || e == TabEdge.Right)
            {
                x = e == TabEdge.Left ? w.Left : w.Right - cw;
                y = tabsSpan.Top + tabsSpan.Height / 2 - ch / 2;
                y = Math.Max(w.Top, Math.Min(w.Bottom - ch, y));
            }
            else
            {
                y = e == TabEdge.Top ? w.Top : w.Bottom - ch;
                x = tabsSpan.Left + tabsSpan.Width / 2 - cw / 2;
                x = Math.Max(w.Left, Math.Min(w.Right - cw, x));
            }
            return new Rectangle(x, y, cw, ch);
        }

        // slide animation: offset of the shelf content towards its edge (k = 0 hidden .. 1 fully out), ease-out cubic
        public static Point SlideOffset(TabEdge e, Size size, float k)
        {
            k = Math.Max(0f, Math.Min(1f, k)); float u = 1f - (1f - k) * (1f - k) * (1f - k);
            int off = (int)Math.Round((1f - u) * (e == TabEdge.Left || e == TabEdge.Right ? size.Width : size.Height));
            switch (e)
            {
                case TabEdge.Left: return new Point(-off, 0);
                case TabEdge.Right: return new Point(off, 0);
                case TabEdge.Top: return new Point(0, -off);
                default: return new Point(0, off);
            }
        }

        // snap preview while dragging: the strip a tab would land in
        public static Rectangle Preview(Rectangle w, TabDock d)
        {
            int usable = Math.Max(0, (d.Vertical ? w.Height : w.Width) - WinLength);
            int start = (int)Math.Round(Math.Max(0f, Math.Min(1f, d.Along)) * usable);
            return TabWindow(w, d.Edge, start);
        }

        // 1.2.1: the grip (opens the whole shelf) sits just past the last tab of a strip, or before the first one if the
        // strip runs to the end of the edge. span = the union of the strip's tab windows.
        public static int GripDepth { get { return S(22); } }
        public static Rectangle Grip(Rectangle w, TabEdge e, Rectangle span)
        {
            int gl = GripLength, gd = GripDepth, inset = Margin - Gap / 2;
            if (e == TabEdge.Left || e == TabEdge.Right)
            {
                int y = span.Bottom - inset; if (y + gl > w.Bottom) y = span.Top + inset - gl;
                y = Math.Max(w.Top, Math.Min(w.Bottom - gl, y));
                return new Rectangle(e == TabEdge.Left ? w.Left : w.Right - gd, y, gd, gl);
            }
            int x = span.Right - inset; if (x + gl > w.Right) x = span.Left + inset - gl;
            x = Math.Max(w.Left, Math.Min(w.Right - gl, x));
            return new Rectangle(x, e == TabEdge.Top ? w.Top : w.Bottom - gd, gl, gd);
        }

        // the edge length of a work area for Stack
        public static int EdgeLength(Rectangle w, TabEdge e) { return e == TabEdge.Left || e == TabEdge.Right ? w.Height : w.Width; }
    }

    // 1.2.3: the seam dwell timer for one drag (fed the cursor every move/poll; see TabLayout.SeamAt)
    class SeamDwell
    {
        Point anchor; double since = -1; int screen = -1; TabEdge edge;
        public bool Armed; public TabDock Dock;
        public void Reset() { since = -1; screen = -1; Armed = false; }
        // a tab that already sits on a seam keeps it while it is dragged within keepReach px of that edge
        public void Prime(TabDock d) { screen = d.Screen; edge = d.Edge; Dock = d; Armed = true; since = 0; }
        public bool Update(Rectangle[] work, Point p, double now, int keepReach = TabLayout.SeamReachPx)
        {
            TabDock d;
            if (Armed && TabLayout.DistanceTo(work, p, screen, edge) <= keepReach)
            {
                var w = work[screen]; Dock.Screen = screen; Dock.Edge = edge; Dock.Along = TabLayout.AlongFor(w, edge, Dock.Vertical ? p.Y : p.X);
                return true;
            }
            if (!TabLayout.SeamAt(work, p, TabLayout.SeamReachPx, out d)) { Reset(); return false; }
            if (Armed || since < 0 || d.Screen != screen || d.Edge != edge || Math.Abs(p.X - anchor.X) > TabLayout.SeamStill || Math.Abs(p.Y - anchor.Y) > TabLayout.SeamStill)
            {
                // a new seam, or it moved too much before arming (or an armed dock was left): start the hold again here
                Armed = false; screen = d.Screen; edge = d.Edge; anchor = p; since = now; return false;
            }
            if (now - since >= TabLayout.SeamHold) { Armed = true; Dock = d; }
            return Armed;
        }
    }
}
