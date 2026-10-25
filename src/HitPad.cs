using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace BotBeacons
{
    // 1.2.3: a floating beacon is a per-pixel-alpha (layered) window, and Windows only hit-tests pixels with alpha > 0,
    // so clicks went through the gaps in the sprite (between ears, round the antenna, its soft rim) and the beacon felt
    // like a small button. The pad is the sprite's visible pixels grown by Margin px; every frame, pad pixels the frame
    // left fully transparent get alpha 1 (premultiplied black: invisible, but they take the mouse).
    // 1.2.4: still hit and miss. The pad hugged the sprite as drawn upright, but the sprite is drawn tilted (sway, alert
    // hops, wiggles, play tilts) about its pivot, and holes wider than 2 x Margin (the dip between ears, round an
    // antenna) stayed click-through. Now: the visible pixels of every frame, swept through +/-TiltDeg about the pivot,
    // grown by Margin, then filled solid row by row (a silhouette with no holes).
    static class HitPad
    {
        public const int Margin = 8;
        public const float TiltDeg = 12f;
        public const int Pixel = 0x01000000;
        public static int[] Build(Bitmap[] frames, int margin) { return Build(frames, margin, 0f, 0f, 0f); }
        public static int[] Build(Bitmap[] frames, int margin, float pivotX, float pivotY, float tiltDeg)
        {
            int w = frames[0].Width, h = frames[0].Height;
            var vis = new bool[w * h];
            foreach (var f in frames)
            {
                if (f == null || f.Width != w || f.Height != h) continue;
                var bd = f.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    var row = new int[w];
                    for (int y = 0; y < h; y++)
                    {
                        Marshal.Copy(new IntPtr(bd.Scan0.ToInt64() + (long)y * bd.Stride), row, 0, w);
                        for (int x = 0; x < w; x++) if (((row[x] >> 24) & 255) > 8) vis[y * w + x] = true;
                    }
                }
                finally { f.UnlockBits(bd); }
            }
            return Solid(vis, w, h, margin, pivotX, pivotY, tiltDeg);
        }
        // the 1.2.4 pad: sweep (tilt) + grow (margin) + row fill
        public static int[] Solid(bool[] vis, int w, int h, int margin, float pivotX, float pivotY, float tiltDeg)
        {
            var swept = tiltDeg > 0 ? Sweep(vis, w, h, pivotX, pivotY, tiltDeg) : vis;
            var grown = new bool[w * h]; foreach (int i in Grow(swept, w, h, margin)) grown[i] = true;
            var list = new List<int>();
            for (int y = 0; y < h; y++)
            {
                int a = -1, b = -1;
                for (int x = 0; x < w; x++) if (grown[y * w + x]) { if (a < 0) a = x; b = x; }
                if (a >= 0) for (int x = a; x <= b; x++) list.Add(y * w + x);
            }
            return list.ToArray();
        }
        // every pixel the sprite covers at some tilt in [-deg, +deg] (2 degree steps) about (px, py)
        public static bool[] Sweep(bool[] vis, int w, int h, float px, float py, float deg)
        {
            var o = new bool[w * h];
            int steps = Math.Max(1, (int)Math.Ceiling(deg / 2f));
            for (int k = -steps; k <= steps; k++)
            {
                double a = deg * k / steps * Math.PI / 180, c = Math.Cos(a), sn = Math.Sin(a);
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        if (o[y * w + x]) continue;
                        double dx = x + 0.5 - px, dy = y + 0.5 - py;   // inverse-rotate the destination pixel into the sprite
                        int sx = (int)Math.Floor(px + dx * c + dy * sn), sy = (int)Math.Floor(py - dx * sn + dy * c);
                        if (sx >= 0 && sy >= 0 && sx < w && sy < h && vis[sy * w + sx]) o[y * w + x] = true;
                    }
            }
            return o;
        }
        // indices of every pixel within margin px of a visible one
        public static int[] Grow(bool[] vis, int w, int h, int margin)
        {
            var pad = new bool[w * h]; int m2 = margin * margin;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (!vis[y * w + x]) continue;
                    for (int dy = -margin; dy <= margin; dy++)
                    {
                        int yy = y + dy; if (yy < 0 || yy >= h) continue;
                        for (int dx = -margin; dx <= margin; dx++)
                        {
                            int xx = x + dx; if (xx < 0 || xx >= w || dx * dx + dy * dy > m2) continue;
                            pad[yy * w + xx] = true;
                        }
                    }
                }
            var list = new List<int>();
            for (int i = 0; i < pad.Length; i++) if (pad[i]) list.Add(i);
            return list.ToArray();
        }
        public static void Apply(int[] pad, int[] frame)
        {
            if (pad == null) return;
            foreach (int i in pad) if (i < frame.Length && (frame[i] & unchecked((int)0xFF000000)) == 0) frame[i] = Pixel;
        }
        public static void Apply(int[] pad, IntPtr bits)
        {
            if (pad == null || bits == IntPtr.Zero) return;
            foreach (int i in pad) { var o = i * 4; if ((Marshal.ReadInt32(bits, o) & unchecked((int)0xFF000000)) == 0) Marshal.WriteInt32(bits, o, Pixel); }
        }
    }
}
