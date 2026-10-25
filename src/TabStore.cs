using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Web.Script.Serialization;

namespace BotBeacons
{
    // 1.2.0: the user's own edits to each tab, kept in %APPDATA%\BotBeacons\tabs.json. They are local overrides and win
    // over beacons.json (a bot or a config update never undoes them). Remove tab resets a beacon's overrides.
    class TabPrefs
    {
        public string Name = "";          // "" = the beacon's own name
        // 1.2.3: "beacon" = the beacon's own art (default), "profile" = the bot's profile picture (sent over the relay),
        // "custom" = the user's file. 1.2.0-1.2.2 wrote "bot" for every tab (it was the only non-custom choice, so it is
        // not a real choice) and it now reads as the default; "custom" keeps working.
        public string Image = "beacon";
        public string Color = "";         // "#RRGGBB" or "" = the beacon's colour
        public string Sound = "";         // "" = the global sound set, "custom" or "classic" (Off = the 1.1.5 per-beacon mute)
        public string Monitor = "";       // Screen.DeviceName the tab is docked on ("" = primary)
        public string Edge = "";          // left|right|top|bottom ("" = right)
        public double Along = -1;         // 0..1 along the edge (-1 = not placed yet)
        public string ImageBy = "";       // 1.2.6: "user" = picked with the pencil after a Tab pictures choice (an arriving profile picture leaves it)
        public bool Seam;                 // 1.2.3: deliberately docked on a seam (held there): stays on that monitor's inner edge
    }

    // a tab added for a bot (1.2.5: when a new bot's signed message was answered Add; 1.2.0-1.2.4 from "+ Add tab"). A normal beacon
    class UserTab { public string Id = "", Name = "", Color = "", Purpose = ""; }

    static class TabStore
    {
        static string FilePath { get { return Path.Combine(Settings.Dir, "tabs.json"); } }
        public static string CustomDir { get { return Path.Combine(Settings.Dir, "tabs"); } }
        static Dictionary<string, TabPrefs> prefs;
        static List<UserTab> users;
        static readonly Dictionary<string, string> origName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public static int Version;   // bumped on every change (windows redraw when it moves)

        static void EnsureLoaded()
        {
            if (prefs != null) return;
            prefs = new Dictionary<string, TabPrefs>(StringComparer.OrdinalIgnoreCase); users = new List<UserTab>();
            try
            {
                if (!File.Exists(FilePath)) return;
                var o = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(FilePath)) as Dictionary<string, object>;
                if (o == null) return;
                object t;
                if (o.TryGetValue("tabs", out t) && t is Dictionary<string, object>)
                    foreach (var kv in (Dictionary<string, object>)t)
                    {
                        var d = kv.Value as Dictionary<string, object>; if (d == null || !SafeId(kv.Key)) continue;
                        var p = new TabPrefs { Name = Str(d, "name"), Image = NormImage(Str(d, "image")), Color = Str(d, "color"), Sound = Str(d, "sound"), Monitor = Str(d, "monitor"), Edge = Str(d, "edge") };
                        p.Seam = Str(d, "seam") == "1";
                        p.ImageBy = Str(d, "imageBy") == "user" ? "user" : "";
                        double a; p.Along = double.TryParse(Str(d, "along"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out a) ? a : -1;
                        if (p.Sound != "custom" && p.Sound != "classic") p.Sound = "";
                        if (ParseColor(p.Color) == null) p.Color = "";
                        prefs[kv.Key] = p;
                    }
                if (o.TryGetValue("added", out t) && t is object[])
                    foreach (var x in (object[])t)
                    {
                        var d = x as Dictionary<string, object>; if (d == null) continue;
                        var u = new UserTab { Id = Str(d, "id"), Name = Str(d, "name"), Color = Str(d, "color"), Purpose = Str(d, "purpose") };
                        if (SafeId(u.Id) && u.Name.Length > 0) users.Add(u);
                    }
            }
            catch (Exception ex) { Log.Write("tabs.json could not be read (" + ex.Message + "); starting with no tab edits."); }
        }

        static string Str(Dictionary<string, object> d, string k) { object v; return d.TryGetValue(k, out v) && v != null ? Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) : ""; }

