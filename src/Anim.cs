using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;

namespace BotBeacons
{
    // ---------------------------------------------------------------- animation setting
    enum AnimMode { Full, Subtle, Off }

    // settings.ini "anim=full|subtle|off". Full: bob, sway, wander, idle actions, twitches. Subtle: half bob, no sway or
    // wander, idle actions half as often and smaller, no twitches. Off: no idle motion at all (alerts glow, ping wiggles).
    static class AnimSettings
    {
        public static AnimMode Mode = Parse(Settings.Get("anim", "full"));
        public static AnimMode Parse(string s)
        {
            s = (s ?? "").Trim().ToLowerInvariant();
            return s == "off" ? AnimMode.Off : s == "subtle" ? AnimMode.Subtle : AnimMode.Full;
        }
        public static string Name(AnimMode m) { return m == AnimMode.Off ? "off" : m == AnimMode.Subtle ? "subtle" : "full"; }
        public static void Set(AnimMode m)
        {
            Mode = m; Settings.Set("anim", Name(m)); Settings.Save();
            Log.Write("Animations set to " + Name(m) + ".");
            FrameClock.Retarget();
        }
    }

    // ---------------------------------------------------------------- data (beacons.json "anim")
    // One step of an action, active over [at, at+len] of the action (fractions 0..1). The kind comes from its key:
    //   {"layer":"cat-ears.png","rotate":1.6,"pivot":[75,40],"wave":3}   move one overlay (rotate/dx/dy/scale/alpha)
    //   {"glint":[60,151],"size":7}                                         a four-point sparkle
    //   {"glow":"eye","r":16,"alpha":180,"flicker":true}                     a soft glow (eye/lamp/antenna or [x,y])
    //   {"sparks":[52,188],"n":7,"color":"#FFD27A"}                           a little burst of sparks
    //   {"tilt":3} / {"hop":4}                                               tilt the whole beacon (deg) / hop up (px)
    // "wave": n gives a decaying n-cycle oscillation; 0 (default) a single smooth out-and-back.
    class AnimStep
    {
        public string Kind = "", Layer = "", PointRef = "";
        public PointF Point, Pivot; public bool HasPivot;
        public float Rotate, Dx, Dy, Scale, Alpha = 1f, Tilt, Hop, Wave, At = 0f, Len = 1f, Size = 6f, R = 14f;
        public int AlphaMax = 180, N = 6; public bool Flicker; public Color Color = Color.Empty;   // empty = the beacon's colour
    }

    class AnimAction
    {
        public string Name = ""; public float Dur = 1f;
        public readonly List<AnimStep> Steps = new List<AnimStep>();
    }

    class AnimSpec
    {
        public float IdleMin = 20, IdleMax = 60, TwitchMin = 12, TwitchMax = 35;
        public PointF Antenna = new PointF(106, 53);
        public List<AnimAction> Idle = new List<AnimAction>(), Twitches = new List<AnimAction>();

        public static AnimSpec Theme = new AnimSpec();   // top-level "anim" of beacons.json (defaults for every beacon)

        static float F(object v, float def) { try { return v == null ? def : Convert.ToSingle(v, CultureInfo.InvariantCulture); } catch { return def; } }
        static float F(Dictionary<string, object> o, string k, float def) { object v; return o.TryGetValue(k, out v) ? F(v, def) : def; }
        static bool Pt(object v, out PointF p)
        {
            p = PointF.Empty; var a = v as object[];
            if (a == null || a.Length != 2) return false;
            p = new PointF(F(a[0], 0), F(a[1], 0)); return true;
        }
        static void Range(Dictionary<string, object> o, string k, ref float mn, ref float mx)
        {
            object v; var a = o.TryGetValue(k, out v) ? v as object[] : null;
            if (a != null && a.Length == 2) { mn = Math.Max(1f, F(a[0], mn)); mx = Math.Max(mn, F(a[1], mx)); }
        }

        // a beacon's spec = the theme defaults, overridden by the beacon's own "anim" keys
        public static AnimSpec Parse(Dictionary<string, object> o, AnimSpec inherit)
        {
            var s = new AnimSpec();
            if (inherit != null) { s.IdleMin = inherit.IdleMin; s.IdleMax = inherit.IdleMax; s.TwitchMin = inherit.TwitchMin; s.TwitchMax = inherit.TwitchMax; s.Antenna = inherit.Antenna; s.Idle.AddRange(inherit.Idle); s.Twitches.AddRange(inherit.Twitches); }
            if (o == null) return s;
            Range(o, "idleEvery", ref s.IdleMin, ref s.IdleMax);
            Range(o, "twitchEvery", ref s.TwitchMin, ref s.TwitchMax);
            object v; PointF p;
            if (o.TryGetValue("antenna", out v) && Pt(v, out p)) s.Antenna = p;
            if (o.TryGetValue("idle", out v) && v is object[]) { s.Idle = new List<AnimAction>(); foreach (var a in (object[])v) { var x = Action(a as Dictionary<string, object>); if (x != null) s.Idle.Add(x); } }
            if (o.TryGetValue("twitches", out v) && v is object[]) { s.Twitches = new List<AnimAction>(); foreach (var a in (object[])v) { var x = Action(a as Dictionary<string, object>); if (x != null) s.Twitches.Add(x); } }
            return s;
        }

