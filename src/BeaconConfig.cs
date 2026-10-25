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
        // 1.2.7: dressed by a skin (Skin > Theme): its art is looked up in this skin folder first; Look = the skin's look id
        // for a beacon the skin dressed from its generic "looks" ("" = matched by id, or not dressed)
        public string ArtDir = "", Look = "";
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

        public static string ShippedPath { get { return Path.Combine(Settings.Dir, "beacons.shipped.json"); } }   // 1.2.6: the built-in list the user's file came from
        public static string OverridePath { get { return Path.Combine(Settings.Dir, "beacons.user.json"); } }    // 1.2.6: optional, never written by the app
        public static Dictionary<string, object> Root;   // 1.2.6: the loaded theme (beacons.json + beacons.user.json)

        static Dictionary<string, object> Parse(string text) { return new JavaScriptSerializer().DeserializeObject(text) as Dictionary<string, object>; }

        public static List<BeaconDef> Load() { string e; return Load(Skins.Active, out e); }
        // 1.2.7: theme = the skin folder to dress the beacons in ("" = built-in). skinError = why it couldn't be used (the
        // list is then the built-in look); the beacons themselves always come from beacons.json (+ beacons.user.json).
        public static string SkinDir = "";   // the skin the loaded list is dressed in ("" = built-in)
        public static List<BeaconDef> Load(string theme, out string skinError) { skinError = null; return Load(true, theme ?? "", ref skinError); }
        // the full list the app shows: beacons.json, then the tabs added for new bots, with tab name edits applied
        public static List<BeaconDef> LoadAll(string theme, out string skinError)
        {
            var defs = Load(theme, out skinError);
            foreach (var u in TabStore.Added) if (!defs.Exists(d => string.Equals(d.Id, u.Id, StringComparison.OrdinalIgnoreCase))) defs.Add(TabStore.DefFor(u));
            foreach (var d in defs) TabStore.ApplyName(d, true);
            return defs;
        }
        static List<BeaconDef> Load(bool useOverride, string theme, ref string skinError)
        {
            string text = null;
            try { if (File.Exists(FilePath)) text = File.ReadAllText(FilePath); } catch { }
            string emb = ReadEmbedded("beacons.json");
            int want = Version(emb);
            if (text != null)
            {
                // a newer built-in beacon list: 1.2.6 merges it into the user's file (ThemeMerge: the user's changes are
                // kept), with the old file kept as beacons.vN.bak.json as before. 1.2.5 and older replaced the file.
                int have = Version(text);
                if (want > have)
                {
                    string bak = Path.Combine(Settings.Dir, "beacons.v" + have + ".bak.json");
                    try { File.Copy(FilePath, bak, true); } catch { }
                    try
                    {
                        Dictionary<string, object> oldBase = null;
                        try { if (File.Exists(ShippedPath)) { string bt = File.ReadAllText(ShippedPath); if (Version(bt) == have) oldBase = Parse(bt); } } catch { }
                        var notes = new List<string>();
                        var merged = ThemeMerge.Upgrade(Parse(text), oldBase, Parse(emb), notes);
                        text = ThemeMerge.Pretty(merged);
                        WriteAtomic(FilePath, text);
                        Log.Write("beacons.json upgraded from version " + have + " to " + want + ", your changes kept (" + (oldBase != null ? "merged with the old built-in list" : "no old built-in list: your values kept, missing ones filled in") + (notes.Count > 0 ? "; " + string.Join("; ", notes.ToArray()) : "") + "). Old copy: " + Path.GetFileName(bak) + ".");
                    }
                    catch (Exception ex)
                    {
                        // the user's file can't be read as JSON: the 1.2.5 behaviour (the old file is in the backup)
                        try { File.WriteAllText(FilePath, emb); } catch { }
                        Log.Write("beacons.json upgraded from version " + have + " to " + want + " (it could not be merged: " + ex.Message + "; old copy kept as " + Path.GetFileName(bak) + ").");
                        text = emb;
                    }
                }
            }
            if (text == null)
            {
                text = emb;
                try { File.WriteAllText(FilePath, text); } catch { }
            }
            // remember which built-in list this file matches, so the next version bump can tell the user's edits apart
            if (Version(text) == want)
                try { if (!File.Exists(ShippedPath) || File.ReadAllText(ShippedPath) != emb) WriteAtomic(ShippedPath, emb); } catch { }
            var list = new List<BeaconDef>();
            try
            {
                var root = Parse(text);
                SkinDir = "";
                if (useOverride && theme.Length > 0)
                {
                    // 1.2.7: the skin's art and animations go over beacons.json; beacons.user.json and tabs.json still win
                    try { var notes = new List<string>(); root = Skins.DressFromSettings(root, theme, notes); SkinDir = Skins.PathOf(theme); Log.Write("Theme '" + theme + "': " + string.Join("; ", notes.ToArray()) + "."); }
                    catch (Exception ex) { skinError = ex.Message; Log.Write("Theme '" + theme + "' could not be used (" + ex.Message + "); the built-in look is used."); }
                }
                if (useOverride && File.Exists(OverridePath))
                    try { root = ThemeMerge.Overlay(root, Parse(File.ReadAllText(OverridePath))); Log.Write("beacons.user.json applied over beacons.json."); }
                    catch (Exception ex) { Log.Write("beacons.user.json could not be read (" + ex.Message + "); ignored."); }
                Root = root;
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
                    d.ArtDir = Crypto.Str(o, "skinDir"); d.Look = Crypto.Str(o, "skinLook");
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
            if (list.Count == 0 && useOverride && (File.Exists(OverridePath) || theme.Length > 0)) { Log.Write("No valid beacons with beacons.user.json" + (theme.Length > 0 ? " and the theme" : "") + "; loading without them."); if (theme.Length > 0 && skinError == null) skinError = "no valid beacons with it"; return Load(false, "", ref skinError); }
            if (list.Count == 0) { Log.Write("No valid beacons in beacons.json; using the built-in default."); File.Delete(FilePath); return Load(false, "", ref skinError); }
            return list;
        }

        static void WriteAtomic(string path, string text)
        {
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, text);
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        // 1.2.6: theme-level options (top level of beacons.json / beacons.user.json)
        public static string RootStr(string key)
        {
            object v; return Root != null && Root.TryGetValue(key, out v) && v is string ? ((string)v).Trim() : "";
        }

        // "beaconWord": the plural word the menus use ("Pause beacons", "Test beacons"); default "beacons"
        public static string Word
        {
            get
            {
                string w = RootStr("beaconWord");
                if (w.Length == 0 || w.Length > 24) return "beacons";
                foreach (char c in w) if (char.IsControl(c)) return "beacons";
                return w;
            }
        }
        public static string WordCap { get { string w = Word; return char.ToUpper(w[0]) + w.Substring(1); } }

        // "trayIcon": an .ico or .png for the tray, looked up like art (%APPDATA%\BotBeacons\art, art\ next to the exe,
        // inside the exe). null = not set or not loadable (the caller keeps the exe's icon).
        public static Icon TrayIcon()
        {
            string name = RootStr("trayIcon");
            if (name.Length == 0) return null;
            string inSkin = SkinDir.Length > 0 ? Skins.Resolve(SkinDir, name) : null;   // 1.2.7: a skin's own tray icon
            if (inSkin == null && name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) { Log.Write("trayIcon: '" + name + "' is not a plain file name; using the app icon."); return null; }
            try
            {
                byte[] data = inSkin != null ? File.ReadAllBytes(inSkin) : null;
                if (data == null)
                foreach (var dir in new[] { Path.Combine(Settings.Dir, "art"), Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "art") })
                {
                    string p = Path.Combine(dir, name);
                    if (File.Exists(p)) { data = File.ReadAllBytes(p); break; }
                }
                if (data == null)
                    using (var st = typeof(BeaconConfig).Assembly.GetManifestResourceStream(name))
                        if (st != null) { var ms = new MemoryStream(); st.CopyTo(ms); data = ms.ToArray(); }
                if (data == null) { Log.Write("trayIcon: '" + name + "' not found; using the app icon."); return null; }
                if (data.Length > 1024 * 1024) { Log.Write("trayIcon: '" + name + "' is over 1 MB; using the app icon."); return null; }
                if (data.Length > 4 && data[0] == 0 && data[1] == 0 && data[2] == 1 && data[3] == 0)
                {
                    try { using (var ms = new MemoryStream(data)) return new Icon(ms, 32, 32); }
                    catch { byte[] png = IcoPngEntry(data); if (png == null) throw; data = png; }   // some GDI+ builds can't read PNG-compressed entries
                }
                using (var ms = new MemoryStream(data))
                using (var src = new Bitmap(ms))
                using (var bmp = new Bitmap(32, 32, PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality; g.Clear(Color.Transparent);
                        float k = Math.Min(32f / src.Width, 32f / src.Height); float w = src.Width * k, h = src.Height * k;
                        g.DrawImage(src, (32 - w) / 2, (32 - h) / 2, w, h);
                    }
                    IntPtr hi = bmp.GetHicon();
                    using (var tmp = Icon.FromHandle(hi)) { var copy = (Icon)tmp.Clone(); try { DestroyIcon(hi); } catch { } return copy; }
                }
            }
            catch (Exception ex) { Log.Write("trayIcon: '" + name + "' could not be loaded (" + ex.Message + "); using the app icon."); return null; }
        }
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);

        // the PNG-compressed entry of an .ico closest to 32 px (at least 32 if there is one), or null
        static byte[] IcoPngEntry(byte[] d)
        {
            int n = BitConverter.ToUInt16(d, 4), best = -1, bestW = 0;
            for (int i = 0; i < n && 6 + 16 * i + 16 <= d.Length; i++)
            {
                int w = d[6 + 16 * i]; if (w == 0) w = 256;
                int size = BitConverter.ToInt32(d, 6 + 16 * i + 8), off = BitConverter.ToInt32(d, 6 + 16 * i + 12);
                if (off < 0 || size < 8 || off + size > d.Length || d[off] != 0x89 || d[off + 1] != (byte)'P') continue;
                bool better = best < 0 || (bestW < 32 ? w > bestW : w >= 32 && w < bestW);
                if (better) { best = i; bestW = w; }
            }
            if (best < 0) return null;
            int o = BitConverter.ToInt32(d, 6 + 16 * best + 12), s = BitConverter.ToInt32(d, 6 + 16 * best + 8);
            var r = new byte[s]; Array.Copy(d, o, r, 0, s); return r;
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

        public static Bitmap LoadArt(string name) { return LoadArt(name, null); }
        // 1.2.7: skinDir = a skin folder looked in first (its file name may be "art/x.png" or just "x.png")
        public static Bitmap LoadArt(string name, string skinDir)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (!string.IsNullOrEmpty(skinDir))
            {
                string sp = Skins.Resolve(skinDir, name);
                if (sp != null) using (var b = new Bitmap(sp)) return new Bitmap(b);
            }
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
            var b = LoadArt(d.Base, d.ArtDir); if (b == null) return null;
            Color col = d.Tint.IsEmpty ? d.Color : d.Tint;
            TintLens(b, d, col, alert);
            var o = new Bitmap(b.Width, b.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(o))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias; g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                if (alert) Glow(g, d, col, 200);
                g.DrawImage(b, 0, 0, b.Width, b.Height);
                foreach (var name in d.Overlays) { var ov = LoadArt(name, d.ArtDir); if (ov == null) { Log.Write(d.Id + ": overlay " + name + " not found."); continue; } g.DrawImage(ov, 0, 0, b.Width, b.Height); ov.Dispose(); }
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
            var b = LoadArt(d.Base, d.ArtDir); if (b == null) return list;
            TintLens(b, d, d.Tint.IsEmpty ? d.Color : d.Tint, false);
            list.Add(new KeyValuePair<string, Bitmap>("", b));
            foreach (var name in d.Overlays) { var ov = LoadArt(name, d.ArtDir); if (ov != null) list.Add(new KeyValuePair<string, Bitmap>(name, ov)); }
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
