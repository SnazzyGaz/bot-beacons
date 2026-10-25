using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.IO;
using System.Web.Script.Serialization;

namespace BotBeacons
{
    // One entry of beacons.json
    class BeaconDef
    {
        public string Id = "", Name = "", Purpose = "", Art = "", AlertArt = "", Gist = "";   // Gist: one-line sample shown by "Test glow"
        public Color Color = Color.FromArgb(255, 110, 40);
        public PointF Eye = new PointF(70, 110), Lamp = new PointF(67, 160);
        public bool Fallback;   // gets messages addressed to unknown bot ids
        public bool Test;       // test beacon: hidden unless "Show test beacons" is on
        public string AgentId = "";   // the Grok Bot agent this beacon carries messages for (info only)
        public string Sound = "", AttentionSound = "";   // 1.1.5: WAV file names in sounds\ ("" = theme default)
        // layered art: a shared base beacon + overlay PNGs (same canvas size), eye/lamp tinted at load
        public string Base = "";
        public List<string> Overlays = new List<string>();
        public Color Tint = Color.Empty;   // empty = use Color
        public float EyeR = 11, LampR = 7;
        public float EyeGlow, LampGlow;   // alert glow radius in px ("eyeGlow"/"lampGlow"); 0 = 2.6 x eyeR / 2.8 x lampR
        // the eye glow drawn OVER the art: radius ("glowOver", -1 = same as eyeGlow, 0 = none), strength ("glowOverAlpha"
        // 0-255, default 100) and the white core ("eyeCore" radius, default 5, 0 = none). Lower these for dark visors.
        public float GlowOver = -1, GlowOverAlpha = 100, EyeCore = 5;
        public bool Layered { get { return Base.Length > 0; } }
        public AnimSpec Anim = new AnimSpec();   // idle actions + twitches ("anim" in beacons.json, over the top-level defaults)
    }

    // %APPDATA%\BotBeacons\beacons.json (created from the built-in default on first run).
    // Art is looked up in %APPDATA%\BotBeacons\art\, then next to the exe in art\, then inside the exe.
    static class BeaconConfig
    {
        public static string FilePath { get { return Path.Combine(Settings.Dir, "beacons.json"); } }

        static int Version(string text)
        {
            try { var r = new JavaScriptSerializer().DeserializeObject(text) as Dictionary<string, object>; return Convert.ToInt32(r["version"]); } catch { return 0; }
        }

