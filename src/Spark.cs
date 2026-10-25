using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Windows.Forms;

namespace BotBeacons
{
    // 1.2.7 handoff sparks. A bot that hands work to another bot calls "beaconctl.py handoff <from> <to> [label]"; the
    // app then sends a small glowing spark in the sender's colour along a gentle arc from the sender's beacon (or tab)
    // to the receiver's, over about a second, and the receiver gives one soft pulse. Nothing else: no bubble, badge,
    // sound or history, and nothing is sent back. Skipped quietly when either beacon has no place on screen, when
    // animations are off, while the session is locked, or when sparks are turned off (settings.ini sparks=0, menu
    // Skin > Handoff sparks). At most one spark per sender/receiver pair every 4 s and 3 on screen at once; extras are
    // dropped. Each spark is a small click-through layered window that moves with the head of the spark: it never
    // takes focus or clicks, and costs one 128 px redraw per frame while it flies.
    static class Sparks
    {
        public const double PairGap = 4.0; public const int MaxLive = 3;
        public static bool On { get { return Settings.Get("sparks", "1") != "0"; } }
        public static void SetOn(bool on)
        {
            Settings.Set("sparks", on ? "1" : "0"); Settings.Save();
            Log.Write("Handoff sparks " + (on ? "on" : "off") + ".");
            if (!on) StopAll();
        }
        public static int Shown, Dropped;   // counters for the status line / harness

        static readonly List<SparkWindow> live = new List<SparkWindow>();
        static readonly Dictionary<string, double> lastPair = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        static readonly Stopwatch clock = Stopwatch.StartNew();
        static FrameTimer timer;

        // the rate limits alone (pure, for tests): may a spark from -> to start at time now?
        public static string Gate(Dictionary<string, double> last, int liveCount, string from, string to, double now)
        {
            if (liveCount >= MaxLive) return "too many sparks on screen";
            double t; if (last.TryGetValue(from + ">" + to, out t) && now - t < PairGap) return "same pair less than " + PairGap + " s ago";
            return null;
        }

