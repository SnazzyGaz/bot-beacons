using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace BotBeacons
{
    // v0.10: the common frame (one cached, pre-composed sprite, rotated by the sway and offset by a sub-pixel amount)
    // drawn by a small bilinear sampler instead of GDI+. GDI+ took about 2 ms per beacon frame; this samples only the
    // sprite's own area in 16.16 fixed point on premultiplied pixels (two colour channels per multiply).
    // Used for idle and plain-glow frames; actions, attention flares and badges still go through GDI+.
    static class FastBlit
    {
        public sealed class Src { public int W, H, S; public int[] P; public Rectangle Content; }

        // copy a 32bpp premultiplied bitmap into an array with a 1 px transparent border (so taps never need bounds checks)
        public static Src From(Bitmap b, Rectangle content)
        {
            int w = b.Width, h = b.Height, st = w + 2;
            var src = new Src { W = w, H = h, S = st, P = new int[st * (h + 2)], Content = content };
            var d = b.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            try { var row = new int[w]; for (int y = 0; y < h; y++) { Marshal.Copy(d.Scan0 + y * d.Stride, row, 0, w); Array.Copy(row, 0, src.P, (y + 1) * st + 1, w); } }
            finally { b.UnlockBits(d); }
            // the drawn area: this bitmap's own non-transparent box (glow frames reach past the plain sprite)
            int x0 = w, y0 = h, x1 = -1, y1 = -1;
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) if (src.P[(y + 1) * st + x + 1] != 0) { if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y; }
            src.Content = x1 >= 0 ? new Rectangle(x0, y0, x1 - x0 + 1, y1 - y0 + 1) : content;
            return src;
        }

        static uint Lerp(uint a, uint b, uint w)
        {
            uint iw = 256 - w;
            uint rb = (((a & 0x00FF00FFu) * iw + (b & 0x00FF00FFu) * w) >> 8) & 0x00FF00FFu;
            uint ag = (((a >> 8) & 0x00FF00FFu) * iw + ((b >> 8) & 0x00FF00FFu) * w) & 0xFF00FF00u;
            return rb | ag;
        }

        // dst = cleared w*h buffer. Same mapping as the GDI+ path: translate (fx,fy), rotate deg about (cx,cy),
        // pixel centres at +0.5 (PixelOffsetMode.Half), bilinear.
        public static void Draw(Src s, int[] dst, int dw, int dh, float cx, float cy, float deg, float fx, float fy)
        {
            Array.Clear(dst, 0, dst.Length);
            double r = deg * Math.PI / 180, c = Math.Cos(r), sn = Math.Sin(r);
            // destination box: the content rectangle's corners mapped forward, plus 2 px
            var ct = s.Content; double minX = 1e9, minY = 1e9, maxX = -1e9, maxY = -1e9;
            for (int i = 0; i < 4; i++)
            {
                double sx = (i & 1) == 0 ? ct.Left : ct.Right, sy = (i & 2) == 0 ? ct.Top : ct.Bottom;
                double px = sx - cx, py = sy - cy, dx = fx + cx + c * px - sn * py, dy = fy + cy + sn * px + c * py;
                if (dx < minX) minX = dx; if (dx > maxX) maxX = dx; if (dy < minY) minY = dy; if (dy > maxY) maxY = dy;
            }
            int x0 = Math.Max(0, (int)minX - 2), x1 = Math.Min(dw, (int)maxX + 3), y0 = Math.Max(0, (int)minY - 2), y1 = Math.Min(dh, (int)maxY + 3);
            int W = s.W, H = s.H, S = s.S; int[] P = s.P;
            int dU = (int)(c * 65536), dV = (int)(-sn * 65536);
            for (int y = y0; y < y1; y++)
            {
                double qx = x0 + 0.5 - fx - cx, qy = y + 0.5 - fy - cy;
                double u = cx + c * qx + sn * qy - 0.5, v = cy - sn * qx + c * qy - 0.5;
                int U = (int)Math.Floor(u * 65536), V = (int)Math.Floor(v * 65536), row = y * dw;
                for (int x = x0; x < x1; x++, U += dU, V += dV)
                {
                    int iu = U >> 16, iv = V >> 16;
                    if (iu < -1 || iu >= W || iv < -1 || iv >= H) continue;
                    int idx = (iv + 1) * S + iu + 1;
                    uint p00 = (uint)P[idx], p01 = (uint)P[idx + 1], p10 = (uint)P[idx + S], p11 = (uint)P[idx + S + 1];
                    if ((p00 | p01 | p10 | p11) == 0) continue;
                    uint wx = (uint)((U >> 8) & 255), wy = (uint)((V >> 8) & 255);
                    dst[row + x] = (int)Lerp(Lerp(p00, p01, wx), Lerp(p10, p11, wx), wy);
                }
            }
        }
    }
}
