using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace BotBeacons
{
    // 1.2.7: hot-swappable skins (Skin > Theme). %APPDATA%\BotBeacons\skins\<folder>\ holds one skin: skin.json (the
    // beacons.json schema: "beacons", "anim", "bubble"..., with art paths relative to the folder), its art\ folder, an
    // optional preview.png and metadata ("name", "author", "description", "credits"). A .zip dropped in skins\ is
    // unpacked into its own folder (SkinZip: no paths out of it, size caps, PNG/GIF/ICO/WAV/JSON only); the folder is
    // watched, so new skins show up in the menu and a changed skin reloads, with no restart.
    // A skin dresses the user's beacons; it never adds or removes one (they still come from beacons.json and the tabs
    // added for new bots):
    //   * a "beacons" entry whose "id" is a beacon's id dresses that beacon (its look, and its name and gist);
    //   * the "looks" list (and "beacons" entries marked "generic": true) dress the other beacons, one look each, in a
    //     stable order; the choice is kept per skin in settings.ini (skinlooks.<folder>), so a bot keeps its character.
    //     Skin > Theme > Looks picks one by hand (skinpick.<folder>), or "Built-in look";
    //   * a beacon the skin can't cover keeps its built-in look;
    //   * beacons.user.json is laid over the result, and tabs.json edits (name, colour, sound, picture) still win.
    // "Built-in" (theme= empty) is exactly the app without skins.
    class SkinError : Exception { public SkinError(string m) : base(m) { } }

    class SkinLook { public string Id = "", Name = ""; public Dictionary<string, object> Entry; }

    class SkinPack
    {
        public string Folder = "", Dir = "", Name = "", Author = "", Description = "", Credits = "";
        public Dictionary<string, object> Doc;
        public readonly Dictionary<string, Dictionary<string, object>> ById = new Dictionary<string, Dictionary<string, object>>(StringComparer.OrdinalIgnoreCase);
        public readonly List<SkinLook> Looks = new List<SkinLook>();
        public string Display { get { return Name.Length > 0 ? Name : Folder; } }
    }

    static class Skins
    {
        public static string Dir { get { return Path.Combine(Settings.Dir, "skins"); } }
        public const string BuiltIn = "";
        public const long MaxFile = 8L * 1024 * 1024, MaxJson = 1024 * 1024;
        public const int MaxSide = 1024;
        static readonly string[] ArtKeys = { "art", "alertArt", "base" };
        // the look of a beacon: what a skin entry with its own "art" or "base" replaces as one (ThemeMerge's look fields + anim)
        static readonly string[] LookKeys = { "art", "alertArt", "base", "overlays", "tint", "eye", "lamp", "eyeR", "lampR", "eyeGlow", "lampGlow", "glowOver", "glowOverAlpha", "eyeCore", "anim" };
        static readonly string[] ExtraKeys = { "color", "sound", "attentionSound" };   // taken when given, kept otherwise
        static readonly string[] TopKeys = { "bubble", "beaconWord", "trayIcon" };      // top-level keys a skin may set

        // the chosen theme: settings.ini theme=<folder> ("" = built-in)
        public static string Active { get { var t = Settings.Get("theme", "").Trim(); return t.Equals("builtin", StringComparison.OrdinalIgnoreCase) || t.Equals("built-in", StringComparison.OrdinalIgnoreCase) ? "" : t; } }

        public static bool SafeFolder(string f)
        {
            if (string.IsNullOrEmpty(f) || f.Length > 64 || f[0] == '.' || f[0] == '_') return false;
            foreach (char c in f) if (!(char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.' || c == ' ')) return false;
            return f.Trim() == f && !f.EndsWith(".");
        }
        public static string PathOf(string folder) { return Path.Combine(Dir, folder); }

        // a file inside a skin folder: "art/x.png", "x.png" (also looked for in art\); null = not there or outside the folder
        public static string Resolve(string skinDir, string name)
        {
            if (string.IsNullOrEmpty(skinDir) || string.IsNullOrEmpty(name)) return null;
            string rel = name.Replace('\\', '/');
            if (rel.StartsWith("/") || rel.Contains(":") || rel.Split('/').Any(x => x == "..")) return null;
            try
            {
                string root = Path.GetFullPath(skinDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                foreach (var cand in new[] { rel, "art/" + rel })
                {
                    string full = Path.GetFullPath(Path.Combine(root, cand.Replace('/', Path.DirectorySeparatorChar)));
                    if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(full)) return full;
                }
            }
            catch { }
            return null;
        }

        // ------------------------------------------------------------ discovery
        public static List<SkinPack> List()
        {
            var r = new List<SkinPack>();
            try
            {
                if (!Directory.Exists(Dir)) return r;
                foreach (var d in Directory.GetDirectories(Dir).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                {
                    string f = Path.GetFileName(d);
                    if (!SafeFolder(f) || !File.Exists(Path.Combine(d, "skin.json"))) continue;
                    try { r.Add(Read(d)); } catch (Exception ex) { r.Add(new SkinPack { Folder = f, Dir = d, Name = f, Description = "(can't be used: " + ex.Message + ")" }); }
                }
            }
            catch (Exception ex) { Log.Write("Skins folder could not be read: " + ex.Message); }
            return r;
        }

        static string Str(Dictionary<string, object> d, string k) { object v; return d != null && d.TryGetValue(k, out v) && v != null && !(v is Dictionary<string, object>) && !(v is object[]) ? Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture).Trim() : ""; }
        static string Meta(Dictionary<string, object> d, string k, int max) { var s = Str(d, k); s = new string(s.Where(c => !char.IsControl(c)).ToArray()); return s.Length > max ? s.Substring(0, max) : s; }

        // reads and checks one skin folder (throws SkinError with the reason)
        public static SkinPack Read(string dir)
        {
            var p = new SkinPack { Dir = Path.GetFullPath(dir), Folder = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar, '/')) };
            string sj = Path.Combine(dir, "skin.json");
            if (!File.Exists(sj)) throw new SkinError("no skin.json");
            if (new FileInfo(sj).Length > MaxJson) throw new SkinError("skin.json is over 1 MB");
            try { p.Doc = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(sj)) as Dictionary<string, object>; }
            catch (Exception ex) { throw new SkinError("skin.json is not valid JSON (" + ex.Message + ")"); }
            if (p.Doc == null) throw new SkinError("skin.json is not a JSON object");
            p.Name = Meta(p.Doc, "name", 60); p.Author = Meta(p.Doc, "author", 80); p.Description = Meta(p.Doc, "description", 300);
            p.Credits = Meta(p.Doc, "credits", 300); if (p.Credits.Length == 0) p.Credits = Meta(p.Doc, "origin", 300);
            object bv, lv; p.Doc.TryGetValue("beacons", out bv); p.Doc.TryGetValue("looks", out lv);
            if (bv != null && !(bv is object[])) throw new SkinError("\"beacons\" must be a list");
            if (lv != null && !(lv is object[])) throw new SkinError("\"looks\" must be a list");
            int n = 0;
            foreach (var src in new[] { bv as object[], lv as object[] })
            {
                if (src == null) continue;
                bool looks = src == lv;
                foreach (var x in src)
                {
                    var e = x as Dictionary<string, object>; n++;
                    if (e == null) throw new SkinError("entry " + n + " is not an object");
                    string id = Str(e, "id"), look = Str(e, "look");
                    bool generic = looks || Str(e, "generic").ToLowerInvariant() == "true";
                    string label = id.Length > 0 ? id : look.Length > 0 ? look : "entry " + n;
                    CheckArt(p.Dir, e, label);
                    if (generic)
                    {
                        string lid = look.Length > 0 ? look : id.Length > 0 ? id : "look" + (p.Looks.Count + 1);
                        if (!TabStore.SafeId(lid)) throw new SkinError(label + ": look id '" + lid + "' may only use letters, digits, - _ .");
                        if (p.Looks.Exists(l => string.Equals(l.Id, lid, StringComparison.OrdinalIgnoreCase))) throw new SkinError("look '" + lid + "' is listed twice");
                        p.Looks.Add(new SkinLook { Id = lid, Name = Str(e, "name").Length > 0 ? Meta(e, "name", 40) : lid, Entry = e });
                    }
                    else
                    {
                        if (id.Length == 0) throw new SkinError("entry " + n + " has no \"id\" (or mark it \"generic\": true)");
                        if (p.ById.ContainsKey(id)) throw new SkinError("id '" + id + "' is listed twice");
                        p.ById[id] = e;
                    }
                }
            }
            if (n == 0) throw new SkinError("no beacons or looks in skin.json");
            string ti = Str(p.Doc, "trayIcon");
            if (ti.Length > 0) CheckFile(p.Dir, ti, "trayIcon", new[] { ".ico", ".png" });
            return p;
        }

        static void CheckArt(string dir, Dictionary<string, object> e, string label)
        {
            bool any = false;
            foreach (var k in ArtKeys) { string v = Str(e, k); if (v.Length == 0) continue; any = true; CheckFile(dir, v, label + " " + k, new[] { ".png", ".gif" }); }
            object ov; if (e.TryGetValue("overlays", out ov) && ov != null)
            {
                var a = ov as object[]; if (a == null) throw new SkinError(label + ": \"overlays\" must be a list");
                foreach (var x in a) CheckFile(dir, Convert.ToString(x), label + " overlay", new[] { ".png", ".gif" });
            }
            if (!any && (e.ContainsKey("overlays"))) throw new SkinError(label + ": overlays need a \"base\"");
            object an; if (e.TryGetValue("anim", out an) && an != null && !(an is Dictionary<string, object>)) throw new SkinError(label + ": \"anim\" must be an object");
        }

        static void CheckFile(string dir, string name, string what, string[] exts)
        {
            string ext = Path.GetExtension(name ?? "").ToLowerInvariant();
            if (Array.IndexOf(exts, ext) < 0) throw new SkinError(what + " '" + name + "' must be " + string.Join(" or ", exts));
            string p = Resolve(dir, name);
            if (p == null) throw new SkinError(what + " '" + name + "' is not in the skin folder");
            var fi = new FileInfo(p);
            if (fi.Length > MaxFile) throw new SkinError(what + " '" + name + "' is over 8 MB");
            var sz = ImageSize(p);
            if (sz.Width > MaxSide || sz.Height > MaxSide) throw new SkinError(what + " '" + name + "' is " + sz.Width + "x" + sz.Height + " (at most " + MaxSide + " px a side)");
        }

        // width and height from a PNG or GIF header, without decoding it (0,0 = not one of those)
        public static Size ImageSize(string path)
        {
            try
            {
                var h = new byte[26];
                using (var fs = File.OpenRead(path)) { int got = 0; while (got < h.Length) { int k = fs.Read(h, got, h.Length - got); if (k <= 0) break; got += k; } }
                if (h[0] == 0x89 && h[1] == 'P' && h[2] == 'N' && h[3] == 'G') return new Size((h[16] << 24) | (h[17] << 16) | (h[18] << 8) | h[19], (h[20] << 24) | (h[21] << 16) | (h[22] << 8) | h[23]);
                if (h[0] == 'G' && h[1] == 'I' && h[2] == 'F') return new Size(h[6] | (h[7] << 8), h[8] | (h[9] << 8));
            }
            catch { }
            return Size.Empty;
        }

        // ------------------------------------------------------------ dressing (pure: SkinHarness checks it)
        // root = beacons.json (as loaded); extra = entries for beacons not in it (tabs added for new bots); saved / picks =
        // the remembered and hand-picked looks (beacon id -> look id, "-" = built-in look); assigned = the looks it used.
        public static Dictionary<string, object> Dress(Dictionary<string, object> root, SkinPack p, IList<Dictionary<string, object>> extra,
            IDictionary<string, string> saved, IDictionary<string, string> picks, Dictionary<string, string> assigned, List<string> notes)
        {
            var r = ThemeMerge.Copy(root);
            foreach (var k in TopKeys) { object v; if (p.Doc.TryGetValue(k, out v) && v is string) r[k] = v; }
            var list = new List<object>(); object bl; if (r.TryGetValue("beacons", out bl) && bl is object[]) list.AddRange((object[])bl);
            if (extra != null) foreach (var x in extra) { string id = Str(x, "id"); if (id.Length > 0 && !list.Exists(y => string.Equals(Str(y as Dictionary<string, object>, "id"), id, StringComparison.OrdinalIgnoreCase))) list.Add(ThemeMerge.Copy(x)); }
            var unmatched = new List<string>(); var tests = new List<string>(); int matched = 0;
            for (int i = 0; i < list.Count; i++)
            {
                var e = list[i] as Dictionary<string, object>; string id = Str(e, "id"); if (id.Length == 0) continue;
                Dictionary<string, object> se;
                if (p.ById.TryGetValue(id, out se)) { list[i] = Wear(e, se, p, "", true); matched++; }
                else (Str(e, "test").ToLowerInvariant() == "true" ? tests : unmatched).Add(id);
            }
            unmatched.AddRange(tests);   // test beacons pick last, so they never take a look from a real bot
            var map = Assign(unmatched, p.Looks.Select(l => l.Id).ToList(), saved, picks);
            int looked = 0;
            for (int i = 0; i < list.Count; i++)
            {
                var e = list[i] as Dictionary<string, object>; string id = Str(e, "id"); string lid;
                if (id.Length == 0 || !map.TryGetValue(id, out lid)) continue;
                var lk = p.Looks.First(l => l.Id == lid);
                list[i] = Wear(e, lk.Entry, p, lk.Id, false); looked++;
                if (assigned != null) assigned[id] = lid;
            }
            r["beacons"] = list.ToArray();
            if (notes != null) notes.Add(matched + " beacon(s) by id, " + looked + " with a look, " + (unmatched.Count - looked) + " kept the built-in look");
            return r;
        }

        // which look each beacon gets: a pick first, then the remembered one, then the least-used look (first in the list)
        public static Dictionary<string, string> Assign(IList<string> ids, IList<string> looks, IDictionary<string, string> saved, IDictionary<string, string> picks)
        {
            var res = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var used = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase); foreach (var l in looks) used[l] = 0;
            var open = new List<string>();
            string v;
            foreach (var id in ids)
            {
                if (picks != null && picks.TryGetValue(id, out v))
                {
                    if (v == "-") continue;   // built-in look by hand
                    string l = looks.FirstOrDefault(x => string.Equals(x, v, StringComparison.OrdinalIgnoreCase));
                    if (l != null) { res[id] = l; used[l]++; continue; }
                }
                open.Add(id);
            }
            var rest = new List<string>();
            foreach (var id in open)
            {
                string l = saved != null && saved.TryGetValue(id, out v) ? looks.FirstOrDefault(x => string.Equals(x, v, StringComparison.OrdinalIgnoreCase)) : null;
                if (l != null) { res[id] = l; used[l]++; } else rest.Add(id);
            }
            if (looks.Count == 0) return res;
            foreach (var id in rest)
            {
                string best = looks[0];
                foreach (var l in looks) if (used[l] < used[best]) best = l;
                res[id] = best; used[best]++;
            }
            return res;
        }

        static Dictionary<string, object> Wear(Dictionary<string, object> e, Dictionary<string, object> se, SkinPack p, string look, bool identity)
        {
            var m = ThemeMerge.Copy(e);
            bool full = Str(se, "art").Length > 0 || Str(se, "base").Length > 0;
            if (full) foreach (var k in LookKeys) m.Remove(k);
            foreach (var k in LookKeys.Concat(ExtraKeys)) { object v; if (se.TryGetValue(k, out v) && v != null && k != "anim") m[k] = Clone(v); }
            if (identity) foreach (var k in new[] { "name", "gist" }) { var v = Str(se, k); if (v.Length > 0) m[k] = v; }
            // animations: the skin's top-level "anim" (its defaults), then the entry's own
            object ta, ea; p.Doc.TryGetValue("anim", out ta); se.TryGetValue("anim", out ea);
            var tad = ta as Dictionary<string, object>; var ead = ea as Dictionary<string, object>;
            if (full || ead != null)
            {
                if (tad != null && ead != null) { var a = ThemeMerge.Copy(tad); foreach (var kv in ead) a[kv.Key] = Clone(kv.Value); m["anim"] = a; }
                else if (ead != null) m["anim"] = ThemeMerge.Copy(ead);
                else if (tad != null) m["anim"] = ThemeMerge.Copy(tad);
            }
            foreach (var k in new[] { "sound", "attentionSound" })
            {
                var sv = Str(se, k); if (sv.Length == 0) continue;
                var f = Path.GetExtension(sv).ToLowerInvariant() == ".wav" ? Resolve(p.Dir, sv) ?? Resolve(p.Dir, "sounds/" + sv) : null;
                if (f != null) m[k] = f;
            }
            m["skinDir"] = p.Dir; m["skinLook"] = look;
            return m;
        }
        static object Clone(object v) { var d = v as Dictionary<string, object>; if (d != null) return ThemeMerge.Copy(d); var a = v as object[]; if (a != null) return a.Select(Clone).ToArray(); return v; }

        // Dress with the settings: the remembered looks (updated when new ones are handed out) and the hand-picked ones
        public static Dictionary<string, object> DressFromSettings(Dictionary<string, object> root, string folder, List<string> notes)
        {
            if (!SafeFolder(folder)) throw new SkinError("'" + folder + "' is not a skin folder name");
            if (!Directory.Exists(PathOf(folder))) throw new SkinError("skins\\" + folder + " is not there");
            var p = Read(PathOf(folder));
            var extra = TabStore.Added.Select(u => new Dictionary<string, object> { { "id", u.Id }, { "name", u.Name }, { "purpose", u.Purpose }, { "art", "beacon.png" }, { "color", TabStore.ParseColor(u.Color) != null ? u.Color : "#7B84FF" } }).ToList();
            var saved = ReadMap("skinlooks." + folder); var picks = ReadMap("skinpick." + folder);
            var assigned = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var r = Dress(root, p, extra, saved, picks, assigned, notes);
            var keep = new Dictionary<string, string>(saved, StringComparer.OrdinalIgnoreCase);
            foreach (var kv in assigned) if (!(picks.ContainsKey(kv.Key) && picks[kv.Key] != "-")) keep[kv.Key] = kv.Value;   // a hand pick is not "remembered"
            string text = WriteMap(keep);
            if (text != Settings.Get("skinlooks." + folder, "")) { Settings.Set("skinlooks." + folder, text); Settings.Save(); }
            return r;
        }

        public static Dictionary<string, string> ReadMap(string key)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in Settings.Get(key, "").Split(';'))
            {
                int i = part.IndexOf(':'); if (i <= 0) continue;
                string id = part.Substring(0, i).Trim(), v = part.Substring(i + 1).Trim();
                if (id.Length > 0 && v.Length > 0) d[id] = v;
            }
            return d;
        }
        public static string WriteMap(IDictionary<string, string> d) { return string.Join(";", d.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase).Select(kv => kv.Key + ":" + kv.Value).ToArray()); }

        // Skin > Theme > Looks: "" = automatic, "-" = the built-in look, else a look id
        public static void Pick(string beaconId, string look)
        {
            string folder = Active; if (folder.Length == 0) return;
            var picks = ReadMap("skinpick." + folder);
            if (string.IsNullOrEmpty(look)) picks.Remove(beaconId); else picks[beaconId] = look;
            if (string.IsNullOrEmpty(look)) { var saved = ReadMap("skinlooks." + folder); saved.Remove(beaconId); Settings.Set("skinlooks." + folder, WriteMap(saved)); }
            Settings.Set("skinpick." + folder, WriteMap(picks)); Settings.Save();
            Log.Write("Look for " + beaconId + " in theme '" + folder + "': " + (string.IsNullOrEmpty(look) ? "automatic" : look == "-" ? "built-in" : look) + ".");
            Switch(folder, "look changed");
        }

        // ------------------------------------------------------------ live switch
        static string loadedSig = "", loadedFolder = null, lastFail = "";
        public static string LastError = "";
        public static string Status() { return "theme=" + (Active.Length > 0 ? Active : "built-in") + " skinDir=" + (BeaconConfig.SkinDir.Length > 0 ? Path.GetFileName(BeaconConfig.SkinDir) : "none") + " skinError=" + (LastError.Length > 0 ? LastError : "none") + " looks=" + string.Join(",", Program.Beacons.Select(b => b.Def.Id + ":" + b.LookTag).ToArray()); }

        // start-up: after BeaconConfig.Load the list is dressed (or not); remember it, and say once if the skin failed
        public static void Started(string skinError)
        {
            loadedFolder = BeaconConfig.SkinDir.Length > 0 ? Active : "";
            loadedSig = Sig(loadedFolder);
            if (skinError != null) { failedSig = Sig(Active); Fail(Active, skinError, true); }
        }

        // folder "" = built-in. Builds every new look first; if anything fails, nothing changes (log + one balloon).
        public static bool Switch(string folder, string why)
        {
            folder = folder ?? "";
            var oldRoot = BeaconConfig.Root; var oldAnim = AnimSpec.Theme; var oldDir = BeaconConfig.SkinDir;
            string err = null; var looks = new List<KeyValuePair<BeaconForm, BeaconForm.Look>>();
            try
            {
                if (folder.Length > 0 && !SafeFolder(folder)) throw new SkinError("'" + folder + "' is not a skin folder name");
                var defs = BeaconConfig.LoadAll(folder, out err);
                if (err == null)
                    foreach (var b in Program.Beacons.ToArray())
                    {
                        var nd = defs.FirstOrDefault(d => string.Equals(d.Id, b.Def.Id, StringComparison.OrdinalIgnoreCase));
                        if (nd == null) continue;   // not in the list any more (beacons.json edited since start-up): keeps its look
                        looks.Add(new KeyValuePair<BeaconForm, BeaconForm.Look>(b, BeaconForm.BuildLook(nd)));
                    }
            }
            catch (Exception ex) { err = ex.Message; }
            if (err != null)
            {
                foreach (var kv in looks) kv.Value.Dispose();
                BeaconConfig.Root = oldRoot; AnimSpec.Theme = oldAnim; BeaconConfig.SkinDir = oldDir;
                Fail(folder, err, false);
                return false;
            }
            var sw = Stopwatch.StartNew();
            foreach (var kv in looks) kv.Key.ApplyLook(kv.Value);
            if (Active != folder) { Settings.Set("theme", folder); Settings.Save(); }
            loadedFolder = folder; loadedSig = Sig(folder); LastError = ""; lastFail = "";
            try { Program.ThemeChanged(); } catch (Exception ex) { Log.Write("Theme change: refresh failed: " + ex.Message); }
            Log.Write("Theme " + (folder.Length > 0 ? "'" + folder + "'" : "built-in") + " applied live (" + why + ", " + looks.Count + " beacon(s), " + sw.ElapsedMilliseconds + " ms).");
            return true;
        }

        static void Fail(string folder, string err, bool startup)
        {
            LastError = err;
            string name = folder.Length > 0 ? folder : "built-in";
            Log.Write("Theme '" + name + "' could not be loaded: " + err + (startup ? " (the built-in look is used)" : " (the current look is kept)") + ".");
            if (lastFail == folder + "|" + err) return;   // one balloon per problem, not one per retry
            lastFail = folder + "|" + err;
            if (startup) pendingBalloon = "Skin '" + name + "' could not be loaded: " + err + ". The built-in look is used.";
            else Program.Balloon("Skin not loaded", "'" + name + "': " + err + ". The current look is kept.", ToolTipIcon.Warning, "skin");
        }
        static string pendingBalloon;
        public static void ShowPending() { if (pendingBalloon != null) { Program.Balloon("Skin not loaded", pendingBalloon, ToolTipIcon.Warning, "skin"); pendingBalloon = null; } }

        // a cheap signature of a skin folder (file names, sizes, times): a change reloads the active skin
        static string Sig(string folder)
        {
            if (string.IsNullOrEmpty(folder)) return "";
            try
            {
                var d = PathOf(folder); if (!Directory.Exists(d)) return "gone";
                var sb = new StringBuilder();
                foreach (var f in Directory.GetFiles(d, "*", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal)) { var fi = new FileInfo(f); sb.Append(f).Append('|').Append(fi.Length).Append('|').Append(fi.LastWriteTimeUtc.Ticks).Append(';'); }
                return sb.ToString();
            }
            catch { return "?"; }
        }

        public static void OpenFolder()
        {
            try { Directory.CreateDirectory(Dir); Process.Start("explorer.exe", "\"" + Dir + "\""); } catch (Exception ex) { Log.Write("Could not open the skins folder: " + ex.Message); }
        }

        // ------------------------------------------------------------ watching skins\
        static FileSystemWatcher watcher; static Timer debounce; static Control ui;
        static readonly Dictionary<string, string> zipSeen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public static void Watch(Control owner)
        {
            ui = owner;
            try { Directory.CreateDirectory(Dir); } catch { }
            debounce = new Timer { Interval = 1500 };
            debounce.Tick += (s, e) => { debounce.Stop(); try { Scan(); } catch (Exception ex) { Log.Write("Skins scan failed: " + ex.Message); } };
            try
            {
                watcher = new FileSystemWatcher(Dir) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size };
                FileSystemEventHandler h = (s, e) => Poke();
                watcher.Created += h; watcher.Changed += h; watcher.Deleted += h; watcher.Renamed += (s, e) => Poke();
                watcher.Error += (s, e) => Poke();
                watcher.EnableRaisingEvents = true;
            }
            catch (Exception ex) { Log.Write("Skins folder is not watched (" + ex.Message + "); new skins show after a restart."); }
            Poke();   // a zip dropped while the app was closed
        }
        static void Poke()
        {
            try { if (ui != null && ui.IsHandleCreated && !ui.IsDisposed) ui.BeginInvoke((Action)(() => { debounce.Stop(); debounce.Start(); })); } catch { }
        }

        // unpack new zips, then reload the active skin if its files changed (or fall back if it was deleted)
        public static void Scan()
        {
            bool again = false;
            if (Directory.Exists(Dir))
                foreach (var z in Directory.GetFiles(Dir, "*.zip"))
                {
                    var fi = new FileInfo(z); string sig = fi.Length + "|" + fi.LastWriteTimeUtc.Ticks; string prev;
                    // still being copied: wait until it has stopped changing and nobody has it open
                    if (!zipSeen.TryGetValue(z, out prev) || prev != sig || (DateTime.UtcNow - fi.LastWriteTimeUtc).TotalSeconds < 1) { zipSeen[z] = sig; again = true; continue; }
                    try { using (File.Open(z, FileMode.Open, FileAccess.Read, FileShare.None)) { } } catch { again = true; continue; }
                    zipSeen.Remove(z);
                    InstallZip(z);
                }
            if (again && debounce != null) { debounce.Stop(); debounce.Start(); }
            if (loadedFolder != null && loadedFolder.Length > 0)
            {
                string now = Sig(loadedFolder);
                if (now == "gone") { Log.Write("Theme '" + loadedFolder + "' was removed from the skins folder; back to the built-in look."); Switch("", "skin folder removed"); }
                else if (now != loadedSig) { loadedSig = now; Switch(loadedFolder, "skin files changed"); }
            }
            else if (Active.Length > 0 && LastError.Length > 0 && Directory.Exists(PathOf(Active)))
            {
                // the chosen skin failed earlier (start-up): try again when its folder changes
                string now = Sig(Active); if (now != failedSig) { failedSig = now; Switch(Active, "skin files changed"); }
            }
        }
        static string failedSig = "";

        public static string InstallZip(string zip)
        {
            string folder = SkinZip.FolderFor(zip);
            try
            {
                string done = SkinZip.Install(zip, Dir, folder);
                string keep = Path.Combine(Dir, "_installed"); Directory.CreateDirectory(keep);
                string dest = Path.Combine(keep, Path.GetFileName(zip)); if (File.Exists(dest)) File.Delete(dest);
                File.Move(zip, dest);
                Log.Write("Skin '" + done + "' installed from " + Path.GetFileName(zip) + " (the zip is kept in skins\\_installed).");
                Program.Balloon("New skin", "'" + (SafeRead(Path.Combine(Dir, done)) ?? done) + "' is installed. Pick it from Skin > Theme.", ToolTipIcon.Info, "skin");
                return done;
            }
            catch (Exception ex)
            {
                Log.Write("Skin zip " + Path.GetFileName(zip) + " was not installed: " + ex.Message);
                try { string bad = zip + ".failed"; if (File.Exists(bad)) File.Delete(bad); File.Move(zip, bad); } catch { }
                Program.Balloon("Skin not installed", Path.GetFileName(zip) + ": " + ex.Message, ToolTipIcon.Warning, "skin");
                return null;
            }
        }
        static string SafeRead(string dir) { try { return Read(dir).Display; } catch { return null; } }

        // ------------------------------------------------------------ menu: Skin > Theme
        public static ToolStripMenuItem ThemeMenu(BeaconForm from)
        {
            var theme = new ToolStripMenuItem("Theme") { ToolTipText = "Character art and colours: the built-in look or a skin from the skins folder" };
            theme.DropDownItems.Add("(none)");   // so the arrow shows; refilled on opening
            theme.DropDownOpening += (s, e) => Fill(theme, from);
            return theme;
        }

        static void Fill(ToolStripMenuItem theme, BeaconForm from)
        {
            var old = new ToolStripItem[theme.DropDownItems.Count]; theme.DropDownItems.CopyTo(old, 0); theme.DropDownItems.Clear();
            foreach (var it in old) it.Dispose();
            string cur = Active;
            theme.DropDownItems.Add(new ToolStripMenuItem("Built-in", null, (s, e) => Later(() => Switch("", "chosen in the menu"))) { Checked = cur.Length == 0, ToolTipText = "The look from beacons.json" });
            SkinPack active = null;
            foreach (var p in List())
            {
                var k = p.Folder; bool bad = p.Doc == null;
                string tip = (p.Description.Length > 0 ? p.Description : "") + (p.Author.Length > 0 ? "\nBy " + p.Author : "") + (p.Credits.Length > 0 ? "\n" + p.Credits : "");
                var it = new ToolStripMenuItem(p.Display + (bad ? " (can't be used)" : ""), null, (s, e) => Later(() => Switch(k, "chosen in the menu"))) { Checked = string.Equals(cur, k, StringComparison.OrdinalIgnoreCase), Enabled = !bad, ToolTipText = tip.Trim() };
                theme.DropDownItems.Add(it);
                if (it.Checked && !bad) active = p;
            }
            if (active != null && active.Looks.Count > 0)
            {
                theme.DropDownItems.Add(new ToolStripSeparator());
                if (from != null) theme.DropDownItems.Add(LooksFor(from, active, "Look for " + from.Def.Name));
                var all = new ToolStripMenuItem("Assign looks");
                foreach (var b in Program.Beacons) if (!b.Def.Test || Program.ShowTest) all.DropDownItems.Add(LooksFor(b, active, b.Def.Name));
                theme.DropDownItems.Add(all);
            }
            theme.DropDownItems.Add(new ToolStripSeparator());
            theme.DropDownItems.Add(new ToolStripMenuItem("Open skins folder", null, (s, e) => OpenFolder()) { ToolTipText = "Drop a skin's .zip (or folder) here; it shows up in this menu" });
        }

        static ToolStripMenuItem LooksFor(BeaconForm b, SkinPack p, string text)
        {
            var m = new ToolStripMenuItem(text); string id = b.Def.Id;
            if (p.ById.ContainsKey(id)) { m.DropDownItems.Add(new ToolStripMenuItem("Dressed by the skin (matched by id)") { Enabled = false }); return m; }
            string pick; ReadMap("skinpick." + p.Folder).TryGetValue(id, out pick); pick = pick ?? "";
            m.DropDownItems.Add(new ToolStripMenuItem("Automatic", null, (s, e) => Later(() => Pick(id, ""))) { Checked = pick.Length == 0 });
            foreach (var l in p.Looks) { var lid = l.Id; m.DropDownItems.Add(new ToolStripMenuItem(l.Name, null, (s, e) => Later(() => Pick(id, lid))) { Checked = string.Equals(b.Def.Look, lid, StringComparison.OrdinalIgnoreCase) && pick != "-" }); }
            m.DropDownItems.Add(new ToolStripMenuItem("Built-in look", null, (s, e) => Later(() => Pick(id, "-"))) { Checked = pick == "-" });
            return m;
        }

        // after the menu has closed (a swap rebuilds menus, which must not happen inside a click on one)
        static void Later(Action a)
        {
            var t = new Timer { Interval = 30 };
            t.Tick += (s, e) => { t.Stop(); t.Dispose(); try { a(); } catch (Exception ex) { Log.Write("Theme: " + ex.Message); } };
            t.Start();
        }
    }

    // A skin zip, unpacked safely: every entry must stay inside the new folder (no absolute paths, drive letters, "..",
    // or ":"), only .png .gif .ico .wav .json files are kept (anything else is skipped), at most 400 files, 8 MB each and
    // 64 MB in all (counted while inflating, not taken from the headers), stored or deflated only, no encryption or
    // ZIP64, and the CRC must match. One top folder inside the zip is fine (it is dropped). The new folder must be a
    // skin that loads (Skins.Read); an older folder of the same name is kept as skins\_installed\<name>.previous.
    static class SkinZip
    {
        public const int MaxEntries = 400; public const long MaxTotal = 64L * 1024 * 1024;
        static readonly string[] Allowed = { ".png", ".gif", ".ico", ".wav", ".json" };

        public static string FolderFor(string zip)
        {
            var n = Path.GetFileNameWithoutExtension(zip) ?? "";
            var sb = new StringBuilder(); foreach (char c in n) sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.' ? c : c == ' ' ? ' ' : '-');
            string f = sb.ToString().Trim().TrimStart('.', '_').TrimEnd('.');
            if (f.Length > 40) f = f.Substring(0, 40).Trim();
            return Skins.SafeFolder(f) ? f : "skin";
        }

        class Entry { public string Name; public int Method, Flags; public long CSize, USize, LocalOffset; public uint Crc; }

        static List<Entry> Directory_(byte[] z)
        {
            int eocd = -1;
            for (int i = z.Length - 22; i >= Math.Max(0, z.Length - 22 - 65535); i--) if (z[i] == 0x50 && z[i + 1] == 0x4b && z[i + 2] == 5 && z[i + 3] == 6) { eocd = i; break; }
            if (eocd < 0) throw new SkinError("not a zip file");
            int count = U16(z, eocd + 10); long cdSize = U32(z, eocd + 12), cdOff = U32(z, eocd + 16);
            if (count == 0xFFFF || cdOff == 0xFFFFFFFF) throw new SkinError("ZIP64 archives are not supported");
            if (count > MaxEntries * 4) throw new SkinError("too many files in the zip");
            if (cdOff + cdSize > z.Length) throw new SkinError("the zip is damaged");
            var list = new List<Entry>(); long p = cdOff;
            for (int i = 0; i < count; i++)
            {
                if (p + 46 > z.Length || U32(z, (int)p) != 0x02014b50) throw new SkinError("the zip is damaged");
                int ip = (int)p;
                var e = new Entry { Flags = U16(z, ip + 8), Method = U16(z, ip + 10), Crc = (uint)U32(z, ip + 16), CSize = U32(z, ip + 20), USize = U32(z, ip + 24), LocalOffset = U32(z, ip + 42) };
                int nl = U16(z, ip + 28), xl = U16(z, ip + 30), cl = U16(z, ip + 32);
                if (ip + 46 + nl > z.Length) throw new SkinError("the zip is damaged");
                e.Name = ((e.Flags & 0x800) != 0 ? Encoding.UTF8 : Dos()).GetString(z, ip + 46, nl);
                list.Add(e); p += 46 + nl + xl + cl;
            }
            return list;
        }
        static Encoding Dos() { try { return Encoding.GetEncoding(437); } catch { return Encoding.GetEncoding(28591); } }
        static int U16(byte[] b, int i) { return b[i] | (b[i + 1] << 8); }
        static long U32(byte[] b, int i) { return (uint)(b[i] | (b[i + 1] << 8) | (b[i + 2] << 16) | (b[i + 3] << 24)); }

        // the entry's path inside the skin, or null if it must not be unpacked (reason says why it is refused, "" = skipped)
        public static string SafeName(string name, out string reason)
        {
            reason = "";
            if (name == null) { reason = "empty name"; return null; }
            string n = name.Replace('\\', '/');
            if (n.EndsWith("/")) return null;   // a folder entry
            if (n.StartsWith("/") || n.Contains(":") || n.Any(c => c < 32)) { reason = "unsafe path '" + name + "'"; return null; }
            var parts = n.Split('/').Where(x => x.Length > 0 && x != ".").ToArray();
            if (parts.Any(x => x == ".." || x.Trim() != x || x.EndsWith("."))) { reason = "unsafe path '" + name + "'"; return null; }
            if (parts.Length == 0) return null;
            if (parts[0] == "__MACOSX" || parts.Any(x => x.StartsWith("."))) return null;   // junk from other zippers
            if (Array.IndexOf(Allowed, Path.GetExtension(parts[parts.Length - 1]).ToLowerInvariant()) < 0) return null;   // only skin files
            if (parts.Length > 4) { reason = "folders nested too deep ('" + name + "')"; return null; }
            return string.Join("/", parts);
        }

        // unpacks zip into skinsDir\folder (replacing an older one); returns the folder name
        public static string Install(string zip, string skinsDir, string folder)
        {
            var fi = new FileInfo(zip);
            if (fi.Length > MaxTotal) throw new SkinError("the zip is over 64 MB");
            byte[] z = File.ReadAllBytes(zip);
            var entries = Directory_(z);
            var keep = new List<KeyValuePair<Entry, string>>();
            int skipped = 0;
            foreach (var e in entries)
            {
                string why; string n = SafeName(e.Name, out why);
                if (why.Length > 0) throw new SkinError(why);
                if (n == null) { if (!e.Name.EndsWith("/")) skipped++; continue; }
                if ((e.Flags & 1) != 0) throw new SkinError("encrypted zips are not supported");
                if (e.Method != 0 && e.Method != 8) throw new SkinError("'" + n + "' uses an unsupported compression");
                if (e.USize > Skins.MaxFile) throw new SkinError("'" + n + "' is over 8 MB");
                keep.Add(new KeyValuePair<Entry, string>(e, n));
            }
            if (keep.Count == 0) throw new SkinError("no skin files in the zip");
            if (keep.Count > MaxEntries) throw new SkinError("more than " + MaxEntries + " files in the zip");
            // one top folder holding everything (and no skin.json at the root): drop it
            if (!keep.Any(k => k.Value.Equals("skin.json", StringComparison.OrdinalIgnoreCase)))
            {
                var tops = keep.Select(k => k.Value.Split('/')[0]).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (tops.Count == 1 && keep.All(k => k.Value.Contains("/")))
                    keep = keep.Select(k => new KeyValuePair<Entry, string>(k.Key, k.Value.Substring(k.Value.IndexOf('/') + 1))).ToList();
            }
            if (!keep.Any(k => k.Value.Equals("skin.json", StringComparison.OrdinalIgnoreCase))) throw new SkinError("no skin.json in the zip");
            if (keep.Select(k => k.Value.ToLowerInvariant()).Distinct().Count() != keep.Count) throw new SkinError("the zip lists a file twice");

            string tmp = Path.Combine(skinsDir, ".unpack-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            string root = Path.GetFullPath(tmp) + Path.DirectorySeparatorChar;
            long total = 0;
            try
            {
                Directory.CreateDirectory(tmp);
                foreach (var k in keep)
                {
                    string dest = Path.GetFullPath(Path.Combine(tmp, k.Value.Replace('/', Path.DirectorySeparatorChar)));
                    if (!dest.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new SkinError("unsafe path '" + k.Key.Name + "'");
                    byte[] data = Inflate(z, k.Key, Skins.MaxFile, MaxTotal - total);
                    total += data.Length;
                    if (Crc32(data) != k.Key.Crc) throw new SkinError("'" + k.Value + "' is damaged (CRC)");
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    File.WriteAllBytes(dest, data);
                }
                Skins.Read(tmp);   // must be a skin that loads
                string target = Path.Combine(skinsDir, folder);
                if (Directory.Exists(target))
                {
                    string keepDir = Path.Combine(skinsDir, "_installed"); Directory.CreateDirectory(keepDir);
                    string prev = Path.Combine(keepDir, folder + ".previous");
                    if (Directory.Exists(prev)) Directory.Delete(prev, true);
                    Directory.Move(target, prev);
                }
                Directory.Move(tmp, target);
                if (skipped > 0) Log.Write("Skin zip " + Path.GetFileName(zip) + ": " + skipped + " file(s) that aren't skin files were skipped.");
                return folder;
            }
            finally { try { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch { } }
        }

        static byte[] Inflate(byte[] z, Entry e, long maxFile, long maxLeft)
        {
            int lo = (int)e.LocalOffset;
            if (lo < 0 || lo + 30 > z.Length || U32(z, lo) != 0x04034b50) throw new SkinError("the zip is damaged");
            long start = lo + 30 + U16(z, lo + 26) + U16(z, lo + 28);
            if (start + e.CSize > z.Length) throw new SkinError("the zip is damaged");
            long cap = Math.Min(maxFile, maxLeft);
            using (var src = new MemoryStream(z, (int)start, (int)e.CSize))
            using (var outp = new MemoryStream())
            {
                Stream s = e.Method == 8 ? (Stream)new DeflateStream(src, CompressionMode.Decompress) : src;
                var buf = new byte[65536]; int k;
                while ((k = s.Read(buf, 0, buf.Length)) > 0)
                {
                    outp.Write(buf, 0, k);
                    if (outp.Length > cap) throw new SkinError(outp.Length > maxFile ? "a file in the zip is over 8 MB" : "the zip unpacks to over 64 MB");
                }
                if (e.Method == 8) s.Dispose();
                if (outp.Length != e.USize) throw new SkinError("the zip is damaged (size)");
                return outp.ToArray();
            }
        }

        static uint[] crcTable;
        public static uint Crc32(byte[] d)
        {
            if (crcTable == null) { var t = new uint[256]; for (uint i = 0; i < 256; i++) { uint c = i; for (int j = 0; j < 8; j++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1; t[i] = c; } crcTable = t; }
            uint r = 0xFFFFFFFFu; foreach (byte b in d) r = crcTable[(r ^ b) & 0xFF] ^ (r >> 8); return r ^ 0xFFFFFFFFu;
        }
    }
}