        // UI thread
        public static void Handoff(string from, string to, string label)
        {
            string why = Start(from, to, label);
            if (why != null) { Dropped++; Log.Write("Handoff spark skipped (" + why + ")."); }
        }
        static string Start(string from, string to, string label)
        {
            if (!On) return "sparks are off";
            if (AnimSettings.Mode == AnimMode.Off) return "animations are off";
            if (Program.SessionLocked) return "session locked";
            if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to) || string.Equals(from, to, StringComparison.OrdinalIgnoreCase)) return "needs two different beacons";
            var a = Program.Find(from); var b = Program.Find(to);
            if (a == null || b == null) return "unknown beacon";
            Point pa, pb;
            if (!TabsSkin.SparkPoint(a, out pa) || !TabsSkin.SparkPoint(b, out pb)) return "a beacon is hidden";
            live.RemoveAll(x => x.IsDisposed || x.Done);
            double now = clock.Elapsed.TotalSeconds;
            string g = Gate(lastPair, live.Count, from, to, now); if (g != null) return g;
            lastPair[from + ">" + to] = now;
            if (lastPair.Count > 64) { var old = new List<string>(); foreach (var kv in lastPair) if (now - kv.Value > PairGap) old.Add(kv.Key); foreach (var k in old) lastPair.Remove(k); }
            label = (label ?? "").Replace("\r", " ").Replace("\n", " ").Trim(); if (label.Length > 40) label = label.Substring(0, 39) + "\u2026";
            var w = new SparkWindow(a, b, pa, pb, TabStore.ColorFor(a.Def), label, now);
            live.Add(w); Shown++;
            if (timer == null) { timer = new FrameTimer { Interval = 16 }; timer.Tick += (s, e) => Step(); }
            timer.Enabled = true;
            w.Frame(now);
            return null;
        }
        static void Step()
        {
            double now = clock.Elapsed.TotalSeconds;
            foreach (var w in live.ToArray()) { if (!w.IsDisposed && !w.Done) w.Frame(now); if (w.Done) { live.Remove(w); w.Close(); w.Dispose(); } }
            if (live.Count == 0 && timer != null) timer.Enabled = false;
        }
        static void StopAll() { foreach (var w in live) { try { w.Close(); w.Dispose(); } catch { } } live.Clear(); if (timer != null) timer.Enabled = false; }
        public static string Status() { return "sparks=" + (On ? 1 : 0) + " live=" + live.Count + " shown=" + Shown + " dropped=" + Dropped; }

        // the path (pure, for tests): a quadratic arc that bows upward by 18% of the distance (30..160 px)
        public static PointF Control(PointF a, PointF b)
        {
            float dx = b.X - a.X, dy = b.Y - a.Y, len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 1) return a;
            float bow = Math.Max(30f, Math.Min(160f, len * 0.18f));
            float nx = -dy / len, ny = dx / len; if (ny > 0) { nx = -nx; ny = -ny; }   // the normal that points up
            return new PointF((a.X + b.X) / 2 + nx * bow, (a.Y + b.Y) / 2 + ny * bow);
        }
        public static PointF At(PointF a, PointF c, PointF b, float t) { float u = 1 - t; return new PointF(u * u * a.X + 2 * u * t * c.X + t * t * b.X, u * u * a.Y + 2 * u * t * c.Y + t * t * b.Y); }
        public static float Ease(float t) { t = Math.Max(0, Math.Min(1, t)); return (float)(0.5 - 0.5 * Math.Cos(Math.PI * t)); }   // ease in and out
        public static double Duration(PointF a, PointF b) { double d = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y)); return 0.8 + Math.Min(0.4, d / 4000.0); }
    }

    class SparkWindow : LayeredWindow
    {
        const int S = 128;   // the window moves with the spark's head; the trail fits inside it
        readonly BeaconForm to; readonly PointF a; PointF b; readonly Color col; readonly string label;
        readonly double t0, dur; bool arrived; double arrivedAt;
        public bool Done;
        Bitmap labelBmp;

        public SparkWindow(BeaconForm from, BeaconForm to, Point a, Point b, Color col, string label, double now)
        {
            this.to = to; this.a = a; this.b = b; this.col = col; this.label = label; t0 = now;
            dur = Sparks.Duration(this.a, this.b);
            Text = "spark";
        }
        protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x20; return cp; } }   // click-through

        public void Frame(double now)
        {
            if (Done || IsDisposed) return;
            if (!arrived)
            {
                Point pb; if (TabsSkin.SparkPoint(to, out pb)) b = pb;   // follow the receiver if it bobs or moves
                float u = (float)((now - t0) / dur);
                if (u >= 1) { arrived = true; arrivedAt = now; Land(); if (label.Length == 0) { Done = true; Hide(); return; } }
                else { Draw(u); return; }
            }
            // after landing: the label (if any) fades in and out beside the receiver
            double lt = now - arrivedAt; const double fin = 0.15, hold = 1.1, fout = 0.45;
            if (lt >= fin + hold + fout) { Done = true; Hide(); return; }
            float alpha = (float)(lt < fin ? lt / fin : lt < fin + hold ? 1 : 1 - (lt - fin - hold) / fout);
            DrawLabel(alpha);
        }

        void Land()
        {
            if (Skin.IsTab(to)) TabsSkin.FlashTab(to); else to.HandoffPulse();
        }

        void Draw(float u)
        {
            var c = Sparks.Control(a, b);
            float e = Sparks.Ease(u);
            var head = Sparks.At(a, c, b, e);
            int ox = (int)Math.Round(head.X) - S / 2, oy = (int)Math.Round(head.Y) - S / 2;
            float fade = Math.Min(1f, Math.Min(u / 0.12f, (1 - u) / 0.12f + 0.35f));   // soft start, no hard pop at the end
            using (var bmp = new Bitmap(S, S, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Color.Transparent);
                    // the trail: points a little behind the head on the same arc, kept within 54 px so it fits the window
                    for (int i = 7; i >= 1; i--)
                    {
                        float te = Sparks.Ease(Math.Max(0, u - i * 0.022f));
                        var p = Sparks.At(a, c, b, te);
                        float dx = p.X - head.X, dy = p.Y - head.Y; float d = (float)Math.Sqrt(dx * dx + dy * dy);
                        if (d > 54) { dx *= 54 / d; dy *= 54 / d; }
                        float r = 5.5f - i * 0.6f; int al = (int)(fade * (130 - i * 16));
                        if (al <= 0 || r <= 0.5f) continue;
                        using (var br = new SolidBrush(Color.FromArgb(al, col))) g.FillEllipse(br, S / 2 + dx - r, S / 2 + dy - r, 2 * r, 2 * r);
                    }
                    // the head: a soft glow in the sender's colour with a bright core
                    using (var path = new GraphicsPath())
                    {
                        float gr = 16; path.AddEllipse(S / 2 - gr, S / 2 - gr, 2 * gr, 2 * gr);
                        using (var pg = new PathGradientBrush(path) { CenterColor = Color.FromArgb((int)(fade * 200), col), SurroundColors = new[] { Color.FromArgb(0, col) } })
                            g.FillPath(pg, path);
                    }
                    var core = Color.FromArgb((int)(fade * 235), Mix(col, Color.White, 0.65f));
                    using (var br = new SolidBrush(core)) g.FillEllipse(br, S / 2 - 3.5f, S / 2 - 3.5f, 7, 7);
                }
                Blit(bmp, new Point(ox, oy));
            }
            ShowQuiet();
        }

        void DrawLabel(float alpha)
        {
            if (labelBmp == null)
            {
                using (var f = new Font("Segoe UI", 9f, FontStyle.Regular))
                {
                    var sz = TextRenderer.MeasureText(label, f);
                    int w = sz.Width + 16, h = sz.Height + 8;
                    labelBmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
                    using (var g = Graphics.FromImage(labelBmp))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit; g.Clear(Color.Transparent);
                        using (var gp = Round(new RectangleF(0.5f, 0.5f, w - 1, h - 1), h / 2f))
                        {
                            using (var br = new SolidBrush(Color.FromArgb(225, 28, 30, 36))) g.FillPath(br, gp);
                            using (var pn = new Pen(Color.FromArgb(200, col), 1.2f)) g.DrawPath(pn, gp);
                        }
                        using (var br = new SolidBrush(Color.FromArgb(240, 236, 238, 242))) g.DrawString(label, f, br, new RectangleF(8, 3, w - 12, h - 4), StringFormat.GenericTypographic);
                    }
                }
            }
            Point pb; if (TabsSkin.SparkPoint(to, out pb)) b = pb;
            var scr = Screen.FromPoint(Point.Round(b)).WorkingArea;
            int x = (int)b.X - labelBmp.Width / 2, y = (int)b.Y - 46 - labelBmp.Height;
            x = Math.Max(scr.Left + 2, Math.Min(scr.Right - labelBmp.Width - 2, x)); if (y < scr.Top + 2) y = (int)b.Y + 40;
            using (var bmp = new Bitmap(labelBmp.Width, labelBmp.Height, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                using (var ia = new ImageAttributes())
                {
                    g.Clear(Color.Transparent);
                    ia.SetColorMatrix(new ColorMatrix { Matrix33 = Math.Max(0, Math.Min(1, alpha)) });
                    g.DrawImage(labelBmp, new Rectangle(0, 0, bmp.Width, bmp.Height), 0, 0, bmp.Width, bmp.Height, GraphicsUnit.Pixel, ia);
                }
                Blit(bmp, new Point(x, y));
            }
            ShowQuiet();
        }

        static Color Mix(Color x, Color y, float k) { return Color.FromArgb((int)(x.R + (y.R - x.R) * k), (int)(x.G + (y.G - x.G) * k), (int)(x.B + (y.B - x.B) * k)); }
        static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath(); float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90); p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure(); return p;
        }
        protected override void Dispose(bool disposing) { if (disposing && labelBmp != null) { labelBmp.Dispose(); labelBmp = null; } base.Dispose(disposing); }
    }
}