        public static bool SafeId(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > 40) return false;
            foreach (char c in id) if (!(char.IsLetterOrDigit(c) && c < 128) && c != '-' && c != '_') return false;
            return true;
        }

        public static void Save()
        {
            EnsureLoaded(); Version++;
            try
            {
                var tabs = new Dictionary<string, object>();
                foreach (var kv in prefs)
                {
                    var p = kv.Value;
                    tabs[kv.Key] = new Dictionary<string, object> { { "name", p.Name }, { "image", p.Image }, { "color", p.Color }, { "sound", p.Sound }, { "monitor", p.Monitor }, { "edge", p.Edge }, { "along", p.Along.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) }, { "seam", p.Seam ? "1" : "" } };
                    if (p.ImageBy.Length > 0) ((Dictionary<string, object>)tabs[kv.Key])["imageBy"] = p.ImageBy;   // older apps ignore it
                }
                var added = new List<object>();
                foreach (var u in users) added.Add(new Dictionary<string, object> { { "id", u.Id }, { "name", u.Name }, { "color", u.Color }, { "purpose", u.Purpose } });
                var root = new Dictionary<string, object> { { "version", 1 }, { "tabs", tabs }, { "added", added } };
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, new JavaScriptSerializer().Serialize(root));
                if (File.Exists(FilePath)) File.Delete(FilePath);
                File.Move(tmp, FilePath);
            }
            catch (Exception ex) { Log.Write("tabs.json save failed: " + ex.Message); }
        }

        public static TabPrefs Get(string id)
        {
            EnsureLoaded(); TabPrefs p;
            if (!prefs.TryGetValue(id, out p)) { p = new TabPrefs(); prefs[id] = p; }
            return p;
        }
        public static bool Has(string id) { EnsureLoaded(); return prefs.ContainsKey(id); }
        public static void Reset(string id) { EnsureLoaded(); prefs.Remove(id); try { var f = CustomPath(id); if (File.Exists(f)) File.Delete(f); f = TabGif.PathFor(id); if (File.Exists(f)) File.Delete(f); } catch { } TabGif.Drop(id); }

        // ---- user-added tabs
        public static List<UserTab> Added { get { EnsureLoaded(); return users; } }
        public static bool IsAdded(string id) { EnsureLoaded(); return users.Exists(u => string.Equals(u.Id, id, StringComparison.OrdinalIgnoreCase)); }
        public static void Add(UserTab u) { EnsureLoaded(); users.RemoveAll(x => string.Equals(x.Id, u.Id, StringComparison.OrdinalIgnoreCase)); users.Add(u); Save(); }
        public static void RemoveAdded(string id) { EnsureLoaded(); users.RemoveAll(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase)); Reset(id); Save(); }

        public static BeaconDef DefFor(UserTab u)
        {
            var c = ParseColor(u.Color);
            return new BeaconDef { Id = u.Id, Name = u.Name, Purpose = u.Purpose, Art = "beacon.png", Color = c ?? System.Drawing.Color.FromArgb(123, 132, 255) };
        }

        // ---- effective values
        // the name edit is applied to the beacon itself, so bubbles and menus show it too
        public static void ApplyName(BeaconDef d)
        {
            if (!origName.ContainsKey(d.Id)) origName[d.Id] = d.Name;
            string n = Has(d.Id) ? Get(d.Id).Name : "";
            d.Name = n.Length > 0 ? n : origName[d.Id];
        }
        public static string OriginalName(BeaconDef d) { string n; return origName.TryGetValue(d.Id, out n) ? n : d.Name; }

        public static Color ColorFor(BeaconDef d) { var c = Has(d.Id) ? ParseColor(Get(d.Id).Color) : null; return c ?? d.Color; }
        public static string SoundFor(string id) { return Has(id) ? Get(id).Sound : ""; }

        public static Color? ParseColor(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return null;
            string h = hex.Trim().TrimStart('#');
            int v; if (h.Length != 6 || !int.TryParse(h, System.Globalization.NumberStyles.HexNumber, null, out v)) return null;
            return System.Drawing.Color.FromArgb(255, (v >> 16) & 255, (v >> 8) & 255, v & 255);
        }
        public static string Hex(Color c) { return "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2"); }

        public static string CustomPath(string id) { return Path.Combine(CustomDir, id + ".png"); }

        // the user's chosen file (PNG / ICO / JPG / BMP / GIF) -> a 128 px square PNG in tabs\<id>.png
        // 1.2.6: an animated GIF is also kept as tabs\<id>.gif (the PNG is its first frame); any other file removes it
        public static string ImportCustom(string id, string file)
        {
            try
            {
                bool animated = false;
                if (file.EndsWith(".gif", StringComparison.OrdinalIgnoreCase)) { string err = TabGif.Check(file, out animated); if (err != null) return err; }
                Bitmap src;
                if (file.EndsWith(".ico", StringComparison.OrdinalIgnoreCase)) using (var ic = new Icon(file, 256, 256)) src = ic.ToBitmap();
                else { if (new FileInfo(file).Length > 20 * 1024 * 1024) return "That file is too big (over 20 MB)."; using (var fs = File.OpenRead(file)) using (var im = Image.FromStream(fs)) src = new Bitmap(im); }
                using (src) using (var sq = Square(src, 128))
                {
                    Directory.CreateDirectory(CustomDir);
                    sq.Save(CustomPath(id), ImageFormat.Png);
                }
                string gif = TabGif.PathFor(id);
                if (animated) { string tmp = gif + ".tmp"; File.Copy(file, tmp, true); if (File.Exists(gif)) File.Delete(gif); File.Move(tmp, gif); }
                else if (File.Exists(gif)) File.Delete(gif);
                TabGif.Drop(id);
                Version++;
                return null;
            }
            catch (Exception ex) { return "Could not read that image (" + ex.Message + ")."; }
        }

        // centre-cropped square, high quality
        public static Bitmap Square(Image src, int size)
        {
            int s = Math.Min(src.Width, src.Height);
            var dst = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(dst))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality; g.Clear(System.Drawing.Color.Transparent);
                g.DrawImage(src, new Rectangle(0, 0, size, size), new Rectangle((src.Width - s) / 2, (src.Height - s) / 2, s, s), GraphicsUnit.Pixel);
            }
            return dst;
        }

        // ---- the tab image: custom file, else the bot's profile picture, else initials on the colour (cached per file version)
        static readonly Dictionary<string, KeyValuePair<string, Bitmap>> imgCache = new Dictionary<string, KeyValuePair<string, Bitmap>>(StringComparer.OrdinalIgnoreCase);
        public static string NormImage(string v) { return v == "custom" ? "custom" : v == "profile" ? "profile" : "beacon"; }
        // what the tab actually shows: custom (if its file is there), the bot's picture (if chosen and sent), else the beacon's art
        public static string ImageSource(BeaconDef d)
        {
            string want = Has(d.Id) ? Get(d.Id).Image : "beacon";
            if (want == "custom" && File.Exists(CustomPath(d.Id))) return "custom";
            if (want == "profile" && File.Exists(Avatars.PathFor(d.Id))) return "bot";
            return "beacon";
        }
        // the beacon's own sprite (what the floating skin shows), or null
        public static Bitmap Sprite(BeaconDef d)
        {
            Bitmap s = null;
            try { if (d.Layered) s = BeaconConfig.Compose(d, false); if (s == null) s = BeaconConfig.LoadArt(d.Art) ?? BeaconConfig.LoadArt("beacon.png"); } catch { s = null; }
            return s;
        }
        public static Bitmap BeaconPicture(BeaconDef d)
        {
            using (var s = Sprite(d)) return s != null ? TabRender.BeaconPicture(s, ColorFor(d), 96) : TabRender.Initials(d.Name, ColorFor(d), 96);
        }
        // 1.2.6: the frame of an animated custom picture ("" = not animated); part of the tab's redraw signature
        public static string FrameTag(BeaconDef d)
        {
            if (ImageSource(d) != "custom") return "";
            int i; return TabGif.Current(d.Id, out i) != null ? "f" + i : "";
        }
        public static Bitmap ImageFor(BeaconDef d)
        {
            string src = ImageSource(d);
            if (src == "custom") { int fi; var fr = TabGif.Current(d.Id, out fi); if (fr != null) return fr; }   // 1.2.6: animated GIF
            string file = src == "custom" ? CustomPath(d.Id) : src == "bot" ? Avatars.PathFor(d.Id) : null;
            string key = src + "|" + (file != null ? File.GetLastWriteTimeUtc(file).Ticks.ToString() : d.Name + "|" + d.Art + "|" + d.Base + "|" + Hex(ColorFor(d)));
            KeyValuePair<string, Bitmap> hit;
            if (imgCache.TryGetValue(d.Id, out hit) && hit.Key == key) return hit.Value;
            Bitmap b = null;
            if (file != null) try { using (var fs = File.OpenRead(file)) using (var im = Image.FromStream(fs)) b = Square(im, 96); } catch { b = null; }
            if (b == null) b = BeaconPicture(d);
            if (imgCache.ContainsKey(d.Id) && imgCache[d.Id].Value != null) imgCache[d.Id].Value.Dispose();
            imgCache[d.Id] = new KeyValuePair<string, Bitmap>(key, b);
            return b;
        }
        public static Bitmap LoadFile(string file) { try { using (var fs = File.OpenRead(file)) using (var im = Image.FromStream(fs)) return Square(im, 96); } catch { return null; } }
    }
}
