using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace BotBeacons
{
    // 1.2.6: user themes survive a beacons.json version bump. 1.2.5 and older renamed the user's file to
    // beacons.vN.bak.json and replaced it with the new built-in list. Now the new list is merged in:
    //   * the app keeps a copy of the built-in list the user's file came from (beacons.shipped.json, the "base");
    //   * a value the user changed (differs from the base) is kept; a value the user left alone takes the new default;
    //   * new built-in fields and top-level keys are added; a key or beacon the user deleted stays deleted;
    //   * a new built-in beacon is added only if the user's file still has every built-in beacon of the base (a theme
    //     with its own set of beacons is left alone);
    //   * a beacon's look (art, base, overlays, tint, eye/lamp...) moves as one: if the user changed any of it, none of
    //     the new built-in look fields are taken for that beacon (a theme's own art never gets half a new look);
    //   * with no base (installs from before 1.2.6), the user's beacons are kept exactly as they are and only
    //     top-level keys the file doesn't have yet are added.
    // "upgrade": "keep" at the top of beacons.json skips the merge (the file is never touched; only "version" moves on).
    // Overlay: %APPDATA%\BotBeacons\beacons.user.json is laid over beacons.json at load (never written by the app):
    // top-level keys replace (objects merge key by key), beacons merge by "id" field by field, new ids are appended and
    // {"id": "x", "remove": true} drops one. Pure (ThemeHarness checks it).
    static class ThemeMerge
    {
        public static Dictionary<string, object> Upgrade(Dictionary<string, object> user, Dictionary<string, object> oldBase, Dictionary<string, object> shipped, List<string> notes)
        {
            if (oldBase != null && Same(Strip(user), Strip(oldBase))) { notes.Add("unchanged default: took the new built-in list"); return Copy(shipped); }
            var r = Copy(user);
            object sv; shipped.TryGetValue("version", out sv); if (sv != null) r["version"] = sv;
            if (S(user, "upgrade") == "keep") { notes.Add("\"upgrade\": \"keep\": only the version number moved"); return r; }
            foreach (var kv in shipped)
            {
                if (kv.Key == "version" || kv.Key == "beacons") continue;
                object u; bool hu = user.TryGetValue(kv.Key, out u);
                object b = null; bool hb = oldBase != null && oldBase.TryGetValue(kv.Key, out b);
                bool has; object v = Merge(hu, u, oldBase != null, hb, b, kv.Value, out has);
                if (has) r[kv.Key] = v;
                if (!hu && has) notes.Add("added \"" + kv.Key + "\"");
            }
            var ub = List(user, "beacons"); var sb = List(shipped, "beacons"); var bb = oldBase == null ? null : List(oldBase, "beacons");
            if (ub == null || sb == null || oldBase == null || bb == null) return r;   // no base: the user's beacons as they are
            var outList = new List<object>();
            foreach (var x in ub)
            {
                var ud = x as Dictionary<string, object>; if (ud == null) { outList.Add(x); continue; }
                var sd = ById(sb, S(ud, "id")); var bd = bb == null ? null : ById(bb, S(ud, "id"));
                if (sd == null) { outList.Add(Copy(ud)); continue; }
                if (bd == null) { outList.Add(Copy(ud)); continue; }   // the user's own beacon that happens to share a new built-in id
                var m = Copy(ud);
                bool ownLook = LookFields.Any(k => { object lx, ly; bool hx = ud.TryGetValue(k, out lx), hy = bd.TryGetValue(k, out ly); return hx != hy || (hx && !Same(lx, ly)); });
                foreach (var f in sd)
                {
                    if (ownLook && Array.IndexOf(LookFields, f.Key) >= 0) continue;
                    object u; bool hu = ud.TryGetValue(f.Key, out u);
                    object b = null; bool hb = bd != null && bd.TryGetValue(f.Key, out b);
                    bool has; object v = Merge(hu, u, bd != null, hb, b, f.Value, out has);
                    if (has) m[f.Key] = v;
                }
                outList.Add(m);
            }
            bool keptAll = bb != null && bb.All(x => { var d = x as Dictionary<string, object>; return d == null || ById(ub, S(d, "id")) != null; });
            foreach (var x in sb)
            {
                var sd = x as Dictionary<string, object>; if (sd == null) continue;
                string id = S(sd, "id");
                if (ById(ub, id) != null) continue;
                if (bb != null && ById(bb, id) != null) continue;   // the user deleted it
                if (keptAll) { outList.Add(Copy(sd)); notes.Add("added new built-in beacon '" + id + "'"); }
                else notes.Add("new built-in beacon '" + id + "' not added (your list has its own beacons)");
            }
            r["beacons"] = outList.ToArray();
            return r;
        }

        static readonly string[] LookFields = { "art", "alertArt", "base", "overlays", "tint", "eye", "lamp", "eyeR", "lampR", "eyeGlow", "lampGlow", "glowOver", "glowOverAlpha", "eyeCore" };

        // one value, three ways. baseKnown: we have the old built-in list at all; hb: the base had this key.
        static object Merge(bool hu, object u, bool baseKnown, bool hb, object b, object s, out bool has)
        {
            has = true;
            if (!hu)
            {
                if (hb) { has = false; return null; }   // the user deleted it
                return Clone(s);                          // new (or, with no base, missing): fill it in
            }
            if (baseKnown && hb && Same(u, b)) return Clone(s);   // the user left it alone: take the new default
            var ud = u as Dictionary<string, object>; var sd = s as Dictionary<string, object>;
            if (ud != null && sd != null)
            {
                var bd = hb ? b as Dictionary<string, object> : null;
                var m = Copy(ud);
                foreach (var kv in sd)
                {
                    object uu; bool huu = ud.TryGetValue(kv.Key, out uu);
                    object bv = null; bool hbb = bd != null && bd.TryGetValue(kv.Key, out bv);
                    bool h; object v = Merge(huu, uu, baseKnown && bd != null, hbb, bv, kv.Value, out h);
                    if (h) m[kv.Key] = v;
                }
                return m;
            }
            return Clone(u);   // the user's own value wins
        }

        public static Dictionary<string, object> Overlay(Dictionary<string, object> doc, Dictionary<string, object> over)
        {
            var r = Copy(doc);
            if (over == null) return r;
            foreach (var kv in over)
            {
                if (kv.Key == "version") continue;
                if (kv.Key == "beacons" && kv.Value is object[])
                {
                    var list = new List<object>(); var cur = List(r, "beacons") ?? new object[0];
                    foreach (var x in cur) list.Add(x);
                    foreach (var x in (object[])kv.Value)
                    {
                        var od = x as Dictionary<string, object>; if (od == null) continue;
                        string id = S(od, "id"); if (id.Length == 0) continue;
                        int at = list.FindIndex(y => y is Dictionary<string, object> && S((Dictionary<string, object>)y, "id") == id);
                        if (S(od, "remove") == "true") { if (at >= 0) list.RemoveAt(at); continue; }
                        if (at < 0) { list.Add(Copy(od)); continue; }
                        list[at] = Deep((Dictionary<string, object>)list[at], od);
                    }
                    r["beacons"] = list.ToArray();
                    continue;
                }
                object c; var cd = r.TryGetValue(kv.Key, out c) ? c as Dictionary<string, object> : null;
                var vd = kv.Value as Dictionary<string, object>;
                r[kv.Key] = cd != null && vd != null ? Deep(cd, vd) : Clone(kv.Value);
            }
            return r;
        }
        static Dictionary<string, object> Deep(Dictionary<string, object> a, Dictionary<string, object> b)
        {
            var r = Copy(a);
            foreach (var kv in b)
            {
                object c; var cd = r.TryGetValue(kv.Key, out c) ? c as Dictionary<string, object> : null;
                var vd = kv.Value as Dictionary<string, object>;
                r[kv.Key] = cd != null && vd != null ? Deep(cd, vd) : Clone(kv.Value);
            }
            return r;
        }

        // ---- helpers
        static Dictionary<string, object> Strip(Dictionary<string, object> d) { var c = Copy(d); c.Remove("version"); return c; }
        // a field as text: "id" exactly, anything else lower-cased ("remove": true -> "true", "upgrade": "Keep" -> "keep")
        static string S(Dictionary<string, object> d, string k)
        {
            object v; if (d == null || !d.TryGetValue(k, out v) || v == null) return "";
            string s = Convert.ToString(v, CultureInfo.InvariantCulture);
            return k == "id" ? s : s.ToLowerInvariant();
        }
        static object[] List(Dictionary<string, object> d, string k) { object v; return d.TryGetValue(k, out v) ? v as object[] : null; }
        static Dictionary<string, object> ById(object[] list, string id)
        {
            if (id.Length == 0) return null;
            foreach (var x in list) { var d = x as Dictionary<string, object>; if (d != null && S(d, "id") == id) return d; }
            return null;
        }
        public static Dictionary<string, object> Copy(Dictionary<string, object> d) { return (Dictionary<string, object>)Clone(d); }
        static object Clone(object v)
        {
            var d = v as Dictionary<string, object>;
            if (d != null) { var r = new Dictionary<string, object>(); foreach (var kv in d) r[kv.Key] = Clone(kv.Value); return r; }
            var a = v as object[];
            if (a != null) { var r = new object[a.Length]; for (int i = 0; i < a.Length; i++) r[i] = Clone(a[i]); return r; }
            return v;
        }
        public static bool Same(object a, object b) { return Canon(a) == Canon(b); }
        static string Canon(object v)
        {
            var sb = new StringBuilder(); Write(sb, v, null, 0, true); return sb.ToString();
        }

        // JSON text: Pretty for files people edit (2-space indent, short arrays of numbers on one line)
        public static string Pretty(object v) { var sb = new StringBuilder(); Write(sb, v, "  ", 0, false); sb.Append('\n'); return sb.ToString(); }
        static readonly JavaScriptSerializer js = new JavaScriptSerializer();
        static void Write(StringBuilder sb, object v, string ind, int depth, bool sorted)
        {
            var d = v as Dictionary<string, object>;
            var a = v as object[];
            if (d != null)
            {
                if (d.Count == 0) { sb.Append("{}"); return; }
                sb.Append('{');
                var keys = sorted ? d.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList() : d.Keys.ToList();
                for (int i = 0; i < keys.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    if (ind != null) { sb.Append('\n'); for (int j = 0; j <= depth; j++) sb.Append(ind); }
                    sb.Append(js.Serialize(keys[i])).Append(ind != null ? ": " : ":");
                    Write(sb, d[keys[i]], ind, depth + 1, sorted);
                }
                if (ind != null) { sb.Append('\n'); for (int j = 0; j < depth; j++) sb.Append(ind); }
                sb.Append('}');
            }
            else if (a != null)
            {
                bool flat = ind == null || a.All(x => !(x is Dictionary<string, object>) && !(x is object[]));
                sb.Append('[');
                for (int i = 0; i < a.Length; i++)
                {
                    if (i > 0) sb.Append(flat && ind != null ? ", " : ",");
                    if (!flat) { sb.Append('\n'); for (int j = 0; j <= depth; j++) sb.Append(ind); }
                    Write(sb, a[i], ind, depth + 1, sorted);
                }
                if (!flat && a.Length > 0) { sb.Append('\n'); for (int j = 0; j < depth; j++) sb.Append(ind); }
                sb.Append(']');
            }
            else if (v is decimal || v is double || v is float) sb.Append(Convert.ToDouble(v, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture));
            else sb.Append(js.Serialize(v));
        }
    }
}