        public static List<BeaconDef> Load()
        {
            string text = null;
            try { if (File.Exists(FilePath)) text = File.ReadAllText(FilePath); } catch { }
            if (text != null)
            {
                // a newer built-in beacon list replaces an older copied one (the old file is kept as a backup)
                string emb = ReadEmbedded("beacons.json");
                int have = Version(text), want = Version(emb);
                if (want > have)
                {
                    try { File.Copy(FilePath, Path.Combine(Settings.Dir, "beacons.v" + have + ".bak.json"), true); File.WriteAllText(FilePath, emb); } catch { }
                    Log.Write("beacons.json upgraded from version " + have + " to " + want + " (old copy kept).");
                    text = emb;
                }
            }
            if (text == null)
            {
                text = ReadEmbedded("beacons.json");
                try { File.WriteAllText(FilePath, text); } catch { }
            }
            var list = new List<BeaconDef>();
            try
            {
                var root = new JavaScriptSerializer().DeserializeObject(text) as Dictionary<string, object>;
                object ta; root.TryGetValue("anim", out ta);
                AnimSpec.Theme = AnimSpec.Parse(ta as Dictionary<string, object>, null);
                var arr = root["beacons"] as object[];
                foreach (var it in arr)
                {
                    var o = it as Dictionary<string, object>; if (o == null) continue;
                    var d = new BeaconDef { Id = Crypto.Str(o, "id"), Name = Crypto.Str(o, "name"), Purpose = Crypto.Str(o, "purpose"), Art = Crypto.Str(o, "art"), AlertArt = Crypto.Str(o, "alertArt") };
                    d.Base = Crypto.Str(o, "base"); d.AgentId = Crypto.Str(o, "agentId"); d.Gist = Crypto.Str(o, "gist");
                    d.Sound = Crypto.Str(o, "sound"); d.AttentionSound = Crypto.Str(o, "attentionSound");
                    object ov; if (o.TryGetValue("overlays", out ov) && ov is object[]) foreach (var x in (object[])ov) if (x != null) d.Overlays.Add(x.ToString());
                    try { var t = Crypto.Str(o, "tint"); if (t.Length > 0) d.Tint = ColorTranslator.FromHtml(t); } catch { }
                    object rv; if (o.TryGetValue("eyeR", out rv)) d.EyeR = Convert.ToSingle(rv); if (o.TryGetValue("lampR", out rv)) d.LampR = Convert.ToSingle(rv);
                    if (o.TryGetValue("eyeGlow", out rv)) d.EyeGlow = Convert.ToSingle(rv); if (o.TryGetValue("lampGlow", out rv)) d.LampGlow = Convert.ToSingle(rv);
                    if (o.TryGetValue("glowOver", out rv)) d.GlowOver = Convert.ToSingle(rv); if (o.TryGetValue("glowOverAlpha", out rv)) d.GlowOverAlpha = Math.Max(0, Math.Min(255, Convert.ToSingle(rv))); if (o.TryGetValue("eyeCore", out rv)) d.EyeCore = Convert.ToSingle(rv);
                    d.Test = Crypto.Str(o, "test").ToLowerInvariant() == "true";
                    if (d.Id.Length == 0 || (d.Art.Length == 0 && d.Base.Length == 0)) continue;
                    if (d.Name.Length == 0) d.Name = d.Id;
                    try { d.Color = ColorTranslator.FromHtml(Crypto.Str(o, "color")); } catch { }
                    d.Eye = Pt(o, "eye", d.Eye); d.Lamp = Pt(o, "lamp", d.Lamp);
                    d.Fallback = Crypto.Str(o, "fallback").ToLowerInvariant() == "true";
                    object an; o.TryGetValue("anim", out an);
                    d.Anim = AnimSpec.Parse(an as Dictionary<string, object>, AnimSpec.Theme);
                    list.Add(d);
                }
            }
            catch (Exception ex) { Log.Write("beacons.json could not be read: " + ex.Message); }
            if (list.Count == 0) { Log.Write("No valid beacons in beacons.json; using the built-in default."); File.Delete(FilePath); return Load(); }
            return list;
        }

        static PointF Pt(Dictionary<string, object> o, string k, PointF def)
        {
            object v; if (!o.TryGetValue(k, out v)) return def;
            var a = v as object[]; if (a == null || a.Length != 2) return def;
            return new PointF(Convert.ToSingle(a[0]), Convert.ToSingle(a[1]));
        }

        public static string ReadEmbedded(string name)
        {
            using (var st = typeof(BeaconConfig).Assembly.GetManifestResourceStream(name))
            using (var rd = new StreamReader(st)) return rd.ReadToEnd();
        }

        public static Bitmap LoadArt(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (var dir in new[] { Path.Combine(Settings.Dir, "art"), Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "art") })
            {
                string p = Path.Combine(dir, name);
                if (File.Exists(p)) using (var b = new Bitmap(p)) return new Bitmap(b);
            }
            using (var st = typeof(BeaconConfig).Assembly.GetManifestResourceStream(name))
            {
                if (st == null) return null;
                using (var b = new Bitmap(st)) return new Bitmap(b);
            }
        }
    

