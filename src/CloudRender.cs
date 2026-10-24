using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace BotBeacons
{
    // One history line as the cloud shows it
    class CloudItem { public string Time = "", Who = "", Text = ""; public bool FromUser, Attention; }

    // What the cloud shows (filled in by CloudBubble)
    class CloudModel
    {
        public string Name = "", Subtitle = "is thinking\u2026", Gist = "", Status = "", Cue = "";
        public Color Accent = Color.FromArgb(255, 140, 30);
        public bool Attention; public string AttentionText = "";
        public List<CloudItem> Items = new List<CloudItem>();
        public bool TrailRight = true;          // true: the beacon is to the right of the cloud
        public bool HoverSend, HoverOpen, HoverDismiss, Sending;
        public int Scroll;                      // pixels scrolled down from the top of the history
        public string FieldText = "";           // only drawn by DrawFieldText (frame dumps); live, the TextBox draws it
    }

    // Draws the "Classic cloud" bubble with GDI+ into a premultiplied-alpha bitmap for a layered window: cream cloud
    // (rounded body + bumps), dark outline, soft shadow, a trail of three shrinking circles towards the beacon, title in
    // the beacon's accent colour with an "is thinking..." subtitle, gist line, attention panel, scrolling history,
    // rounded reply field and round send button. Layout first (sizes and hit areas), then Draw.
    class CloudRender : IDisposable
    {
        public const int M = 52, TrailW = 84, BW = 330, Pad = 22, HistMax = 196, FieldH = 34;
        static readonly Color Cream = Color.FromArgb(250, 247, 240), Ink = Color.FromArgb(40, 34, 40), BodyInk = Color.FromArgb(50, 46, 50),
            Dim = Color.FromArgb(150, 140, 140), Faint = Color.FromArgb(160, 150, 146), Field = Color.FromArgb(236, 232, 224), FieldLine = Color.FromArgb(205, 198, 190),
            You = Color.FromArgb(70, 90, 150), Shade = Color.FromArgb(200, 190, 180),
            AttBg = Color.FromArgb(255, 239, 204), AttLine = Color.FromArgb(232, 172, 64), AttInk = Color.FromArgb(150, 86, 0);
        public static readonly Color FieldColor = Field, TextColor = BodyInk, CueColor = Color.FromArgb(150, 142, 140);

        public readonly Font FName = Px("Segoe UI", 18f, FontStyle.Bold), FSub = Px("Segoe UI", 12.5f, FontStyle.Italic), FGist = Px("Segoe UI Semibold", 14f, FontStyle.Regular),
            FTime = Px("Segoe UI", 11f, FontStyle.Regular), FWho = Px("Segoe UI", 12.5f, FontStyle.Bold), FMsg = Px("Segoe UI", 12.5f, FontStyle.Regular),
            FField = Px("Segoe UI", 13.5f, FontStyle.Regular), FFoot = Px("Segoe UI", 10.5f, FontStyle.Regular), FAtt = Px("Segoe UI", 12.5f, FontStyle.Bold), FBtn = Px("Segoe UI Semibold", 12f, FontStyle.Regular);
        static Font Px(string family, float size, FontStyle st) { return new Font(family, size, st, GraphicsUnit.Pixel); }

        // layout results (window coordinates)
        public int W, H, X0, Y0, X1, Y1;
        public Rectangle Hist, FieldRect, SendRect, OpenRect, DismissRect, AttRect, TextBoxRect;
        public RectangleF GistRect, NameRect;
        public int HistTotal, FootY;
        public PointF[] Trail = new PointF[3]; public float[] TrailR = { 15f, 10f, 6.5f };
        public PointF Tip { get { return Trail[2]; } }
        readonly List<float> itemH = new List<float>(), itemWhoW = new List<float>();
        float attTextH;

        readonly Bitmap measureBmp = new Bitmap(1, 1); readonly Graphics mg;
        public CloudRender() { mg = Graphics.FromImage(measureBmp); mg.TextRenderingHint = TextRenderingHint.AntiAlias; }

        SizeF Measure(string s, Font f, float width) { return mg.MeasureString(s.Length == 0 ? " " : s, f, new SizeF(width, 100000f)); }

        public int MaxScroll { get { return Math.Max(0, HistTotal - Hist.Height); } }

        public void Layout(CloudModel m)
        {
            X0 = m.TrailRight ? M : M + TrailW; Y0 = M; X1 = X0 + BW;
            int cx = X0 + Pad, cw = BW - 2 * Pad;
            float y = Y0 - 10;
            NameRect = new RectangleF(cx, y, cw, 26); y += 30;
            if (!m.Attention && m.Gist.Length > 0)
            {
                float gh = Math.Min(Measure(m.Gist, FGist, cw).Height, FGist.GetHeight() * 2 + 2);
                GistRect = new RectangleF(cx, y, cw, gh); y += gh + 8;
            }
            else GistRect = RectangleF.Empty;
            if (m.Attention)
            {
                attTextH = Measure(m.AttentionText, FMsg, cw - 22).Height;
                int ah = (int)(10 + 18 + attTextH + 10);   // v0.10: no buttons in the panel; the footer links clear it
                AttRect = new Rectangle(cx - 4, (int)y, cw + 8, ah);
                y = AttRect.Bottom + 10;
            }
            else AttRect = Rectangle.Empty;
            itemH.Clear(); itemWhoW.Clear(); float total = 0;
            if (m.Items.Count == 0) total = Measure("No messages yet.", FMsg, cw).Height * 2;
            foreach (var it in m.Items)
            {
                float ww = Measure(WhoLabel(it), FWho, cw).Width;
                float tw = cw - 42 - ww;
                if (tw < 120) { ww = 0; tw = cw - 42; }   // very long name: the text goes on its own line
                float h = Measure(it.Text, FMsg, tw).Height + (ww == 0 ? FWho.GetHeight() : 0);
                itemH.Add(h); itemWhoW.Add(ww); total += h + 5;
            }
            HistTotal = (int)Math.Ceiling(total);
            Hist = new Rectangle(cx, (int)y, cw, Math.Min(HistMax, Math.Max(18, HistTotal)));
            y = Hist.Bottom + 12;
            FieldRect = new Rectangle(cx, (int)y, cw - 46, FieldH);
            SendRect = new Rectangle(FieldRect.Right + 10, (int)y, FieldH, FieldH);
            y += FieldH + 9;
            FootY = (int)y; y += 16;
            // footer links (v0.10), right-aligned on the status line: "Open in chat" and "Dismiss"
            {
                var so = Measure("Open in chat", FFoot, 400); var sd = Measure("Dismiss", FFoot, 400);
                int right = X0 + BW - Pad;
                DismissRect = new Rectangle(right - (int)Math.Ceiling(sd.Width), FootY - 2, (int)Math.Ceiling(sd.Width), 18);
                OpenRect = new Rectangle(DismissRect.X - 10 - (int)Math.Ceiling(so.Width), FootY - 2, (int)Math.Ceiling(so.Width), 18);
            }
            Y1 = (int)y + 8;
            W = M + BW + M + TrailW; H = Y1 + M + 26;   // room below for the trail, bumps and the offset shadow
            if (m.TrailRight) { Trail[0] = new PointF(X1 + 22, Y1 + 20); Trail[1] = new PointF(X1 + 48, Y1 + 3); Trail[2] = new PointF(X1 + 69, Y1 - 11); }
            else { Trail[0] = new PointF(X0 - 22, Y1 + 20); Trail[1] = new PointF(X0 - 48, Y1 + 3); Trail[2] = new PointF(X0 - 69, Y1 - 11); }
            int th = (int)Math.Ceiling(FField.GetHeight()) + 4;
            TextBoxRect = new Rectangle(FieldRect.X + 15, FieldRect.Y + (FieldH - th) / 2, FieldRect.Width - 26, th);
            m.Scroll = Math.Max(0, Math.Min(m.Scroll, MaxScroll));
        }

        static string WhoLabel(CloudItem it) { return it.Who + (it.Attention ? " \u26A0" : "") + ":"; }

        // ---------------------------------------------------------------- cloud shape, outline, shadow
        // the cloud as separate pieces (body, bumps, trail); each is filled on its own, which unions them on any GDI+
        List<GraphicsPath> Shape()
        {
            var list = new List<GraphicsPath>();
            int r = Math.Min(46, (Y1 - Y0) / 2);
            list.Add(RoundRect(new RectangleF(X0, Y0, X1 - X0, Y1 - Y0), r));
            var rnd = new Random(3);
            Bumps(list, rnd, X0 + 30, Y0, X1 - 30, Y0, 6, 30);
            Bumps(list, rnd, X0 + 30, Y1, X1 - 30, Y1, 6, 28);
            int side = Math.Max(2, (int)Math.Round((Y1 - Y0 - 60) / 50.0) + 1);
            Bumps(list, rnd, X0, Y0 + 30, X0, Y1 - 30, side, 26);
            Bumps(list, rnd, X1, Y0 + 30, X1, Y1 - 30, side, 26);
            for (int i = 0; i < 3; i++) list.Add(Circle(Trail[i].X, Trail[i].Y, TrailR[i]));
            return list;
        }

        static GraphicsPath Circle(float x, float y, float r) { var p = new GraphicsPath(); p.AddEllipse(x - r, y - r, r * 2, r * 2); return p; }

        static void Bumps(List<GraphicsPath> list, Random rnd, float ax, float ay, float bx, float by, int n, float r)
        {
            for (int i = 0; i < n; i++)
            {
                float t = (i + 0.5f) / n, x = ax + (bx - ax) * t, y = ay + (by - ay) * t, rr = r * (float)(0.85 + rnd.NextDouble() * 0.25);
                list.Add(Circle(x, y, rr));
            }
        }

        public static GraphicsPath RoundRect(RectangleF r, float rad)
        {
            var p = new GraphicsPath(); float d = rad * 2;
            if (rad <= 0.5f) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure(); return p;
        }

        // the cloud background (shadow + outline + fill + inner shade), rebuilt only when the size or side changes
        Bitmap bg; string bgKey = "";
        Bitmap Background(CloudModel m)
        {
            string key = W + "x" + H + ":" + Y1 + ":" + m.TrailRight;
            if (bg != null && key == bgKey) return bg;
            if (bg != null) bg.Dispose();
            byte[] fill, grown;
            var pieces = Shape();
            fill = AlphaOf(pieces, 0f); grown = AlphaOf(pieces, 6f);
            foreach (var pc in pieces) pc.Dispose();
            int n = W * H;
            var sh = Blur(grown, W, H, 7, 3); var inner = Blur(fill, W, H, 9, 3);
            var px = new byte[n * 4];
            const int offY = 8;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int i = y * W + x;
                    // premultiplied BGRA, composed back to front: shadow, outline, cream fill, inner shade
                    float a = 0, r = 0, g = 0, b = 0;
                    int sy = y - offY; float sa = sy >= 0 ? sh[sy * W + x] / 255f * 0.55f : 0f;
                    Over(ref a, ref r, ref g, ref b, sa, 0, 0, 0);
                    Over(ref a, ref r, ref g, ref b, grown[i] / 255f, Ink.R, Ink.G, Ink.B);
                    float fa = fill[i] / 255f;
                    Over(ref a, ref r, ref g, ref b, fa, Cream.R, Cream.G, Cream.B);
                    float edge = Math.Max(0f, fill[i] - inner[i]) / 255f * 0.38f * fa;
                    Over(ref a, ref r, ref g, ref b, edge, Shade.R, Shade.G, Shade.B);
                    int o = i * 4;
                    px[o] = (byte)Math.Min(255f, b); px[o + 1] = (byte)Math.Min(255f, g); px[o + 2] = (byte)Math.Min(255f, r); px[o + 3] = (byte)Math.Min(255f, a * 255f);
                }
            bg = new Bitmap(W, H, PixelFormat.Format32bppPArgb);
            var data = bg.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            for (int y = 0; y < H; y++) Marshal.Copy(px, y * W * 4, data.Scan0 + y * data.Stride, W * 4);
            bg.UnlockBits(data);
            bgKey = key; return bg;
        }

        // "over" in premultiplied space; colour channels kept as 0..255 premultiplied
        static void Over(ref float a, ref float r, ref float g, ref float b, float sa, float sr, float sg, float sb)
        {
            if (sa <= 0) return;
            float k = 1 - sa; a = sa + a * k; r = sr * sa + r * k; g = sg * sa + g * k; b = sb * sa + b * k;
        }

        byte[] AlphaOf(List<GraphicsPath> pieces, float stroke)
        {
            using (var b = new Bitmap(W, H, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(b))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias; g.PixelOffsetMode = PixelOffsetMode.HighQuality; g.Clear(Color.Transparent);
                    foreach (var path in pieces) g.FillPath(Brushes.White, path);
                    if (stroke > 0) using (var pen = new Pen(Color.White, stroke) { LineJoin = LineJoin.Round }) foreach (var path in pieces) g.DrawPath(pen, path);
                }
                var data = b.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                var raw = new byte[data.Stride * H]; Marshal.Copy(data.Scan0, raw, 0, raw.Length); b.UnlockBits(data);
                var a = new byte[W * H];
                for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) a[y * W + x] = raw[y * data.Stride + x * 4 + 3];
                return a;
            }
        }

        // repeated box blur (close to a Gaussian)
        static byte[] Blur(byte[] src, int w, int h, int r, int passes)
        {
            var a = new int[w * h]; for (int i = 0; i < a.Length; i++) a[i] = src[i];
            var t = new int[w * h]; int d = 2 * r + 1;
            for (int p = 0; p < passes; p++)
            {
                for (int y = 0; y < h; y++)
                {
                    int s = 0, row = y * w;
                    for (int x = -r; x <= r; x++) s += a[row + Math.Max(0, Math.Min(w - 1, x))];
                    for (int x = 0; x < w; x++)
                    {
                        t[row + x] = s / d;
                        s += a[row + Math.Min(w - 1, x + r + 1)] - a[row + Math.Max(0, x - r)];
                    }
                }
                for (int x = 0; x < w; x++)
                {
                    int s = 0;
                    for (int y = -r; y <= r; y++) s += t[Math.Max(0, Math.Min(h - 1, y)) * w + x];
                    for (int y = 0; y < h; y++)
                    {
                        a[y * w + x] = s / d;
                        s += t[Math.Min(h - 1, y + r + 1) * w + x] - t[Math.Max(0, y - r) * w + x];
                    }
                }
            }
            var o = new byte[w * h]; for (int i = 0; i < o.Length; i++) o[i] = (byte)Math.Min(255, a[i]); return o;
        }

        // ---------------------------------------------------------------- full frame
        // Text on the cloud: darken the accent until it reads on cream; the send button uses a slightly lighter shade.
        public static Color OnCream(Color c, double maxLum)
        {
            double r = c.R, g = c.G, b = c.B;
            for (int i = 0; i < 20 && Lum(r, g, b) > maxLum; i++) { r *= 0.9; g *= 0.9; b *= 0.9; }
            return Color.FromArgb((int)r, (int)g, (int)b);
        }
        static double Lum(double r, double g, double b)
        {
            Func<double, double> f = v => { v /= 255; return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); };
            return 0.2126 * f(r) + 0.7152 * f(g) + 0.0722 * f(b);
        }

        public void Draw(Graphics g, CloudModel m)
        {
            g.CompositingMode = CompositingMode.SourceCopy; g.DrawImageUnscaled(Background(m), 0, 0);
            g.CompositingMode = CompositingMode.SourceOver;
            g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.AntiAlias; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            Color acc = OnCream(m.Accent, 0.20), accBtn = OnCream(m.Accent, 0.32);
            using (var bAcc = new SolidBrush(acc)) using (var bInk = new SolidBrush(Ink)) using (var bBody = new SolidBrush(BodyInk)) using (var bDim = new SolidBrush(Dim)) using (var bFaint = new SolidBrush(Faint))
            {
                // title + subtitle
                float nw = g.MeasureString(m.Name, FName).Width;
                using (var sf = new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter })
                {
                    g.DrawString(m.Name, FName, bAcc, new RectangleF(NameRect.X - 2, NameRect.Y, NameRect.Width, NameRect.Height + 4), sf);
                    float sx = NameRect.X - 2 + nw + 2;
                    if (sx + 70 < NameRect.Right) g.DrawString(m.Subtitle, FSub, bDim, new RectangleF(sx, NameRect.Y + 6, NameRect.Right - sx, 20), sf);
                }
                if (!GistRect.IsEmpty)
                    using (var sf = new StringFormat { Trimming = StringTrimming.EllipsisWord, FormatFlags = StringFormatFlags.LineLimit })
                        g.DrawString(m.Gist, FGist, bInk, GistRect, sf);

                // attention panel
                if (m.Attention)
                {
                    using (var p = RoundRect(AttRect, 12)) { using (var b = new SolidBrush(AttBg)) g.FillPath(b, p); using (var pen = new Pen(AttLine, 1.3f)) g.DrawPath(pen, p); }
                    using (var bAtt = new SolidBrush(AttInk)) g.DrawString("\u26A0 Needs you in the Grok Bot app", FAtt, bAtt, AttRect.X + 9, AttRect.Y + 8);
                    g.DrawString(m.AttentionText, FMsg, bBody, new RectangleF(AttRect.X + 11, AttRect.Y + 28, AttRect.Width - 22, attTextH + 2));
                }

                // history (clipped, scrolled)
                var oldClip = g.Clip; g.SetClip(Hist);
                float y = Hist.Y - m.Scroll;
                if (m.Items.Count == 0)
                    using (var fi = new Font(FMsg, FontStyle.Italic)) g.DrawString("No messages yet. Type below to talk to " + m.Name + ".", fi, bDim, new RectangleF(Hist.X, Hist.Y, Hist.Width, Hist.Height + 20));
                for (int i = 0; i < m.Items.Count; i++)
                {
                    var it = m.Items[i]; float h = itemH[i], ww = itemWhoW[i];
                    if (y + h >= Hist.Y - 2 && y <= Hist.Bottom + 2)
                    {
                        g.DrawString(it.Time, FTime, bDim, Hist.X + 1, y + 1.5f);
                        var whoB = it.FromUser ? null : bAcc;
                        using (var bYou = new SolidBrush(You)) g.DrawString(WhoLabel(it), FWho, whoB ?? bYou, Hist.X + 40, y);
                        if (ww > 0) g.DrawString(it.Text, FMsg, bBody, new RectangleF(Hist.X + 42 + ww, y, Hist.Width - 42 - ww, h + 2));
                        else g.DrawString(it.Text, FMsg, bBody, new RectangleF(Hist.X + 40, y + FWho.GetHeight(), Hist.Width - 40, h + 2));
                    }
                    y += h + 5;
                }
                g.Clip = oldClip;
                if (MaxScroll > 0)
                {
                    float trackH = Hist.Height, thumbH = Math.Max(24f, trackH * Hist.Height / HistTotal), ty = Hist.Y + (trackH - thumbH) * m.Scroll / MaxScroll;
                    using (var p = RoundRect(new RectangleF(Hist.Right + 6, ty, 5, thumbH), 2.5f)) using (var b = new SolidBrush(Color.FromArgb(70, 40, 34, 40))) g.FillPath(b, p);
                }

                // reply field + send button
                using (var p = RoundRect(FieldRect, FieldH / 2f)) { using (var b = new SolidBrush(Field)) g.FillPath(b, p); using (var pen = new Pen(FieldLine, 1f)) g.DrawPath(pen, p); }
                var sc = m.Sending ? Color.FromArgb(150, accBtn) : (m.HoverSend ? Lighter(accBtn) : accBtn);
                using (var b = new SolidBrush(sc)) g.FillEllipse(b, SendRect);
                float ax = SendRect.X + SendRect.Width / 2f + 1, ay = SendRect.Y + SendRect.Height / 2f, ar = 10f;
                g.FillPolygon(Brushes.White, new[] { new PointF(ax - ar * 0.55f, ay - ar * 0.6f), new PointF(ax + ar * 0.7f, ay), new PointF(ax - ar * 0.55f, ay + ar * 0.6f), new PointF(ax - ar * 0.25f, ay) });

                // footer
                using (var sf = new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter })
                    g.DrawString(m.Status, FFoot, bFaint, new RectangleF(FieldRect.X + 4, FootY, OpenRect.X - 10 - (FieldRect.X + 4), 16), sf);
                // footer links: faint text (amber while an attention call is pending), darker + underlined on hover
                using (var fu = new Font(FFoot, FontStyle.Underline))
                using (var bl = new SolidBrush(m.Attention ? AttInk : Faint)) using (var bh = new SolidBrush(Ink))
                {
                    g.DrawString("Open in chat", m.HoverOpen ? fu : FFoot, m.HoverOpen ? bh : bl, OpenRect.X, FootY);
                    g.DrawString("Dismiss", m.HoverDismiss ? fu : FFoot, m.HoverDismiss ? bh : bl, DismissRect.X, FootY);
                }
            }
        }

        // frame dumps only: what the live TextBox would show (its text, or the grey cue)
        public void DrawFieldText(Graphics g, CloudModel m)
        {
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            bool cue = m.FieldText.Length == 0;
            using (var b = new SolidBrush(cue ? CueColor : TextColor))
            using (var sf = new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter, LineAlignment = StringAlignment.Center })
                g.DrawString(cue ? m.Cue : m.FieldText, FField, b, new RectangleF(TextBoxRect.X - 2, TextBoxRect.Y, TextBoxRect.Width, TextBoxRect.Height), sf);
        }

        static Color Lighter(Color c) { return Color.FromArgb(c.A, Math.Min(255, c.R + 28), Math.Min(255, c.G + 28), Math.Min(255, c.B + 28)); }

        public void Dispose()
        {
            if (bg != null) bg.Dispose(); mg.Dispose(); measureBmp.Dispose();
            foreach (var f in new[] { FName, FSub, FGist, FTime, FWho, FMsg, FField, FFoot, FAtt, FBtn }) f.Dispose();
        }
    }
}
