using System;
using System.Drawing;
using System.Windows.Forms;

namespace BotBeacons
{
    // 1.1.4: where a beacon's bubble and right-click menu open, so they're never cut off by a screen edge.
    // Pure math on rectangles (no windows), so the box harness can check every edge, corner and monitor layout.
    // All coordinates are virtual-screen pixels; the app is system-DPI-aware, so Screen and window positions agree.
    struct Placed
    {
        public Point Window;    // where the bubble window goes
        public Rectangle Body;  // the part that must stay on screen (card: the window; cloud: the body and its bumps)
        public bool Right;      // the bubble is to the right of the beacon
        public bool Down;       // the bubble hangs below the beacon's top (card) / the cloud is below the beacon
        public bool Clamped;    // no side fitted, so it was nudged on screen as a last resort
        public Point Nudge;     // how far it was nudged (0,0 unless Clamped)
    }

    static class Placement
    {
        // the taskbar-free area of the monitor the beacon is on (the nearest one if it's between monitors)
        public static Rectangle WorkArea(Rectangle beacon)
        {
            return Screen.FromPoint(new Point(beacon.X + beacon.Width / 2, beacon.Y + beacon.Height / 2)).WorkingArea;
        }

        // Tries the preferred side, then the horizontal flip, then the vertical flip, then both. The first whose body
        // fits inside the work area wins. If none fits, the one showing the most is nudged inside (left/top first if
        // it's bigger than the screen).
        public static Placed Choose(Func<bool, bool, Placed> at, bool preferRight, bool preferDown, Rectangle wa)
        {
            var order = new[] { new[] { preferRight, preferDown }, new[] { !preferRight, preferDown }, new[] { preferRight, !preferDown }, new[] { !preferRight, !preferDown } };
            Placed best = default(Placed); long bestArea = -1;
            foreach (var o in order)
            {
                var p = at(o[0], o[1]);
                if (wa.Contains(p.Body)) return p;
                var vis = Rectangle.Intersect(p.Body, wa); long a = (long)vis.Width * vis.Height;
                if (a > bestArea) { bestArea = a; best = p; }
            }
            int dx = 0, dy = 0; var b = best.Body;
            if (b.Left < wa.Left || b.Width > wa.Width) dx = wa.Left - b.Left; else if (b.Right > wa.Right) dx = wa.Right - b.Right;
            if (b.Top < wa.Top || b.Height > wa.Height) dy = wa.Top - b.Top; else if (b.Bottom > wa.Bottom) dy = wa.Bottom - b.Bottom;
            best.Window.Offset(dx, dy); best.Body.Offset(dx, dy); best.Clamped = true; best.Nudge = new Point(dx, dy);
            return best;
        }

        // Dark card: by default to the right of the beacon, top just below the beacon's top; flips left and/or up.
        public static Placed Card(Rectangle beacon, Size card, Rectangle wa)
        {
            return Choose((right, down) =>
            {
                int x = right ? beacon.Right - 18 : beacon.X - card.Width + 18;
                int y = down ? beacon.Y + 10 : beacon.Bottom - 10 - card.Height;
                return new Placed { Window = new Point(x, y), Body = new Rectangle(x, y, card.Width, card.Height), Right = right, Down = down };
            }, true, true, wa);
        }

        // Cloud: by default to the beacon's left and above it (beacons usually live near the bottom-right). The trail's
        // smallest circle (the tip) always lands on the beacon: near its top when the cloud is above, near its bottom
        // when the cloud is below, on the near side. bodyH is CloudRender's body height (Y1 - Y0), the same either way.
        public const int CloudEdge = 34;   // the bumps and outline around the body that must stay on screen too
        public static Placed Cloud(Rectangle beacon, int bodyH, Rectangle wa)
        {
            return Choose((right, down) =>
            {
                bool trailRight = !right;   // the trail points right when the beacon is right of the cloud
                Point o = CloudRender.Origin(trailRight, down); PointF tip = CloudRender.TipAt(trailRight, down, o.X, o.Y, bodyH);
                int tx = trailRight ? beacon.X + 22 : beacon.Right - 22, ty = down ? beacon.Bottom - 70 : beacon.Y + 70;
                var w = new Point(tx - (int)tip.X, ty - (int)tip.Y);
                var body = new Rectangle(w.X + o.X - CloudEdge, w.Y + o.Y - CloudEdge, CloudRender.BW + 2 * CloudEdge, bodyH + 2 * CloudEdge);
                return new Placed { Window = w, Body = body, Right = right, Down = down };
            }, false, false, wa);
        }

        // Right-click menu at point p: open right/down by default, towards the side with room otherwise.
        // (WinForms still nudges it inside the work area if neither side has room.)
        public static ToolStripDropDownDirection Menu(Point p, Size menu, Rectangle wa)
        {
            bool right = p.X + menu.Width <= wa.Right || (p.X - menu.Width < wa.Left && wa.Right - p.X >= p.X - wa.Left);
            bool down = p.Y + menu.Height <= wa.Bottom || (p.Y - menu.Height < wa.Top && wa.Bottom - p.Y >= p.Y - wa.Top);
            return down ? (right ? ToolStripDropDownDirection.BelowRight : ToolStripDropDownDirection.BelowLeft)
                        : (right ? ToolStripDropDownDirection.AboveRight : ToolStripDropDownDirection.AboveLeft);
        }

        // A submenu opens beside its parent menu: right if it fits, else left, else whichever side has more room.
        public static ToolStripDropDownDirection Submenu(Rectangle parent, int width, Rectangle wa)
        {
            bool right = parent.Right + width <= wa.Right || (parent.Left - width < wa.Left && wa.Right - parent.Right >= parent.Left - wa.Left);
            return right ? ToolStripDropDownDirection.Right : ToolStripDropDownDirection.Left;
        }
    }
}