        static AnimAction Action(Dictionary<string, object> o)
        {
            if (o == null) return null;
            var a = new AnimAction { Name = Crypto.Str(o, "name"), Dur = Math.Max(0.1f, Math.Min(5f, F(o, "dur", 1f))) };
            object v; if (!o.TryGetValue("steps", out v) || !(v is object[])) return null;
            foreach (var x in (object[])v)
            {
                var d = x as Dictionary<string, object>; if (d == null) continue;
                var st = new AnimStep();
                st.At = Math.Max(0, Math.Min(1, F(d, "at", 0))); st.Len = Math.Max(0.02f, Math.Min(1 - st.At, F(d, "len", 1 - st.At)));
                st.Wave = F(d, "wave", 0); st.Size = F(d, "size", 6); st.R = F(d, "r", 14); st.AlphaMax = (int)F(d, "alpha", 180); st.N = (int)F(d, "n", 6);
                st.Flicker = Crypto.Str(d, "flicker").ToLowerInvariant() == "true";
                try { var c = Crypto.Str(d, "color"); if (c.Length > 0) st.Color = ColorTranslator.FromHtml(c); } catch { }
                PointF p;
                if (d.ContainsKey("layer"))
                {
                    st.Kind = "layer"; st.Layer = Crypto.Str(d, "layer");
                    st.Rotate = F(d, "rotate", 0); st.Dx = F(d, "dx", 0); st.Dy = F(d, "dy", 0); st.Scale = F(d, "scale", 0);
                    st.Alpha = F(d, "alpha", 1f); st.AlphaMax = 255;
                    if (d.TryGetValue("pivot", out v) && Pt(v, out p)) { st.Pivot = p; st.HasPivot = true; }
                }
                else if (d.ContainsKey("glint") || d.ContainsKey("glow") || d.ContainsKey("sparks"))
                {
                    st.Kind = d.ContainsKey("glint") ? "glint" : d.ContainsKey("glow") ? "glow" : "sparks";
                    object pv = d[st.Kind];
                    if (Pt(pv, out p)) st.Point = p; else st.PointRef = Convert.ToString(pv, CultureInfo.InvariantCulture).ToLowerInvariant();
                }
                else if (d.ContainsKey("tilt")) { st.Kind = "tilt"; st.Tilt = F(d, "tilt", 0); }
                else if (d.ContainsKey("hop")) { st.Kind = "hop"; st.Hop = F(d, "hop", 0); }
                else continue;
                a.Steps.Add(st);
            }
            return a.Steps.Count > 0 ? a : null;
        }
    }

    // ---------------------------------------------------------------- playback
    // A running action: evaluates its steps at time t. Layer steps become per-layer transforms; the rest are drawn
    // by Effects() in sprite coordinates (inside the beacon's sway/tilt transform).
    class AnimPlay
    {
        public readonly AnimAction A; public readonly double Start; public readonly float K;   // K: amplitude scale (subtle = 0.6)
        readonly int seed;
        public AnimPlay(AnimAction a, double start, float k, int seed) { A = a; Start = start; K = k; this.seed = seed; }
        public bool Done(double t) { return t - Start >= A.Dur; }
        float U(double t) { return (float)Math.Max(0, Math.Min(1, (t - Start) / A.Dur)); }

        // 0..1 position inside a step, or -1 when the step is not active
        static float Local(AnimStep s, float u) { float l = (u - s.At) / s.Len; return l < 0 || l > 1 ? -1 : l; }
        static float Shape(AnimStep s, float l)
        {
            if (l < 0) return 0;
            if (s.Wave > 0) return (float)(Math.Sin(2 * Math.PI * s.Wave * l) * (1 - l));   // decaying oscillation
            return (float)Math.Sin(Math.PI * l);                                            // out and back
        }
        float Flick(double t, int salt) { int k = (int)((t - Start) * 25) * 7919 + salt * 104729 + seed; k = (k ^ (k >> 13)) * 1274126177; return 0.15f + 0.85f * ((k >> 8) & 0xFF) / 255f; }

        public float Tilt(double t) { float u = U(t), r = 0; foreach (var s in A.Steps) if (s.Kind == "tilt") r += s.Tilt * Shape(s, Local(s, u)); return r * K; }
        public float Hop(double t) { float u = U(t), r = 0; foreach (var s in A.Steps) if (s.Kind == "hop") r += s.Hop * Math.Abs(Shape(s, Local(s, u))); return r * K; }

