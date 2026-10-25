using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace BotBeacons
{
    // 1.2.0 Tabs skin drawing: the bookmark tabs and the shelf, drawn to plain 32-bit bitmaps (no windows), so the box can
    // render previews (tools/TabLayoutHarness.cs.txt) and the windows just blit them with UpdateLayeredWindow.
    static class TabRender
    {
        public static readonly Color Panel = Color.FromArgb(246, 24, 25, 30), Row = Color.FromArgb(255, 34, 36, 42), RowHot = Color.FromArgb(255, 44, 47, 55),
            Fg = Color.FromArgb(232, 234, 238), Dim = Color.FromArgb(146, 151, 160), Faint = Color.FromArgb(60, 255, 255, 255),
            Amber = Color.FromArgb(255, 186, 52), Red = Color.FromArgb(232, 58, 44), Green = Color.FromArgb(70, 200, 112), Grey = Color.FromArgb(120, 124, 132),
            TabBg = Color.FromArgb(244, 30, 32, 38);
        const string FontName = "Segoe UI";

        public static GraphicsPath Round(RectangleF r, float rad) { return Round(r, rad, rad, rad, rad); }
        public static GraphicsPath Round(RectangleF r, float tl, float tr, float br, float bl)
        {
            var p = new GraphicsPath();
            if (tl > 0) p.AddArc(r.Left, r.Top, tl * 2, tl * 2, 180, 90); else p.AddLine(r.Left, r.Top, r.Left, r.Top);
            if (tr > 0) p.AddArc(r.Right - tr * 2, r.Top, tr * 2, tr * 2, 270, 90); else p.AddLine(r.Right, r.Top, r.Right, r.Top);
            if (br > 0) p.AddArc(r.Right - br * 2, r.Bottom - br * 2, br * 2, br * 2, 0, 90); else p.AddLine(r.Right, r.Bottom, r.Right, r.Bottom);
            if (bl > 0) p.AddArc(r.Left, r.Bottom - bl * 2, bl * 2, bl * 2, 90, 90); else p.AddLine(r.Left, r.Bottom, r.Left, r.Bottom);
            p.CloseFigure(); return p;
        }

        static void Hq(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias; g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality; g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        }

        // initials on the tab colour: the tab image until the bot sends its picture or the user picks one
        public static Bitmap Initials(string name, Color c, int size)
        {
            var b = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(b))
            {
                Hq(g); g.Clear(Color.Transparent);
                using (var br = new LinearGradientBrush(new Rectangle(0, 0, size, size), Light(c, 0.25f), Dark(c, 0.25f), 45f)) g.FillRectangle(br, 0, 0, size, size);
                string t = "";
                foreach (var w in (name ?? "").Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries)) { if (char.IsLetterOrDigit(w[0])) t += char.ToUpperInvariant(w[0]); if (t.Length == 2) break; }
                if (t.Length == 0) t = "?";
                using (var f = new Font(FontName, size * (t.Length == 1 ? 0.46f : 0.38f), FontStyle.Bold, GraphicsUnit.Pixel))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString(t, f, Brushes.White, new RectangleF(0, size * 0.03f, size, size), sf);
            }
            return b;
        }
        public static Color Light(Color c, float k) { return Color.FromArgb(c.A, (int)(c.R + (255 - c.R) * k), (int)(c.G + (255 - c.G) * k), (int)(c.B + (255 - c.B) * k)); }
        public static Color Dark(Color c, float k) { return Color.FromArgb(c.A, (int)(c.R * (1 - k)), (int)(c.G * (1 - k)), (int)(c.B * (1 - k))); }
        static Color A(Color c, int a) { return Color.FromArgb(Math.Max(0, Math.Min(255, a)), c.R, c.G, c.B); }

        // 1.2.3: the default tab picture: the beacon's own sprite (orb / robot / cat / echo) on a disc tinted with its colour.
        // The sprite is trimmed to its visible pixels and fitted, so art with wide transparent margins still fills the disc.
        public static Bitmap BeaconPicture(Bitmap sprite, Color c, int size)
        {
            var b = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(b))
            {
                Hq(g); g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.Clear(Color.Transparent);
                using (var p = new GraphicsPath())
                {
                    p.AddEllipse(-size * 0.2f, -size * 0.25f, size * 1.4f, size * 1.4f);
                    using (var br = new PathGradientBrush(p) { CenterColor = Dark(c, 0.35f), SurroundColors = new[] { Dark(c, 0.78f) }, CenterPoint = new PointF(size * 0.5f, size * 0.42f) })
                        g.FillRectangle(br, 0, 0, size, size);
                }
                var vis = Visible(sprite);
                if (vis.Width > 0 && vis.Height > 0)
                {
                    float box = size * 0.76f, k = Math.Min(box / vis.Width, box / vis.Height), w = vis.Width * k, h = vis.Height * k;
                    g.DrawImage(sprite, new RectangleF((size - w) / 2f, (size - h) / 2f, w, h), vis, GraphicsUnit.Pixel);
                }
            }
            return b;
        }
        // the bounding box of a bitmap's visible (alpha > 16) pixels
        public static Rectangle Visible(Bitmap s)
        {
            int x0 = s.Width, y0 = s.Height, x1 = -1, y1 = -1;
            var bd = s.LockBits(new Rectangle(0, 0, s.Width, s.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var row = new byte[s.Width * 4];
                for (int y = 0; y < s.Height; y++)
                {
                    System.Runtime.InteropServices.Marshal.Copy(new IntPtr(bd.Scan0.ToInt64() + (long)y * bd.Stride), row, 0, row.Length);
                    for (int x = 0; x < s.Width; x++) if (row[x * 4 + 3] > 16) { if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; y1 = y; }
                }
            }
            finally { s.UnlockBits(bd); }
            return x1 < 0 ? Rectangle.Empty : Rectangle.FromLTRB(x0, y0, x1 + 1, y1 + 1);
        }

        // round picture with a ring (1.2.3: the ring is drawn inside r, width w, so the picture never spills past its rect)
        public static void Avatar(Graphics g, Bitmap img, RectangleF r, Color ring, float w = 1.2f)
        {
            using (var p = new GraphicsPath())
            {
                p.AddEllipse(r);
                var st = g.Save(); g.SetClip(p, CombineMode.Intersect);
                if (img != null) g.DrawImage(img, r); else using (var b = new SolidBrush(Grey)) g.FillEllipse(b, r);
                g.Restore(st);
            }
            var rr = RectangleF.Inflate(r, -w / 2f, -w / 2f);
            using (var pen = new Pen(ring, w)) g.DrawEllipse(pen, rr);
        }

        static void Dot(Graphics g, PointF c, float rad, Color col, Color edge)
        {
            using (var b = new SolidBrush(col)) g.FillEllipse(b, c.X - rad, c.Y - rad, rad * 2, rad * 2);
            using (var pen = new Pen(edge, 2f)) g.DrawEllipse(pen, c.X - rad, c.Y - rad, rad * 2, rad * 2);
        }

        // 1.2.2: unread count on the tab card's picture
        public static void CountBadge(Graphics g, PointF c, int n, float f)
        {
            string s = n > 9 ? "9+" : n.ToString(); float r = 8.5f * f;
            using (var b = new SolidBrush(Amber)) g.FillEllipse(b, c.X - r, c.Y - r, 2 * r, 2 * r);
            using (var fn = new Font("Segoe UI", 7.5f * f, FontStyle.Bold)) using (var b = new SolidBrush(Color.FromArgb(30, 24, 10)))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString(s, fn, b, new RectangleF(c.X - r, c.Y - r, 2 * r, 2 * r), sf);
        }
        public static void Badge(Graphics g, PointF c, float rad, float pulse)
        {
            using (var halo = new SolidBrush(Color.FromArgb((int)(50 + 80 * pulse), 255, 70, 40))) g.FillEllipse(halo, c.X - rad - 3, c.Y - rad - 3, rad * 2 + 6, rad * 2 + 6);
            using (var b = new SolidBrush(Red)) g.FillEllipse(b, c.X - rad, c.Y - rad, rad * 2, rad * 2);
            using (var pen = new Pen(Color.White, 1.6f)) g.DrawEllipse(pen, c.X - rad, c.Y - rad, rad * 2, rad * 2);
            using (var f = new Font(FontName, rad * 1.45f, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString("!", f, Brushes.White, new RectangleF(c.X - rad, c.Y - rad + 0.5f, rad * 2, rad * 2), sf);
        }

        // canonical (right-edge) point -> the same point on a tab drawn for edge e (1.2.5: shared with TabLayout's badge/glow rects)
        static PointF Map(TabEdge e, PointF p, float W, float H) { return TabLayout.MapBase(e, p); }
        static RectangleF Mx(RectangleF r, float W, bool mir) { return mir ? new RectangleF(W - r.Right, r.Y, r.Width, r.Height) : r; }
        static float OutAngle(TabEdge e) { return e == TabEdge.Right ? 180f : e == TabEdge.Left ? 0f : e == TabEdge.Top ? 90f : 270f; }

        public class TabLook
        {
            public Bitmap Image; public Color Color; public bool Attention, Unread, Hot, Ghost, Connected = true;
            public int Count; public float Depth = TabLayout.Depth; public float Pulse;
        }

        // one tab window's pixels (size TabLayout.WinDepth x WinLength, rotated for top/bottom)
        public static Bitmap Tab(TabEdge e, TabLook k)
        {
            // drawn in base units (1.2.5: 86 x 70, roomier than 1.2.0's 80 x 66 so the badge and glow fit) and scaled by TabLayout.Scale
            const float W = TabLayout.BaseWinDepth, H = TabLayout.BaseWinLength, M = TabLayout.BaseMargin, L = TabLayout.BaseLength;
            float d = TabLayout.DrawDepthBase(k.Depth);
            var can = new Bitmap(TabLayout.WinDepth, TabLayout.WinLength, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(can))
            {
                Hq(g); g.Clear(Color.Transparent); g.ScaleTransform(can.Width / (float)W, can.Height / (float)H);
                bool mir = e == TabEdge.Left;   // left edge: the same tab mirrored (explicit geometry; flips and negative scales are unreliable under Mono)
                var body = Mx(new RectangleF(W - d, M, d + 12, L), W, mir);   // runs past the edge, so only the outer corners are round
                using (var p = Round(body, 11))
                {
                    using (var b = new SolidBrush(k.Hot ? Color.FromArgb(250, 42, 45, 53) : TabBg)) g.FillPath(b, p);
                    if (k.Attention) using (var pen = new Pen(Amber, 2f)) g.DrawPath(pen, p);
                    else using (var pen = new Pen(Color.FromArgb(k.Hot ? 70 : 38, 255, 255, 255), 1f)) g.DrawPath(pen, p);
                }
                // colour strip at the outer end
                using (var b = new SolidBrush(k.Color)) using (var p = Round(Mx(new RectangleF(W - d + 3.5f, M + 9, 3.5f, L - 18), W, mir), 1.7f)) g.FillPath(b, p);
            }
            Bitmap outB = can;
            if (e == TabEdge.Top) can.RotateFlip(RotateFlipType.Rotate270FlipNone);
            else if (e == TabEdge.Bottom) can.RotateFlip(RotateFlipType.Rotate90FlipNone);
            using (var g = Graphics.FromImage(outB))
            {
                Hq(g);
                if (e == TabEdge.Top || e == TabEdge.Bottom) g.ScaleTransform(outB.Width / (float)H, outB.Height / (float)W); else g.ScaleTransform(outB.Width / (float)W, outB.Height / (float)H);
                float ar = TabLayout.AvatarR;
                var cc = Map(e, TabLayout.AvatarCentreBase(d), W, H);   // = TabLayout.ImageRect (inset 10, diameter 30)
                if (k.Attention)
                {
                    using (var halo = new SolidBrush(Color.FromArgb((int)(40 + 70 * k.Pulse), Amber))) g.FillEllipse(halo, cc.X - ar - 4, cc.Y - ar - 4, ar * 2 + 8, ar * 2 + 8);
                    // sound waves on the outer side
                    float a0 = OutAngle(e);
                    for (int i = 0; i < 2; i++)
                    {
                        float rr = ar + 8 + i * 5; int al = (int)((150 - i * 50) * (0.45f + 0.55f * k.Pulse));
                        using (var pen = new Pen(A(Amber, al), 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawArc(pen, cc.X - rr, cc.Y - rr, rr * 2, rr * 2, a0 - 28, 56);
                    }
                }
                // 1.2.3: ringed in the beacon's colour on every edge and size, so the colour coding shows whatever the picture is
                Avatar(g, k.Image, new RectangleF(cc.X - ar, cc.Y - ar, ar * 2, ar * 2), k.Attention ? Amber : k.Color, 2.2f);
                if (!k.Attention && k.Unread) Dot(g, new PointF(cc.X + ar * 0.72f, cc.Y + ar * 0.72f), 4.5f, Amber, TabBg);
                if (k.Attention)
                {
                    var bc = Map(e, TabLayout.BadgeCentreBase(d), W, H);   // 1.2.5: outer corner, overlapping the frame, inside the window
                    Badge(g, bc, TabLayout.BadgeRad, k.Pulse);
                }
            }
            if (k.Ghost)
            {
                var gb = new Bitmap(outB.Width, outB.Height, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(gb)) using (var ia = new ImageAttributes())
                {
                    ia.SetColorMatrix(new ColorMatrix { Matrix33 = 0.75f });
                    g.DrawImage(outB, new Rectangle(0, 0, gb.Width, gb.Height), 0, 0, gb.Width, gb.Height, GraphicsUnit.Pixel, ia);
                }
                outB.Dispose(); outB = gb;
            }
            return outB;
        }

        // 1.2.1: the grip at the end of a strip: a slim pull handle that opens the whole shelf (size in pixels, against edge e)
        public static Bitmap Grip(TabEdge e, Size sz, bool hot)
        {
            var b = new Bitmap(sz.Width, sz.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(b))
            {
                Hq(g); g.Clear(Color.Transparent);
                bool v = e == TabEdge.Left || e == TabEdge.Right;
                float rad = Math.Min(sz.Width, sz.Height) * 0.45f;
                var r = new RectangleF(0.5f, 0.5f, sz.Width - 1, sz.Height - 1);
                // flat against the edge, round on the open side
                float tl = e == TabEdge.Left || e == TabEdge.Top ? 0 : rad, tr = e == TabEdge.Right || e == TabEdge.Top ? 0 : rad,
                      br = e == TabEdge.Right || e == TabEdge.Bottom ? 0 : rad, bl = e == TabEdge.Left || e == TabEdge.Bottom ? 0 : rad;
                using (var p = Round(r, tl, tr, br, bl))
                {
                    using (var f = new SolidBrush(hot ? Color.FromArgb(250, 46, 49, 58) : Color.FromArgb(225, 30, 32, 38))) g.FillPath(f, p);
                    using (var pen = new Pen(Color.FromArgb(hot ? 90 : 46, 255, 255, 255), 1f)) g.DrawPath(pen, p);
                }
                // three dots along the strip
                float cx = sz.Width / 2f, cy = sz.Height / 2f, step = (v ? sz.Height : sz.Width) * 0.2f, dr = Math.Max(1.6f, Math.Min(sz.Width, sz.Height) * 0.09f);
                using (var f = new SolidBrush(hot ? Fg : Dim))
                    for (int i = -1; i <= 1; i++) { float x = v ? cx : cx + i * step, y = v ? cy + i * step : cy; g.FillEllipse(f, x - dr, y - dr, dr * 2, dr * 2); }
            }
            return b;
        }

        // dashed "lands here" outline while a tab is dragged
        public static Bitmap SnapPreview(Size s, string label)
        {
            var b = new Bitmap(s.Width, s.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(b))
            {
                Hq(g); g.Clear(Color.Transparent);
                using (var p = Round(new RectangleF(2, 2, s.Width - 4, s.Height - 4), 9))
                {
                    using (var br = new SolidBrush(Color.FromArgb(46, 140, 170, 255))) g.FillPath(br, p);
                    using (var pen = new Pen(Color.FromArgb(220, 190, 205, 255), 1.6f) { DashStyle = DashStyle.Dash }) g.DrawPath(pen, p);
                }
            }
            return b;
        }

        // ------------------------------------------------------------------ shelf
        // 1.2.1: laid out in base units and drawn scaled by TabLayout.Scale (Size: small 1.1x, normal 1.4x, large 1.7x of
        // these numbers), so the panel is about 460 px wide at Normal. A Card is one tab's row on its own (hover a tab).
        public class ShelfRow
        {
            public string Id = "", Name = "", Purpose = "", Message = "", Time = "";
            public Bitmap Image; public Color Color; public bool Attention, Unread, Connected = true; public int Count;
        }
        public class ShelfModel
        {
            public TabEdge Edge; public List<ShelfRow> Rows = new List<ShelfRow>();
            public string Sounds = "custom";   // custom | classic | off
            public string Volume = "normal";   // quiet | normal | loud (the speaker shows it)
            public string LastSet = "custom";  // what the speaker turns sound back on to
            public string Skin = "Tabs"; public string Hot = ""; public int TotalTabs;
            public string Size = "normal";      // small | normal | large
            public string Hover = "tab";        // tab | shelf: what hovering a tab slides out
            public bool Card;                   // one tab's card instead of the whole shelf
        }
        public class Hit { public Rectangle R; public string Action, Id; public Hit(Rectangle r, string a, string id) { R = r; Action = a; Id = id; } }
        public class ShelfLayout
        {
            public Size Size;            // pixels (the window)
            public float Scale = 1f;     // pixels per base unit; everything below is in base units
            public Rectangle Panel; public List<Hit> Hits = new List<Hit>(); public List<KeyValuePair<Rectangle, ShelfRow>> Rows = new List<KeyValuePair<Rectangle, ShelfRow>>(); public Rectangle Header;
            public List<Rectangle> Groups = new List<Rectangle>();   // 1.2.4: outlined control groups (Sound, Skin, Open on)
            public Rectangle ToPx(Rectangle r) { return Rectangle.FromLTRB((int)Math.Floor(r.Left * Scale), (int)Math.Floor(r.Top * Scale), (int)Math.Ceiling(r.Right * Scale), (int)Math.Ceiling(r.Bottom * Scale)); }
        }

        public const int ShadowPad = 10;
        public const int RowH = 72, CardW = 330;
        public static bool Vertical(TabEdge e) { return e == TabEdge.Left || e == TabEdge.Right; }
        const string NameFont = "Segoe UI Semibold";

        public static ShelfLayout Layout(ShelfModel m)
        {
            var l = new ShelfLayout { Scale = TabLayout.Scale }; bool v = Vertical(m.Edge); int S = ShadowPad;
            // the panel touches its edge; the shadow pad is on the other sides
            int padL = m.Edge == TabEdge.Left ? 0 : S, padR = m.Edge == TabEdge.Right ? 0 : S, padT = m.Edge == TabEdge.Top ? 0 : S, padB = m.Edge == TabEdge.Bottom ? 0 : S;
            Size baseSize;
            if (m.Card)
            {
                int w = CardW, h = RowH + 12;
                l.Panel = new Rectangle(padL, padT, w, h); baseSize = new Size(w + padL + padR, h + padT + padB);
                var P = l.Panel;
                foreach (var r in m.Rows.Count > 0 ? new[] { m.Rows[0] } : new ShelfRow[0])
                {
                    var rr = new Rectangle(P.X + 6, P.Y + 6, P.Width - 12, RowH);
                    l.Rows.Add(new KeyValuePair<Rectangle, ShelfRow>(rr, r));
                    l.Hits.Add(new Hit(new Rectangle(rr.Right - 70, rr.Y + 5, 24, 24), "edit", r.Id));
                    l.Hits.Add(new Hit(rr, "row", r.Id));
                }
                l.Header = Rectangle.Empty;
            }
            else if (v)
            {
                int w = 330, gap = 6, headH = 52, footH = 114;   // 1.2.5: no "+ Add a tab" row (bots create their own tabs)
                int h = headH + m.Rows.Count * (RowH + gap) + 2 + footH;
                l.Panel = new Rectangle(padL, padT, w, h); baseSize = new Size(w + padL + padR, h + padT + padB);
                var P = l.Panel;
                l.Header = new Rectangle(P.X, P.Y, P.Width, headH);
                l.Hits.Add(new Hit(new Rectangle(P.Right - 34, P.Y + 12, 26, 26), "more", ""));
                int y = P.Y + headH;
                foreach (var r in m.Rows)
                {
                    var rr = new Rectangle(P.X + 8, y, P.Width - 16, RowH);
                    l.Rows.Add(new KeyValuePair<Rectangle, ShelfRow>(rr, r));
                    l.Hits.Add(new Hit(new Rectangle(rr.Right - 70, rr.Y + 5, 24, 24), "edit", r.Id));
                    l.Hits.Add(new Hit(rr, "row", r.Id));
                    y += RowH + gap;
                }
                y += 2;
                // 1.2.4: [speaker] Sound: Custom | Classic | Off as one outlined group; then Size; then Skin and Open on
                int fy = y + 10, fy2 = fy + 32, fy3 = fy2 + 32;
                l.Hits.Add(new Hit(new Rectangle(P.X + 14, fy, 26, 24), "speaker", ""));
                l.Hits.Add(new Hit(new Rectangle(P.X + 88, fy, 56, 24), "snd:custom", ""));
                l.Hits.Add(new Hit(new Rectangle(P.X + 144, fy, 56, 24), "snd:classic", ""));
                l.Hits.Add(new Hit(new Rectangle(P.X + 200, fy, 38, 24), "snd:off", ""));
                l.Groups.Add(new Rectangle(P.X + 8, fy - 3, 236, 30));
                l.Hits.Add(new Hit(new Rectangle(P.X + 62, fy2, 50, 24), "size:small", ""));
                l.Hits.Add(new Hit(new Rectangle(P.X + 112, fy2, 56, 24), "size:normal", ""));
                l.Hits.Add(new Hit(new Rectangle(P.X + 168, fy2, 48, 24), "size:large", ""));
                l.Hits.Add(new Hit(new Rectangle(P.X + 11, fy3, 120, 24), "skin", ""));
                l.Groups.Add(new Rectangle(P.X + 8, fy3 - 3, 126, 30));
                l.Hits.Add(new Hit(new Rectangle(P.Right - 135, fy3, 124, 24), "hover", ""));
                l.Groups.Add(new Rectangle(P.Right - 138, fy3 - 3, 130, 30));
                l.Hits.Add(new Hit(l.Header, "header", ""));
            }
            else
            {
                int cardW = 220, cardH = 104, gap = 8, headH = 44, pad = 10;
                int w = Math.Max(980, pad * 2 + m.Rows.Count * (cardW + gap) - gap);
                int h = headH + cardH + pad * 2 - 4;
                l.Panel = new Rectangle(padL, padT, w, h); baseSize = new Size(w + padL + padR, h + padT + padB);
                var P = l.Panel;
                l.Header = new Rectangle(P.X, P.Y, P.Width, headH);
                int fy = P.Y + 10, x0 = P.Right - 10;
                l.Hits.Add(new Hit(new Rectangle(x0 - 26, fy, 26, 24), "more", ""));
                // 1.2.4: the speaker sits in the Sound group ([speaker] Sound: Custom | Classic | Off), not far right on its own
                l.Hits.Add(new Hit(new Rectangle(x0 - 130, fy, 96, 24), "hover", ""));
                l.Groups.Add(new Rectangle(x0 - 133, fy - 3, 102, 30));
                l.Hits.Add(new Hit(new Rectangle(x0 - 236, fy, 92, 24), "skin", ""));
                l.Groups.Add(new Rectangle(x0 - 239, fy - 3, 98, 30));
                l.Hits.Add(new Hit(new Rectangle(x0 - 292, fy, 38, 24), "snd:off", ""));
                l.Hits.Add(new Hit(new Rectangle(x0 - 348, fy, 56, 24), "snd:classic", ""));
                l.Hits.Add(new Hit(new Rectangle(x0 - 404, fy, 56, 24), "snd:custom", ""));
                l.Hits.Add(new Hit(new Rectangle(x0 - 476, fy, 26, 24), "speaker", ""));
                l.Groups.Add(new Rectangle(x0 - 482, fy - 3, 234, 30));
                l.Hits.Add(new Hit(new Rectangle(x0 - 594, fy, 48, 24), "size:large", ""));
                l.Hits.Add(new Hit(new Rectangle(x0 - 650, fy, 56, 24), "size:normal", ""));
                l.Hits.Add(new Hit(new Rectangle(x0 - 700, fy, 50, 24), "size:small", ""));
                int x = P.X + pad, y = P.Y + headH;
                foreach (var r in m.Rows)
                {
                    var rr = new Rectangle(x, y, cardW, cardH);
                    l.Rows.Add(new KeyValuePair<Rectangle, ShelfRow>(rr, r));
                    l.Hits.Add(new Hit(new Rectangle(rr.Right - 30, rr.Y + 8, 24, 24), "edit", r.Id));
                    l.Hits.Add(new Hit(rr, "row", r.Id));
                    x += cardW + gap;
                }
                l.Hits.Add(new Hit(l.Header, "header", ""));
            }
            l.Size = new Size((int)Math.Ceiling(baseSize.Width * l.Scale), (int)Math.Ceiling(baseSize.Height * l.Scale));
            return l;
        }

        // p in window pixels
        public static Hit HitTest(ShelfLayout l, Point p)
        {
            var b = new Point((int)Math.Floor(p.X / l.Scale), (int)Math.Floor(p.Y / l.Scale));
            foreach (var h in l.Hits) if (h.R.Contains(b)) return h;
            return null;
        }

        static void Text(Graphics g, string s, Font f, Color c, RectangleF r, bool right = false, bool center = false)
        {
            using (var b = new SolidBrush(c))
            using (var sf = new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter, Alignment = right ? StringAlignment.Far : center ? StringAlignment.Center : StringAlignment.Near, LineAlignment = StringAlignment.Center })
                g.DrawString(s ?? "", f, b, r, sf);
        }

        static void Pencil(Graphics g, Rectangle r, bool hot)
        {
            if (hot) using (var b = new SolidBrush(Color.FromArgb(60, 255, 255, 255))) g.FillEllipse(b, r);
            float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f;
            using (var pen = new Pen(hot ? Fg : Dim, 2.2f) { StartCap = LineCap.Flat, EndCap = LineCap.Round }) g.DrawLine(pen, cx - 4, cy + 4, cx + 3.5f, cy - 3.5f);
            using (var pen = new Pen(hot ? Fg : Dim, 1f)) g.DrawLine(pen, cx - 5.2f, cy + 5.2f, cx - 3.8f, cy + 3.8f);
        }

        static void Speaker(Graphics g, Rectangle r, bool on, bool hot) { Speaker(g, r, on, hot, "normal"); }
        // 1.2.4: the waves show the volume (quiet 1, normal 2, loud 3); a cross when sound is off
        static void Speaker(Graphics g, Rectangle r, bool on, bool hot, string volume)
        {
            var c = hot ? Fg : Dim; float x = r.X + 6, y = r.Y + r.Height / 2f;
            using (var b = new SolidBrush(c))
            using (var p = new GraphicsPath())
            {
                p.AddPolygon(new[] { new PointF(x, y - 3), new PointF(x + 3, y - 3), new PointF(x + 7, y - 6.5f), new PointF(x + 7, y + 6.5f), new PointF(x + 3, y + 3), new PointF(x, y + 3) });
                g.FillPath(b, p);
            }
            using (var pen = new Pen(c, 1.4f))
            {
                if (on)
                {
                    int n = volume == "quiet" ? 1 : volume == "loud" ? 3 : 2;
                    for (int i = 0; i < n; i++) g.DrawArc(pen, x + 4, y - 5 - i * 3, 8 + i * 4, 10 + i * 6, -50, 100);
                }
                else { g.DrawLine(pen, x + 10, y - 3.5f, x + 15, y + 3.5f); g.DrawLine(pen, x + 15, y - 3.5f, x + 10, y + 3.5f); }
            }
        }

        // up to two lines, word-trimmed with an ellipsis, only whole lines shown
        static void TextWrap(Graphics g, string s, Font f, Color c, RectangleF r)
        {
            using (var b = new SolidBrush(c))
            using (var sf = new StringFormat(StringFormatFlags.LineLimit) { Trimming = StringTrimming.EllipsisWord, Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Near })
                g.DrawString(s ?? "", f, b, r, sf);
        }

        static void Seg(Graphics g, ShelfModel m, ShelfLayout l, Font f, string prefix, string current)
        {
            Rectangle a = Rectangle.Empty;
            foreach (var h in l.Hits) if (h.Action.StartsWith(prefix)) a = a.IsEmpty ? h.R : Rectangle.Union(a, h.R);
            if (a.IsEmpty) return;
            using (var p = Round(a, 6)) { using (var b = new SolidBrush(Color.FromArgb(255, 32, 34, 40))) g.FillPath(b, p); using (var pen = new Pen(Faint)) g.DrawPath(pen, p); }
            foreach (var h in l.Hits)
            {
                if (!h.Action.StartsWith(prefix)) continue;
                string k = h.Action.Substring(prefix.Length); bool sel = current == k, hot = m.Hot == h.Action;
                if (sel) using (var p = Round(new RectangleF(h.R.X + 2, h.R.Y + 2, h.R.Width - 4, h.R.Height - 4), 4.5f)) using (var b = new SolidBrush(Color.FromArgb(255, 62, 66, 76))) g.FillPath(b, p);
                Text(g, char.ToUpper(k[0]) + k.Substring(1), f, sel || hot ? Fg : Dim, h.R, false, true);
            }
        }

        static Hit Find(ShelfLayout l, string action, string id = "") { foreach (var h in l.Hits) if (h.Action == action && h.Id == id) return h; return null; }

        // 1.2.4: hover tips for the shelf controls (ShelfWindow shows them)
        public static string TipFor(string action, ShelfModel m)
        {
            switch (action ?? "")
            {
                case "snd:custom": return "Custom: Bot Beacons alert sounds";
                case "snd:classic": return "Classic: Windows system sounds";
                case "snd:off": return "Off: silent";
                case "speaker":
                    if (m != null && m.Sounds == "off") return "Sound is off. Click to turn it back on (" + Cap(m.LastSet) + "). Right-click: volume";
                    return "Sound: " + Cap(m != null ? m.Sounds : "custom") + ", volume " + (m != null ? m.Volume : "normal") + ". Click to turn it off. Right-click: volume";
                case "skin": return "Skin: all floating beacons, all tabs, or mixed";
                case "hover": return "What opens a tab's card: a click, or just hovering";
                case "size:small": case "size:normal": case "size:large": return "Tab size";
                case "more": return "More options";
                case "row": return "Click: open   Middle-click: ping   Right-click: menu";   // 1.2.6
            }
            return "";
        }
        static string Cap(string s) { return string.IsNullOrEmpty(s) ? "" : char.ToUpper(s[0]) + s.Substring(1); }

        public static string HoverLabel(string open) { return open == "hover" ? "Open on: hover" : "Open on: click"; }

        class Fonts : IDisposable
        {
            public Font Title, Name, Small, Role, Msg, Big;
            public Fonts()
            {
                // base units; at Normal (1.4x) on a 96 DPI screen: name 16 px (12 pt) semibold, message 14 px (10.5 pt), role 12.6 px (9.5 pt)
                Title = new Font(FontName, 14f, FontStyle.Bold, GraphicsUnit.Pixel);
                Name = MakeName(11.5f);
                Small = new Font(FontName, 9f, GraphicsUnit.Pixel);
                Role = new Font(FontName, 9f, GraphicsUnit.Pixel);
                Msg = new Font(FontName, 10f, GraphicsUnit.Pixel);
                Big = new Font(FontName, 18f, GraphicsUnit.Pixel);
            }
            static Font MakeName(float px)
            {
                var f = new Font(NameFont, px, FontStyle.Regular, GraphicsUnit.Pixel);
                if (f.Name == NameFont) return f;   // Segoe UI Semibold exists (Windows); otherwise bold of the plain face
                f.Dispose(); return new Font(FontName, px, FontStyle.Bold, GraphicsUnit.Pixel);
            }
            public void Dispose() { foreach (var f in new[] { Title, Name, Small, Role, Msg, Big }) f.Dispose(); }
        }

        // one tab's row (vertical shelf, and the hover card)
        static void RowV(Graphics g, ShelfModel m, ShelfLayout l, Rectangle rr, ShelfRow r, Fonts F)
        {
            bool hot = m.Hot == "row:" + r.Id || m.Hot == "edit:" + r.Id;
            using (var p = Round(rr, 9))
            {
                using (var b = new SolidBrush(r.Attention ? Color.FromArgb(255, 48, 40, 28) : hot ? RowHot : Row)) g.FillPath(b, p);
                if (r.Attention) using (var pen = new Pen(Amber, 1.4f)) g.DrawPath(pen, p);
            }
            using (var b = new SolidBrush(r.Color)) using (var p = Round(new RectangleF(rr.X - 4, rr.Y + 10, 3.5f, rr.Height - 20), 1.7f)) g.FillPath(b, p);
            var av = new RectangleF(rr.X + 10, rr.Y + (rr.Height - 42) / 2f, 42, 42);
            Avatar(g, r.Image, av, Color.FromArgb(70, 255, 255, 255));
            Dot(g, new PointF(av.Right - 4, av.Bottom - 4), 4.6f, StateColor(r), r.Attention ? Color.FromArgb(255, 48, 40, 28) : Row);
            float nx = rr.X + 62, right = rr.Right - 74;   // right: before the pencil + time
            var nameSz = g.MeasureString(r.Name, F.Name);
            float nameW = Math.Min(nameSz.Width, Math.Max(40, right - nx - 30));
            Text(g, r.Name, F.Name, Fg, new RectangleF(nx, rr.Y + 6, nameW + 2, 20));
            if (r.Purpose.Length > 0) Text(g, r.Purpose, F.Role, Dim, new RectangleF(nx + nameW + 5, rr.Y + 7, right - (nx + nameW + 5), 19));
            var he = Find(l, "edit", r.Id); Pencil(g, he.R, m.Hot == "edit:" + r.Id);
            Text(g, r.Time, F.Small, r.Attention ? Amber : Dim, new RectangleF(rr.Right - 46, rr.Y + 6, 40, 22), true);
            float msgRight = r.Attention || r.Count > 0 ? rr.Right - 36 : rr.Right - 10;
            TextWrap(g, r.Message.Length > 0 ? r.Message : "No messages yet", F.Msg, r.Attention ? Amber : r.Message.Length > 0 ? Fg : Dim, new RectangleF(nx, rr.Y + 29, msgRight - nx, 30));
            if (r.Attention) Badge(g, new PointF(rr.Right - 19, rr.Y + 46), 8.5f, 1f);
            else if (r.Count > 0) CountPill(g, r.Count, new PointF(rr.Right - 19, rr.Y + 46), F.Small);
        }

        static void RowH_(Graphics g, ShelfModel m, ShelfLayout l, Rectangle rr, ShelfRow r, Fonts F)
        {
            bool hot = m.Hot == "row:" + r.Id || m.Hot == "edit:" + r.Id;
            using (var p = Round(rr, 9))
            {
                using (var b = new SolidBrush(r.Attention ? Color.FromArgb(255, 48, 40, 28) : hot ? RowHot : Row)) g.FillPath(b, p);
                if (r.Attention) using (var pen = new Pen(Amber, 1.4f)) g.DrawPath(pen, p);
            }
            using (var b = new SolidBrush(r.Color)) using (var p = Round(new RectangleF(rr.X + 8, rr.Y - 1, rr.Width - 16, 3.5f), 1.7f)) g.FillPath(b, p);
            var av = new RectangleF(rr.X + 10, rr.Y + 12, 34, 34);
            Avatar(g, r.Image, av, Color.FromArgb(70, 255, 255, 255));
            Dot(g, new PointF(av.Right - 3, av.Bottom - 3), 4f, StateColor(r), r.Attention ? Color.FromArgb(255, 48, 40, 28) : Row);
            Text(g, r.Name, F.Name, Fg, new RectangleF(rr.X + 52, rr.Y + 10, rr.Width - 52 - 32, 19));
            Text(g, (r.Purpose.Length > 0 ? r.Purpose + (r.Time.Length > 0 ? "  \u00B7  " : "") : "") + r.Time, F.Role, r.Attention ? Amber : Dim, new RectangleF(rr.X + 52, rr.Y + 29, rr.Width - 52 - 8, 16));
            var he = Find(l, "edit", r.Id); Pencil(g, he.R, m.Hot == "edit:" + r.Id);
            float msgRight = r.Attention || r.Count > 0 ? rr.Right - 32 : rr.Right - 8;
            TextWrap(g, r.Message.Length > 0 ? r.Message : "No messages yet", F.Msg, r.Attention ? Amber : r.Message.Length > 0 ? Fg : Dim, new RectangleF(rr.X + 10, rr.Y + 54, msgRight - rr.X - 10, 30));
            if (r.Attention) Badge(g, new PointF(rr.Right - 17, rr.Y + 70), 8f, 1f);
            else if (r.Count > 0) CountPill(g, r.Count, new PointF(rr.Right - 17, rr.Y + 70), F.Small);
        }

        // offset: the slide animation, in window pixels
        public static Bitmap Shelf(ShelfModel m, ShelfLayout l, Point offset)
        {
            var bmp = new Bitmap(l.Size.Width, l.Size.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                Hq(g); g.Clear(Color.Transparent);
                g.ScaleTransform(l.Scale, l.Scale);
                g.TranslateTransform(offset.X / l.Scale, offset.Y / l.Scale);
                var P = l.Panel; bool v = Vertical(m.Edge);
                float rTL = m.Edge == TabEdge.Left || m.Edge == TabEdge.Top ? 0 : 12, rTR = m.Edge == TabEdge.Right || m.Edge == TabEdge.Top ? 0 : 12,
                      rBR = m.Edge == TabEdge.Right || m.Edge == TabEdge.Bottom ? 0 : 12, rBL = m.Edge == TabEdge.Left || m.Edge == TabEdge.Bottom ? 0 : 12;
                // soft shadow
                for (int i = ShadowPad; i >= 1; i -= 2)
                    using (var sp = Round(new RectangleF(P.X - i * 0.6f, P.Y - i * 0.4f, P.Width + i * 1.2f, P.Height + i * 1.2f), 12 + i)) using (var b = new SolidBrush(Color.FromArgb(9, 0, 0, 0))) g.FillPath(b, sp);
                using (var pp = Round(P, rTL, rTR, rBR, rBL))
                {
                    using (var b = new SolidBrush(Panel)) g.FillPath(b, pp);
                    using (var pen = new Pen(Color.FromArgb(40, 255, 255, 255))) g.DrawPath(pen, pp);
                }
                using (var F = new Fonts())
                {
                    if (m.Card)
                    {
                        foreach (var kv in l.Rows) RowV(g, m, l, kv.Key, kv.Value, F);
                        return bmp;
                    }
                    int needs = 0; foreach (var r in m.Rows) if (r.Attention) needs++;
                    string sub = m.TotalTabs + (m.TotalTabs == 1 ? " tab" : " tabs") + (needs > 0 ? " \u00B7 " + needs + " need" + (needs == 1 ? "s" : "") + " you" : "");
                    if (v)
                    {
                        Text(g, "Bot Beacons", F.Title, Fg, new RectangleF(P.X + 14, P.Y + 9, 200, 21));
                        Text(g, sub, F.Small, Dim, new RectangleF(P.X + 14, P.Y + 30, 220, 15));
                    }
                    else
                    {
                        Text(g, "Bot Beacons", F.Title, Fg, new RectangleF(P.X + 14, P.Y + 11, 110, 21));
                        Text(g, sub, F.Small, Dim, new RectangleF(P.X + 124, P.Y + 13, 140, 18));
                    }
                    // 1.2.4: outlined groups first, so the controls draw on top
                    foreach (var gr in l.Groups) using (var p = Round(gr, 8)) { using (var b = new SolidBrush(Color.FromArgb(10, 255, 255, 255))) g.FillPath(b, p); using (var pen = new Pen(Color.FromArgb(40, 255, 255, 255))) g.DrawPath(pen, p); }
                    var hs = Find(l, "speaker"); Speaker(g, hs.R, m.Sounds != "off", m.Hot == "speaker", m.Volume);
                    Text(g, "Sound:", F.Small, Dim, new RectangleF(hs.R.Right + 1, hs.R.Y, 44, hs.R.Height), false, true);
                    var hm = Find(l, "more");
                    using (var b = new SolidBrush(m.Hot == "more" ? Fg : Dim)) for (int i = -1; i <= 1; i++) g.FillEllipse(b, hm.R.X + hm.R.Width / 2f + i * 5 - 1.6f, hm.R.Y + hm.R.Height / 2f - 1.6f, 3.2f, 3.2f);

                    foreach (var kv in l.Rows) { if (v) RowV(g, m, l, kv.Key, kv.Value, F); else RowH_(g, m, l, kv.Key, kv.Value, F); }

                    // footer (vertical) / header controls (horizontal)
                    if (v)
                    {
                        var hc = Find(l, "snd:custom"); var hz = Find(l, "size:small");
                        using (var pen = new Pen(Color.FromArgb(28, 255, 255, 255))) g.DrawLine(pen, P.X + 10, hc.R.Y - 9, P.Right - 10, hc.R.Y - 9);
                        Text(g, "Size", F.Small, Dim, new RectangleF(P.X + 14, hz.R.Y, 48, hz.R.Height));
                    }
                    Seg(g, m, l, F.Small, "snd:", m.Sounds);
                    Seg(g, m, l, F.Small, "size:", m.Size);
                    var hk = Find(l, "skin");
                    Text(g, "Skin: " + m.Skin + "  \u25BE", F.Small, m.Hot == "skin" ? Fg : Dim, hk.R, false, true);
                    var hv = Find(l, "hover");
                    Text(g, HoverLabel(m.Hover) + "  \u25BE", F.Small, m.Hot == "hover" ? Fg : Dim, hv.R, false, true);
                }
            }
            return bmp;
        }

        static Color StateColor(ShelfRow r) { return r.Attention ? Red : r.Unread ? Amber : r.Connected ? Green : Grey; }

        static void CountPill(Graphics g, int n, PointF c, Font f)
        {
            string s = n > 99 ? "99+" : n.ToString(); float w = Math.Max(18, 8 + s.Length * 6.5f);
            using (var p = Round(new RectangleF(c.X - w / 2, c.Y - 8, w, 16), 8)) using (var b = new SolidBrush(Amber)) g.FillPath(b, p);
            Text(g, s, f, Color.FromArgb(30, 24, 10), new RectangleF(c.X - w / 2, c.Y - 8, w, 16), false, true);
        }
    }
}