        // ------------------------------------------------------------ layered art
        // base + overlays, with the neutral (white/grey) lens and lamp pixels of the base tinted.
        // The alert frame adds a soft glow behind and over the lens/lamp plus a white core.
        public static Bitmap Compose(BeaconDef d, bool alert)
        {
            var b = LoadArt(d.Base); if (b == null) return null;
            Color col = d.Tint.IsEmpty ? d.Color : d.Tint;
            TintLens(b, d, col, alert);
            var o = new Bitmap(b.Width, b.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(o))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias; g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                if (alert) Glow(g, d, col, 200);
                g.DrawImage(b, 0, 0, b.Width, b.Height);
                foreach (var name in d.Overlays) { var ov = LoadArt(name); if (ov == null) { Log.Write(d.Id + ": overlay " + name + " not found."); continue; } g.DrawImage(ov, 0, 0, b.Width, b.Height); ov.Dispose(); }
                if (alert)
                {
                    float eo = d.GlowOver < 0 ? (d.EyeGlow > 0 ? d.EyeGlow : d.EyeR * 2.6f) : d.GlowOver;
                    if (eo > 0 && d.GlowOverAlpha > 0) Radial(g, d.Eye.X, d.Eye.Y, eo, Color.FromArgb((int)d.GlowOverAlpha, col));
                    Radial(g, d.Lamp.X, d.Lamp.Y, d.LampGlow > 0 ? d.LampGlow : d.LampR * 2.8f, Color.FromArgb(100, col));
                    if (d.EyeCore > 0) Radial(g, d.Eye.X, d.Eye.Y, d.EyeCore, Color.FromArgb(190, 255, 255, 255));
                }
            }
            b.Dispose();
            return o;
        }

        // the idle stack as separate pieces (tinted base, then each overlay), for animating single layers
        public static List<KeyValuePair<string, Bitmap>> IdleLayers(BeaconDef d)
        {
            var list = new List<KeyValuePair<string, Bitmap>>();
            var b = LoadArt(d.Base); if (b == null) return list;
            TintLens(b, d, d.Tint.IsEmpty ? d.Color : d.Tint, false);
            list.Add(new KeyValuePair<string, Bitmap>("", b));
            foreach (var name in d.Overlays) { var ov = LoadArt(name); if (ov != null) list.Add(new KeyValuePair<string, Bitmap>(name, ov)); }
            return list;
        }

        static void Glow(Graphics g, BeaconDef d, Color c, int a)
        {
            Radial(g, d.Eye.X, d.Eye.Y, d.EyeGlow > 0 ? d.EyeGlow : d.EyeR * 2.6f, Color.FromArgb(a, c));
            Radial(g, d.Lamp.X, d.Lamp.Y, d.LampGlow > 0 ? d.LampGlow : d.LampR * 2.8f, Color.FromArgb(a, c));
        }

        static void Radial(Graphics g, float x, float y, float r, Color c)
        {
            using (var p = new GraphicsPath())
            {
                p.AddEllipse(x - r, y - r, 2 * r, 2 * r);
                using (var br = new PathGradientBrush(p) { CenterColor = c, SurroundColors = new[] { Color.FromArgb(0, c) }, CenterPoint = new PointF(x, y) })
                {
                    var blend = new Blend(); blend.Factors = new float[] { 0f, 0.35f, 0.8f, 1f }; blend.Positions = new float[] { 0f, 0.35f, 0.7f, 1f }; br.Blend = blend;
                    g.FillPath(br, p);
                }
            }
        }

        static void TintLens(Bitmap b, BeaconDef d, Color col, bool alert)
        {
            var r = new Rectangle(0, 0, b.Width, b.Height);
            var data = b.LockBits(r, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            int n = data.Stride * b.Height; var px = new byte[n];
            Marshal.Copy(data.Scan0, px, 0, n);
            float er2 = d.EyeR * d.EyeR, lr2 = d.LampR * d.LampR;
            for (int y = 0; y < b.Height; y++)
                for (int x = 0; x < b.Width; x++)
                {
                    float ex = x - d.Eye.X, ey = y - d.Eye.Y, lx = x - d.Lamp.X, ly = y - d.Lamp.Y;
                    if (ex * ex + ey * ey >= er2 && lx * lx + ly * ly >= lr2) continue;
                    int i = y * data.Stride + x * 4;
                    if (px[i + 3] == 0) continue;
                    float B = px[i] / 255f, G = px[i + 1] / 255f, R = px[i + 2] / 255f;
                    float mx = Math.Max(R, Math.Max(G, B)), mn = Math.Min(R, Math.Min(G, B)), L = (R + G + B) / 3f;
                    if ((mx - mn) / (mx + 1e-6f) >= 0.25f || L <= 0.42f) continue;   // only the neutral glass
                    float LL = alert ? Math.Min(1f, L * 1.25f + 0.08f) : Math.Min(1f, L * 1.05f);
                    float k = Math.Max(0f, Math.Min(1f, (LL - 0.88f) / 0.12f)) * (alert ? 0.85f : 0.6f);
                    px[i + 2] = (byte)Math.Min(255f, col.R * LL * (1 - k) + 255 * k);
                    px[i + 1] = (byte)Math.Min(255f, col.G * LL * (1 - k) + 255 * k);
                    px[i] = (byte)Math.Min(255f, col.B * LL * (1 - k) + 255 * k);
                }
            Marshal.Copy(px, 0, data.Scan0, n);
            b.UnlockBits(data);
        }
    }
}