        // the layers this action moves (names), for picking the split render cache
        public IEnumerable<string> Layers() { foreach (var s in A.Steps) if (s.Kind == "layer") yield return s.Layer; }

        // transform + alpha for one overlay at time t (identity when the step is idle)
        public bool LayerXform(string layer, double t, Matrix m, out float alpha)
        {
            alpha = 1f; bool any = false; float u = U(t);
            foreach (var s in A.Steps)
            {
                if (s.Kind != "layer" || !string.Equals(s.Layer, layer, StringComparison.OrdinalIgnoreCase)) continue;
                float l = Local(s, u); if (l < 0) continue;
                float v = Shape(s, l) * K; any = true;
                var pv = s.HasPivot ? s.Pivot : new PointF(72, 118);
                if (s.Dx != 0 || s.Dy != 0) m.Translate(s.Dx * v, s.Dy * v);
                if (s.Rotate != 0) m.RotateAt(s.Rotate * v, pv);
                if (s.Scale != 0) { m.Translate(pv.X, pv.Y); m.Scale(1 + s.Scale * v, 1 + s.Scale * v); m.Translate(-pv.X, -pv.Y); }
                if (s.Alpha < 1f) alpha *= s.Flicker ? 1f - (1f - s.Alpha) * Flick(t, 3) : 1f - (1f - s.Alpha) * Math.Abs(v);
            }
            return any;
        }

        public void Effects(Graphics g, double t, BeaconDef d, AnimSpec spec)
        {
            float u = U(t); int i = 0;
            foreach (var s in A.Steps)
            {
                i++;
                if (s.Kind != "glint" && s.Kind != "glow" && s.Kind != "sparks") continue;
                float l = Local(s, u); if (l < 0) continue;
                PointF p = s.PointRef == "eye" ? d.Eye : s.PointRef == "lamp" ? d.Lamp : s.PointRef == "antenna" ? spec.Antenna : s.Point;
                Color c = s.Color.IsEmpty ? d.Color : s.Color;
                float env = (float)Math.Sin(Math.PI * l);
                if (s.Kind == "glint") Glint(g, p, s.Size * (0.4f + 0.6f * env) * (0.5f + 0.5f * K), (int)(255 * env), s.Color.IsEmpty ? Color.White : c, l * 90f);
                else if (s.Kind == "glow") { float a = s.AlphaMax * env * (s.Flicker ? Flick(t, i) : 1f) * (0.5f + 0.5f * K); Glow(g, p, s.R, (int)a, c); }
                else Sparks(g, p, l, s.N, s.Color.IsEmpty ? Color.FromArgb(255, 210, 120) : c, A.Dur * s.Len, seed + i);
            }
        }

        public static void Glow(Graphics g, PointF c, float r, int alpha, Color col)
        {
            if (alpha <= 2 || r < 1) return;
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(c.X - r, c.Y - r, r * 2, r * 2);
                using (var br = new PathGradientBrush(path) { CenterColor = Color.FromArgb(Math.Min(255, alpha), col), SurroundColors = new[] { Color.FromArgb(0, col) } })
                    g.FillPath(br, path);
            }
        }

        static void Glint(Graphics g, PointF p, float size, int alpha, Color col, float rotDeg)
        {
            if (alpha <= 4 || size < 0.5f) return;
            Glow(g, p, size * 1.4f, alpha / 2, col);
            var st = g.Save();
            g.TranslateTransform(p.X, p.Y); g.RotateTransform(rotDeg);
            using (var b = new SolidBrush(Color.FromArgb(Math.Min(255, alpha), col)))
            {
                float w = Math.Max(0.8f, size * 0.18f);
                g.FillPolygon(b, new[] { new PointF(0, -size), new PointF(w, -w), new PointF(size, 0), new PointF(w, w), new PointF(0, size), new PointF(-w, w), new PointF(-size, 0), new PointF(-w, -w) });
            }
            g.Restore(st);
        }

        static void Sparks(Graphics g, PointF p, float l, int n, Color col, float dur, int seed)
        {
            var r = new Random(seed);
            float time = l * dur;
            using (var pen = new Pen(Color.FromArgb((int)(255 * (1 - l)), col), 1.4f))
                for (int i = 0; i < n; i++)
                {
                    double ang = -Math.PI / 2 + (r.NextDouble() * 2 - 1) * 1.3, sp = 30 + r.NextDouble() * 40;
                    float x = p.X + (float)(Math.Cos(ang) * sp * time), y = p.Y + (float)(Math.Sin(ang) * sp * time + 60 * time * time);
                    float tx = (float)(Math.Cos(ang) * 3), ty = (float)(Math.Sin(ang) * 3 + 2 * time);
                    g.DrawLine(pen, x - tx, y - ty, x, y);
                }
            Glow(g, p, 6, (int)(200 * (1 - l)), Color.FromArgb(255, 240, 200));
        }
    }
}
