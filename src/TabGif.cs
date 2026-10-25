using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace BotBeacons
{
    // 1.2.6: an animated GIF as a tab's custom picture. The original file is kept as tabs\<id>.gif next to the usual
    // tabs\<id>.png (its first frame, which older versions and the still places show). Frames are decoded once at 96 px.
    // Animations Off = the first frame; Subtle = one loop, then an 8 s rest on the first frame; Full = the GIF's own
    // speed. Frame delays are never shorter than one frame at the frame-rate setting (and 20 ms); tabs redraw at most
    // 25 times a second. Profile pictures from bots stay PNG (the relay limit), so only custom pictures animate.
    static class TabGif
    {
        public const long MaxBytes = 5 * 1024 * 1024;
        public const int MaxFrames = 150;
        public const double SubtleRestMs = 8000;

        public static string PathFor(string id) { return Path.Combine(TabStore.CustomDir, id + ".gif"); }

        class Anim { public string Key; public Bitmap[] Frames; public int[] Delays; }
        static readonly Dictionary<string, Anim> cache = new Dictionary<string, Anim>(StringComparer.OrdinalIgnoreCase);
        static readonly Stopwatch clock = Stopwatch.StartNew();

        // the number of frames in an image (1 for stills)
        public static int FrameCount(Image im)
        {
            try { foreach (var g in im.FrameDimensionsList) if (g == FrameDimension.Time.Guid) return im.GetFrameCount(FrameDimension.Time); } catch { }
            return 1;
        }

        // raw GIF delays (1/100 s each, from property 0x5100); 0/1 mean "as fast as possible", which browsers show at 100 ms
        public static int[] Delays(Image im, int n)
        {
            var d = new int[n];
            byte[] raw = null;
            try { raw = im.GetPropertyItem(0x5100).Value; } catch { }
            bool perFrame = raw != null && raw.Length < 4 * n;   // some GDI+ builds give only the active frame's delay
            for (int i = 0; i < n; i++)
            {
                if (perFrame) try { im.SelectActiveFrame(FrameDimension.Time, i); raw = im.GetPropertyItem(0x5100).Value; } catch { raw = null; }
                int at = perFrame ? 0 : 4 * i;
                int cs = raw != null && raw.Length >= at + 4 ? BitConverter.ToInt32(raw, at) : 10;
                d[i] = cs <= 1 ? 100 : cs * 10;
            }
            if (perFrame) try { im.SelectActiveFrame(FrameDimension.Time, 0); } catch { }
            return d;
        }

        // pure (ClickHarness): the shortest frame for a frame-rate setting
        public static int MinDelay(int fps) { return Math.Max(20, fps > 0 ? (int)Math.Ceiling(1000.0 / fps) : 40); }
        public static int[] Clamp(int[] delays, int fps)
        {
            int min = MinDelay(fps); var r = new int[delays.Length];
            for (int i = 0; i < delays.Length; i++) r[i] = Math.Max(min, Math.Min(60000, delays[i]));
            return r;
        }
        // pure (ClickHarness): which frame shows at time ms
        public static int FrameAt(int[] delays, double ms, AnimMode mode)
        {
            if (delays == null || delays.Length <= 1 || mode == AnimMode.Off) return 0;
            long total = 0; foreach (var x in delays) total += x;
            if (total <= 0) return 0;
            double pos;
            if (mode == AnimMode.Subtle) { pos = ms % (total + SubtleRestMs); if (pos >= total) return 0; }
            else pos = ms % total;
            for (int i = 0; i < delays.Length; i++) { if (pos < delays[i]) return i; pos -= delays[i]; }
            return 0;
        }

        // checks a picked GIF; null = fine (or not animated: then it is imported as a still)
        public static string Check(string file, out bool animated)
        {
            animated = false;
            if (new FileInfo(file).Length > MaxBytes) return "That animated GIF is too big (over 5 MB).";
            using (var fs = File.OpenRead(file)) using (var im = Image.FromStream(fs))
            {
                int n = FrameCount(im);
                if (n > MaxFrames) return "That GIF has " + n + " frames; tab pictures can have up to " + MaxFrames + ".";
                animated = n > 1;
            }
            return null;
        }

        // the frame to show now, or null if the tab has no animated picture (TabStore.ImageFor falls back to the still)
        public static Bitmap Current(string id, out int index)
        {
            index = 0;
            var a = Load(id); if (a == null) return null;
            if (!Program.SessionLocked) index = FrameAt(Clamp(a.Delays, FrameClock.EffectiveFps), clock.Elapsed.TotalMilliseconds, AnimSettings.Mode);
            return a.Frames[Math.Min(index, a.Frames.Length - 1)];
        }

        static Anim Load(string id)
        {
            string f = PathFor(id);
            if (!File.Exists(f)) { Drop(id); return null; }
            // a still picked later by an older version (it only writes the PNG) wins over a leftover GIF
            try { string png = TabStore.CustomPath(id); if (File.Exists(png) && File.GetLastWriteTimeUtc(png) > File.GetLastWriteTimeUtc(f).AddSeconds(5)) { Drop(id); return null; } } catch { }
            string key; try { key = File.GetLastWriteTimeUtc(f).Ticks + "|" + new FileInfo(f).Length; } catch { return null; }
            Anim a;
            if (cache.TryGetValue(id, out a) && a.Key == key) return a.Frames == null ? null : a;
            Drop(id);
            a = new Anim { Key = key };
            try
            {
                if (new FileInfo(f).Length <= MaxBytes)
                    using (var fs = File.OpenRead(f)) using (var im = Image.FromStream(fs))
                    {
                        int n = FrameCount(im);
                        if (n > 1 && n <= MaxFrames)
                        {
                            a.Delays = Delays(im, n); a.Frames = new Bitmap[n];
                            for (int i = 0; i < n; i++) { im.SelectActiveFrame(FrameDimension.Time, i); a.Frames[i] = TabStore.Square(im, 96); }
                        }
                    }
            }
            catch (Exception ex) { Log.Write(id + ": animated tab picture could not be read (" + ex.Message + "); showing the still."); a.Frames = null; }
            cache[id] = a;   // a failed or non-animated file is remembered too (not re-read every frame)
            return a.Frames == null ? null : a;
        }

        public static void Drop(string id)
        {
            Anim a; if (!cache.TryGetValue(id, out a)) return;
            if (a.Frames != null) foreach (var b in a.Frames) if (b != null) b.Dispose();
            cache.Remove(id);
        }
    }
}
